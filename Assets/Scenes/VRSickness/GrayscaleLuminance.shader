// GrayscaleLuminance.shader
// FlowCaptureCamera専用。シーンのカラー映像を輝度(グレースケール)に変換して出力する。
// OpticalFlowManagerが要求する RenderTextureFormat.RFloat の .r チャンネルに
// 輝度値が書き込まれれば良いため、RGB全てに同じ値を書き込んでいる。
Shader "Hidden/NonCircularFOV/GrayscaleLuminance"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "GrayscaleLuminance"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 Frag(Varyings input) : SV_Target
            {
                float4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);

                // ITU-R BT.601 輝度係数
                float luminance = dot(sceneColor.rgb, float3(0.299, 0.587, 0.114));

                return float4(luminance, luminance, luminance, 1.0);
            }
            ENDHLSL
        }
    }
}
