Shader "Custom/ClearCanvas"
{
     Properties
     {
     }
 
     CGINCLUDE
     #include "UnityCG.cginc"
 
     struct v2f
     {
          float4 pos : SV_POSITION;
          float2 uv : TEXCOORD0;
     };
 
     v2f vert( appdata_img v )
     {
          v2f o = (v2f)0;
          o.pos = UnityObjectToClipPos(v.vertex);
          o.uv = v.texcoord;
 
          return o;
     }
 
     float4 frag(v2f input) : SV_Target
     {
        float4 result = float4(0.9,0.8,0.9,1);
        // float4 result = float4(0.7,0.9,0.99,1);
        return result;
     }
 
     ENDCG
     SubShader
     {
          Pass
          {
               ZTest Always Cull Off ZWrite Off
 
               CGPROGRAM
               #pragma vertex vert
               #pragma fragment frag
               ENDCG
          }
     }
     Fallback off
}