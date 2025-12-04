Shader "Unlit/SlimeMask"
{
    Properties
    {
        _MaskTex ("Mask (R)", 2D) = "white" {}
        _Color   ("Fill Color", Color) = (0.20, 1.00, 0.50, 1)
        _RimColor("Rim Color",  Color) = (1, 1, 1, 1)
        _OutlineColor ("Outline Color", Color) = (0.00, 0.25, 0.00, 1)

        // Edge shaping
        _Threshold    ("Fill Threshold", Range(0,1)) = 0.5
        _Softness     ("Edge Softness", Range(0.001,0.25)) = 0.06
        _OutlineWidth ("Outline Width", Range(0.001,0.25)) = 0.08
        _OutlineSoft  ("Outline Softness", Range(0.001,0.25)) = 0.06

        // Mask smoothing (rounder edges)
        _BlurAmount   ("Mask Blur Amount", Range(0,1)) = 0.5    // 0=no blur, 1=strong
        _BlurSharpen  ("Post-Blur Sharpen", Range(0,1)) = 0.15   // pulls detail back a bit

        // Lighting controls
        _LightDir ("Fake Light Dir (xy)", Vector) = (0.5, 0.8, 0, 0)
        _NormalStrength ("Normal Strength", Range(0,4)) = 1.5
        _ShadeStrength  ("Shadow/Light Strength", Range(0,2)) = 1.0
        _Ambient        ("Ambient", Range(0,1)) = 0.35
        _RimPower       ("Rim Power", Range(0.5,8)) = 2.0
        _RimStrength    ("Rim Strength", Range(0,2)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MaskTex;
            float4 _MaskTex_TexelSize; // x=1/width, y=1/height

            fixed4 _Color;
            fixed4 _RimColor;
            fixed4 _OutlineColor;

            float _Threshold, _Softness, _OutlineWidth, _OutlineSoft;
            float _BlurAmount, _BlurSharpen;

            float4 _LightDir;
            float _NormalStrength, _ShadeStrength, _Ambient, _RimPower, _RimStrength;

            struct appdata {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            // 3x3 box blur with tunable strength (lerp to center)
            float sampleMask(sampler2D t, float2 uv)
            {
                return tex2D(t, uv).r;
            }

            float blurMask(sampler2D t, float2 uv, float amount)
            {
                if (amount <= 0.001)
                    return sampleMask(t, uv);

                float2 texel = _MaskTex_TexelSize.xy;
                // 3x3 kernel
                float m =
                      sampleMask(t, uv)
                    + sampleMask(t, uv + float2(+texel.x, 0))
                    + sampleMask(t, uv + float2(-texel.x, 0))
                    + sampleMask(t, uv + float2(0, +texel.y))
                    + sampleMask(t, uv + float2(0, -texel.y))
                    + sampleMask(t, uv + float2(+texel.x, +texel.y))
                    + sampleMask(t, uv + float2(+texel.x, -texel.y))
                    + sampleMask(t, uv + float2(-texel.x, +texel.y))
                    + sampleMask(t, uv + float2(-texel.x, -texel.y));

                m *= (1.0/9.0);

                // Lerp between center sample and blurred for adjustable strength
                float c = sampleMask(t, uv);
                return lerp(c, m, saturate(amount));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 1) Smooth/round mask
                float m = blurMask(_MaskTex, i.uv, _BlurAmount);

                // Optional micro-sharpen to pull back detail after blur
                if (_BlurSharpen > 0.001)
                {
                    float c = m;
                    float2 texel = _MaskTex_TexelSize.xy;
                    float n = sampleMask(_MaskTex, i.uv + float2(0, +texel.y));
                    float s = sampleMask(_MaskTex, i.uv + float2(0, -texel.y));
                    float e = sampleMask(_MaskTex, i.uv + float2(+texel.x, 0));
                    float w = sampleMask(_MaskTex, i.uv + float2(-texel.x, 0));
                    float lap = (n+s+e+w - 4*c);
                    m = saturate(m + _BlurSharpen * lap);
                }

                // 2) Rounded fill & outline bands using smoothstep
                float fill = smoothstep(_Threshold - _Softness, _Threshold + _Softness, m);

                float edgeLo = _Threshold - _OutlineWidth;
                float edgeHi = _Threshold + _OutlineWidth;
                float outlineBand = smoothstep(edgeLo - _OutlineSoft, edgeLo + _OutlineSoft, m)
                                  * (1.0 - smoothstep(edgeHi - _OutlineSoft, edgeHi + _OutlineSoft, m));

                // 3) Pseudo normals from gradient of the *smoothed* mask
                float2 texel = _MaskTex_TexelSize.xy;
                float mx = blurMask(_MaskTex, i.uv + float2(+texel.x, 0), _BlurAmount) - m;
                float my = blurMask(_MaskTex, i.uv + float2(0, +texel.y), _BlurAmount) - m;

                float3 n = normalize(float3(-mx, -my, 1.0)); // face-forward Z
                n.xy *= _NormalStrength;                     // exaggerate bump
                n = normalize(n);

                float2 L2 = normalize(_LightDir.xy);
                float ndl = saturate(dot(normalize(n.xy), L2));

                // 4) Compose lighting: ambient + lambert + rim
                float lambert = (_Ambient + _ShadeStrength * ndl);
                float rim = pow(1.0 - fill, _RimPower) * _RimStrength;

                float3 col = _Color.rgb * lambert;
                col += _RimColor.rgb * rim;

                // Outline on top
                col = lerp(col, _OutlineColor.rgb, outlineBand);

                float alpha = saturate(fill + outlineBand * _Color.a);
                return float4(col, alpha * _Color.a);
            }
            ENDCG
        }
    }
}