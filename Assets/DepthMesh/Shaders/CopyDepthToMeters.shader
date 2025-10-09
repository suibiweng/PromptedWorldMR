// Assets/DepthMesh/Shaders/CopyDepthToMeters.shader
Shader "Hidden/CopyDepthToMeters"
{
    Properties { _MetersMul ("Meters Multiplier", Float) = 0.001 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _MetersMul;

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            float4 frag (v2f i) : SV_Target
            {
                float d = tex2D(_MainTex, i.uv).r * _MetersMul;
                return float4(d, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
