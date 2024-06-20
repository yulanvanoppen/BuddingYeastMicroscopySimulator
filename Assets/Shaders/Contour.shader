// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Unlit/Contour" {
    Properties
    {
        _Color ("Main Color", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Outline Width", Range(1,5)) = 1.01
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    struct appdata
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
    };

    struct v2f
    {
        float4 pos : POSITION;
        float3 normal : NORMAL;
    };
    
    float _OutlineWidth;
    float4 _OutlineColor;

    v2f vert(appdata v)
    {
        v.vertex.xyz *= _OutlineWidth;

        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        return o;
    }

    ENDCG

    Subshader
    {

        Tags{ "Queue" = "Transparent" }
        //Pass
        //{
        //    // write 1 to the stencil buffer only where it's not already set
        //    Stencil
        //    {
        //        Ref 1
        //        Comp NotEqual
        //        Pass Replace
        //    }
        //    ColorMask 0 // don't render to color
        //    ZWrite Off // don't render to depth
        //    ZTest Always // if you want the outline to appear through walls
 
        //    CGPROGRAM
        //    #pragma vertex vert2
        //    #pragma fragment frag
 
        //    float4 vert2(float4 vertex : POSITION) : SV_POSITION
        //    {
        //        return UnityObjectToClipPos(vertex);
        //    }
        //    fixed4 frag() : SV_Target
        //    {
        //        return 0;
        //    }
        //    ENDCG
        //}
        Pass // Rendering Outlines 
        {
            //Stencil
            //{
            //    Ref 1
            //    Comp NotEqual
            //}
            ZWrite Off // don't render to depth
            ZTest Always // if you want the outline to appear through walls

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag 

            half4 frag(v2f i) : COLOR
            {
                return _OutlineColor;
            }
            ENDCG
        }
        Pass // Normal Render
        {
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // vertex shader
            // this time instead of using "appdata" struct, just spell inputs manually,
            // and instead of returning v2f struct, also just return a single output
            // float4 clip position
            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }
            
                        // color from the material
            fixed4 _Color;

                        // pixel shader, no inputs needed
            fixed4 frag() : SV_Target
            {
                return _Color; // just return it
            }
            ENDCG
        }
    }
}