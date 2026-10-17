// URP surface (Blinn-Phong) with a tiling detail texture + normal map projected along the surface's dominant axis, so it needs no UVs.
// The cab plastics of the tractor model have degenerate UVs (made for plain colours), hence the projection.
// Projection is in the object's own axes, scaled to metres, so the texture sticks to the moving tractor.
// Vertex colour red = baked ambient occlusion (ProjectSetup.BakeAmbientOcclusion), used when _AOStrength > 0.
Shader "TractorVR/TriplanarLit"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (0.3, 0.3, 0.3, 1)
        _DetailMap ("Detail (grey around 1)", 2D) = "white" {}
        [Normal] _NormalMap ("Normal map", 2D) = "bump" {}
        _NormalStrength ("Normal strength", Float) = 1
        _Tiling ("Repeats per metre", Float) = 7
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3
        _Metallic ("Metallic", Range(0, 1)) = 0
        _AOStrength ("Baked AO strength", Range(0, 1)) = 0
        _Highlight ("Highlight (added light, per renderer)", Color) = (0, 0, 0, 0)
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

            TEXTURE2D(_DetailMap); SAMPLER(sampler_DetailMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _DetailMap_ST;
                float4 _NormalMap_ST;
                half _NormalStrength;
                float _Tiling;
                half _Smoothness;
                half _Metallic;
                half _AOStrength;
                half4 _Highlight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOM : TEXCOORD1;   // object space, in metres
                float3 normalOS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 scale = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22));
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.positionOM = v.positionOS.xyz * scale;
                o.normalOS = v.normalOS;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 n = normalize(i.normalOS);
                float3 an = abs(n);
                float3 p = i.positionOM * _Tiling;
                // Quest budget: project from the surface's dominant axis only (one detail + one normal read instead of three each).
                // The grain / weave textures hide the seams where the axis changes on curved parts.
                half3 detail;
                float3 normalOS;
                UNITY_BRANCH if (an.x >= an.y && an.x >= an.z)
                {
                    detail = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, p.zy).rgb;
                    half3 t = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, p.zy), _NormalStrength);
                    normalOS = half3(t.xy + n.zy, abs(t.z) * n.x).zyx;
                }
                else if (an.y >= an.z)
                {
                    detail = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, p.xz).rgb;
                    half3 t = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, p.xz), _NormalStrength);
                    normalOS = half3(t.xy + n.xz, abs(t.z) * n.y).xzy;
                }
                else
                {
                    detail = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, p.xy).rgb;
                    half3 t = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, p.xy), _NormalStrength);
                    normalOS = half3(t.xy + n.xy, abs(t.z) * n.z);
                }
                float3 normalWS = TransformObjectToWorldNormal(normalize(normalOS));

                half ao = lerp(1.0h, i.color.r, _AOStrength);

                SurfaceData s = (SurfaceData)0;
                s.albedo = _BaseColor.rgb * detail * lerp(1.0h, ao, 0.6h);
                s.metallic = _Metallic;
                s.smoothness = _Smoothness;
                s.specular = half3(0.2h, 0.2h, 0.2h) * _Smoothness;
                s.occlusion = ao;
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
                c.rgb += _Highlight.rgb;   // grab highlight (CabControls sets it with a MaterialPropertyBlock)
                c.rgb = MixFog(c.rgb, i.fogFactor);
                return c;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
    }
}
