// Standing grass and wheat (phase 5 step 3), drawn with Graphics.RenderMeshInstanced by Vegetation.cs. Opaque, two-sided blades,
// lit per vertex (Blinn-Phong would cost a fragment loop per blade on the Quest), no textures. Per instance it reads the FieldMap
// at the clump's origin: cut (R) -> stubble, swath (G) -> lying hay, ploughed (B) -> gone. Near the draw distance it sinks into
// the ground. Vertex colour: R = height along the blade (0 root .. 1 tip), G = per-blade tint variation, B = 1 on ears (wheat).
Shader "TractorVR/Vegetation"
{
    Properties
    {
        _BaseColor ("Root colour", Color) = (0.13, 0.25, 0.06, 1)
        _TipColor ("Tip colour", Color) = (0.42, 0.62, 0.20, 1)
        _EarColor ("Ear colour (wheat)", Color) = (0.85, 0.72, 0.38, 1)
        _HayColor ("Cut, dry colour", Color) = (0.62, 0.62, 0.30, 1)
        _StubbleHeight ("Stubble height (share of the clump)", Float) = 0.18
        _Wind ("Wind sway (m at the tip)", Float) = 0.05
        _FadeEnd ("Gone at (m from the camera)", Float) = 60
        _FadeWidth ("Sinks over (m)", Float) = 8
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_FieldMap); SAMPLER(sampler_FieldMap);   // global (FieldMap.cs)
            float4 _FieldRect;

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _TipColor, _EarColor, _HayColor;
                float _StubbleHeight, _Wind, _FadeEnd, _FadeWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : COLOR;
                half fog : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                half4 fm = 0;
                float2 fuv = (origin.xz - _FieldRect.xy) * _FieldRect.zw;
                if (all(fuv > 0.0) && all(fuv < 1.0)) fm = SAMPLE_TEXTURE2D_LOD(_FieldMap, sampler_FieldMap, fuv, 0);
                half cut = step(0.5h, fm.r), swath = step(0.5h, fm.g), ploughed = step(0.5h, fm.b);

                float3 p = v.positionOS.xyz;
                half h = v.color.r;
                // Standing: sways in the wind. Cut: stubble. In the swath: lying flat along local X, a little higher.
                float phase = _Time.y * 1.7 + origin.x * 0.31 + origin.z * 0.23;
                p.xz += (1.0 - cut) * h * h * _Wind * float2(sin(phase), cos(phase * 0.8));
                float3 lying = float3(p.x + p.y * 0.9, 0.03 + p.y * 0.12, p.z);
                p = lerp(p, float3(p.x, p.y * _StubbleHeight, p.z), cut * (1.0 - swath));
                p = lerp(p, lying, swath);
                // Gone on ploughed ground; sinks into the ground towards the draw distance.
                float d = distance(origin, _WorldSpaceCameraPos);
                float keep = saturate((_FadeEnd - d) / _FadeWidth) * (1.0 - ploughed);
                p.y *= keep;
                p.xz *= lerp(0.3, 1.0, keep);

                float3 pw = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(pw);

                half3 col = lerp(_BaseColor.rgb, _TipColor.rgb, h) * (0.8h + 0.4h * v.color.g);
                col = lerp(col, _EarColor.rgb * (0.85h + 0.3h * v.color.g), v.color.b);
                col = lerp(col, _HayColor.rgb * (0.8h + 0.4h * v.color.g), max(swath, cut * 0.6h));
                // Lit as if facing up (a field of blades reads as one surface), per vertex: sun, sky, a little self-shade at the root.
                Light sun = GetMainLight();
                half ndl = saturate(sun.direction.y) * 0.65h + 0.35h;
                half3 light = sun.color * ndl + SampleSH(half3(0, 1, 0));
                o.color = col * light * lerp(0.55h, 1.0h, h);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return half4(MixFog(i.color, i.fog), 1);
            }
            ENDHLSL
        }
    }
}
