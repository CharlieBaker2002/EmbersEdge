Shader "EmbersEdge/ForceField2D"
{
    // The Force Field wall as a TRIANGULAR-PRISM, light-bending barrier. Like EdgeWaterfall2D it samples
    // the already-rendered scene (_CameraSortingLayerTexture) and pushes the sample along the surface
    // normal, so the world behind the wall genuinely refracts. Rendered on a custom capsule ribbon mesh
    // (EnergyWall.cs): uv.x = WORLD arc-length along the wall, uv.y = 0..1 across its thickness, normals
    // = the in-plane outward direction.
    //
    //   • Prism cross-section: a sharp central RIDGE with two flat faces (not a round dome). The
    //     refraction direction flips at the ridge so each face bends the scene outward.
    //   • Waterfall copies: the refracted scene scrolls across the width as several crossfading copies
    //     that loop seamlessly (multiple images animate through, even over static scenery).
    //   • Surface noise: flowing fbm breaks up the faces so it reads as a live energy field.
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
        _Stripes    ("Refraction copies across width", Float) = 4
        _Speed      ("Copy scroll speed", Float) = 0.7
        _SpeedVar   ("Copy waviness along length", Range(0,1)) = 0.4
        _Mag        ("Edge pile-up of copies", Float) = 1.2
        _RimWidth   ("Base-edge rim start", Range(0,1)) = 0.5
        _RidgeWidth ("Apex ridge width", Range(0.02,1)) = 0.16
        _NoiseScale ("Noise scale", Float) = 3
        _NoiseAmt   ("Noise amount", Range(0,1)) = 0.25
        _NoiseSpeed ("Noise flow speed", Float) = 0.6
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

            // ---- value-noise fbm ----
            float hash21 (float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }
            float vnoise (float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }
            float fbm (float2 p)
            {
                float v = 0.0, amp = 0.5;
                [unroll] for (int k = 0; k < 3; k++) { v += amp * vnoise(p); p *= 2.0; amp *= 0.5; }
                return v;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float alongW = IN.uv.x;                 // world arc length (negative / >len in the caps)
                float vac    = IN.uv.y;                 // 0..1 across the thickness
                float s      = vac * 2.0 - 1.0;         // -1..1, signed across position (0 = the ridge)
                float aedge  = abs(s);
                float halfT  = max(_Thick * 0.5, 1e-3);
                float acrossW= (vac - 0.5) * _Thick;
                float u01    = (_WallLen > 1e-4) ? alongW / _WallLen : 0.0;

                // ---- rounded-capsule silhouette via SDF (continuous, anti-aliased) ----
                float ax   = clamp(alongW, 0.0, _WallLen);
                float dist = length(float2(alongW - ax, acrossW)) - halfT;   // <0 inside
                float aa   = fwidth(dist) + 1e-4;
                float mask = 1.0 - smoothstep(-aa, 0.0, dist);

                // ---- flowing surface noise ----
                float n = fbm(float2(alongW * _NoiseScale, vac * _NoiseScale * 2.0 + _Time.y * _NoiseSpeed));

                // ---- waterfall-style refraction: copies scroll INWARD to the ridge and loop seamlessly ----
                // Phase runs on aedge (distance from the centre line), so copies travel toward the centre
                // on both halves — up if you're below it, down if you're above it. The bend direction
                // still flips at the ridge so each prism FACE refracts outward.
                float face    = clamp(s / 0.05, -1.0, 1.0);          // ~sign(s), with a tiny soft apex
                float edgeMag = 1.0 + _Mag * aedge;                  // copies pile up toward the base edges
                float ph = aedge * _Stripes + _Time.y * _Speed + _SpeedVar * 0.5 * sin(alongW * 0.7);
                float pA = frac(ph);
                float pB = frac(ph + 0.5);
                float wA = 1.0 - abs(2.0 * pA - 1.0);                // crossfade so the loop reset is invisible
                float wB = 1.0 - abs(2.0 * pB - 1.0);
                float wsum = max(wA + wB, 1e-4);

                float2 nrm = normalize(float2(IN.nWorld.x / _Aspect, IN.nWorld.y) + 1e-6);
                float jitter = _Strength * 0.25 * (n - 0.5);         // noise wobble on the bend
                float ampA = _Strength * edgeMag * pA + jitter;
                float ampB = _Strength * edgeMag * pB + jitter;
                float2 offA = nrm * face * ampA;
                float2 offB = nrm * face * ampB;
                float2 ca   = nrm * face * _Strength * _Chroma * (0.35 + 0.65 * aedge);
                float2 sUV  = IN.screenUV;

                half3 colA = half3(SAMP(sUV + offA + ca).r, SAMP(sUV + offA).g, SAMP(sUV + offA - ca).b);
                half3 colB = half3(SAMP(sUV + offB + ca).r, SAMP(sUV + offB).g, SAMP(sUV + offB - ca).b);
                half3 scene = (colA * wA + colB * wB) / wsum;

                // ---- triangular-prism shading: sharp apex ridge + two flat faces + base rim ----
                float ridge = exp(-pow(s / max(_RidgeWidth, 0.02), 2.0));      // bright apex line (the ridge)
                float faceTone = 0.4 + 0.35 * smoothstep(-0.2, 0.2, s);        // two flat faces, one lit brighter
                float rim    = smoothstep(_RimWidth, 1.0, aedge);
                float capRim = 1.0 - smoothstep(-halfT * 0.9, -halfT * 0.15, dist);
                rim = saturate(max(rim, capRim));
                float shimmer = (0.9 + 0.1 * sin(_Time.y * 1.5 + alongW * 0.5)) * (1.0 + _NoiseAmt * (n - 0.5));

                half3 tint = lerp(_Color2.rgb, _Color.rgb, saturate(ridge * 0.9 + rim * 0.6 + faceTone * 0.2));
                half3 col  = scene * lerp(0.95, 1.08, ridge);
                col += tint * (ridge * 0.7 + rim * 0.5 + faceTone * 0.2) * _Intensity * shimmer;

                // ---- white pulses where the wall was struck ----
                float white = HitWhite(_Hit0, u01) + HitWhite(_Hit1, u01)
                            + HitWhite(_Hit2, u01) + HitWhite(_Hit3, u01);
                white = saturate(white);
                col = lerp(col, half3(1.0, 1.0, 1.0), white * (0.6 + 0.4 * rim));
                col += white * (0.5 + rim);

                float a = mask * saturate(0.2 + rim * 0.8 + ridge * 0.4 + faceTone * 0.15 + white) * _Intensity;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
