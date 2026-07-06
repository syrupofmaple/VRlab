using UnityEngine;

/// <summary>
/// 論文3.3節「視野制限マスク」の実装。
/// 8個の制御点(角度は固定、半径のみ可変)を用いて非円形の視野制限を行う。
///
/// 各制御点は自分の担当セクター(360°/8=45°)内で、
///  ・FOVmin(最小視野角)の外側にあり
///  ・オプティカルフローが閾値を超えている(=大きいグリッド)
///  ・中心に最も近い
/// グリッドセルを覆い隠す位置まで半径を縮める。該当グリッドが無ければFOVmaxへ戻る。
///
/// 半径の移動速度は論文の式(1)に基づき、
///  ・95°以上では最速(0.3秒で補間完了)
///  ・95°未満では中心に近いほど遅く
/// なるようイージングする。
/// </summary>
public class NonCircularFOVController : MonoBehaviour
{
    [Header("依存コンポーネント")]
    public OpticalFlowManager opticalFlow;

    [Header("視野制限パラメータ")]
    [Tooltip("制御点の数。論文では8個(3.3.3節)")]
    public int controlPointCount = 8;
    [Tooltip("最小視野角(度)。論文では45°(Google Earth VR等で用いられる値)")]
    public float fovMin = 45f;
    [Tooltip("最大視野角(度)。制御点の初期位置=視野制限なしの状態")]
    public float fovMax = 95f;
    [Tooltip("この値を超えるグリッド平均フロー量を「大きい」とみなす。実機で要調整")]
    public float flowThreshold = 0.02f;
    [Tooltip("マスクのぼかし幅(度)。論文では20°")]
    public float blurWidthDegrees = 20f;

    [Header("カメラ視野角(グリッドの角度マッピング用)")]
    [Tooltip("解析に使ったキャプチャカメラの水平視野角")]
    public float cameraHorizontalFovDeg = 110f;
    [Tooltip("解析に使ったキャプチャカメラの垂直視野角")]
    public float cameraVerticalFovDeg = 100f;

    float[] controlPointRadii;   // 現在の半径(度) 各制御点
    float[] controlPointAngles;  // 固定角度(度) 0=右方向、反時計回り
    ComputeBuffer radiiBuffer;

    static readonly int ID_ControlRadii = Shader.PropertyToID("_ControlPointRadii");
    static readonly int ID_ControlCount = Shader.PropertyToID("_ControlPointCount");
    static readonly int ID_FovMax = Shader.PropertyToID("_FovMax");
    static readonly int ID_BlurWidth = Shader.PropertyToID("_BlurWidthDeg");
    static readonly int ID_CamHFov = Shader.PropertyToID("_CamHFovDeg");
    static readonly int ID_CamVFov = Shader.PropertyToID("_CamVFovDeg");

    void Start()
    {
        controlPointRadii = new float[controlPointCount];
        controlPointAngles = new float[controlPointCount];
        for (int i = 0; i < controlPointCount; i++)
        {
            controlPointAngles[i] = i * (360f / controlPointCount);
            controlPointRadii[i] = fovMax; // 初期状態は視野制限なし
        }
        radiiBuffer = new ComputeBuffer(controlPointCount, sizeof(float));
    }

    void OnDestroy()
    {
        radiiBuffer?.Release();
    }

    void Update()
    {
        if (opticalFlow == null || opticalFlow.GridFlow == null) return;

        int gridSize = opticalFlow.gridSize;
        float[] grid = opticalFlow.GridFlow;
        float sectorWidth = 360f / controlPointCount;

        for (int i = 0; i < controlPointCount; i++)
        {
            float target = FindTargetRadius(grid, gridSize, controlPointAngles[i], sectorWidth);
            controlPointRadii[i] = MoveTowardWithEasing(controlPointRadii[i], target);
        }

        // シェーダーへ転送
        radiiBuffer.SetData(controlPointRadii);
        Shader.SetGlobalBuffer(ID_ControlRadii, radiiBuffer);
        Shader.SetGlobalInt(ID_ControlCount, controlPointCount);
        Shader.SetGlobalFloat(ID_FovMax, fovMax);
        Shader.SetGlobalFloat(ID_BlurWidth, blurWidthDegrees);
        Shader.SetGlobalFloat(ID_CamHFov, cameraHorizontalFovDeg);
        Shader.SetGlobalFloat(ID_CamVFov, cameraVerticalFovDeg);
    }

    /// <summary>
    /// 指定セクター内で、FOVminの外側かつ中心に最も近い「大きいグリッド」の
    /// 角度距離(度)を返す。見つからなければfovMax(制限なし)を返す。
    /// </summary>
    float FindTargetRadius(float[] grid, int gridSize, float centerAngle, float sectorWidth)
    {
        float best = fovMax;
        float halfSector = sectorWidth * 0.5f;

        for (int gy = 0; gy < gridSize; gy++)
        {
            for (int gx = 0; gx < gridSize; gx++)
            {
                float flow = grid[gy * gridSize + gx];
                if (flow < flowThreshold) continue;

                // グリッドセル中心を[-1,1]の正規化座標に変換
                float nx = (gx + 0.5f) / gridSize * 2f - 1f;
                float ny = (gy + 0.5f) / gridSize * 2f - 1f;

                float angleDeg = Mathf.Atan2(ny, nx) * Mathf.Rad2Deg;
                if (angleDeg < 0) angleDeg += 360f;

                // 楕円近似で中心からの角距離を算出(水平/垂直FOVが異なる場合に対応)
                float radiusDeg = Mathf.Sqrt(
                    Mathf.Pow(nx * cameraHorizontalFovDeg * 0.5f, 2f) +
                    Mathf.Pow(ny * cameraVerticalFovDeg * 0.5f, 2f));

                if (radiusDeg <= fovMin) continue; // FOVmin以内は制限対象外(3.3.1節)

                float diff = Mathf.DeltaAngle(centerAngle, angleDeg);
                if (Mathf.Abs(diff) > halfSector) continue; // 担当セクター外

                if (radiusDeg < best) best = radiusDeg; // 中心に最も近いものを採用
            }
        }

        return Mathf.Clamp(best, fovMin, fovMax);
    }

    /// <summary>
    /// 論文式(1)のイージング。
    /// FOV(現在の半径)が95°以上なら最速(0.3秒でフルレンジ移動)、
    /// 95°未満では中心に近いほど速度を落とす。
    /// </summary>
    float MoveTowardWithEasing(float current, float target)
    {
        float baseSpeedDegPerSec = (fovMax - fovMin) / 0.3f;
        float speedFactor = (current >= 95f) ? 1f : (current / 95f);
        speedFactor = Mathf.Max(speedFactor, 0.05f); // 停止しないよう下限を設定
        float speed = baseSpeedDegPerSec * speedFactor;
        return Mathf.MoveTowards(current, target, speed * Time.deltaTime);
    }
}
