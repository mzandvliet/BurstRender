Shader "Custom/PaintStroke" {
	Properties 	{
		_MainTex("Texture", 2D) = "white" {}
	}

	SubShader{
	Tags{ "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }

	ZWrite Off
	// BlendOp Add// Max // Max Add , LogicalSet is funky
	// Blend SrcAlpha OneMinusSrcAlpha

	Pass{
		CGPROGRAM
		#include "UnityCG.cginc"
		#pragma target 5.0
		#pragma vertex vertex_shader
		#pragma fragment fragment_shader

		sampler2D _MainTex;
		float4 _MainTex_ST;
		uniform fixed4 _LightColor0;


		struct Vertex {
			float3 vertex;
			float3 normal;
			float2 uv;
			float3 color;
		};

		StructuredBuffer<Vertex> verts;

		struct v2f {
			float4 pos : SV_POSITION;
			float4 col : COLOR;
			float2 uv : TEXCOORD0;
		};

		v2f vertex_shader(uint id : SV_VertexID, uint inst : SV_InstanceID)
		{
			v2f o;
			Vertex v = verts[id];
			float4 vertex_position = float4(v.vertex, 1.0f);
			float4 vertex_normal = float4(v.normal, 1.0f);
			o.pos = mul(UNITY_MATRIX_VP, vertex_position);
			o.uv = TRANSFORM_TEX(v.uv, _MainTex);

			// float3 normalDirection = normalize(vertex_normal.xyz);
			// float4 AmbientLight = UNITY_LIGHTMODEL_AMBIENT;
			// float4 LightDirection = normalize(_WorldSpaceLightPos0);
			// float4 DiffuseLight = saturate(dot(LightDirection, normalDirection))*_LightColor0;
			// o.col = float4(AmbientLight + DiffuseLight);

			o.col = float4(v.color, 1);
			return o;
		}

		fixed4 fragment_shader(v2f i) : SV_Target
		{
			fixed4 final = tex2D(_MainTex, i.uv);
			final *= i.col;
			return final;
		}

			ENDCG
		}
	}
}