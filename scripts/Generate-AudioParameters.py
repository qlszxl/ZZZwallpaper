"""Shared, stable parameter IDs for the managed UI/MCP and native renderer."""
from pathlib import Path
root=Path(__file__).resolve().parent.parent
# effect, property, native member, label, minimum, maximum, UI multiplier, unit, scaled with effect strength, phase
rows=[]
def add(effect,path,member,label,lo,hi,scale=1,unit='',strength=False,phase=False):
    rows.append((effect,path,member,label,lo,hi,scale,unit,strength,phase))
for path,label,lo,hi,neutral in [('Brightness','亮度',-.5,.5,0),('Contrast','对比度',.5,1.5,1),('Saturation','饱和度',0,2,1)]:
    add('Color',path,path[0].lower()+path[1:],label,lo,hi,100,'%',True)
add('RgbSplit','RgbOffset','rgbOffset','偏移',0,16384,1,'px',True)
add('Softening','SoftRadius','softRadius','柔化半径',0,100,1,'px',True)
for effect,path,label,hi in [('Glow','GlowStrength','强度',3),('Vignette','VignetteStrength','强度',1),('Noise','NoiseStrength','强度',.3),('Scanlines','ScanlineStrength','强度',.35),('Glitch','GlitchStrength','强度',1),('Glitch','ClusterBlend','集群混合',1),('LargeGlitch','LargeGlitchBlend','混合比例',1),('Dots','DotStrength','强度',1),('DotRipple','RippleDotStrength','强度',1),('Blur','BlurStrength','强度',1),('Distortion','DistortionStrength','强度',1)]:
    add(effect,path,path[0].lower()+path[1:],label,-1 if effect=='Distortion' else 0,hi,100,'%',True)
add('Glow','GlowThreshold','glowThreshold','发光阈值',0,1,100,'%')
for path,label,lo,hi,scale,unit,phase in [('ScanlineDensity','密度',1,260,1,'',False),('ScanlineWidth','线宽',.25,12,1,'px',False),('ScanlineSoftness','柔和度',0,1,100,'%',False),('ScanlineAngle','角度',-90,90,1,'°',False),('ScanlineSpeed','运动速度',-60,60,1,'',True),('ScanBendAmplitude','折线幅度',0,80,1,'px',False),('ScanBendSpan','折线跨度',40,800,1,'px',False)]:
    add('Scanlines',path,path[0].lower()+path[1:],label,lo,hi,scale,unit,False,phase)
for effect,path,label,lo,hi,scale,unit in [('Glitch','GlitchCoverage','覆盖',0,1,100,'%'),('Glitch','GlitchRgbOffset','色差偏移',0,80,1,'px'),('Glitch','GlitchHueShift','色相',-180,180,1,'°'),('Glitch','ClusterSize','集群尺寸',.08,.4,100,'%'),('LargeGlitch','LargeGlitchStrength','位移强度',0,1,100,'%'),('LargeGlitch','LargeGlitchArea','覆盖范围',.2,.6,100,'%'),('LargeGlitch','LargeGlitchRgbOffset','色差偏移',0,80,1,'px'),('LargeGlitch','LargeGlitchHueShift','色相',-180,180,1,'°')]:
    add(effect,path,path[0].lower()+path[1:],label,lo,hi,scale,unit)
for effect,prefix in [('Dots','Dot'),('DotRipple','RippleDot')]:
    for suffix,label,lo,hi,scale,unit in [('Spacing','间距',4,40,1,'px'),('Size','大小',.1,1,100,'%'),('Rotation','旋转',0,360,1,'°')]:
        path=prefix+suffix;add(effect,path,path[0].lower()+path[1:],label,lo,hi,scale,unit)
add('Dots','DotGlowStrength','dotGlowStrength','发光强度',0,1,100,'%')
add('Dots','DotSpeed','dotSpeed','运动速度',-2,2,1,'',False,True)
add('DotRipple','DotRippleStrength','dotRippleStrength','扩散强度',0,2,100,'%')
add('DotRipple','DotRippleWidth','dotRippleWidth','扩散宽度',.02,.5,100,'%')
add('DotRipple','DotRippleSpeed','dotRippleSpeed','扩散速度',0,4,1,'',False,True)
add('Blur','BlurRadius','blurRadius','模糊半径',0,100,1,'px')
add('Blur','BlurDirection','blurDirection','方向',-180,180,1,'°')
for suffix,label,lo,hi,scale,unit,phase in [('Scale','纹理尺度',1,20,1,'',False),('Speed','运动速度',-4,4,1,'',True),('CenterX','中心 X',0,1,100,'%',False),('CenterY','中心 Y',0,1,100,'%',False)]:
    add('Distortion','Distortion'+suffix,'distortion'+suffix,label,lo,hi,scale,unit,False,phase)
