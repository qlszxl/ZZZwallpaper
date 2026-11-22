#pragma once
// Range/matrix conversion is already done by MediaSession. Glow uses the declared SDR transfer curve.
static constexpr const char* PostProcessShader=R"(
struct GlitchGroup {float strength;float coverage;float size;float rgb;uint tick;uint mode;uint seed;float hue;float2 center;float2 halfExtent;};
struct LargeSatellite {float2 center;float2 halfExtent;float2 shift;float rgb;float weight;float hue;float3 padding;};
struct MaskData {float4 rect;float4 params;float4 dynamics;};
cbuffer Settings:register(b0) {
    float2 crop;float angle;float intensity;
    float brightness;float contrast;float saturation;float vignette;
    float glow;float glowThreshold;float2 pixelSize;
    float2 blurStep;float rgbOffset;float softRadius;
    float scanStrength;float scanDensity;float noiseStrength;float transfer;
    float outputHeight;uint frameIndex;float scanPhase;float dotPhase;
    float glitchStrength;float glitchCoverage;uint glitchTick;float dotStrength;
    float dotSpacing;float dotSize;float dotGlow;float dotPulse;
    float glitchRgb;uint groupCount;float rippleStrength;float ripplePhase;
    float rippleWidth;float rippleCount;float scanExposure;float scanSweep;
    float rippleDots;float rippleSpacing;float rippleSize;float dotShape;
    float clusterBlend;float bendAmplitude;float bendSpan;float bendDensity;
    float blurMix;float distortionStrength;float distortionScale;float distortionPhase;
    float2 distortionCenter;uint distortionMode;float scanWidth;
    float scanSoftness;float scanAngle;float dotRotation;float rippleRotation;
    uint blurMotion;uint blurMotionMode;float blurMotionPhase;float blurCoverage;
    float blurScale;float blurFeather;float blurAngle;float blurMaskPass;
    float largeStrength;float largeBlend;float largeRgb;float glitchHue;
    float2 largeCenter;float2 largeHalfExtent;
    float2 largeShift;uint largeTick;uint largeMode;
    uint largeSatelliteCount;float largeHue;uint smallRandomHue;float rippleShape;
    GlitchGroup groups[6];
    LargeSatellite largeSatellites[10];
    float zoomMix;float zoomPhase;float zoomScale;float zoomCenterX;
    float zoomCenterY;float rainMix;float rainPhase;float rainDensity;
    float rainRadius;float rainWidth;float waterMix;float waterPhase;
    float waterAmplitude;float waterFrequency;float waterAngle;float edgeMix;
    float edgeWidth;float edgeThreshold;float edgeMode;float pixelMix;
    float pixelBlock;float sunMix;float sunPhase;float sunAngle;
    float sunWidth;float sunPosition;float extendedPad1;float extendedPad2;
    float2 originalCrop;float originalAngle;float originalPadding;
    float2 sourceOffset;float2 originalOffset;
    float noiseColor;float rippleColorMode;float rippleHueMin;float rippleHueSpan;
    float3 rippleColor;float rippleColorTick;
    float rippleSatMin;float rippleSatMax;float rippleValueMin;float rippleValueMax;
    float blurMethod;float blurDirection;float2 blurCenter;
    float rainOutlineMix;float rainOutlineWidth;float rainPad1;float rainPad2;
    float3 rainColor;float rainColorPad;
    float sunShape;float sunCurvature;float sunArc;float sunRadius;
    float sunColorCount;float sunRepeat;float maskPreview;float filterMethod;
    float4 sunColors[12];MaskData masks[16];float4 maskIndices[7];
    float2 sunCenter;float sunConeAngle;float maskCount;
    float sunConeCorner;float sunConeDirection;float sunConeLength;float sunConeSwing;
    float3 edgeColor;float edgeColorPadding;
    float particleMix;float particleStyle;float particleCount;float particleSize;
    float particleTime;float particleLifetime;float particleSpeed;float particleDirection;
    float3 particleColor;float particleSpread;
    float2 particleCenter;float2 particleRegion;
    float particleSway;float particleRotation;float particleTwinkle;float particleFade;
    float particleEmission;float particleEmitter;float particleGravity;float particleSizeRandom;
    float particleSpeedRandom;float particleBurstInterval;float particleCustomImage;float particleRandomRotation;
    float particleRandomMask;float particleBuiltinImage;float particleSeedOffset;float particlePadding;
    float2 inputScale;float2 inputBias;
    float2 rasterOrigin;float2 rasterPadding;
    uint blurSamples;float3 blurPadding;
    float4 blurWeights[65];
    float2 flareCenter;float flareRadius;float flareMix;
    float3 flareColor;float flareBrightness;
    float3 flareHaloColor;float flareCoreSize;
    float flareHaloStrength;float flareHaloRadius;float flareSoftness;float flareRayAxes;
    float flareRayWidth;float flareGhostCount;float flareBlendMode;float flareBounds;
    float4 flareRays[6];float4 flareGhosts[4];float4 flareGhostColors[4];
    float4 transformInverse;float2 transformOffset;float transformMix;float transformBorder;
};
struct V {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};
V vs(uint id:SV_VertexID) {float2 p=float2((id<<1)&2,id&2);V o;o.pos=float4(p*float2(2,-2)+float2(-1,1),0,1);o.uv=p;return o;}
Texture2D tex:register(t0);Texture2D bloom:register(t1);Texture2D videoBlur:register(t2);Texture2D originalTex:register(t3);Texture2DArray maskImages:register(t4);Texture2DArray maskLayers:register(t6);Texture2D particleSprite:register(t7);SamplerState smp:register(s0);
float2 inputUV(float2 p) {return p*inputScale+inputBias;}
float2 sourceUV(float2 p) {float2 u=(p-.5)*crop+sourceOffset;float c=cos(angle),s=sin(angle);return inputUV(float2(c*u.x+s*u.y,-s*u.x+c*u.y)+.5);}
// Procedural/image masks are prepared only when changed (or animated), then
// all effects share the same lightweight lookup at their original screen UV.
float effectMask(float2 p,uint effect) {
    if(effect>=25)return 1;
    float index=maskIndices[effect/4][effect%4]-1;
    if(index<0||index>=maskCount)return 1;
    return maskLayers.SampleLevel(smp,float3(saturate(p),index),0).r;
}
float maskValue(float2 p,uint index) {
    if(index>=16||index>=(uint)maskCount)return 0;
    MaskData m=masks[index];uint shape=(uint)m.params.x;float feather=m.params.z;
    float c=cos(m.params.y),s=sin(m.params.y);float2 q=p-m.rect.xy;
    q=float2(c*q.x+s*q.y,-s*q.x+c*q.y)/max(m.rect.zw,.01)*2;
    float value=0;
    if(shape==5) {
        float2 delta=feather*.04;
        value=maskImages.SampleLevel(smp,float3(p,index),0).r*4;
        value+=maskImages.SampleLevel(smp,float3(saturate(p+float2(delta.x,0)),index),0).r;
        value+=maskImages.SampleLevel(smp,float3(saturate(p-float2(delta.x,0)),index),0).r;
        value+=maskImages.SampleLevel(smp,float3(saturate(p+float2(0,delta.y)),index),0).r;
        value+=maskImages.SampleLevel(smp,float3(saturate(p-float2(0,delta.y)),index),0).r;value/=8;
    } else if(shape>=6) {
        q=(p-.5)*float2(pixelSize.y/pixelSize.x,1);q=float2(c*q.x-s*q.y,s*q.x+c*q.y)*m.dynamics.w;
        float phase=m.dynamics.y,field;
        if(shape==6)field=.5+.22*sin(q.x*6.2831853+phase)+.18*sin(q.y*6.2831853-phase)+.1*sin((q.x+q.y)*3.14159265+phase*2);
        else if(shape==7)field=1-length(frac(q-float2(phase/6.2831853,.18*sin(phase))+.5)-.5)/.70710678;
        else field=1-abs(frac(q.x-phase/6.2831853+.5)-.5)*2;
        float edge=max(.001,feather*.3),threshold=1-m.dynamics.z;
        value=m.dynamics.z<=0?0:m.dynamics.z>=1?1:smoothstep(threshold-edge*.5,threshold+edge*.5,field);
    } else {
        float distance=max(abs(q.x),abs(q.y));
        if(shape==1)distance=length(q);
        else if(shape==2) {float radius=m.dynamics.x;float2 d=abs(q)-(1-radius);distance=length(max(d,0))+min(max(d.x,d.y),0)-radius+1;}
        else if(shape==3)distance=abs(q.x)+abs(q.y);
        else if(shape==4)distance=length(q)/(.72+.28*cos(atan2(q.y,q.x)*5));
        value=1-smoothstep(1-max(.003,feather),1+max(.003,feather),distance);
    }
    return m.params.w>0?1-value:value;
}
float4 maskLayer(V v):SV_TARGET {return maskValue(v.uv,(uint)originalPadding).xxxx;}
float3 decode(float3 c) {
    c=saturate(c);
    if(transfer==8)return c;
    if(transfer==4)return pow(c,2.2);
    if(transfer==5)return pow(c,2.8);
    if(transfer==13)return lerp(pow((c+.055)/1.055,2.4),c/12.92,step(c,.04045));
    float a=transfer==7?1.1115:(transfer==15?1.0993:1.099);
    float slope=transfer==7?4:4.5;
    float beta=transfer==7?.0228:(transfer==15?.0181:.018);
    return lerp(pow((c+a-1)/a,1/.45),c/slope,step(c,beta*slope));
}
float3 encode(float3 c) {
    c=saturate(c);
    if(transfer==8)return c;
    if(transfer==4)return pow(c,1/2.2);
    if(transfer==5)return pow(c,1/2.8);
    if(transfer==13)return lerp(1.055*pow(c,1/2.4)-.055,c*12.92,step(c,.0031308));
    float a=transfer==7?1.1115:(transfer==15?1.0993:1.099);
    float slope=transfer==7?4:4.5;
    float beta=transfer==7?.0228:(transfer==15?.0181:.018);
    return lerp(a*pow(c,.45)-(a-1),c*slope,step(c,beta));
}
// Independent UV effects use viewport aspect, so rings stay round on portrait displays.
float rainHash(float2 p) {return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float2 surfaceUV(float2 p) {
    float2 aspect=float2(pixelSize.y/pixelSize.x,1);
    float2 q=p*aspect;
    [branch] if(waterMix>0&&waterAmplitude>0) {
        float2 direction=float2(cos(waterAngle),sin(waterAngle));
        float2 across=float2(-direction.y,direction.x);
        float wave1=sin(dot(q,direction)*waterFrequency*6.28318530718+waterPhase);
        float wave2=sin(dot(q,across)*waterFrequency*4.71238898038-waterPhase*2);
        p+=(direction*wave1+across*wave2*.5)/aspect*(waterAmplitude/1080)*waterMix*effectMask(p,17);
    }
    [branch] if(rainMix>0) {
        float2 cellPosition=q*rainDensity;float2 cell=floor(cellPosition);float2 displacement=0;
        // Keep loops bounded without cloning the complete sampling graph in FXC.
        [loop] for(int i=0;i<9;++i) {
            float2 id=cell+float2(i%3-1,i/3-1);
            float2 center=id+.15+.7*float2(rainHash(id),rainHash(id+23.7));
            float phase=rainPhase<0?saturate(-rainPhase-1):frac(rainPhase+rainHash(id+71.3));
            float2 delta=cellPosition-center;float distance=length(delta);
            float ring=(distance-phase*rainRadius)/rainWidth;
            float envelope=exp2(-ring*ring*2)*sin(phase*3.14159265359)*(1-phase);
            displacement+=delta/max(distance,.001)*sin(ring*6.28318530718)*envelope;
        }
        p+=displacement/aspect*(rainMix*12/1080)*effectMask(p,16);
    }
    return p;
}
float2 distortionUV(float2 p) {
    if(distortionStrength==0)return p;
    float2 aspect=float2(pixelSize.y/pixelSize.x,1);float2 q=(p-distortionCenter)*aspect;
    if(distortionMode==0)return p+sin((p.yx-distortionCenter.yx)*distortionScale*6.28318530718+distortionPhase)*distortionStrength*.035;
    if(distortionMode==1)q*=1+distortionStrength*.6*dot(q,q);
    else {float turn=distortionStrength*2.5*(1-smoothstep(0,1.2,length(q)))*cos(distortionPhase);float c=cos(turn),s=sin(turn);q=float2(c*q.x-s*q.y,s*q.x+c*q.y);}
    return q/aspect+distortionCenter;
}
float2 warpUV(float2 p) {return lerp(p,distortionUV(p),effectMask(p,12));}
float3 sourceColor(float2 p) {
    float3 c=tex.Sample(smp,sourceUV(p)).rgb;
    if(blurMix>0) {
        float4 softened=videoBlur.Sample(smp,p);
        c=lerp(c,encode(softened.rgb),blurMix*softened.a*effectMask(p,11));
    }
    return c;
}
// Geometry is rendered once before the original effect graph, avoiding repeated
// water/zoom evaluations for every RGB split, glitch and bloom sample.
float3 extendedRawColor(float2 p) {return tex.Sample(smp,sourceUV(p)).rgb;}
)"
R"(
float3 extendedPixelColor(float2 p) {
    float3 c=extendedRawColor(p);
    [branch] if(pixelMix>0) {
        float2 block=pixelSize*max(1,pixelBlock*outputHeight/1080);
        // Sample a cell's center rather than interpolating UVs (which would smear it).
        float2 center=saturate((floor(p/block)+.5)*block);
        c=lerp(c,extendedRawColor(center),pixelMix*effectMask(p,19));
    }
    return c;
}
float3 extendedSourceColor(float2 p) {
    float3 c=extendedPixelColor(p);
    [branch] if(zoomMix>0) {
        float2 center=float2(zoomCenterX,zoomCenterY);
        float scale=pow(zoomScale,zoomPhase);
        float2 front=center+(p-center)/scale;
        float2 rear=center+(p-center)/(scale/zoomScale);
        // Feather the entering plane into the moving outgoing layer. A narrow
        // mirrored margin avoids clamped edge streaks without tiling the viewport.
        float2 border=min(rear,1-rear);
        float feather=max(.0001,.22*(1-zoomPhase));
        float planeMask=smoothstep(-feather,feather,min(border.x,border.y));
        rear=1-abs(frac(rear*.5)*2-1);
        float fade=smoothstep(0,1,zoomPhase);
        float3 tunnel=lerp(extendedPixelColor(front),extendedPixelColor(rear),fade*planeMask);
        c=lerp(c,tunnel,zoomMix*effectMask(p,15));
    }
    return c;
}
float rainRings(float2 p) {
    float2 position=p*float2(pixelSize.y/pixelSize.x,1)*rainDensity,cell=floor(position);float alpha=0;
    float ringThickness=max(.001,rainOutlineWidth/1080*rainDensity);
    [loop] for(int i=0;i<9;++i) {
        float2 id=cell+float2(i%3-1,i/3-1),center=id+.15+.7*float2(rainHash(id),rainHash(id+23.7));
        float phase=rainPhase<0?saturate(-rainPhase-1):frac(rainPhase+rainHash(id+71.3));float distance=abs(length(position-center)-phase*rainRadius);
        float ring=(1-smoothstep(ringThickness*.4,ringThickness*1.4,distance))*sin(phase*3.14159265)*(1-phase);
        alpha=max(alpha,ring);
    }
    return alpha*rainOutlineMix*effectMask(p,16);
}
float4 geometry(V v):SV_TARGET {
    float3 c=extendedSourceColor(surfaceUV(v.uv));
    if(rainOutlineMix>0)c=lerp(c,rainColor,rainRings(v.uv));return float4(c,1);
}
float3 baseColor(float2 p) {
    p=warpUV(p);float3 c=sourceColor(p);
    float3 beforeColor=c;
    c=(c-.5)*contrast+.5+brightness;
    float luma=dot(c,float3(.2126,.7152,.0722));return lerp(beforeColor,saturate(lerp(luma.xxx,c,saturation)),effectMask(p,0));
}
float4 original(V v):SV_TARGET {return float4(tex.Sample(smp,sourceUV(v.uv)).rgb,1);}
// The ordered graph operates at full layer strength. Its only global blend
// occurs here, after sprites, geometry and every following filter have landed.
float4 stackComposite(V v):SV_TARGET {
    float2 q=(v.uv-.5)*originalCrop+originalOffset;
    float ca=cos(originalAngle),sa=sin(originalAngle);
    float2 originalUV=float2(ca*q.x+sa*q.y,-sa*q.x+ca*q.y)+.5;
    return float4(saturate(lerp(originalTex.Sample(smp,originalUV).rgb,tex.Sample(smp,inputUV(v.uv)).rgb,intensity)),1);
}
float randomNoise(uint2 p) {
    uint n=p.x*1973u+p.y*9277u+frameIndex*26699u+911u;n=(n^(n>>13u))*1274126177u;n^=n>>16u;
    return (n&65535u)/65535.0*2-1;
}
float hashValue(uint seed) {
    seed=(seed^(seed>>16u))*2246822519u;seed=(seed^(seed>>13u))*3266489917u;seed^=seed>>16u;
    return (seed&65535u)/65536.0;
}
float3 shiftGlitchHue(float3 c,float turns) {
    // Rotate HSV hue while preserving saturation and value, including neutral grays.
    float hi=max(c.r,max(c.g,c.b)),lo=min(c.r,min(c.g,c.b)),chroma=hi-lo;
    if(chroma<.000001)return c;
    float hue;
    if(hi==c.r)hue=(c.g-c.b)/chroma;
    else if(hi==c.g)hue=2+(c.b-c.r)/chroma;
    else hue=4+(c.r-c.g)/chroma;
    hue=frac(hue/6+turns);
    float3 wave=saturate(abs(frac(hue+float3(0,2.0/3.0,1.0/3.0))*6-3)-1);
    return lo+chroma*wave;
}
)"
// Keep each source literal below MSVC's per-literal size limit.
R"(
float3 surfaceColor(float2 p) {
    float2 displacement=0;float2 redOffset=float2(rgbOffset*effectMask(p,1),0),greenOffset=0,blueOffset=float2(-rgbOffset*effectMask(p,1),0);
    float scale=outputHeight/1080;float clusterMask=0,hueMask=0,selectedHue=0;
    [branch] if(groupCount==0&&glitchStrength>0&&glitchCoverage>0) {
        uint band=(uint)floor(p.y/pixelSize.y/max(2,20*outputHeight/1080));
        uint seed=band*1973u+glitchTick*26699u+911u;
        float selected=(hashValue(seed)<glitchCoverage?1:0)*effectMask(p,7);
        selectedHue=smallRandomHue!=0?hashValue(seed+491u)-.5:glitchHue;hueMask=selectedHue!=0?selected:0;
        float tear=selected*glitchStrength;
        displacement.x+=(hashValue(seed+71u)*2-1)*48*tear;
        // Independently adjustable separation, including a small green-channel displacement.
        redOffset+=float2(glitchRgb,glitchRgb*.12)*tear;
        greenOffset+=float2(-glitchRgb*.18,0)*tear;
        blueOffset-=float2(glitchRgb,glitchRgb*.12)*tear;
    }
    // A main patch and small satellite patches preserve the image outside their bounded regions.
    [loop] for(uint i=0;i<groupCount;++i) {
        GlitchGroup g=groups[i];
        [branch] if(g.strength<=0||g.coverage<=0)continue;
        float2 region=abs((p-g.center)/g.halfExtent);
        float localMask=(1-smoothstep(.75,1,max(region.x,region.y)))*effectMask(p,7);
        [branch] if(localMask<=0)continue;
        float size=max(1,g.size*scale);float2 pixels=p/pixelSize;
        uint2 cell=(uint2)floor(pixels/float2(size*2,size));
        uint seed=cell.y*1973u+g.tick*26699u+g.seed;
        if(g.mode==1||g.mode==3)seed+=cell.x*9277u;
        float selected=hashValue(seed)<g.coverage?1:0;
        float amount=selected*g.strength*localMask;clusterMask=max(clusterMask,selected*localMask);
        if(g.hue!=0&&selected*localMask>hueMask) {hueMask=selected*localMask;selectedHue=g.hue;}
        float2 direction=float2(hashValue(seed+71u)*2-1,hashValue(seed+113u)*2-1);
        if(g.mode==0)displacement+=float2(direction.x*72,0)*amount;
        else if(g.mode==1)displacement+=direction*float2(56,24)*amount;
        else if(g.mode==2)displacement+=float2(direction.x*20,direction.y*3)*amount;
        float2 separation=float2((direction.x<0?-1:1)*(.6+.4*abs(direction.x)),direction.y*.35)*g.rgb*amount;
        redOffset+=separation;greenOffset+=float2(-separation.x*.22,separation.y*.3);blueOffset-=separation;
    }
    float largeMask=0;float2 largeDisplacement=0,largeSeparation=0;
    [branch] if(largeStrength>0) {
        float2 region=abs((p-largeCenter)/largeHalfExtent);
        largeMask=(1-smoothstep(.98,1,max(region.x,region.y)))*effectMask(p,8);
        if(largeHue!=0&&largeMask>=hueMask) {hueMask=largeMask;selectedHue=largeHue;}
        [branch] if(largeMask>0) {
            float2 local=(p-largeCenter)/largeHalfExtent*.5+.5;
            uint2 cell=(uint2)floor(local*float2(5,9));
            uint seed=largeTick*26699u+cell.y*1973u+17117u;
            if(largeMode!=0)seed+=cell.x*9277u;
            float2 tear=float2(hashValue(seed+71u)*2-1,hashValue(seed+113u)*2-1);
            // The broad shift is normalized to the visible frame; fine tears and
            // RGB splitting remain independent from the 20..60% region area.
            largeDisplacement=(largeShift+tear*float2(.016,.008)*largeStrength)*largeMask;
            largeSeparation=float2(largeRgb,largeRgb*.12)*largeStrength*largeMask*scale*pixelSize;
        }
        [loop] for(uint n=0;n<largeSatelliteCount;++n) {
            LargeSatellite patch=largeSatellites[n];
            float2 edge=abs((p-patch.center)/patch.halfExtent);
            float mask=(1-smoothstep(.65,1,max(edge.x,edge.y)))*patch.weight*effectMask(p,8);
            largeMask=max(largeMask,mask);
            if(patch.hue!=0&&mask>hueMask) {hueMask=mask;selectedHue=patch.hue;}
            largeDisplacement+=patch.shift*mask;
            largeSeparation+=float2(patch.rgb,patch.rgb*.12)*mask*scale*pixelSize;
        }
    }
    float limit=groupCount>0?24:160;
    float2 displaced=p+clamp(displacement,-limit,limit)*scale*pixelSize+largeDisplacement;
    float3 c=baseColor(displaced);
    [branch] if(any(redOffset!=0)||largeSeparation.x!=0)c.r=baseColor(displaced+clamp(redOffset,-limit,limit)*scale*pixelSize+largeSeparation).r;
    [branch] if(any(greenOffset!=0)||largeSeparation.x!=0)c.g=baseColor(displaced+clamp(greenOffset,-limit,limit)*scale*pixelSize-largeSeparation*.18).g;
    [branch] if(any(blueOffset!=0)||largeSeparation.x!=0)c.b=baseColor(displaced+clamp(blueOffset,-limit,limit)*scale*pixelSize-largeSeparation).b;
    if(selectedHue!=0&&hueMask>0)c=lerp(c,shiftGlitchHue(c,selectedHue),hueMask);
    if(groupCount>0||largeMask>0) {
        float3 clean=baseColor(p);
        if(rgbOffset>0) {clean.r=baseColor(p+float2(rgbOffset*effectMask(p,1),0)*scale*pixelSize).r;clean.b=baseColor(p-float2(rgbOffset*effectMask(p,1),0)*scale*pixelSize).b;}
        c=lerp(clean,c,max(clusterBlend*clusterMask,largeBlend*largeMask));
    }
    return c;
}
// Shapes use a shared outer radius and pixel-space antialiasing. The cell
// sample, audio envelope and ripple mask remain independent of the silhouette.
float dotDistance(float2 p,uint shape) {
    float2 q=abs(p);
    if(shape==1)return max(q.x,q.y)*1.41421356;
    if(shape==2)return q.x+q.y;
    if(shape==3)return max(p.y,.8660254*q.x-.5*p.y)*2;
    if(shape==4)return max(.8660254*q.x+.5*q.y,q.y)/.8660254;
    if(shape==5) {
        float sector=frac((atan2(p.x,-p.y)+6.28318530718)/6.28318530718)*10;
        float index=floor(sector),a=frac(sector)*.628318530718;
        float r1=fmod(index,2)<1?1:.45,r2=1.45-r1;
        float boundary=r1*r2*.58778525229/max(.0001,r2*sin(.628318530718-a)+r1*sin(a));
        return length(p)/boundary;
    }
    if(shape==6)return max(min(q.x,q.y)/.32,max(q.x,q.y))*1.049952;
    return length(p);
}
float3 hsvColor(float h,float saturation,float value) {
    float3 rgb=saturate(abs(frac(h+float3(0,2.0/3.0,1.0/3.0))*6-3)-1);
    return lerp(float3(1,1,1),rgb,saturation)*value;
}
float3 rippleInk(float2 center) {
    if(rippleColorMode<1.5)return rippleColor;
    uint seed=(uint)center.x*1973u+(uint)center.y*9277u+(uint)rippleColorTick*26699u;
    float h=rippleColorMode<2.5?hashValue(seed):rippleHueMin+hashValue(seed)*rippleHueSpan;
    return hsvColor(h,lerp(rippleSatMin,rippleSatMax,hashValue(seed+37u)),lerp(rippleValueMin,rippleValueMax,hashValue(seed+91u)));
}
)"
R"(
float3 dotCell(float2 p,float spacingRef,float size,float phase,float pulseAmount,float glowAmount,uint shape,float rotation,bool isRipple,out float2 center) {
    float spacing=max(2,spacingRef*outputHeight/1080);
    float2 grid=p/pixelSize/spacing-phase;
    float2 local=frac(grid)-.5;
    center=(floor(grid)+.5+phase)*spacing;
    float3 cell=surfaceColor(center*pixelSize);
    if(isRipple&&rippleColorMode>0)cell=rippleInk(center);
    float luma=dot(cell,float3(.2126,.7152,.0722));
    float pulse=1-pulseAmount*.3+pulseAmount*.3*cos(6.28318530718*(phase+dot(center/spacing,float2(.09,.07))));
    float radius=size*.48*(.4+.6*sqrt(saturate(luma)))*pulse;
    float c=cos(rotation),s=sin(rotation);local=float2(c*local.x+s*local.y,-s*local.x+c*local.y);
    float distance=dotDistance(local,shape),aa=.65/spacing;
    if(shape==7)distance=abs(distance-radius*.7)+radius*.7;
    float mask=1-smoothstep(max(0,radius-aa),radius+aa,distance);
    float3 dotted=cell*mask;
    if(glowAmount>0) {
        float halo=exp2(-12*distance*distance)*(1-smoothstep(.42,.71,distance));
        dotted=encode(decode(cell)*(mask*(1+glowAmount*.3)+halo*glowAmount*pulse));
    }
    return dotted;
}
float3 decoratedColor(float2 p) {
    float3 source=surfaceColor(p);float2 center;
    [branch] if(dotStrength>0)source=lerp(source,dotCell(p,dotSpacing,dotSize,dotPhase,dotPulse,dotGlow,(uint)dotShape,dotRotation,false,center),dotStrength*effectMask(p,9));
    [branch] if(rippleDots>0) {
        float spacing=max(2,rippleSpacing*outputHeight/1080);
        center=(floor(p/pixelSize/spacing)+.5)*spacing;
        float2 extent=1/pixelSize;
        float radial=length(center-extent*.5)/max(1,length(extent*.5));
        float ringDistance=ripplePhase<0?abs(radial+ripplePhase+1):abs(frac(radial*rippleCount-ripplePhase+.5)-.5)/rippleCount;
        float width=min(rippleWidth,.45/rippleCount);
        // Compact support: outside the travelling band the original pixels are untouched.
        float band=1-smoothstep(width*.5,width,ringDistance);
        [branch] if(band>0)source=lerp(source,dotCell(p,rippleSpacing,rippleSize,0,0,rippleStrength*band,(uint)rippleShape,rippleRotation,true,center),rippleDots*band*effectMask(p,10));
    }
    return source;
}
float bendNode(uint node) {
    return hashValue(node+417u)<bendDensity?(hashValue(node+1709u)*2-1)*bendAmplitude*outputHeight/1080:0;
}
float3 outlineColor(float2 p,float3 color) {
    float2 center=warpUV(p),offset=pixelSize*edgeWidth*outputHeight/1080;
    float gx=0,gy=0;
    [loop] for(int i=0;i<9;++i) {
        int x=i%3-1,y=i/3-1;
        if(x==0&&y==0)continue;
        float3 sampleColor=sourceColor(center+float2(x,y)*offset);
        float luma=dot(sampleColor,float3(.2126,.7152,.0722));
        gx+=luma*x*(y==0?2:1);gy+=luma*y*(x==0?2:1);
    }
    float magnitude=saturate(length(float2(gx,gy))*.5);
    float edge=smoothstep(edgeThreshold,min(1,edgeThreshold+.12)+.0001,magnitude);
    if(edgeMode>1.5)return lerp(color,edge*edgeColor,edgeMix);
    return lerp(color,edgeColor,edge*edgeMix);
}
float3 prismPalette(float t) {
    t=saturate(t);uint count=(uint)clamp(sunColorCount,2,12);
    if(t<=sunColors[0].a)return sunColors[0].rgb;
    [loop] for(uint i=1;i<count;i++) {
        if(t<=sunColors[i].a)return lerp(sunColors[i-1].rgb,sunColors[i].rgb,saturate((t-sunColors[i-1].a)/max(.000001,sunColors[i].a-sunColors[i-1].a)));
    }
    return sunColors[count-1].rgb;
}
float3 prismColor(float2 p,float3 color) {
    float2 q=(p-sunCenter)*float2(pixelSize.y/pixelSize.x,1);float c=cos(sunAngle),s=sin(sunAngle);
    q=float2(c*q.x+s*q.y,-s*q.x+c*q.y);q.y-=sunPosition+sin(sunPhase)*.18;
    float distance=q.y-sunCurvature*q.x*q.x,gate=1,t=0,envelope=0,halo=0;
    if(sunShape>4.5) {
        // Screen-corner coordinates keep the apex fixed under every animation.
        float aspect=pixelSize.y/pixelSize.x;
        uint corner=(uint)sunConeCorner;
        float2 inward=float2((corner==1||corner==3)?1-p.x:p.x,corner>=2?1-p.y:p.y);
        inward.x*=aspect;
        float direction=clamp(sunConeDirection+sin(sunPhase)*sunConeSwing,0,1.57079632679);
        float2 axis=normalize(float2(cos(direction)*aspect,sin(direction)));
        float along=dot(inward,axis),across=dot(inward,float2(-axis.y,axis.x));
        float reach=max(.01,sunConeLength*length(float2(aspect,1)));
        across-=sunCurvature*along*along/max(reach,.01);
        float halfWidth=max(pixelSize.y*.5,max(0,along)*tan(sunConeAngle*.5));
        t=across/(2*halfWidth)+.5;
        gate=(along>=0?1:0)*(1-smoothstep(reach*.65,reach,along));
        envelope=(1-smoothstep(.82,1,abs(across)/halfWidth))*gate;
        halo=exp2(-6*across*across/(halfWidth*halfWidth))*.12*gate;
    } else {
        if(sunShape>.5) {
            float turn=atan2(q.y,q.x);float radius=sunRadius*(1+sunCurvature*.2*cos(turn*2));
            if(sunShape>3.5)radius*=.72+.28*cos(turn*5);
            distance=length(q)-radius;
            if(sunShape>1.5&&sunShape<2.5)gate=1-smoothstep(sunArc*.5-.03,sunArc*.5+.03,abs(turn));
            if(sunShape>2.5&&sunShape<3.5)distance=max(0,length(q)-radius*.4);
        }
        t=distance/sunWidth+.5;
        envelope=(1-smoothstep(.25,.6,abs(distance)/sunWidth))*gate;
        halo=exp2(-8*distance*distance/(sunWidth*sunWidth))*.12*gate;
    }
    float3 rainbow=prismPalette(sunRepeat<=1?saturate(t):frac(t*sunRepeat));
    float3 light=saturate((rainbow*envelope+halo)*sunMix*effectMask(p,20));
    return 1-(1-saturate(color))*(1-light);
}
// Instanced quads: cost follows covered sprite pixels, not screen pixels x particle count.
Texture2DArray particleBuiltinSprites:register(t8);
struct ParticleVertex {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float2 screen:TEXCOORD1;float alpha:TEXCOORD2;nointerpolation uint style:TEXCOORD3;};
ParticleVertex particleVS(uint vertexId:SV_VertexID,uint instanceId:SV_InstanceID) {
    uint seed=(instanceId+(uint)particleSeedOffset)*747796405u+2891336453u;
    float r=hashValue(seed),r2=hashValue(seed+97u),r3=hashValue(seed+229u),r4=hashValue(seed+503u);
    float period=particleLifetime+(particleEmission>.5?particleBurstInterval:0);
    float clock=particleTime+(particleEmission>.5?0:(float)instanceId/max(1,particleCount)*particleLifetime);
    float cycle=floor(clock/period),age=clock-cycle*period;
    float progress=saturate(age/particleLifetime);
    float direction=particleDirection+(r-.5)*particleSpread;
    float2 velocity=float2(cos(direction),sin(direction))*particleSpeed*(1-particleSpeedRandom*r2);
    float2 origin=particleCenter;
    if(particleEmitter<.5)origin+=(float2(r3,r4)-.5)*particleRegion;
    else if(particleEmitter>1.5)origin.x+=(r3-.5)*particleRegion.x;
    float2 motion=velocity*age+float2(0,.5*particleGravity*age*age);
    motion+=float2(-sin(direction),cos(direction))*particleSway*24*(sin(age*1.6+r3*6.2831853)-sin(r3*6.2831853));
    float2 unit=float2(pixelSize.y/pixelSize.x*1080,1080);
    float2 center=origin+motion/unit;
    float2 corner=vertexId==0?float2(-1,-1):vertexId==1?float2(1,-1):vertexId==2?float2(-1,1):vertexId==3?float2(-1,1):vertexId==4?float2(1,-1):float2(1,1);
    // Rotation uses independent birth-time samples, never the position's r4.
    // Combining the bounded CPU epoch with the local cycle preserves living
    // particles across period boundaries (including the epoch wrap).
    uint birthCycle=((uint)particlePadding+(uint)cycle)&65535u;
    uint rotationSeed=seed^(birthCycle*1597334677u+3812015801u);
    float initialAngle=hashValue(rotationSeed+13007u)*6.2831853;
    float rotationScale=1;
    if(particleRandomRotation>.5) {
        float directionSign=hashValue(rotationSeed+72341u)<.5?-1:1;
        rotationScale=directionSign*lerp(.35,1,hashValue(rotationSeed+193939u));
    }
    float rotation=particleRotation*rotationScale*age+initialAngle;
    float2 rotated=float2(cos(rotation)*corner.x-sin(rotation)*corner.y,sin(rotation)*corner.x+cos(rotation)*corner.y);
    float2 uv=center+rotated*particleSize*.5*(1-particleSizeRandom*r)/unit;
    ParticleVertex o;o.pos=float4(uv*float2(2,-2)+float2(-1,1),0,1);o.uv=corner*.5+.5;o.screen=uv;
    float fade=particleFade>0?smoothstep(0,.08,progress)*(1-smoothstep(.75,1,progress)):1;
    o.style=(uint)particleStyle;
    uint mask=(uint)particleRandomMask,choices=0;
    [unroll] for(uint bit=0;bit<15;bit++)choices+=(mask>>bit)&1u;
    if(choices>0) {
        uint selected=min(choices-1,(uint)(hashValue(seed+asuint(cycle+particlePadding)*1597334677u+907u)*choices));
        [unroll] for(uint i=0;i<15;i++)if((mask&(1u<<i))!=0) {if(selected==0){o.style=i;break;}selected--;}
    }
    o.alpha=(age<particleLifetime?1:0)*fade*(1-particleTwinkle*(.5+.5*sin(age*3+r2*6.2831853)));return o;
}
float4 particlePS(ParticleVertex v):SV_TARGET {
    float2 q=v.uv*2-1;float d=length(q),shape=0;float3 color=particleColor;
    float edge=max(.01,fwidth(d));
    if(v.style==0)shape=1-smoothstep(1-edge,1,d);
    else if(v.style==1)shape=1-smoothstep(.8,1,length(q*float2(1,1.8)));
    else if(v.style==2)shape=exp2(-d*d*7)*(1-smoothstep(.8,1,d));
    else if(v.style==3)shape=(1-smoothstep(0,1,d))*.8;
    else if(v.style==4)shape=1-smoothstep(1-edge,1,abs(q.x)+abs(q.y));
    else if(v.style==6) {float boundary=.68+.3*cos(atan2(q.y,q.x)*5);shape=1-smoothstep(boundary-edge,boundary,d);}
    else if(v.style==7) {float4 sprite=particleSprite.Sample(smp,v.uv);color*=sprite.rgb;shape=sprite.a*particleCustomImage;}
    else if(v.style>=8) {float4 sprite=particleBuiltinSprites.Sample(smp,float3(v.uv,v.style-8));color*=sprite.rgb;shape=sprite.a*particleBuiltinImage;}
    return float4(color,saturate(shape*v.alpha*particleMix*intensity*effectMask(v.screen,16)));
}
)"
#include "LightFlareShader.h"
#include "TransformShader.h"
R"(
float4 styled(V v):SV_TARGET {
    if(maskPreview>0)return float4(maskLayers.SampleLevel(smp,float3(v.uv,maskPreview-1),0).rrr,1);
    float2 q=(v.uv-.5)*originalCrop+originalOffset;
    float ca=cos(originalAngle),sa=sin(originalAngle);
    float2 originalUV=float2(ca*q.x+sa*q.y,-sa*q.x+ca*q.y)+.5;
    float3 plain=originalTex.Sample(smp,originalUV).rgb;float3 c=decoratedColor(v.uv);
    [branch] if(glow>0||vignette>0) {
        float3 working=decode(c);
        if(glow>0)working+=bloom.Sample(smp,v.uv).rgb*glow*effectMask(v.uv,3);
        if(vignette>0) {float2 q=(v.uv-.5)*2;working*=1-vignette*effectMask(v.uv,4)*.8*smoothstep(.15,1.4,dot(q,q));}
        c=encode(working);
    }
    // Integrated sinusoid; media-time phase scrolls the lines only when motion is enabled.
    [branch] if(scanStrength>0) {
        float2 position=v.uv/pixelSize;float scanY=dot(position,float2(sin(scanAngle),cos(scanAngle)));
        if(bendAmplitude>0) {
            float segment=(v.pos.x+rasterOrigin.x)/max(1,bendSpan*outputHeight/1080);uint node=(uint)floor(segment);
            scanY+=lerp(bendNode(node),bendNode(node+1u),frac(segment));
        }
        float frequency=scanDensity/1080,phase=6.28318530718*(scanY*frequency-scanPhase);
        float duty=clamp(scanWidth*frequency,.01,.95),wave=duty;
        // Band-limited pulse with independently adjustable width, edge softness and angle.
        // Integrate both output-pixel coverage and media-frame exposure to avoid high-speed aliasing.
        [unroll] for(int k=1;k<=4;++k) {
            float spatial=3.14159265359*k*frequency*max(1,fwidth(scanY));
            float temporal=3.14159265359*k*scanExposure;
            float exposure=abs(temporal)<.00001?1:sin(temporal)/temporal;
            wave+=2*sin(3.14159265359*k*duty)/(3.14159265359*k)*cos(k*phase)*sin(spatial)/spatial*exposure*exp(-k*k*scanSoftness*scanSoftness*.8);
        }
        wave=saturate(wave);
        if(scanSweep>=0) {float sweep=frac(scanY/outputHeight-scanSweep)-.5;wave=saturate(wave*.8+.4*exp2(-100*sweep*sweep));}
        c*=1-scanStrength*wave*effectMask(v.uv,6);
    }
    [branch] if(noiseStrength>0) {
        uint2 position=(uint2)floor(v.pos.xy+rasterOrigin);float grain=randomNoise(position);
        float3 noise=noiseColor>0?float3(grain,randomNoise(position+uint2(193,71)),randomNoise(position+uint2(47,337))):grain.xxx;
        c+=noise*noiseStrength*effectMask(v.uv,5);
    }
    [branch] if(edgeMix>0)c=lerp(c,outlineColor(v.uv,c),effectMask(v.uv,18));
    [branch] if(sunMix>0)c=prismColor(v.uv,c);
    [branch] if(flareMix>0)c=lightFlareColor(v.uv,c);
    return float4(saturate(lerp(plain,c,intensity)),1);
}
float3 highlight(float2 uv) {
    float3 c=decode(decoratedColor(uv));float luma=dot(c,float3(.2126,.7152,.0722));float threshold=decode(glowThreshold.xxx).x;
    // Threshold each source sample before averaging into the reduced-resolution target.
    return c*smoothstep(threshold,min(1,threshold+.1)+.0001,luma);
}
float4 extract(V v):SV_TARGET {
    float3 sum=0;
    // Keep one copy of the effect graph; explicitly expanding four copies makes the
    // blur/distortion variants expensive for the shader compiler and driver to optimize.
    [loop] for(uint sampleIndex=0;sampleIndex<4;++sampleIndex) {
        float2 offset=float2((sampleIndex&1u)?1:-1,(sampleIndex&2u)?1:-1)*pixelSize;
        sum+=highlight(v.uv+offset);
    }
    return float4(sum*.25,1);
}
float3 linearSource(float2 p) {return decode(tex.SampleLevel(smp,sourceUV(p),0).rgb);}
float blurWeight(uint i) {return blurWeights[i/4][i%4];}
float4 videoBlurHorizontal(V v):SV_TARGET {
    float3 sum=0;
    [loop] for(uint i=0;i<blurSamples;++i) {
        float t=2.0*i/(blurSamples-1)-1;
        sum+=linearSource(v.uv+blurStep*t)*blurWeight(i);
    }
    return float4(sum,1);
}
float4 videoBlurVertical(V v):SV_TARGET {
    // The horizontal scratch surface is already linear and viewport-normalized.
    float3 sum=0;
    [loop] for(uint i=0;i<blurSamples;++i) {
        float t=2.0*i/(blurSamples-1)-1;
        sum+=tex.SampleLevel(smp,v.uv+blurStep*t,0).rgb*blurWeight(i);
    }
    return float4(sum,1);
}
float4 blur(V v):SV_TARGET {
    // t0 is a reduced, regular scratch surface here, never a stack surface.
    // Its coordinates intentionally do not use the full-resolution inputUV.
    if(filterMethod==1) {float3 sum=0;[loop] for(int i=-8;i<=8;i++)sum+=tex.Sample(smp,v.uv+blurStep*(i/8.0)).rgb;return float4(sum/17,1);}
    float3 c=tex.Sample(smp,v.uv).rgb*.227027;
    c+=(tex.Sample(smp,v.uv+blurStep*1.384615).rgb+tex.Sample(smp,v.uv-blurStep*1.384615).rgb)*.316216;
    c+=(tex.Sample(smp,v.uv+blurStep*3.230769).rgb+tex.Sample(smp,v.uv-blurStep*3.230769).rgb)*.070270;
    // Store the region mask beside the blurred pixels once, so RGB separation,
    // dots and glow extraction can reuse it without repeating the pattern math.
    return float4(c,1);
}
float4 alternateBlur(V v):SV_TARGET {
    float2 aspect=float2(pixelSize.y/pixelSize.x,1),q=(v.uv-blurCenter)*aspect;
    float radius=length(blurStep/pixelSize)/max(1,outputHeight);float3 sum=0;
    [loop] for(uint i=0;i<blurSamples;++i) {
        float t=(i/(float)(blurSamples-1)-.5)*2;float2 uv=v.uv;
        if(blurMethod<2.5)uv+=float2(cos(blurDirection),sin(blurDirection))/aspect*radius*t;
        else if(blurMethod<3.5)uv=blurCenter+q*(1+radius*t*3)/aspect;
        else {float a=radius*t*3,c=cos(a),s=sin(a);uv=blurCenter+float2(c*q.x-s*q.y,s*q.x+c*q.y)/aspect;}
        sum+=linearSource(uv);
    }
    return float4(sum/blurSamples,1);
}
)";
