// FFmpegOut - FFmpeg video encoding plugin for Unity
// https://github.com/keijiro/KlakNDI

// Modified for the SyncSketch Plugin

Shader "Hidden/FFmpegOut/Blitter"
{
    Properties
    {
        _MainTex("", 2D) = "gray" {}
    }

    HLSLINCLUDE

    #if defined(SHADER_API_D3D11) || defined(SHADER_API_PSSL) || defined(SHADER_API_METAL) || defined(SHADER_API_VULKAN) || defined(SHADER_API_SWITCH)
        #define UNITY_UV_STARTS_AT_TOP 1
    #endif

    sampler2D _MainTex;
    float4 _ProjectionParams;

    void Vertex(
        uint vid : SV_VertexID,
        out float4 position : SV_Position,
        out float2 texcoord : TEXCOORD
    )
    {
        float x = (vid == 1) ? 1 : 0;
        float y = (vid == 2) ? 1 : 0;
        position = float4(x * 4 - 1, y * 4 - 1, 1, 1);
        texcoord = float2(x * 2, y * 2);

#if UNITY_UV_STARTS_AT_TOP
        if (_ProjectionParams.x < 0)
        {
            texcoord.y = 1 - texcoord.y;
        }
#endif
    }

    half4 Fragment(
        float4 position : SV_Position,
        float2 texcoord : TEXCOORD
    ) : SV_Target
    {
        return tex2D(_MainTex, texcoord);
    }

    ENDHLSL

    SubShader
    {
        Tags { "Queue" = "Transparent+100" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            ENDHLSL
        }
    }
}
