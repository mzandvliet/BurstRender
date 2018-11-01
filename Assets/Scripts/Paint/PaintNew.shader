Shader "Custom/PaintStrokeMesh" {
	Properties 	{
		_MainTex("Texture", 2D) = "white" {}
	}

    SubShader {
		Tags{ "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
		ZWrite Off
		Blend SrcAlpha OneMinusSrcAlpha
		// Blend OneMinusDstAlpha DstAlpha

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

			sampler2D _MainTex;
			float4 _MainTex_ST;

			struct appdata {
				float4 vertex : POSITION;
				fixed4 color : COLOR;
				float4 texcoord : TEXCOORD1;
			};
         
            struct v2f {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
				float2 uv : TEXCOORD0;
            };
            
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
				o.color = v.color;
                o.color.w = 1.0;
				o.uv = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { 
				fixed4 final = tex2D(_MainTex, i.uv);
				final *= i.color;
				return final;
			 }
            ENDCG
        }
    } 
}