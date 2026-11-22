R"(
float4 transform(V v):SV_TARGET {
    float2 q=float2(dot(transformInverse.xy,v.uv),dot(transformInverse.zw,v.uv))+transformOffset;
    q=lerp(v.uv,q,saturate(transformMix*effectMask(v.uv,24)));
    float coverage=1;
    if(transformBorder<.5) {
        // A one-pixel edge avoids a hard staircase on rotated black borders.
        float2 edge=min(q,1-q)/max(fwidth(q),float2(.000001,.000001));
        coverage=saturate(min(edge.x,edge.y)+.5);
        q=saturate(q);
    } else if(transformBorder<1.5)q=saturate(q);
    else if(transformBorder<2.5)q=1-abs(frac(q*.5)*2-1);
    else q=frac(q);
    return float4(tex.Sample(smp,inputUV(q)).rgb*coverage,1);
}
)"
