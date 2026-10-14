// OhhO glass panel — Built-in unlit, transparent. The project has no URP
// package (GraphicsSettings m_CustomRenderPipeline is unset), so this pass
// uses UnityCG instead of the Universal ShaderLibrary include, which would
// not compile here.
//
// Recreates the website's frosted glass card for world-space panels over
// Quest passthrough: a translucent dark fill, a rounded-rect accent border,
// and a soft inner vignette. Real backdrop blur is omitted: over passthrough
// there is no scene color to sample, and a blur is a full-screen bandwidth hit.
//
// Overdraw: one face only (Cull Back), depth-tested, and fragments outside
// the rounded rect are discarded so the four corners do not blend. The
// translucent fill still composites the panel interior over passthrough;
// that cost is the glass look itself.
//
// Apply to a panel Quad or a UI Image. Drive _FillColor/_AccentColor from
// OhhoTheme via GlassPanel (UI/Theme/GlassPanel.cs).
Shader "OhhO/Glass"
{
    Properties
    {
        _FillColor   ("Fill Color", Color)   = (0.039, 0.055, 0.102, 0.62)
        _AccentColor ("Accent Color", Color) = (0.0, 0.831, 1.0, 1.0)
        _BorderWidth ("Border Width", Range(0,0.2)) = 0.012
        _BorderGlow  ("Border Glow", Range(0,4)) = 1.6
        _Radius      ("Corner Radius", Range(0,0.5)) = 0.06
        _Softness    ("Edge Softness", Range(0.0005,0.05)) = 0.004
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            float4 _FillColor;
            float4 _AccentColor;
            float _BorderWidth;
            float _BorderGlow;
            float _Radius;
            float _Softness;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float roundedBoxSDF(float2 p, float2 halfSize, float radius)
            {
                float2 q = abs(p) - halfSize + radius;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            half4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv - 0.5;
                float d = roundedBoxSDF(p, float2(0.5, 0.5), _Radius);

                // Outside the soft edge the pixel is fully transparent. Discard
                // it so the corner does not read and blend the framebuffer.
                if (d > _Softness) discard;

                float panel = 1.0 - smoothstep(-_Softness, _Softness, d);
                float border = smoothstep(-_BorderWidth - _Softness, -_BorderWidth, d)
                             * (1.0 - smoothstep(-_Softness, _Softness, d));
                float vignette = saturate(1.0 - length(p) * 0.6);

                half4 fill = _FillColor;
                fill.rgb *= (0.85 + 0.3 * vignette);

                half3 rgb = fill.rgb + _AccentColor.rgb * border * _BorderGlow;
                half a = max(fill.a * panel, border) * i.color.a;
                return half4(rgb * i.color.rgb, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
