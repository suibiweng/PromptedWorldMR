Shader "Hidden/QuadHomographyWarp"
{
    Properties { _MainTex ("MainTex", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _P0; // top-left
            float4 _P1; // top-right
            float4 _P2; // bottom-right
            float4 _P3; // bottom-left

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f     { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            float2 mapBilinear(float2 uv)
            {
                float2 top = lerp(_P0.xy, _P1.xy, uv.x);
                float2 bot = lerp(_P3.xy, _P2.xy, uv.x);
                return lerp(top, bot, uv.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 srcUV = mapBilinear(i.uv);
                return tex2D(_MainTex, srcUV);
            }
            ENDCG
        }
    }
}
