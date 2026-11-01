// Terrain: grass / field soil / mud blended from the GroundMap control texture (R soil, G mud, B ploughed = darker soil),
// each with its own tiling albedo + normal map in world XZ. A large-scale variation texture breaks up the tiling.
// Blinn-Phong lighting (main-light shadows, SH ambient, fog, stereo); only the layers present are sampled (Quest GPU budget).
// Phase 5 step 3: the global FieldMap (R cut, G swath, B ploughed, A furrow direction) adds furrows, stubble and swaths.
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

            #define _SPECULAR_COLOR
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Control); SAMPLER(sampler_Control);
            TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex);
            TEXTURE2D(_GrassN); SAMPLER(sampler_GrassN);
            TEXTURE2D(_SoilTex); TEXTURE2D(_SoilN);
            TEXTURE2D(_MudTex); TEXTURE2D(_MudN);
            TEXTURE2D(_Macro); SAMPLER(sampler_Macro);
            TEXTURE2D(_FieldMap); SAMPLER(sampler_FieldMap);   // global (FieldMap.cs)
            float4 _FieldRect;                                 // x0, z0, 1 / width, 1 / height

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
                // FieldMap, only inside the fields' box.
                half4 fm = 0;
                float2 fuv = (xz - _FieldRect.xy) * _FieldRect.zw;
                UNITY_BRANCH if (all(fuv > 0.0) && all(fuv < 1.0)) fm = SAMPLE_TEXTURE2D(_FieldMap, sampler_FieldMap, fuv);
                half ploughed = max(ctrl.b, fm.b);
                half wSoil = max(ctrl.r, fm.b), wMud = ctrl.g * (1.0h - fm.b);
                half wGrass = saturate(1.0h - wSoil - wMud);
                half sum = max(wGrass + wSoil + wMud, 1e-3h);
                wGrass /= sum; wSoil /= sum; wMud /= sum;

                float2 uvG = xz * _Tiling.x, uvS = xz * _Tiling.y, uvM = xz * _Tiling.z;
                // Quest budget: sample only the layers that are present here (most pixels are pure grass), and one normal map
                // (the dominant layer's).
                half3 albedo = 0;
                UNITY_BRANCH if (wGrass > 0.01h)
                {
                    half3 g = SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, uvG).rgb;
                    // Mown: pale stubble; the swath: a darker, lumpier row of cut grass (the texture at another scale and angle).
                    half luma = dot(g, half3(0.3h, 0.6h, 0.1h));
                    g = lerp(g, luma * half3(1.35h, 1.4h, 0.75h), fm.r * 0.65h);
                    half3 hay = SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, float2(xz.y, -xz.x) * 1.7).rgb;
                    g = lerp(g, hay * half3(0.75h, 0.82h, 0.45h), saturate(fm.g * 1.5h - 0.2h));
                    albedo += g * wGrass;
                }
                // Furrows: ridges 0.35 m apart across the plough's direction of travel, troughs darker.
                float heading = fm.a * PI;
                float2 across = float2(cos(heading), -sin(heading));
                float along = dot(xz, across) / 0.35;
                float stripe = frac(along);
                half ridge = 1.0h - abs(stripe * 2.0h - 1.0h);                   // 0 in the trough, 1 on the crest
                // Fade the furrows where one is smaller than ~2 pixels (they shimmer in the distance otherwise).
                half furrows = fm.b * saturate(1.5h - 2.0h * fwidth(along));
                UNITY_BRANCH if (wSoil > 0.01h)
                {
                    half3 soilC = SAMPLE_TEXTURE2D(_SoilTex, sampler_GrassTex, uvS).rgb * (1.0h - 0.3h * ploughed)
                                  * lerp(1.0h, lerp(0.7h, 1.1h, ridge), furrows);
                    // Standing crop (control A, not ploughed or cut): straw gold, so the wheat field reads beyond its clumps.
                    half crop = ctrl.a * (1.0h - fm.b) * (1.0h - fm.r);
                    UNITY_BRANCH if (crop > 0.01h)
                    {
                        half3 g = SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, uvG * 0.5).rgb;
                        soilC = lerp(soilC, dot(g, half3(0.3h, 0.6h, 0.1h)) * half3(2.2h, 1.75h, 0.85h), crop * 0.85h);
                    }
                    albedo += soilC * wSoil;
                }
                UNITY_BRANCH if (wMud > 0.01h) albedo += SAMPLE_TEXTURE2D(_MudTex, sampler_GrassTex, uvM).rgb * wMud;
                half macro = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, xz * _Tiling.w).r;
                albedo *= lerp(0.8h, 1.15h, macro);

                half3 nTS;
                UNITY_BRANCH if (wGrass >= wSoil && wGrass >= wMud) nTS = UnpackNormal(SAMPLE_TEXTURE2D(_GrassN, sampler_GrassN, uvG));
                else if (wSoil >= wMud) nTS = UnpackNormal(SAMPLE_TEXTURE2D(_SoilN, sampler_GrassN, uvS));
                else nTS = UnpackNormal(SAMPLE_TEXTURE2D(_MudN, sampler_GrassN, uvM));
                float3 n = normalize(i.normalWS);
                float3 t = normalize(float3(1, 0, 0) - n * n.x);   // planar XZ mapping: tangent along +X,
                float3 b = cross(t, n);                            // bitangent along +Z
                float3 normalWS = normalize(t * nTS.x + b * nTS.y + n * max(nTS.z, 0.2h));
                // Furrow slopes: the profile's gradient tilts the normal across the furrows.
                half slope = (stripe < 0.5 ? 1.0h : -1.0h) * 0.55h * furrows;
                normalWS = normalize(normalWS + float3(across.x, 0, across.y) * slope);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.smoothness = _Smooth.x * wGrass + _Smooth.y * wSoil + _Smooth.z * wMud;
                s.specular = half3(0.12h, 0.12h, 0.12h) * wMud;   // only wet mud shines a little
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

                half4 c = UniversalFragmentBlinnPhong(d, s);   // cheaper than PBR; the ground is matte
                c.rgb = MixFog(c.rgb, i.fogFactor);
                return c;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
    }
}
