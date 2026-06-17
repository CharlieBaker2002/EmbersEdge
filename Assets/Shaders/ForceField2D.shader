Shader "EmbersEdge/ForceField2D"
{
    // The Force Field wall, rendered on a LineRenderer ribbon (texture mode Stretch:
    // uv.x = 0..1 along the wall, uv.y = 0..1 across its width). Additive, self-contained
    // (no scene grab). Visual language borrows from EdgeWaterfall2D: anti-aliased strands
    // with a looping sin(saw*pi) cascade profile pouring across the ribbon, plus a faint
    // core line and a slow shimmer. _WallLen keeps strand density constant in WORLD units,
    // so stretching the wall spreads the same energy thinner; _Intensity is driven per-wall
    // (charge level, stretch, re-weave flicker) via MaterialPropertyBlock.
    Properties
    {
        _Color          ("Tint (bright)", Color) = (0.765, 1.0, 0.941, 1)
        _Color2         ("Tint (deep)",   Color) = (0.486, 0.635, 0.596, 1)
        _Intensity      ("Intensity", Range(0, 2)) = 1
        _WallLen        ("Wall length (world units)", Float) = 2.4
        _StrandsPerUnit ("Strands per world unit", Float) = 7
        _StrandWidth    ("Strand width", Range(0,1)) = 0.55
        _Stripes        ("Loops across the field", Float) = 2
        _Speed          ("Flow speed", Float) = 1.1
        _SpeedVar       ("Flow speed variance", Range(0,1)) = 0.5
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
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Color2;
                float _Intensity;
                float _WallLen;
                float _StrandsPerUnit;
                float _StrandWidth;
                float _Stripes;
                float _Speed;
                float _SpeedVar;
            CBUFFER_END

            #define PI  3.14159265
            #define TAU 6.2831853

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float u = IN.uv.x * _WallLen * _StrandsPerUnit; // strand coord, constant world density
                float v = IN.uv.y;                              // 0..1 across the ribbon

                // Anti-aliased strands from a continuous wave (same trick as the waterfall).
                float wave   = cos(u * TAU);
                float aa     = fwidth(wave) + 1e-4;
                float thresh = 1.0 - saturate(_StrandWidth) * 2.0;
                float strand = smoothstep(thresh - aa, thresh + aa, wave);

                // Smooth per-strand variance, coherent between neighbours.
                float o = sin(u * 0.37) * 0.5 + sin(u * 0.11) * 0.5;

                // Looping cascade pouring across the ribbon; sin(saw*pi) returns to zero each loop.
                float p    = v * _Stripes + _Time.y * _Speed * (1.0 + _SpeedVar * o) + o;
                float prof = sin(frac(p) * PI);

                // Soft ribbon edges + a faint constant core line so the wall always reads.
                float edges = smoothstep(0.0, 0.30, v) * (1.0 - smoothstep(0.70, 1.0, v));
                float core  = exp(-pow((v - 0.5) * 5.0, 2.0)) * 0.45;

                // Slow shimmer travelling along the wall.
                float shimmer = 0.85 + 0.15 * sin(_Time.y * 3.0 + IN.uv.x * _WallLen * 2.0);

                float body = saturate(strand * prof + core) * edges;
                float a    = body * _Intensity * shimmer;
                half3 col  = lerp(_Color2.rgb, _Color.rgb, saturate(strand * prof)) * a;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
