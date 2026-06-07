Shader "Hidden/EdgeWaterfall2D"
{
    // "Edge of the world" waterfall, done as SCREEN DISTORTION (like Shockwave2D): it samples the
    // already-rendered scene (_CameraSortingLayerTexture) and pushes the sample along the boundary's
    // outward normal by a LOOPING profile that keeps returning to 0 — sin(saw·π) scrolling outward —
    // so the world appears to pour off the edge in repeating cascades.
    //
    // Consistency around curves: uv.x is the rim arc length NORMALISED to 0..1, the strand count is an
    // integer over the whole rim (seamless), strand edges are anti-aliased with fwidth (no aliasing /
    // fragmenting where the boundary curves), and the per-strand variation is smooth (coherent
    // neighbours) rather than a stepped hash. Rendered on a ribbon wrapped around the rim
    // (uv.y = 0 at the lip -> 1 at the bottom). Driven by MapBoundaryWaterfall.cs.
    Properties
    {
        _Strength    ("Strength (screen frac)", Float) = 0.05
        _Aspect      ("Aspect (w/h)", Float) = 1.7777
        _StrandCount ("Strands around the rim", Float) = 200
        _StrandWidth ("Strand width", Range(0,1)) = 0.5
        _Stripes     ("Loops down the fall", Float) = 3
        _Speed       ("Fall speed", Float) = 0.6
        _SpeedVar    ("Fall speed variance", Range(0,1)) = 0.4
        _Chroma      ("Chromatic aberration", Float) = 0.3
        _Mag         ("Magnification (pile-up toward edge)", Float) = 2.5
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

            CBUFFER_START(UnityPerMaterial)
                float _Strength;
                float _Aspect;
                float _StrandCount;
                float _StrandWidth;
                float _Stripes;
                float _Speed;
                float _SpeedVar;
                float _Chroma;
                float _Mag;
            CBUFFER_END

            #define PI  3.14159265
            #define TAU 6.2831853

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;     // outward edge normal (object space)
                float2 uv         : TEXCOORD0;  // x = 0..1 around rim, y = 0..1 across the fall
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

            half4 frag (Varyings IN) : SV_Target
            {
                float u = IN.uv.x;   // 0..1 around the rim
                float v = IN.uv.y;   // 0 = lip, 1 = bottom of the fall

                // Seamless strand coordinate (integer strand count over a 0..1 rim).
                float su = u * _StrandCount;

                // Soft, anti-aliased strands from a continuous wave — no frac/floor aliasing, so the
                // strands stay clean where the boundary curves. Threshold maps _StrandWidth -> duty.
                float wave   = cos(su * TAU);
                float aa     = fwidth(wave) + 1e-4;
                float thresh = 1.0 - saturate(_StrandWidth) * 2.0;
                float strand = smoothstep(thresh - aa, thresh + aa, wave);

                // Fade in at the lip; hold strong toward the end (where the copies pile up), with a soft
                // cap at the very outer edge so the ribbon has no hard cut.
                float env = smoothstep(0.0, 0.12, v) * (1.0 - smoothstep(0.92, 1.0, v));
                float amp = _Strength * strand * env;

                // Warp the fall: nearly linear at the lip (sparse bands -> fast motion), compressing
                // toward the outer edge (many copies piling up). Band speed ~ 1/g'(v), copies ~ g'(v) —
                // the opposite of an event horizon (pile-up at the far edge, quick at the rim).
                float m = max(_Mag, 0.001);
                float fall = (exp(m * v) - 1.0) / (exp(m) - 1.0);

                // One-directional, seamless flow (flow-map style). Two copies of a constant-speed ramp
                // offset by half a cycle; each cross-fade weight peaks in that copy's MIDDLE 50% and
                // reaches 0 at its reset — so the loop never reverses and the reset is never visible.
                // Synchronised around the whole rim (depends only on v + time).
                float p  = fall * _Stripes - _Time.y * _Speed;
                float pA = frac(p);
                float pB = frac(p + 0.5);
                float wA = 1.0 - abs(2.0 * pA - 1.0);
                float wB = 1.0 - abs(2.0 * pB - 1.0);
                float wsum = max(wA + wB, 1e-4);

                // World normal -> aspect-correct screen direction. Sample inward so the inner world is
                // dragged outward, off the edge.
                float2 dir  = normalize(float2(IN.nWorld.x / _Aspect, IN.nWorld.y) + 1e-6);
                float2 offA = -dir * amp * pA;
                float2 offB = -dir * amp * pB;
                float2 ca   = -dir * amp * _Chroma * 0.5;

                float2 sUV = IN.screenUV;
                half3 colA = half3(
                    SAMPLE_TEXTURE2D_X(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, sUV + offA + ca).r,
                    SAMPLE_TEXTURE2D_X(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, sUV + offA).g,
                    SAMPLE_TEXTURE2D_X(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, sUV + offA - ca).b);
                half3 colB = half3(
                    SAMPLE_TEXTURE2D_X(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, sUV + offB + ca).r,
                    SAMPLE_TEXTURE2D_X(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, sUV + offB).g,
                    SAMPLE_TEXTURE2D_X(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, sUV + offB - ca).b);

                half3 rgb = (colA * wA + colB * wB) / wsum;
                float a = saturate(strand * env);
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
