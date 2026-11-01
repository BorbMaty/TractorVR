// FieldMap brush: GL quads in 0..1 texture space, colour from GL.Color, blend op and channel mask per material
// (max for cut / swath / ploughed, overwrite of alpha for the furrow direction).
Shader "Hidden/TractorVR/FieldBrush"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.BlendOp)] _BlendOp ("Blend op", Float) = 4      // Max
        _ColorMask ("Colour mask", Float) = 14                                      // RGB
    }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            BlendOp [_BlendOp]
            Blend One Zero
            ColorMask [_ColorMask]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct A { float4 pos : POSITION; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float4 col : COLOR; };

            V vert(A v) { V o; o.pos = UnityObjectToClipPos(v.pos); o.col = v.col; return o; }
            float4 frag(V i) : SV_Target { return i.col; }
            ENDCG
        }
    }
}
