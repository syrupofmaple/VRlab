using UnityEngine;

/// <summary>
/// 低解像度キャプチャ用カメラの映像から Horn-Schunck 法でオプティカルフローを計算し、
/// 16x16グリッドの平均フロー量を GridFlow として公開する。
///
/// 使い方:
///  1. Main Camera (Center Eye Anchor) の子に、視野角を合わせた低解像度専用カメラを配置
///     (Culling Mask はUIなど不要なレイヤーを外す。Depth/Skybox設定はMain Cameraと合わせる)
///  2. そのカメラをこのスクリプトの flowCaptureCamera にアサイン
///  3. flowCaptureCamera には輝度出力用の Replacement Shader か、
///     URP の Grayscale PostProcess を適用しておく(下記Note参照)
///
/// Note: 単純化のため、本スクリプトは flowCaptureCamera.targetTexture に
///       すでにグレースケール(R成分のみ)の映像が描かれている前提で動作する。
///       カラーのまま渡したい場合は、CSComputeGradientsの手前で
///       Luminance変換パスを別途挟むこと。
/// </summary>
public class OpticalFlowManager : MonoBehaviour
{
    [Header("依存アセット")]
    public ComputeShader opticalFlowCompute;

    [Header("キャプチャ用カメラ")]
    [Tooltip("HMDのセンター視点に追従する、輝度出力専用の低解像度カメラ")]
    public Camera flowCaptureCamera;

    [Header("解析設定")]
    [Tooltip("解析解像度(正方形)。Questでは128前後を推奨")]
    public int frameSize = 128;
    [Tooltip("論文に合わせて16x16=256グリッド")]
    public int gridSize = 16;
    [Range(1, 20)] public int hsIterations = 8;
    [Tooltip("Horn-Schunckの平滑化重み")]
    public float smoothnessAlpha = 20f;

    RenderTexture rtCurrent, rtPrevious;
    ComputeBuffer bufIx, bufIy, bufIt, bufU, bufU2, bufV, bufV2, bufGrid;
    int kGradients, kIterate, kGridReduce;
    bool initialized;

    /// <summary>16x16グリッドの平均フロー量(gridSize*gridSize)。外部から参照する。</summary>
    public float[] GridFlow { get; private set; }

    void Start()
    {
        if (opticalFlowCompute == null || flowCaptureCamera == null)
        {
            Debug.LogError("[OpticalFlowManager] ComputeShaderまたはCaptureCameraが未設定です");
            enabled = false;
            return;
        }

        int px = frameSize * frameSize;
        int gridN = gridSize * gridSize;

        rtCurrent = new RenderTexture(frameSize, frameSize, 0, RenderTextureFormat.RFloat);
        rtPrevious = new RenderTexture(frameSize, frameSize, 0, RenderTextureFormat.RFloat);
        rtCurrent.Create();
        rtPrevious.Create();

        bufIx = new ComputeBuffer(px, sizeof(float));
        bufIy = new ComputeBuffer(px, sizeof(float));
        bufIt = new ComputeBuffer(px, sizeof(float));
        bufU = new ComputeBuffer(px, sizeof(float));
        bufU2 = new ComputeBuffer(px, sizeof(float));
        bufV = new ComputeBuffer(px, sizeof(float));
        bufV2 = new ComputeBuffer(px, sizeof(float));
        bufGrid = new ComputeBuffer(gridN, sizeof(float));

        kGradients = opticalFlowCompute.FindKernel("CSComputeGradients");
        kIterate = opticalFlowCompute.FindKernel("CSIterate");
        kGridReduce = opticalFlowCompute.FindKernel("CSGridReduce");

        GridFlow = new float[gridN];

        flowCaptureCamera.targetTexture = rtCurrent;
        initialized = true;
    }

    void OnDestroy()
    {
        bufIx?.Release(); bufIy?.Release(); bufIt?.Release();
        bufU?.Release(); bufU2?.Release(); bufV?.Release(); bufV2?.Release();
        bufGrid?.Release();
        if (rtCurrent) rtCurrent.Release();
        if (rtPrevious) rtPrevious.Release();
    }

    void LateUpdate()
    {
        if (!initialized) return;

        int groups = Mathf.CeilToInt(frameSize / 8f);

        opticalFlowCompute.SetInt("_FrameWidth", frameSize);
        opticalFlowCompute.SetInt("_FrameHeight", frameSize);
        opticalFlowCompute.SetFloat("_Alpha", smoothnessAlpha);

        // Step1: 勾配計算 + U,V初期化
        opticalFlowCompute.SetTexture(kGradients, "_PrevFrame", rtPrevious);
        opticalFlowCompute.SetTexture(kGradients, "_CurrFrame", rtCurrent);
        opticalFlowCompute.SetBuffer(kGradients, "_Ix", bufIx);
        opticalFlowCompute.SetBuffer(kGradients, "_Iy", bufIy);
        opticalFlowCompute.SetBuffer(kGradients, "_It", bufIt);
        opticalFlowCompute.SetBuffer(kGradients, "_U", bufU);
        opticalFlowCompute.SetBuffer(kGradients, "_V", bufV);
        opticalFlowCompute.Dispatch(kGradients, groups, groups, 1);

        // Step2: Horn-Schunck反復 (Ping-Pong)
        ComputeBuffer uSrc = bufU, uDst = bufU2, vSrc = bufV, vDst = bufV2;
        for (int i = 0; i < hsIterations; i++)
        {
            opticalFlowCompute.SetBuffer(kIterate, "_Ix", bufIx);
            opticalFlowCompute.SetBuffer(kIterate, "_Iy", bufIy);
            opticalFlowCompute.SetBuffer(kIterate, "_It", bufIt);
            opticalFlowCompute.SetBuffer(kIterate, "_USrc", uSrc);
            opticalFlowCompute.SetBuffer(kIterate, "_VSrc", vSrc);
            opticalFlowCompute.SetBuffer(kIterate, "_UDst", uDst);
            opticalFlowCompute.SetBuffer(kIterate, "_VDst", vDst);
            opticalFlowCompute.Dispatch(kIterate, groups, groups, 1);

            (uSrc, uDst) = (uDst, uSrc);
            (vSrc, vDst) = (vDst, vSrc);
        }

        // Step3: 16x16グリッドへ平均化
        opticalFlowCompute.SetInt("_GridSize", gridSize);
        opticalFlowCompute.SetBuffer(kGridReduce, "_U", uSrc);
        opticalFlowCompute.SetBuffer(kGridReduce, "_V", vSrc);
        opticalFlowCompute.SetBuffer(kGridReduce, "_GridAverage", bufGrid);
        int gGroups = Mathf.CeilToInt(gridSize / 16f);
        opticalFlowCompute.Dispatch(kGridReduce, Mathf.Max(gGroups, 1), Mathf.Max(gGroups, 1), 1);

        bufGrid.GetData(GridFlow);

        // 次フレームに向けてCurrent/Previousを入れ替え
        (rtCurrent, rtPrevious) = (rtPrevious, rtCurrent);
        flowCaptureCamera.targetTexture = rtCurrent;
    }
}
