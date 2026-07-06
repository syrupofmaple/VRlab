// FOVMask.shader (Unity 6 / URP RenderGraph対応版)
// Blitter/AddBlitPass経由で使用する前提。_BlitTextureにその時点のカメラ映像が渡される。
// 8制御点の半径を角度方向に補間し、非円形の境界を作る。
// 境界の内側(オプティカルフローが大きい領域)は「黒塗り」ではなく「グレースケール化(彩度低下)」で表現する。
Shader "Hidden/NonCircularFOV/FOVMask"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "FOVMask"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5 // StructuredBuffer利用のため

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            StructuredBuffer<float> _ControlPointRadii;
            int   _ControlPointCount;
            float _FovMax;
            float _BlurWidthDeg;
            float _CamHFovDeg;
            float _CamVFovDeg;

            // 角度(度)における補間後の許容半径(度)を、隣接する2制御点から補間で求める
            float InterpolatedRadius(float angleDeg)
            {
                float sector = 360.0 / _ControlPointCount;
                float idxF = angleDeg / sector;
                int i0 = ((int)floor(idxF)) % _ControlPointCount;
                int i1 = (i0 + 1) % _ControlPointCount;
                float t = frac(idxF);

                float r0 = _ControlPointRadii[i0];
                float r1 = _ControlPointRadii[i1];

                float st = smoothstep(0.0, 1.0, t); // 角度方向のなめらかな補間
                return lerp(r0, r1, st);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);

                // UV[0,1] -> [-1,1]中心座標
                float2 c = input.texcoord * 2.0 - 1.0;

                float angleDeg = degrees(atan2(c.y, c.x));
                if (angleDeg < 0) angleDeg += 360.0;

                // 楕円近似で中心からの角距離(度)を算出
                float radiusDeg = length(float2(c.x * _CamHFovDeg * 0.5, c.y * _CamVFovDeg * 0.5));

                float allowedRadius = InterpolatedRadius(angleDeg);
                float innerEdge = allowedRadius - _BlurWidthDeg;
                float maskAlpha = smoothstep(innerEdge, allowedRadius, radiusDeg);

                // ITU-R BT.601 輝度係数で彩度を落とす(グレースケール化)
                float luminance = dot(sceneColor.rgb, float3(0.299, 0.587, 0.114));
                float3 desaturated = float3(luminance, luminance, luminance);

                float3 finalColor = lerp(sceneColor.rgb, desaturated, maskAlpha);
                return float4(finalColor, sceneColor.a);
            }
            ENDHLSL
        }
    }
}
