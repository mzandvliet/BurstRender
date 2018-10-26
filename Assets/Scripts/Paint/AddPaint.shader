Shader "Custom/AddPaint"
{
     Properties
     {
          _MainTex ("", any) = "" {}
          _PaintTex ("", any) = "" {}
     }
 
     CGINCLUDE
     #include "UnityCG.cginc"
 
     struct v2f
     {
          float4 pos : SV_POSITION;
          float2 uv : TEXCOORD0;
     };
 
     sampler2D _MainTex;
     sampler2D _PaintTex;
 
     v2f vert( appdata_img v )
     {
          v2f o = (v2f)0;
          o.pos = UnityObjectToClipPos(v.vertex);
          o.uv = v.texcoord;
 
          return o;
     }
 
     float4 frag(v2f input) : SV_Target
     {
         float4 canvas = tex2D(_MainTex, input.uv);
         float4 paint = tex2D(_PaintTex, input.uv);

        // Classic blend
        //  float4 result = float4(canvas.rgb * (1 - paint.a) + paint.rgb * paint.a, 1);

        // Premultiplied alpha
        float4 result = float4(paint.rgb * paint.a + (canvas.rgb * canvas.a * (1 - paint.a)), 1);
         return result;

        // return canvas;
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