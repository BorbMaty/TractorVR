// URP surface (Blinn-Phong) for the phase 5 environment assets made in Blender (tools/blender/).
// UVs are in metres / texture size (set by the Blender scripts), vertex colour red = AO baked in Blender (Cycles).
// Quest budget: one texture read. The normal map is read only when the global keyword ENV_NORMALS is on (PC tier, QualityTier).
Shader "TractorVR/EnvLit"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _BaseMap ("Albedo", 2D) = "white" {}
        [Normal] _NormalMap ("Normal map (PC tier)", 2D) = "bump" {}
        _NormalStrength ("Normal strength", Float) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.15
        _AOStrength ("Baked AO strength", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ ENV_NORMALS

            #define _SPECULAR_COLOR
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _NormalMap_ST;
                half _NormalStrength;
                half _Smoothness;
                half _AOStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
            #if defined(ENV_NORMALS)
                half4 tangentWS : TEXCOORD3;
            #endif
                half fogFactor : TEXCOORD4;
                half ao : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.normalWS = n.normalWS;
            #if defined(ENV_NORMALS)
                o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
            #endif
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.ao = lerp(1.0h, v.color.r, _AOStrength);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                float3 normalWS = normalize(i.normalWS);
            #if defined(ENV_NORMALS)
                half3 t = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv), _NormalStrength);
                float3 bitangent = i.tangentWS.w * cross(normalWS, i.tangentWS.xyz);
                normalWS = normalize(TransformTangentToWorld(t, half3x3(i.tangentWS.xyz, bitangent, normalWS)));
            #endif

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo * lerp(1.0h, i.ao, 0.6h);
                s.smoothness = _Smoothness;
                s.specular = half3(0.2h, 0.2h, 0.2h) * _Smoothness;
                s.occlusion = i.ao;
                s.alpha = 1;
                s.normalTS = half3(0, 0, 1);

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = normalWS;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fogFactor;
                d.bakedGI = SampleSH(normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                half4 c = UniversalFragmentBlinnPhong(d, s);
                c.rgb = MixFog(c.rgb, i.fogFactor);
                return c;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
    }
}
