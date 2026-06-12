Shader "Custom/MotionGrayscale"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _MotionThreshold ("Motion Threshold", Float) = 0.02
        _GrayscaleStrength ("Grayscale Strength", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_MotionVectorTexture);
            SAMPLER(sampler_MotionVectorTexture);

            float _MotionThreshold;
            float _GrayscaleStrength;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 元の色を取得
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                // Motion Vectorを取得
                float2 motion = SAMPLE_TEXTURE2D(
                    _MotionVectorTexture, 
                    sampler_MotionVectorTexture, 
                    IN.uv).rg;

                // 動きの大きさを計算
                float speed = length(motion);

                // 閾値を超えた場所だけグレースケール
                if (speed > _MotionThreshold)
                {
                    float gray = dot(color.rgb, float3(0.299, 0.587, 0.114));
                    float blend = saturate(speed / _MotionThreshold);
                    color.rgb = lerp(color.rgb, 
                                    float3(gray, gray, gray), 
                                    blend * _GrayscaleStrength);
                }

                return color;
            }
            ENDHLSL
        }
    }
}