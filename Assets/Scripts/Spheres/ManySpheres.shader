Shader "Custom/ManySpheres" {
	Properties 	{
		_MainTex("Texture", 2D) = "white" {}
	}

	SubShader{
	Tags{ "LightMode" = "ForwardBase" }

	Pass{
		CGPROGRAM
		#include "UnityCG.cginc"
		#pragma target 5.0
		#pragma vertex vertex_shader
		#pragma fragment fragment_shader

		sampler2D _MainTex;
		float4 _MainTex_ST;
		uniform fixed4 _LightColor0;

        StructuredBuffer<float3> spheres;
        StructuredBuffer<uint> indices; // Since Unity doesn't offer DrawProceduralIndexed

		struct v2f {
			float4 pos : SV_POSITION;
			float4 col : COLOR;
			float2 uv : TEXCOORD0;
		};

		v2f vertex_shader(uint id : SV_VertexID, uint inst : SV_InstanceID)
		{
			v2f o;
            // uint sphereId = id / 4;
            // float2 quadCoord = float2(id%2, (id%4)/2) * 2.0 - 1.0;

            // Todo: find quad indexing scheme that doesn't require
            // this mod6 index buffer
            uint idx = indices[id % 6];
            uint sphereId = id / 6;
            float2 quadCoord = float2(idx%2, (idx%4)/2) * 2.0 - 1.0;

            float3 spherePos = spheres[sphereId];

            float4 vertex_position = float4(spherePos.x + quadCoord.x, spherePos.y + quadCoord.y, 0, 1);
            float4 vertex_normal = float4(0,0,-1,1);

			o.pos = mul(UNITY_MATRIX_VP, vertex_position);
			o.uv = TRANSFORM_TEX(quadCoord, _MainTex); // verts[id].uv
			float3 normalDirection = normalize(vertex_normal.xyz);
			float4 AmbientLight = UNITY_LIGHTMODEL_AMBIENT;
			float4 LightDirection = normalize(_WorldSpaceLightPos0);
			float4 DiffuseLight = saturate(dot(LightDirection, normalDirection))*_LightColor0;
			o.col = float4(AmbientLight + DiffuseLight);
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