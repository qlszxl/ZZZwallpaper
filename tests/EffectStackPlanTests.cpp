#include "../src/Windows/WallpaperHost/EffectStack.h"
#include <algorithm>
#include <cstring>
#include <iostream>
#include <sstream>

static unsigned checks=0;
static void Check(bool condition,const char* message) {++checks;if(!condition)throw std::runtime_error(message);}
static std::string WireOrder(const std::vector<int>& order) {
    std::ostringstream text;text<<order.size();for(int kind:order)text<<' '<<kind;return text.str();
}
static void Reject(const std::string& text) {
    bool rejected=false;try {std::istringstream in(text);ReadEffectOrder(in,37);}catch(const std::runtime_error&) {rejected=true;}
    Check(rejected,"Accepted invalid stack order");
}
int main() {
    try {
        std::vector<int> order(VisualEffectKinds.begin(),VisualEffectKinds.end());
        for(size_t rotation=0;rotation<order.size();++rotation) {
            std::istringstream in(WireOrder(order));Check(ReadEffectOrder(in,37)==order,"Wire order was sorted again");
            std::rotate(order.begin(),order.begin()+1,order.end());
        }
        for(size_t i=0;i<order.size();++i)for(int invalid:{-1,13,14,21,32,2147483647}) {
            auto broken=order;broken[i]=invalid;Reject(WireOrder(broken));
        }
        for(size_t i=1;i<order.size();++i) {auto broken=order;broken[i]=broken[0];Reject(WireOrder(broken));}
        Reject("");Reject("0");Reject("18");Reject("19");Reject("21");Reject("20 0 1 2");Reject("20 0.5");
        auto previous=order;previous.erase(std::remove_if(previous.begin(),previous.end(),[](int kind){return kind>=22;}),previous.end());
        std::istringstream oldOrder(WireOrder(previous));auto migrated=ReadEffectOrder(oldOrder,26);
        previous.push_back(22);previous.push_back(23);Check(migrated==previous,"Wire 26 changed old layer order instead of appending disabled layers");
        auto legacy=VideoEffects::Parse("2 1 0 0.1 100 0 0 0 0 0 1 1 0 0 0 0 0.5 0 0");
        Check(legacy.effectOrder.empty(),"Legacy effects enabled the ordered graph");

        for(float origin:{0.f,.25f,.5f,.75f,13.125f,218.75f,-12.25f})for(float extent:{1.f,2.f,7.5f,179.875f,202.5f,640.f}) {
            auto grid=EffectStackRasterAxis(origin,extent);
            Check(grid.count>0,"Empty stack allocation");
            for(int i=0;i<grid.count;i++) {
                float center=grid.first+i+.5f;
                Check(center>=origin&&center<origin+extent,"Stack included a pixel outside the final viewport");
                float screenUv=(center-origin)/extent;
                float intermediateUv=(i+.5f-grid.offset)/extent;
                Check(std::abs(screenUv-intermediateUv)<.00001f,"Intermediate pass moved a logical sample center");
                float textureUv=screenUv*grid.scale+grid.bias;
                Check(std::abs(textureUv-(i+.5f)/grid.count)<.00001f,"Final composite resampled between stack pixels");
            }
        }

        RenderSettings all;VideoEffects fx;fx.effectOrder=order;
        all.brightness=.2f;all.contrast=1.2f;all.saturation=.8f;
        all.rgb=all.soft=all.glow=all.vignette=all.noise=all.scan=all.glitch=all.coverage=1;
        all.largeStrength=all.largeBlend=all.dots=all.rippleDots=all.blurMix=all.distortionStrength=1;
        all.zoomMix=all.rainMix=all.advanced.rainOutlineMix=all.waterMix=all.edgeMix=all.pixelMix=all.sunMix=1;
        all.intensity=.37f;all.cx=.6f;all.cy=.8f;all.angle=.3f;
        all.sourceOffsetX=.2f;all.sourceOffsetY=-.1f;all.originalOffsetX=.2f;all.originalOffsetY=-.1f;
        all.waterAmplitude=1;all.transform.mix=1;all.lightFlare.mix=1;all.lightFlare.brightness=1;
        all.advanced.maskCount=1;all.advanced.masks[0].rect[0]=.25f;
        for(int i=0;i<23;i++)all.advanced.maskIndices[i/4][i%4]=float(i%2);
        fx.particles.enabled=fx.subParticles.enabled=true;
        for(int kind:VisualEffectKinds) {
            auto stage=EffectStageSettings(all,{1u<<kind,kind});auto isolatedFx=fx;
            if(kind!=16)isolatedFx.particles.enabled=isolatedFx.subParticles.enabled=false;
            for(int other:VisualEffectKinds)Check(EffectLayerActive(stage,isolatedFx,other)==(other==kind&&EffectLayerActive(all,fx,kind)),"A layer enabled an unrelated operation");
            Check(stage.intensity==1&&stage.cx==1&&stage.cy==1&&stage.angle==0,"Stage applied the global mix or source transform again");
            Check(stage.sourceOffsetX==0&&stage.sourceOffsetY==0&&stage.originalOffsetX==.2f&&stage.originalOffsetY==-.1f,"Stage reapplied source position or lost original mix position");
            Check(std::memcmp(stage.advanced.masks,all.advanced.masks,sizeof(all.advanced.masks))==0&&
                std::memcmp(stage.advanced.maskIndices,all.advanced.maskIndices,sizeof(all.advanced.maskIndices))==0,"Stage changed mask ownership");
        }
        Check(all.intensity==.37f&&all.cx==.6f,"Layer isolation modified the source constants");
        RenderSettings neutral;VideoEffects disabled;disabled.effectOrder=order;
        Check(BuildEffectStack(neutral,disabled).empty(),"Disabled effects created render passes");
        neutral.groupCount=2;neutral.clusterBlend=1;
        Check(!EffectLayerActive(neutral,disabled,7),"Empty glitch cluster created a render pass");
        neutral.groups[1].strength=.4f;neutral.groups[1].coverage=.2f;
        Check(EffectLayerActive(neutral,disabled,7),"Active cluster was skipped");
        neutral.groups[1].mode=3;neutral.groups[1].rgb=0;
        Check(!EffectLayerActive(neutral,disabled,7),"Zero channel-split cluster created a render pass");
        neutral=RenderSettings{};disabled.subParticles.enabled=true;
        Check(EffectLayerActive(neutral,disabled,16),"Sub-particles require a disabled main layer");
        disabled.subParticles.enabled=false;neutral.advanced.rainOutlineMix=.4f;
        Check(EffectLayerActive(neutral,disabled,16),"Rain outline was lost from the particle layer");
        neutral.advanced.rainOutlineMix=0;neutral.rainMix=.4f;
        Check(EffectLayerActive(neutral,disabled,16),"Rain displacement was lost from the particle layer");

        fx.effectOrder={0,4,6,5,20};auto stages=BuildEffectStack(all,fx);
        Check(stages.size()==1,"Safe adjacent pointwise operations were not fused");
        for(int kind:fx.effectOrder)Check(stages[0].Contains(kind),"Fused stage omitted a pointwise effect");
        std::reverse(fx.effectOrder.begin(),fx.effectOrder.end());stages=BuildEffectStack(all,fx);
        Check(stages.size()==5,"Reversed effects were reordered by fusion");
        fx.effectOrder={0,1,4,6,5,20};stages=BuildEffectStack(all,fx);
        Check(stages.size()==3&&stages[1].Contains(1),"Fusion crossed an active neighbor-reading effect");
        auto noRgb=all;noRgb.rgb=0;stages=BuildEffectStack(noRgb,fx);
        Check(stages.size()==1,"Disabled neighbor effect prevented safe fusion");
        for(int kind:{1,3,7,8,9,10,11,12,15,16,17,18,19,24}) {
            fx.effectOrder={0,kind,4};stages=BuildEffectStack(all,fx);
            Check(stages.size()==3&&stages[1].Contains(kind),"Unsafe cross-layer fusion");
        }
        for(const auto& sequence:std::vector<std::vector<int>>{{16,11,3},{3,11,16},{17,19,15},{15,19,17}}) {
            fx.effectOrder=sequence;stages=BuildEffectStack(all,fx);Check(stages.size()==sequence.size(),"Lost sequential stages");
            for(size_t i=0;i<sequence.size();++i)Check(stages[i].Contains(sequence[i]),"Render plan changed requested order");
        }
        std::cout<<"Effect stack protocol, isolation, activation and fusion: "<<checks<<" checks passed\n";return 0;
    }catch(const std::exception& error) {std::cerr<<error.what()<<'\n';return 1;}
}