extra={
 'ZoomTunnel':[('Strength','强度',0,1,100,'%'),('Zoom','缩放倍率',1.1,8,1,'×'),('CenterX','中心 X',0,1,100,'%'),('CenterY','中心 Y',0,1,100,'%')],
 'RainRipples':[('Strength','强度',0,1,100,'%'),('Density','密度',2,10,1,''),('Radius','半径',.2,1,100,'%'),('Width','线宽',.04,.3,100,'%'),('Speed','速度',0,3,1,'')],
 'WaterWave':[('Strength','强度',0,1,100,'%'),('Amplitude','振幅',0,40,1,'px'),('Frequency','频率',1,30,1,''),('Angle','角度',-180,180,1,'°'),('Speed','速度',0,3,1,'')],
 'EdgeOutline':[('Strength','强度',0,1,100,'%'),('Width','线宽',.5,4,1,'px'),('Threshold','阈值',0,1,100,'%')],
 'Pixelate':[('Strength','强度',0,1,100,'%'),('BlockSize','色块尺寸',2,128,1,'px')],
 'SunPrism':[('Strength','混合比例',0,1,100,'%'),('Angle','形状角度',-180,180,1,'°'),('Width','光带宽度',.05,1,100,'%'),('Position','纵向偏移',-1,1,100,'%'),('Speed','速度',0,1,1,'')]
}
for effect,params in extra.items():
    for prop,label,lo,hi,scale,unit in params:
        member=effect[0].lower()+effect[1:]+'.'+prop[0].lower()+prop[1:]
        add(effect,effect+'.'+prop,member,label,lo,hi,scale,unit,prop=='Strength',prop=='Speed')
add('Transform','AudioZoomAmount','audioZoomAmount','音频放大幅度',0,.5,100,'%')
extra['Transform']=[["Scale","整体缩放",0.1,4,100,"%"],["ScaleX","横向缩放",0.25,4,100,"%"],["ScaleY","纵向缩放",0.25,4,100,"%"],["Rotation","旋转角度",-180,180,1,"°"],["PositionX","水平位置",-1,1,100,"%"],["PositionY","垂直位置",-1,1,100,"%"],["SkewX","水平倾斜",-60,60,1,"°"],["SkewY","垂直倾斜",-60,60,1,"°"]]
for prop,label,lo,hi,scale,unit in extra['Transform']:
    add('Transform','Transform.'+prop,'transform.'+prop[0].lower()+prop[1:],label,lo,hi,scale,unit)
names={'Color':'色彩调整','RgbSplit':'RGB 色散','Softening':'柔化','Glow':'辉光','Vignette':'暗角','Noise':'胶片颗粒','Scanlines':'扫描线','Glitch':'数字故障','LargeGlitch':'故障突发','Dots':'动态点阵','DotRipple':'点阵涟漪','Blur':'模糊','Distortion':'镜头扭曲','ZoomTunnel':'缩放穿梭','RainRipples':'粒子 · 雨滴涟漪','WaterWave':'水波','EdgeOutline':'边缘描线','Pixelate':'马赛克','SunPrism':'阳光色散','Transform':'变换'}
guards={e:'fx.'+e[0].lower()+e[1:]+('.enabled' if e in extra else '') for e in names}
guards['Color']='true'
guards['Softening']='false'
cs=['// Generated by scripts/Generate-AudioParameters.py. IDs are append-only.','namespace LumaWall.Contracts;','public static partial class AudioParameters','{','    public static readonly AudioParameter[] All = [']
native=['// Generated by scripts/Generate-AudioParameters.py. IDs are append-only.','#pragma once','#define AUDIO_PARAMETERS(X) \\']
for i,(e,path,member,label,lo,hi,scale,unit,scaled,phase) in enumerate(rows,1):
    neutral=1 if path in ['Contrast','Saturation','Transform.Scale','Transform.ScaleX','Transform.ScaleY'] else 0
    if e!='Softening':cs.append(f'        new({i},EffectKind.{"Particles" if e=="RainRipples" else e},"{path}","{names[e]}","{label}",{lo},{hi},{scale},"{unit}",{str(scaled).lower()},{neutral},{str(phase).lower()}),')
    guard='(fx.transform.enabled||!fx.transformProtocol)' if path=='AudioZoomAmount' else guards[e]
    native.append(f' X({i},{path.replace(".","_")},{member},{guard},{lo},{hi})'+(' \\' if i<len(rows) else ''))
triggers=[('Glitch','数字故障'),('LargeGlitch','故障突发'),('DotRipple','点阵涟漪'),('ZoomTunnel','缩放穿梭'),('Particles','粒子')]
native+=['','#define AUDIO_TRIGGERS(X) \\']
for i,(effect,label) in enumerate(triggers,len(rows)+1):
    cs.append(f'        new({i},EffectKind.{effect},"{effect}.Trigger","{label}","节拍触发",0,1,1,"",false,0,false,true),')
    native.append(f' X({i},{effect}_Trigger)'+(' \\' if i<len(rows)+len(triggers) else ''))
cs+=['    ];','    internal static double ReadValue(int id,VideoEffects fx)=>id switch {']
for i,(_,path,*_) in enumerate(rows,1):cs.append(f'        {i}=>fx.{path},')
for i,_ in enumerate(triggers,len(rows)+1):cs.append(f'        {i}=>0,')
cs+=['        _=>throw new ArgumentException("Unknown audio parameter")','    };','    internal static bool Enabled(EffectKind kind,VideoEffects fx)=>kind switch {']
for effect in names:
    value='fx.GetCollection().ColorEnabled' if effect=='Color' else 'fx.'+effect+('.Enabled' if effect in extra else '')
    if effect=='RainRipples':value='fx.Particles.Enabled'
    cs.append(f'        EffectKind.{"Particles" if effect=="RainRipples" else effect}=>{value},')
cs+=['        _=>false','    };','}']
(root/'src/Shared/Contracts/AudioParameters.Generated.cs').write_text('\n'.join(cs)+'\n',encoding='utf-8')
(root/'src/Windows/WallpaperHost/AudioParameters.Generated.h').write_text('\n'.join(native)+'\n',encoding='utf-8')
print(f'Generated {len(rows)} audio parameters across {len(names)} groups')
