Shader "Custom/SpriteLitBorder"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HDR] _BorderColor ("Border Color", Color) = (1,1,1,1)
        _Thickness ("Border Thickness", Range(0, 0.1)) = 0.01
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex LitBorderVertex
            #pragma fragment LitBorderFragment

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                half4 color         : COLOR;
                half2 lightingUV    : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _BorderColor;
                half _Thickness;
                half4 _RendererColor;
            CBUFFER_END

            Varyings LitBorderVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.color = input.color * _Color * _RendererColor;
                output.lightingUV = half2(ComputeScreenPos(output.positionCS / output.positionCS.w).xy);

                return output;
            }

            half4 LitBorderFragment(Varyings input) : SV_Target
            {
                half4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // Sample neighbors for edge detection
                half texelSize = _Thickness;
                half alphaUp = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + half2(0, texelSize)).a;
                half alphaDown = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - half2(0, texelSize)).a;
                half alphaLeft = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - half2(texelSize, 0)).a;
                half alphaRight = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + half2(texelSize, 0)).a;

                // Diagonal samples for better border
                half alphaUL = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + half2(-texelSize, texelSize)).a;
                half alphaUR = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + half2(texelSize, texelSize)).a;
                half alphaDL = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + half2(-texelSize, -texelSize)).a;
                half alphaDR = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + half2(texelSize, -texelSize)).a;

                half neighborAlpha = max(max(max(alphaUp, alphaDown), max(alphaLeft, alphaRight)),
                                         max(max(alphaUL, alphaUR), max(alphaDL, alphaDR)));

                // Border where neighbor has alpha but current pixel doesn't
                half border = saturate(neighborAlpha - mainTex.a);

                // Combine sprite color with border
                half4 spriteColor = mainTex * input.color;
                half4 borderColor = half4(_BorderColor.rgb, border * _BorderColor.a);

                // Final color: sprite on top, border behind
                half4 finalColor = lerp(borderColor, spriteColor, mainTex.a);
                finalColor.rgb *= finalColor.a;

                // Apply 2D lighting using URP 17 API
                SurfaceData2D surfaceData;
                InputData2D inputData;

                InitializeSurfaceData(finalColor.rgb, finalColor.a, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);

                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        // Unlit fallback pass for when 2D lighting is disabled
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _BorderColor;
                half _Thickness;
                half4 _RendererColor;
            CBUFFER_END

            Varyings UnlitVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color * _Color * _RendererColor;
                return output;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                half4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                half texelSize = _Thickness;
                half alphaUp = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + half2(0, texelSize)).a;
                half alphaDown = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - half2(0, texelSize)).a;
                half alphaLeft = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - half2(texelSize, 0)).a;
                half alphaRight = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + half2(texelSize, 0)).a;
                half alphaUL = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + half2(-texelSize, texelSize)).a;
                half alphaUR = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + half2(texelSize, texelSize)).a;
                half alphaDL = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + half2(-texelSize, -texelSize)).a;
                half alphaDR = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + half2(texelSize, -texelSize)).a;

                half neighborAlpha = max(max(max(alphaUp, alphaDown), max(alphaLeft, alphaRight)),
                                         max(max(alphaUL, alphaUR), max(alphaDL, alphaDR)));

                half border = saturate(neighborAlpha - mainTex.a);

                half4 spriteColor = mainTex * input.color;
                half4 borderColor = half4(_BorderColor.rgb, border * _BorderColor.a);

                half4 finalColor = lerp(borderColor, spriteColor, mainTex.a);
                finalColor.rgb *= finalColor.a;

                return finalColor;
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
