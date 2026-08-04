Shader "PromptedWorld/No Color No Depth"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0.8, 1, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay-20"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            ColorMask 0
        }
    }
}
