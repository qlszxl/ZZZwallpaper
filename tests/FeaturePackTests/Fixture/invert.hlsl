Texture2D frame : register(t0);
SamplerState sampleFrame : register(s0);
cbuffer Package : register(b1) { float strength; float seconds; float2 inverseSize; }
float4 main(float4 position:SV_POSITION,float2 uv:TEXCOORD0):SV_TARGET {
    float4 source=frame.Sample(sampleFrame,uv);
    return float4(lerp(source.rgb,1-source.rgb,strength),source.a);
}
