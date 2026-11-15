// Насыщенность и контраст для мини-окна 3D-вида (рисуется поверх HUD через Graphics.DrawTexture).
Shader "Hidden/Frontline/MiniViewSat"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Sat ("Saturation", Float) = 1.05
        _Contrast ("Contrast", Float) = 1.03
        _Bright ("Brightness", Float) = 0.01
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" }
        ZTest Always ZWrite Off Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Sat, _Contrast, _Bright;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                float l = dot(c.rgb, float3(0.299, 0.587, 0.114));
                c.rgb = lerp(l.xxx, c.rgb, _Sat);
                c.rgb = (c.rgb - 0.5) * _Contrast + 0.5 + _Bright;
                c.a = 1;
                return saturate(c);
            }
            ENDCG
        }
    }
    Fallback Off
}
