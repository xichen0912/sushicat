// Gradient skybox for the city's day → dusk transition: four colour bands, a sun glow, a cratered moon and
// faint stars, all drawn in the sky itself (never fogged or hidden behind the city). Values are animated at
// runtime by CityCelebration on a material instance.
Shader "CityGen/Gradient Sky"
{
    Properties
    {
        _TopColor ("Zenith", Color) = (0.32, 0.52, 0.88, 1)
        _MidColor ("Mid sky", Color) = (0.50, 0.68, 0.93, 1)
        _HorizonColor ("Horizon", Color) = (0.78, 0.86, 0.96, 1)
        _BottomColor ("Below horizon", Color) = (0.55, 0.53, 0.50, 1)
        _SunDir ("Sun direction (towards the sun)", Vector) = (0.5, 0.6, -0.6, 0)
        _SunColor ("Sun glow", Color) = (1, 0.9, 0.75, 1)
        _MoonDir ("Moon direction", Vector) = (-0.4, 0.45, 0.8, 0)
        _MoonColor ("Moon", Color) = (0.95, 0.95, 1, 1)
        _MoonSize ("Moon radius (radians)", Float) = 0.09
        _MoonAlpha ("Moon visibility", Range(0, 1)) = 0
        _StarAlpha ("Star visibility", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _TopColor, _MidColor, _HorizonColor, _BottomColor, _SunColor, _MoonColor;
            float4 _SunDir, _MoonDir;
            float _MoonSize, _MoonAlpha, _StarAlpha;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash (float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float3 col;
                if (h >= 0)
                {
                    float a = saturate(h / 0.18);            // horizon → mid
                    float b = saturate((h - 0.18) / 0.62);  // mid → zenith
                    col = lerp(lerp(_HorizonColor.rgb, _MidColor.rgb, smoothstep(0, 1, a)), _TopColor.rgb, smoothstep(0, 1, b));
                }
                else col = lerp(_HorizonColor.rgb, _BottomColor.rgb, saturate(-h * 6));

                // Sun: a broad glow near the horizon plus a small bright disc.
                float s = saturate(dot(d, normalize(_SunDir.xyz)));
                col += _SunColor.rgb * (pow(s, 6) * 0.35 + pow(s, 900) * 6);

                // Moon: a disc with soft crater shading and a halo.
                float3 md = normalize(_MoonDir.xyz);
                float m = dot(d, md);
                float edge = cos(_MoonSize);
                float disc = smoothstep(edge, edge + 0.0006, m);
                float3 local = (d - md) / max(_MoonSize, 1e-4);
                float craters = 0.82 + 0.18 * hash(floor(local * 7.0 + 31.0)) - 0.08 * step(0.75, hash(floor(local * 3.0 + 11.0)));
                col = lerp(col, _MoonColor.rgb * craters, disc * _MoonAlpha);
                col += _MoonColor.rgb * pow(saturate(m), 400) * 0.25 * _MoonAlpha;

                // Faint stars high in the dusk sky.
                float star = step(0.9975, hash(floor(d * 260.0))) * saturate((h - 0.25) * 3);
                col += star * _StarAlpha;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
