Shader "Hidden/Shockwave2D_Boundary"
{
    // A constant screen-distortion band shaped like the map's outline.
    // Driven by MapBoundaryDistortion.cs, which builds a ribbon mesh straddling the boundary
    // spline (uv.y 0..1 across the band, the edge line at uv.y = 0.5) and passes the outward
    // normal per vertex. The map's SHAPE is static, so all the life lives here: several
    // phase-offset sine waves travel around the rim, the crest breathes across the band, and the
    // R/G/B chromatic offsets are each phase-shifted by 2π/3 so the colour split shimmers.
    // Samples _CameraSortingLayerTexture (URP 2D Renderer's scene copy), same as Shockwave2D.
    Properties
    {
        _Strength ("Strength (screen frac)",    Float) = 0.025
        _Aspect   ("Aspect (w/h)",              Float) = 1.7777
        _Waves    ("Shimmer waves around edge", Float) = 24
        _Speed    ("Shimmer speed",             Float) = 2
        _Chroma   ("Chromatic aberration",      Float) = 0.45
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
                float _Waves;
                float _Speed;
                float _Chroma;
            CBUFFER_END

            #define TAU 6.2831853
            #define THIRD 2.0943951    // 2π/3
            #define TWOTHIRD 4.1887902 // 4π/3

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;     // outward edge normal (object space)
                float2 uv         : TEXCOORD0;  // x = around the rim, y = across the band
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float2 nWorld     : TEXCOORD2;  // outward normal in world xy
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
                float u = IN.uv.x;
                float t = _Time.y * _Speed;

                // Snap every spatial frequency to a whole number of waves so the pattern closes
                // seamlessly at the u=0/1 seam — non-integer counts (e.g. _Waves*1.7) left a
                // phase break exactly where the spline starts. Time drifts stay non-integer, so
                // it still never visibly repeats.
                float wA = round(_Waves);
                float wB = round(_Waves * 0.5);
                float wC = round(_Waves * 1.7);
                float wD = max(1.0, round(_Waves * 0.33));

                // Living amplitude: layered, phase-offset travelling waves around the rim. Different
                // spatial freqs + drift speeds + phases means it never visibly repeats.
                float shimmer = 0.55
                              + 0.30 * sin(u * TAU * wA - t)
                              + 0.20 * sin(u * TAU * wB + t * 0.73 + THIRD)
                              + 0.15 * sin(u * TAU * wC - t * 1.31 + TWOTHIRD);
                shimmer = max(shimmer, 0.0);

                // Breathing membrane: the crest drifts across the band so the edge ripples in/out.
                float centre = 0.5 + 0.16 * sin(u * TAU * wD + t * 0.5);
                float prof   = saturate(cos(clamp((IN.uv.y - centre) / 0.5, -1.0, 1.0) * 1.5707963));

                float amp = _Strength * prof * shimmer;

                // World normal -> aspect-correct screen direction.
                float2 dir = normalize(float2(IN.nWorld.x / _Aspect, IN.nWorld.y) + 1e-6);

                // Chromatic aberration: each channel pushed by a 2π/3 phase-shifted amount, so the
                // colour split itself pulses and breathes — alive even though the shape is fixed.
                float oR = amp * (1.0 + _Chroma * sin(u * TAU * wA + t));
                float oG = amp * (1.0 + _Chroma * sin(u * TAU * wA + t + THIRD));
                float oB = amp * (1.0 + _Chroma * sin(u * TAU * wA + t + TWOTHIRD));

                float2 uv = IN.screenUV;
                half r = SAMPLE_TEXTURE2D_X(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, uv + dir * oR).r;
                half g = SAMPLE_TEXTURE2D_X(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, uv + dir * oG).g;
                half b = SAMPLE_TEXTURE2D_X(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, uv + dir * oB).b;

                return half4(r, g, b, saturate(prof * (0.5 + 0.5 * shimmer)));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
