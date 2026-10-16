// Black edge vignette for a head-locked quad. Darkness is a function of the view angle from the
// quad centre, so _InnerDeg/_OuterDeg are real field-of-view angles. Drawn on top of everything.
Shader "TractorVR/ComfortVignette"
{
    Properties
    {
        _InnerDeg ("Clear radius (deg)", Float) = 60
        _OuterDeg ("Dark from (deg)", Float) = 75
        _TanHalf ("tan(half angle covered by the quad)", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _InnerDeg;
                float _OuterDeg;
                float _TanHalf;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv * 2.0 - 1.0) * _TanHalf;   // tan of the view angle
                float deg = degrees(atan(r));
                float a = smoothstep(_InnerDeg, _OuterDeg, deg);
                return half4(0, 0, 0, a);
            }
            ENDHLSL
        }
    }
}
