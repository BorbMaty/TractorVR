// Far horizon silhouettes (Horizon.cs): unlit vertex colour hazed towards the current fog colour (day / dusk / night follow
// TractorLights), dimmed with the sun. Not affected by the scene's fog (it would hide them completely at 1.3 km).
Shader "TractorVR/Horizon"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+400" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half3 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                Light sun = GetMainLight();
                half day = saturate(dot(sun.color, half3(0.3h, 0.6h, 0.1h)) * 0.8h + 0.15h);
                o.color = lerp(v.color.rgb * day, unity_FogColor.rgb, v.color.a);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return half4(i.color, 1);
            }
            ENDHLSL
        }
    }
}
