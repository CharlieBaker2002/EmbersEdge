Shader "EmbersEdge/ForceField2D"
{
    // The Force Field wall as a PENTAGONAL, light-bending barrier. Like EdgeWaterfall2D it samples the
    // already-rendered scene (_CameraSortingLayerTexture) and pushes the sample along the surface normal,
    // so the world behind the wall genuinely refracts. Rendered on a custom capsule ribbon mesh
    // (EnergyWall.cs): uv.x = WORLD arc-length along the wall, uv.y = 0..1 across its thickness, normals
    // = the in-plane outward direction.
    //
    //   • Pentagon cross-section: a flat top facet + two angled shoulders + two base sides (5 sides),
    //     with bright crease lines at the facet breaks. The refraction direction is faceted (≈0 on the
    //     flat top, bending outward on the shoulders/sides).
    //   • Waterfall copies: the refracted scene scrolls INWARD toward the centre line as crossfading
    //     copies (up below, down above) that loop seamlessly.
    //   • Flowing Perlin: domain-warped gradient-noise fbm, animated and warped to the pentagon's height
    //     profile, distorts the refraction AND is drawn as moving contour edge lines — a live field.
    // _Color/_Color2 + _Intensity are driven per-wall from health (translucent->bright, constant hue);
    // _Hit0.._Hit3 paint white pulses where it is struck.
    Properties
    {
        _Color      ("Tint (bright)", Color) = (0.78, 1.0, 0.95, 1)
        _Color2     ("Tint (deep)",   Color) = (0.40, 0.62, 0.60, 1)
        _Intensity  ("Intensity", Range(0, 2)) = 1
        _WallLen    ("Wall length (world)", Float) = 2.4
        _Thick      ("Thickness (world)", Float) = 0.8
        _Aspect     ("Aspect (w/h)", Float) = 1.7777
        _Strength   ("Refraction strength (screen frac)", Float) = 0.05
        _Chroma     ("Chromatic aberration", Float) = 0.35
        _Stripes    ("Refraction copies per half", Float) = 2
        _Speed      ("Copy scroll speed", Float) = 0.7
        _SpeedVar   ("Copy waviness along length", Range(0,1)) = 0.4
        _Mag        ("Edge pile-up of copies", Float) = 1.2
        _RimWidth   ("Base-edge rim start", Range(0,1)) = 0.5
        _RidgeWidth ("Crease line width", Range(0.02,1)) = 0.06
        _NoiseScale ("Noise scale", Float) = 3
        _NoiseAmt   ("Noise brightness amount", Range(0,1)) = 0.3
        _NoiseSpeed ("Noise flow speed", Float) = 0.5
        _NoiseWarp  ("Noise refraction warp", Float) = 0.9
        _NoiseLines ("Noise edge line count", Float) = 6
        _EdgeAmt    ("Noise edge brightness", Range(0,2)) = 0.7
        _Hit0       ("Hit 0 (u, strength)", Vector) = (0,0,0,0)
        _Hit1       ("Hit 1 (u, strength)", Vector) = (0,0,0,0)
        _Hit2       ("Hit 2 (u, strength)", Vector) = (0,0,0,0)
        _Hit3       ("Hit 3 (u, strength)", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_CameraSortingLayerTexture);
            SAMPLER(sampler_CameraSortingLayerTexture);
            #define SAMP(uv) SAMPLE_TEXTURE2D_X(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, uv)

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Color2;
                float _Intensity;
                float _WallLen;
                float _Thick;
                float _Aspect;
                float _Strength;
                float _Chroma;
                float _Stripes;
                float _Speed;
                float _SpeedVar;
                float _Mag;
                float _RimWidth;
                float _RidgeWidth;
                float _NoiseScale;
                float _NoiseAmt;
                float _NoiseSpeed;
                float _NoiseWarp;
                float _NoiseLines;
                float _EdgeAmt;
                float4 _Hit0;
                float4 _Hit1;
                float4 _Hit2;
                float4 _Hit3;
            CBUFFER_END

            #define PI  3.14159265
            #define TAU 6.2831853

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;     // in-plane outward (across-the-wall) direction
                float2 uv         : TEXCOORD0;  // x = world arc length along, y = 0..1 across thickness
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float2 nWorld     : TEXCOORD2;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = vp.positionCS;
                OUT.screenUV   = vp.positionNDC.xy / vp.positionNDC.w;
                OUT.uv         = IN.uv;
                OUT.nWorld     = TransformObjectToWorldNormal(IN.normalOS).xy;
                return OUT;
            }

            float HitWhite (float4 h, float u)
            {
                float d = u - h.x;
                return h.y * exp(-d * d * 64.0);
            }

            // ---- gradient (Perlin) noise + fbm + a tiny domain-warp helper ----
            float2 hash22 (float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453) * 2.0 - 1.0;     // gradients in [-1,1]
            }
            float perlin (float2 p)
            {
                float2 ip = floor(p);
                float2 fp = frac(p);
                float2 u = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0);   // quintic
                float a = dot(hash22(ip + float2(0, 0)), fp - float2(0, 0));
                float b = dot(hash22(ip + float2(1, 0)), fp - float2(1, 0));
                float c = dot(hash22(ip + float2(0, 1)), fp - float2(0, 1));
                float d = dot(hash22(ip + float2(1, 1)), fp - float2(1, 1));
                float v = lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
                return v * 0.5 + 0.5;                              // ~0..1
            }
            float fbm (float2 p)
            {
                float v = 0.0, amp = 0.5;
                [unroll] for (int k = 0; k < 3; k++) { v += amp * perlin(p); p = p * 2.03 + 19.1; amp *= 0.5; }
                return v;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float alongW = IN.uv.x;                 // world arc length (negative / >len in the caps)
                float vac    = IN.uv.y;                 // 0..1 across the thickness
                float s      = vac * 2.0 - 1.0;         // -1..1, signed across position (0 = the centre)
                float aedge  = abs(s);
                float halfT  = max(_Thick * 0.5, 1e-3);
                float acrossW= (vac - 0.5) * _Thick;
                float u01    = (_WallLen > 1e-4) ? alongW / _WallLen : 0.0;

                // ---- rounded-capsule silhouette via SDF (continuous, anti-aliased) ----
                float ax   = clamp(alongW, 0.0, _WallLen);
                float dist = length(float2(alongW - ax, acrossW)) - halfT;   // <0 inside
                float aa   = fwidth(dist) + 1e-4;
                float mask = 1.0 - smoothstep(-aa, 0.0, dist);

                // ---- PENTAGON cross-section: flat top + 2 shoulders + 2 base sides ----
                const float aTop  = 0.34;               // half-width of the flat top facet
                const float aSide = 0.80;               // shoulder -> base-side break
                float e = 0.05;
                float topF   = 1.0 - smoothstep(aTop - e, aTop + e, aedge);
                float sideF  = smoothstep(aSide - e, aSide + e, aedge);
                float shoulF = saturate(1.0 - topF - sideF);
                float crease = saturate(exp(-pow((aedge - aTop)  / max(_RidgeWidth, 0.02), 2.0))
                                      + exp(-pow((aedge - aSide) / max(_RidgeWidth, 0.02), 2.0)));
                // pentagon height profile (1 flat top -> ~0 at the edges): warps the noise to the shape.
                float hgt = topF
                          + shoulF * (1.0 - 0.55 * saturate((aedge - aTop) / (aSide - aTop)))
                          + sideF  * (0.45 * saturate((1.0 - aedge) / (1.0 - aSide)));

                // ---- flowing Perlin: domain-warped fbm, animated, sampled in pentagon-height space ----
                float2 nc = float2(alongW * _NoiseScale, hgt * _NoiseScale * 1.5);
                float2 warp = float2(fbm(nc + float2(0.0, _Time.y * _NoiseSpeed)),
                                     fbm(nc + float2(4.3, -_Time.y * _NoiseSpeed * 0.8)));
                float n = fbm(nc + 1.4 * warp + float2(_Time.y * _NoiseSpeed * 0.5, 0.0));
                // flowing Perlin EDGES: anti-aliased contour lines of the warped noise drawn ONTO the
                // wall. They move (n animates) and bend with the pentagon (n is sampled in facet-height
                // space + domain-warped), echoing how the refracted reflections distort across the shape.
                float nl    = n * _NoiseLines;
                float nw    = fwidth(nl) + 1e-4;
                float nf    = frac(nl);
                float edges = 1.0 - smoothstep(0.0, nw * 1.5, min(nf, 1.0 - nf));

                // ---- faceted refraction: copies scroll INWARD to the centre, loop seamlessly ----
                float face    = sign(s) * (shoulF * 0.7 + sideF * 1.0 + topF * 0.12);
                float edgeMag = 1.0 + _Mag * aedge;
                float ph = aedge * _Stripes + _Time.y * _Speed + _SpeedVar * 0.5 * sin(alongW * 0.7);
                float pA = frac(ph);
                float pB = frac(ph + 0.5);
                float wA = 1.0 - abs(2.0 * pA - 1.0);             // crossfade so the loop reset is invisible
                float wB = 1.0 - abs(2.0 * pB - 1.0);
                float wsum = max(wA + wB, 1e-4);

                float2 nrm = normalize(float2(IN.nWorld.x / _Aspect, IN.nWorld.y) + 1e-6);
                float2 patWarp = (warp - 0.5) * _Strength * _NoiseWarp * 2.0;   // flowing perlin distortion
                float2 offA = nrm * face * (_Strength * edgeMag * pA) + patWarp;
                float2 offB = nrm * face * (_Strength * edgeMag * pB) + patWarp;
                float2 ca   = nrm * face * _Strength * _Chroma * (0.35 + 0.65 * aedge);
                float2 sUV  = IN.screenUV;

                half3 colA = half3(SAMP(sUV + offA + ca).r, SAMP(sUV + offA).g, SAMP(sUV + offA - ca).b);
                half3 colB = half3(SAMP(sUV + offB + ca).r, SAMP(sUV + offB).g, SAMP(sUV + offB - ca).b);
                half3 scene = (colA * wA + colB * wB) / wsum;

                // ---- shading: facet creases + base rim + flowing Perlin edge lines (kept tame) ----
                float rim    = smoothstep(_RimWidth, 1.0, aedge);
                float capRim = 1.0 - smoothstep(-halfT * 0.9, -halfT * 0.15, dist);
                rim = saturate(max(rim, capRim));
                float shimmer = (0.9 + 0.1 * sin(_Time.y * 1.5 + alongW * 0.5)) * (1.0 + _NoiseAmt * (n - 0.5) * 2.0);

                half3 tint = lerp(_Color2.rgb, _Color.rgb, saturate(crease * 0.8 + rim * 0.5 + edges * _EdgeAmt * 0.5));
                half3 col  = scene * lerp(0.96, 1.05, topF);                   // flat top reads as a clear pane
                col += tint * (crease * 0.5 + rim * 0.4 + shoulF * 0.18 + edges * _EdgeAmt) * _Intensity * shimmer;

                // ---- white pulses where the wall was struck ----
                float white = HitWhite(_Hit0, u01) + HitWhite(_Hit1, u01)
                            + HitWhite(_Hit2, u01) + HitWhite(_Hit3, u01);
                white = saturate(white);
                col = lerp(col, half3(1.0, 1.0, 1.0), white * (0.6 + 0.4 * rim));
                col += white * (0.5 + rim);

                float a = mask * saturate(0.2 + rim * 0.7 + crease * 0.4 + shoulF * 0.15 + topF * 0.1
                                          + edges * _EdgeAmt * 0.6 + white) * _Intensity;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
