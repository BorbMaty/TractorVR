// Soft glow around a lit lamp: a camera-facing quad (billboarded per eye in the vertex shader), added on top of the scene.
// Size = the object's X scale. A lamp shines forward (the object's +Z), so its halo fades out when seen from the side and
// is gone from behind (no glare in the cab from the cab-front lamps); a Z scale of ~0 = all round (beacons).
// The falloff is computed, no texture. Brightness / colour come from _BaseColor (rgb × a),
// set per lamp with a MaterialPropertyBlock (0 = invisible).
Shader "TractorVR/Halo"
{
    Properties
    {
        _BaseColor ("Colour (rgb) × strength (a)", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Halo"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
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
                half fogFactor : TEXCOORD1;
                half facing : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 centre = TransformObjectToWorld(float3(0, 0, 0));
                float size = length(TransformObjectToWorldDir(float3(1, 0, 0), false));
                float3 vs = TransformWorldToView(centre);
                vs.xy += v.positionOS.xy * size;
                vs.z += size * 0.4;               // a little towards the eye, so the lamp housing doesn't cut it
                o.positionCS = TransformWViewToHClip(vs);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                float3 fwd = TransformObjectToWorldDir(float3(0, 0, 1), false);
                float3 toEye = normalize(GetCameraPositionWS() - centre);
                o.facing = length(fwd) < 0.01 * size ? 1.0h : (half)saturate(dot(normalize(fwd), toEye) * 2.0);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half d = saturate(1.0h - length(i.uv - 0.5h) * 2.0h);
                half3 c = _BaseColor.rgb * _BaseColor.a * d * d * d * i.facing;
                c = MixFogColor(c, half3(0, 0, 0), i.fogFactor);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
