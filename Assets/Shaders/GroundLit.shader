// Terrain: grass / field soil / mud blended from the GroundMap control texture (R soil, G mud, B ploughed = darker soil),
// each with its own tiling albedo + normal map in world XZ. A large-scale variation texture breaks up the tiling.
// URP lighting like TriplanarLit (main-light shadows, SH ambient, fog, stereo).
Shader "TractorVR/GroundLit"
{
    Properties
    {
        _Control ("Control (R soil, G mud, B ploughed)", 2D) = "black" {}
        _GrassTex ("Grass", 2D) = "white" {}
        [Normal] _GrassN ("Grass normal", 2D) = "bump" {}
        _SoilTex ("Soil", 2D) = "white" {}
        [Normal] _SoilN ("Soil normal", 2D) = "bump" {}
        _MudTex ("Mud", 2D) = "white" {}
        [Normal] _MudN ("Mud normal", 2D) = "bump" {}
        _Macro ("Large-scale variation", 2D) = "grey" {}
        _Tiling ("Repeats per metre: grass, soil, mud, macro", Vector) = (0.5, 0.5, 0.33, 0.0156)
        _Smooth ("Smoothness: grass, soil, mud", Vector) = (0.08, 0.05, 0.45, 0)
        _WorldHalf ("Half world size (m)", Float) = 1000
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Control); SAMPLER(sampler_Control);
            TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex);
            TEXTURE2D(_GrassN); SAMPLER(sampler_GrassN);
            TEXTURE2D(_SoilTex); TEXTURE2D(_SoilN);
            TEXTURE2D(_MudTex); TEXTURE2D(_MudN);
            TEXTURE2D(_Macro); SAMPLER(sampler_Macro);

            CBUFFER_START(UnityPerMaterial)
                float4 _Control_ST, _GrassTex_ST, _GrassN_ST, _SoilTex_ST, _SoilN_ST, _MudTex_ST, _MudN_ST, _Macro_ST;
                float4 _Tiling;
                float4 _Smooth;
                float _WorldHalf;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
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
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 xz = i.positionWS.xz;
                half4 ctrl = SAMPLE_TEXTURE2D(_Control, sampler_Control, (xz + _WorldHalf) / (2.0 * _WorldHalf));
                half wSoil = ctrl.r, wMud = ctrl.g;
                half wGrass = saturate(1.0h - wSoil - wMud);
                half sum = max(wGrass + wSoil + wMud, 1e-3h);
                wGrass /= sum; wSoil /= sum; wMud /= sum;

                float2 uvG = xz * _Tiling.x, uvS = xz * _Tiling.y, uvM = xz * _Tiling.z;
                half3 albedo = SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, uvG).rgb * wGrass
                             + SAMPLE_TEXTURE2D(_SoilTex, sampler_GrassTex, uvS).rgb * wSoil * (1.0h - 0.3h * ctrl.b)
                             + SAMPLE_TEXTURE2D(_MudTex, sampler_GrassTex, uvM).rgb * wMud;
                half macro = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, xz * _Tiling.w).r;
                albedo *= lerp(0.8h, 1.15h, macro);

                half3 nTS = UnpackNormal(SAMPLE_TEXTURE2D(_GrassN, sampler_GrassN, uvG)) * wGrass
                          + UnpackNormal(SAMPLE_TEXTURE2D(_SoilN, sampler_GrassN, uvS)) * wSoil
                          + UnpackNormal(SAMPLE_TEXTURE2D(_MudN, sampler_GrassN, uvM)) * wMud;
                float3 n = normalize(i.normalWS);
                float3 t = normalize(float3(1, 0, 0) - n * n.x);   // planar XZ mapping: tangent along +X,
                float3 b = cross(t, n);                            // bitangent along +Z
                float3 normalWS = normalize(t * nTS.x + b * nTS.y + n * max(nTS.z, 0.2h));

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.smoothness = _Smooth.x * wGrass + _Smooth.y * wSoil + _Smooth.z * wMud;
                s.occlusion = 1;
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

                half4 c = UniversalFragmentPBR(d, s);
                c.rgb = MixFog(c.rgb, i.fogFactor);
                return c;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
    }
}
