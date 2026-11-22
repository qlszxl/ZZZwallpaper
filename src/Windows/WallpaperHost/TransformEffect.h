#pragma once
#include <algorithm>
#include <cmath>
#include <istream>
#include <stdexcept>

struct TransformOptions {
    bool enabled=false;
    float strength=1,scale=1,scaleX=1,scaleY=1,rotation=0,positionX=0,positionY=0,skewX=0,skewY=0,centerX=.5f,centerY=.5f;
    int borderMode=0;
    bool Active() const {return enabled&&strength>0&&(scale!=1||scaleX!=1||scaleY!=1||rotation!=0||positionX!=0||positionY!=0||skewX!=0||skewY!=0);}
    void Read(std::istream& in) {
        int on=0;
        if(!(in>>on>>strength>>scale>>scaleX>>scaleY>>rotation>>positionX>>positionY>>skewX>>skewY>>centerX>>centerY>>borderMode))throw std::runtime_error("Incomplete transform parameters");
        auto range=[](float v,float a,float b) {if(!std::isfinite(v)||v<a||v>b)throw std::runtime_error("Transform parameter out of range");};
        if(on<0||on>1||borderMode<0||borderMode>3)throw std::runtime_error("Invalid transform mode");
        range(strength,0,1);range(scale,.1f,4);range(scaleX,.25f,4);range(scaleY,.25f,4);range(rotation,-180,180);
        range(positionX,-1,1);range(positionY,-1,1);range(skewX,-60,60);range(skewY,-60,60);range(centerX,0,1);range(centerY,0,1);
        enabled=on!=0;
    }
};
struct TransformConstants {
    float inverse[4]{1,0,0,1};
    float offsetX=0,offsetY=0,mix=0,borderMode=0;
};
static_assert(sizeof(TransformConstants)==32);

inline TransformConstants PrepareTransform(const TransformOptions& t,float audioZoom,float aspect) {
    TransformConstants c;
    if(!t.enabled||t.strength<=0||(!t.Active()&&audioZoom==1))return c;
    // Invert scale -> horizontal shear -> vertical shear -> rotation.
    // Sequential shears have determinant one, including at extreme settings.
    constexpr double rad=3.141592653589793/180;
    double x=std::tan(t.skewX*rad),y=std::tan(t.skewY*rad),cs=std::cos(t.rotation*rad),sn=std::sin(t.rotation*rad);
    double sx=t.scale*t.scaleX*audioZoom,sy=t.scale*t.scaleY*audioZoom;
    double a=((1+x*y)*cs+x*sn)/sx,b=((1+x*y)*sn-x*cs)/sx;
    double d=(-y*cs-sn)/sy,e=(-y*sn+cs)/sy;
    // Convert the inverse from pixel-aspect space back to normalized UVs.
    b/=aspect;d*=aspect;
    c.inverse[0]=static_cast<float>(a);c.inverse[1]=static_cast<float>(b);c.inverse[2]=static_cast<float>(d);c.inverse[3]=static_cast<float>(e);
    c.offsetX=t.centerX-static_cast<float>(a*(t.centerX+t.positionX)+b*(t.centerY+t.positionY));
    c.offsetY=t.centerY-static_cast<float>(d*(t.centerX+t.positionX)+e*(t.centerY+t.positionY));
    c.mix=t.strength;c.borderMode=static_cast<float>(t.borderMode);return c;
}
