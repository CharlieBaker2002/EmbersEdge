Shader "UI/RadialHighlight"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint Color", Color) = (1,1,1,1)
        _Angle ("Highlight Angle", Range(0, 360)) = 90
        _Spread ("Highlight Spread", Range(0, 180)) = 45
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _Angle;
            float _Spread;

            v2f vert(appdata_t v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color; // Multiply by material color
                return o;
            }

            fixed4 frag(v2f i) : SV_Target {
                fixed4 texColor = tex2D(_MainTex, i.uv) * i.color;
                
                // Center the UV coordinates
                float2 centeredUV = (i.uv - 0.5) * 2.0;
                
                // Calculate distance from center
                float radius = length(centeredUV);
                
                // Calculate angle
                float angle = atan2(centeredUV.y, centeredUV.x) * (180.0 / 3.14159265);
                if (angle < 0) angle += 360;

                // Calculate highlight start and end angles
                float highlightStart = _Angle - _Spread * 0.5;
                float highlightEnd = _Angle + _Spread * 0.5;
                
                // Normalize angles to handle wrap-around
                bool inAngleRange;
                if (highlightStart > highlightEnd) {
                    inAngleRange = (angle >= highlightStart || angle <= highlightEnd);
                } else {
                    inAngleRange = (angle >= highlightStart && angle <= highlightEnd);
                }

                // Only render within the radius of 1 (full circle)
                if (radius <= 1.0 && inAngleRange) {
                    return texColor;
                } else {
                    texColor.a = 0; // Make transparent outside highlight
                    return texColor;
                }
            }
            ENDCG
        }
    }
}