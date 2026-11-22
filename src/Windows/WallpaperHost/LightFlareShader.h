// String literal included in PostProcessShader after its shared declarations.
R"(
float3 lightFlareColor(float2 uv,float3 background) {
    // Aspect correction in units of the image's short side, independent of source crop.
    float2 q=(uv-flareCenter)*(max(pixelSize.x,pixelSize.y)/pixelSize)/flareRadius;
    [branch] if(any(abs(q)>flareBounds))return background;
    float d2=dot(q,q),d=sqrt(d2);
    float core=exp2(-2*d2/(flareCoreSize*flareCoreSize))*(1-smoothstep(flareCoreSize*2,flareCoreSize*3,d));
    float halo=0;
    [branch] if(flareHaloStrength>0&&d<flareHaloRadius) {
        float r=d/flareHaloRadius;
        halo=flareHaloStrength*exp2(-lerp(2,6,flareSoftness)*r*r)*(1-smoothstep(lerp(.8,.35,flareSoftness),1,r));
    }
    float rays=0;
    [loop] for(int i=0;i<(int)flareRayAxes;++i) {
        float4 ray=flareRays[i];
        float along=abs(dot(q,ray.xy)),across=abs(dot(q,float2(-ray.y,ray.x)));
        float width=flareRayWidth*(.3+.7*saturate(1-along/ray.z));
        [branch] if(along<ray.z&&across<width*3) {
            float t=along/ray.z;
            rays+=exp2(-2*across*across/(width*width))*pow(saturate(1-t),2)*ray.w;
        }
    }
    float3 light=flareColor*(core+rays*.8)+flareHaloColor*halo;
    [loop] for(int j=0;j<(int)flareGhostCount;++j) {
        float4 ghost=flareGhosts[j];float r=length(q-ghost.xy)/ghost.z;
        [branch] if(r<1) {
            float disc=exp2(-4*r*r)*(1-smoothstep(.6,1,r));
            float ring=exp2(-160*(r-.72)*(r-.72))*(1-smoothstep(.85,1,r))*.18;
            light+=flareGhostColors[j].rgb*(disc+ring)*ghost.w;
        }
    }
    light*=flareBrightness;
    float coverage=saturate(max(light.r,max(light.g,light.b)));
    [branch] if(coverage<=0)return background;
    float amount=flareMix*effectMask(uv,23);
    if(flareBlendMode<.5)return lerp(background,1-(1-background)*(1-saturate(light)),amount);
    if(flareBlendMode<1.5)return saturate(background+light*amount);
    // Coverage and chroma are separate so transparent edges never darken the frame.
    float3 source=saturate(light/max(coverage,.00001));
    float3 result=source;
    if(flareBlendMode>=2.5&&flareBlendMode<3.5)
        result=lerp(2*background*source,1-2*(1-background)*(1-source),step(.5,background));
    else if(flareBlendMode>=3.5) {
        float3 curve=lerp(((16*background-12)*background+4)*background,sqrt(saturate(background)),step(.25,background));
        result=lerp(background-(1-2*source)*background*(1-background),background+(2*source-1)*(curve-background),step(.5,source));
    }
    return lerp(background,result,amount*coverage);
}
)"
