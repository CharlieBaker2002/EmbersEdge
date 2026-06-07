Shader "Hidden/Shockwave2D"
{
    // A travelling-ring screen distortion for the URP 2D Renderer.
    // It samples the scene that has already been rendered into _CameraSortingLayerTexture
    // (everything up to the renderer's "Foremost Sorting Layer") and re-displays it with a
    // UV push that rolls outward from _Center. Driven entirely by the Shockwave.cs component.
    Properties
    {
        [HideInInspector] _MainTex ("Sprite Texture", 2D) = "white" {}   // unused; sprite is just geometry
        _Strength   ("Push Strength (screen frac)", Float) = 0.04
        _Radius     ("Radius (screen frac)",        Float) = 0.0
        _RingWidth  ("Ring Width (screen frac)",    Float) = 0.05
        _Center     ("Center (viewport xy)",        Vector) = (0.5, 0.5, 0, 0)
        _Aspect     ("Aspect (w/h)",                Float) = 1.7777
        _Chroma     ("Chromatic Aberration",        Float) = 0.4
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

            // The 2D renderer's copy of the scene colour (layers <= Foremost Sorting Layer).
            TEXTURE2D_X(_CameraSortingLayerTexture);
            SAMPLER(sampler_CameraSortingLayerTexture);

            CBUFFER_START(UnityPerMaterial)
                float  _Strength;
                float  _Radius;
                float  _RingWidth;
                float4 _Center;
                float  _Aspect;
                float  _Chroma;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV   : TEXCOORD0;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = vp.positionCS;
                OUT.screenUV   = vp.positionNDC.xy / vp.positionNDC.w;   // 0..1 screen UV
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv = IN.screenUV;

                // Aspect-correct vector from the blast centre so the ring stays circular.
                float2 dir  = uv - _Center.xy;
                float  dist = length(float2(dir.x * _Aspect, dir.y));

                // Travelling crest: 1 on the ring, smoothly 0 at +/- RingWidth. Squared for a softer falloff.
                float ring = 1.0 - smoothstep(0.0, _RingWidth, abs(dist - _Radius));
                ring *= ring;

                // Push the screen sample outward along the crest.
                float2 n    = (dist > 1e-5) ? dir / dist : float2(0.0, 0.0);
                float2 push = n * ring * _Strength;

                // Split the channels slightly for a chromatic-aberration edge.
                float2 ca = push * _Chroma;
                half r = SAMPLE_TEXTURE2D_X(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, uv + push + ca).r;
                half g = SAMPLE_TEXTURE2D_X(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, uv + push).g;
                half b = SAMPLE_TEXTURE2D_X(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, uv + push - ca).b;

                // Alpha = crest mask, so only the ring blends in; the rest stays untouched.
                return half4(r, g, b, saturate(ring));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
