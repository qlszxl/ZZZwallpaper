using LumaWall.Contracts;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private bool effectsReady,loadingEffects;
    private long effectWriteSerial;
    private readonly EffectEditSession effectSession=new();
    private ulong? effectControlState;
    private readonly GlitchLayerEditor[] glitchLayers=new GlitchLayerEditor[6];
    private VideoEffects DraftEffects => new VideoEffects {
        ZoomTunnel=effectSession.ExtendedDraft.ZoomTunnel,
        RainRipples=effectSession.ExtendedDraft.RainRipples,Particles=effectSession.ExtendedDraft.Particles,
        WaterWave=effectSession.ExtendedDraft.WaterWave,
        EdgeOutline=effectSession.ExtendedDraft.EdgeOutline,
        Pixelate=effectSession.ExtendedDraft.Pixelate,
        SunPrism=effectSession.ExtendedDraft.SunPrism,
        LightFlare=effectSession.ExtendedDraft.LightFlare,
        Transform=effectSession.ExtendedDraft.Transform,

        ColoredNoise=effectSession.ExtendedDraft.ColoredNoise,RippleColor=effectSession.ExtendedDraft.RippleColor,BlurMethod=effectSession.ExtendedDraft.BlurMethod,BlurSamples=effectSession.ExtendedDraft.BlurSamples,BlurDirection=effectSession.ExtendedDraft.BlurDirection,BlurCenterX=effectSession.ExtendedDraft.BlurCenterX,BlurCenterY=effectSession.ExtendedDraft.BlurCenterY,Masks=effectSession.ExtendedDraft.Masks,EffectMasks=effectSession.ExtendedDraft.EffectMasks,
        PackageEffects=effectSession.ExtendedDraft.PackageEffects,Collection=effectSession.Collection,Intensity=effectSession.LegacyIntensity,Scanlines=ScanlineSwitch.IsOn,ScanlineStrength=ScanlineSlider.Value/100,ScanlineDensity=DensitySlider.Value,
        RgbSplit=RgbSwitch.IsOn,RgbOffset=RgbSlider.Value/effectRgbScale,
        Brightness=BrightnessSlider.Value/100,Contrast=ContrastSlider.Value/100,Saturation=SaturationSlider.Value/100,
        Vignette=VignetteSwitch.IsOn,VignetteStrength=VignetteSlider.Value/100,Glow=GlowSwitch.IsOn,GlowStrength=GlowSlider.Value/100,
        GlowThreshold=ThresholdSlider.Value/100,Noise=NoiseSwitch.IsOn,NoiseStrength=NoiseSlider.Value/100,
        ScanlineMotion=ScanMotionSwitch.IsOn,ScanlineSpeed=ScanSpeedSlider.Value,Glitch=GlitchSwitch.IsOn,
        GlitchStrength=GlitchStrengthSlider.Value/100,GlitchRate=GlitchRateSlider.Value,GlitchCoverage=GlitchCoverageSlider.Value/100,
        LargeGlitchRandomize=LargeGlitchRandomizeSwitch.IsOn,LargeGlitchVariation=LargeGlitchVariationSlider.Value/100,
        LargeGlitch=LargeGlitchSwitch.IsOn,LargeGlitchMode=Math.Clamp(LargeGlitchModePicker.SelectedIndex,0,3),LargeGlitchArea=LargeGlitchAreaSlider.Value/100,
        LargeGlitchStrength=LargeGlitchStrengthSlider.Value/100,LargeGlitchRate=LargeGlitchRateSlider.Value,LargeGlitchBlend=LargeGlitchBlendSlider.Value/100,
        LargeGlitchRgbOffset=LargeGlitchRgbSlider.Value,GlitchColorShift=GlitchColorShiftSwitch.IsOn,GlitchHueShift=GlitchHueSlider.Value,GlitchRandomHue=GlitchRandomHueSwitch.IsOn,
        LargeGlitchColorShift=LargeGlitchColorShiftSwitch.IsOn,LargeGlitchHueShift=LargeGlitchHueSlider.Value,LargeGlitchRandomHue=LargeGlitchRandomHueSwitch.IsOn,
        LargeGlitchInterval=LargeGlitchIntervalSlider.Value,LargeGlitchDuration=LargeGlitchDurationSlider.Value,
        LargeGlitchSmallCount=(int)Math.Round(LargeGlitchSmallCountSlider.Value),LargeGlitchSmallStrength=LargeGlitchSmallStrengthSlider.Value/100,
        DotShape=(DotShape)Math.Max(0,dotShapePicker.SelectedIndex),DotRotation=dotRotationSlider.Value,RippleDotShape=(DotShape)Math.Max(0,rippleShapePicker.SelectedIndex),RippleDotRotation=rippleRotationSlider.Value,
        Dots=DotSwitch.IsOn,DotStrength=DotStrengthSlider.Value/100,DotSpacing=DotSpacingSlider.Value,DotSize=DotSizeSlider.Value/100,
        DotGlow=DotGlowSwitch.IsOn,DotGlowStrength=DotGlowSlider.Value/100,DotMotion=DotMotionSwitch.IsOn,DotSpeed=DotSpeedSlider.Value,DotPulse=DotPulseSlider.Value/100,
        GlitchRgbSplit=GlitchRgbSwitch.IsOn,GlitchRgbOffset=GlitchRgbSlider.Value,GlitchCluster=GlitchClusterSwitch.IsOn,
        Layer1=glitchLayers[0].Value,Layer2=glitchLayers[1].Value,Layer3=glitchLayers[2].Value,Layer4=glitchLayers[3].Value,Layer5=glitchLayers[4].Value,Layer6=glitchLayers[5].Value,
        DotRipple=DotRippleSwitch.IsOn,DotRippleStrength=RippleStrengthSlider.Value/100,DotRippleSpeed=RippleSpeedSlider.Value,DotRippleWidth=RippleWidthSlider.Value/100,DotRippleCount=(int)Math.Round(RippleCountSlider.Value),
        RippleDotStrength=RippleDotStrengthSlider.Value/100,RippleDotSpacing=RippleDotSpacingSlider.Value,RippleDotSize=RippleDotSizeSlider.Value/100,RippleDotGlow=RippleDotGlowSwitch.IsOn,
        ClusterBlend=ClusterBlendSlider.Value/100,ClusterSize=ClusterSizeSlider.Value/100,ClusterSmallCount=(int)Math.Round(ClusterSmallCountSlider.Value),
        ScanlineBends=ScanBendSwitch.IsOn,ScanBendAmplitude=ScanBendAmplitudeSlider.Value,ScanBendSpan=ScanBendSpanSlider.Value,ScanBendDensity=ScanBendDensitySlider.Value/100,
        Blur=BlurSwitch.IsOn,BlurRadius=BlurRadiusSlider.Value,BlurStrength=BlurStrengthSlider.Value/100,
        BlurMotion=false,BlurMotionMode=Math.Clamp(BlurMotionModePicker.SelectedIndex,0,2),BlurMotionSpeed=BlurMotionSpeedSlider.Value,
        BlurCoverage=BlurCoverageSlider.Value/100,BlurScale=BlurScaleSlider.Value,BlurFeather=BlurFeatherSlider.Value/100,BlurAngle=BlurAngleSlider.Value,
        Distortion=DistortionSwitch.IsOn,DistortionMode=Math.Clamp(DistortionModePicker.SelectedIndex,0,2),DistortionStrength=DistortionStrengthSlider.Value/100,DistortionScale=DistortionScaleSlider.Value,
        DistortionMotion=DistortionMotionSwitch.IsOn,DistortionSpeed=DistortionSpeedSlider.Value,DistortionCenterX=DistortionCenterXSlider.Value/100,DistortionCenterY=DistortionCenterYSlider.Value/100,
        ScanlineWidth=ScanWidthSlider.Value,ScanlineSoftness=ScanSoftnessSlider.Value/100,ScanlineAngle=ScanAngleSlider.Value,
        AudioAttack=effectSession.ExtendedDraft.AudioAttack,AudioRules=effectSession.ExtendedDraft.AudioRules,
        AudioReactive=AudioReactiveSwitch.IsOn,AudioSource=Math.Clamp(AudioSourcePicker.SelectedIndex,0,1),AudioBand=Math.Clamp(AudioBandPicker.SelectedIndex,0,3),
        AudioSensitivity=AudioSensitivitySlider.Value,AudioSmoothing=AudioSmoothingSlider.Value/1000,AudioBeatThreshold=AudioBeatThresholdSlider.Value/100,AudioAmount=AudioAmountSlider.Value/100,
        AudioZoomAmount=effectSession.ExtendedDraft.AudioZoomAmount
    }.Validate();
    private double effectRgbScale=1;
    private void InitializeEffects()
    {
        for(int i=0;i<glitchLayers.Length;i++) {var editor=new GlitchLayerEditor(i);editor.Changed+=DraftChanged;glitchLayers[i]=editor;GlitchLayerPanel.Children.Add(editor);}
        effectsReady=true;
        effectSession.PreviewRequested+=PreviewEffectChanges;
        effectSession.SaveRequested+=()=>FlushEffectChanges(background:true);
        effectSession.Initialize((delay,callback)=>new DispatcherEditTimer(DispatcherQueue,delay,callback));
        Closed+=(_,_)=>effectSession.Dispose();
        InitializeEffectsPage();SetEffectControls(VideoEffects.Original);
    }
    private void SetEffectControls(VideoEffects value)
    {
        loadingEffects=true;var rgbScreen=SelectedScreen??System.Windows.Forms.Screen.PrimaryScreen;effectRgbScale=Math.Max(1,rgbScreen?.Bounds.Height??1080)/1080d;RgbSlider.Maximum=Math.Max(1,(rgbScreen?.Bounds.Width??1920)/10d);effectSession.Collection=value.GetCollection();effectSession.LegacyIntensity=value.Intensity;
        ScanlineSwitch.IsOn=value.Scanlines;ScanlineSlider.Value=value.ScanlineStrength*100;DensitySlider.Value=value.ScanlineDensity;
        RgbSwitch.IsOn=value.RgbSplit;RgbSlider.Value=Math.Min(RgbSlider.Maximum,value.RgbOffset*effectRgbScale);
        BrightnessSlider.Value=value.Brightness*100;ContrastSlider.Value=value.Contrast*100;SaturationSlider.Value=value.Saturation*100;
        VignetteSwitch.IsOn=value.Vignette;VignetteSlider.Value=value.VignetteStrength*100;GlowSwitch.IsOn=value.Glow;GlowSlider.Value=value.GlowStrength*100;
        ThresholdSlider.Value=value.GlowThreshold*100;NoiseSwitch.IsOn=value.Noise;NoiseSlider.Value=value.NoiseStrength*100;
        ScanMotionSwitch.IsOn=value.ScanlineMotion;ScanSpeedSlider.Value=value.ScanlineSpeed;GlitchSwitch.IsOn=value.Glitch;
        GlitchStrengthSlider.Value=value.GlitchStrength*100;GlitchRateSlider.Value=value.GlitchRate;GlitchCoverageSlider.Value=value.GlitchCoverage*100;
        LargeGlitchSwitch.IsOn=value.LargeGlitch;LargeGlitchModePicker.SelectedIndex=value.LargeGlitchMode;LargeGlitchAreaSlider.Value=value.LargeGlitchArea*100;
        LargeGlitchStrengthSlider.Value=value.LargeGlitchStrength*100;LargeGlitchRateSlider.Value=value.LargeGlitchRate;LargeGlitchBlendSlider.Value=value.LargeGlitchBlend*100;
        LargeGlitchRgbSlider.Value=value.LargeGlitchRgbOffset;GlitchColorShiftSwitch.IsOn=value.GlitchColorShift;GlitchHueSlider.Value=value.GlitchHueShift;GlitchRandomHueSwitch.IsOn=value.GlitchRandomHue;
        LargeGlitchColorShiftSwitch.IsOn=value.LargeGlitchColorShift;LargeGlitchHueSlider.Value=value.LargeGlitchHueShift;LargeGlitchRandomHueSwitch.IsOn=value.LargeGlitchRandomHue;
        LargeGlitchIntervalSlider.Value=value.LargeGlitchInterval;LargeGlitchDurationSlider.Value=value.LargeGlitchDuration;
        LargeGlitchSmallCountSlider.Value=value.LargeGlitchSmallCount;LargeGlitchSmallStrengthSlider.Value=value.LargeGlitchSmallStrength*100;
        dotShapePicker.SelectedIndex=(int)value.DotShape;dotRotationSlider.Value=value.DotRotation;rippleShapePicker.SelectedIndex=(int)value.RippleDotShape;rippleRotationSlider.Value=value.RippleDotRotation;
        DotSwitch.IsOn=value.Dots;DotStrengthSlider.Value=value.DotStrength*100;DotSpacingSlider.Value=value.DotSpacing;DotSizeSlider.Value=value.DotSize*100;
        DotGlowSwitch.IsOn=value.DotGlow;DotGlowSlider.Value=value.DotGlowStrength*100;DotMotionSwitch.IsOn=value.DotMotion;DotSpeedSlider.Value=value.DotSpeed;DotPulseSlider.Value=value.DotPulse*100;
        GlitchRgbSwitch.IsOn=value.GlitchRgbSplit;GlitchRgbSlider.Value=value.GlitchRgbOffset;GlitchClusterSwitch.IsOn=value.GlitchCluster;
        var layers=value.Layers;for(int i=0;i<glitchLayers.Length;i++)glitchLayers[i].Load(layers[i]);
        DotRippleSwitch.IsOn=value.DotRipple;RippleStrengthSlider.Value=value.DotRippleStrength*100;RippleSpeedSlider.Value=value.DotRippleSpeed;RippleWidthSlider.Value=value.DotRippleWidth*100;RippleCountSlider.Value=value.DotRippleCount;
        RippleDotStrengthSlider.Value=value.RippleDotStrength*100;RippleDotSpacingSlider.Value=value.RippleDotSpacing;RippleDotSizeSlider.Value=value.RippleDotSize*100;RippleDotGlowSwitch.IsOn=value.RippleDotGlow;
        ClusterBlendSlider.Value=value.ClusterBlend*100;ClusterSizeSlider.Value=value.ClusterSize*100;ClusterSmallCountSlider.Value=value.ClusterSmallCount;
        ScanBendSwitch.IsOn=value.ScanlineBends;ScanBendAmplitudeSlider.Value=value.ScanBendAmplitude;ScanBendSpanSlider.Value=value.ScanBendSpan;ScanBendDensitySlider.Value=value.ScanBendDensity*100;
        BlurSwitch.IsOn=value.Blur;BlurRadiusSlider.Value=value.BlurRadius;BlurStrengthSlider.Value=value.BlurStrength*100;
        BlurMotionSwitch.IsOn=value.BlurMotion;BlurMotionModePicker.SelectedIndex=value.BlurMotionMode;BlurMotionSpeedSlider.Value=value.BlurMotionSpeed;
        BlurCoverageSlider.Value=value.BlurCoverage*100;BlurScaleSlider.Value=value.BlurScale;BlurFeatherSlider.Value=value.BlurFeather*100;BlurAngleSlider.Value=value.BlurAngle;
        DistortionSwitch.IsOn=value.Distortion;DistortionModePicker.SelectedIndex=value.DistortionMode;DistortionStrengthSlider.Value=value.DistortionStrength*100;DistortionScaleSlider.Value=value.DistortionScale;
        DistortionMotionSwitch.IsOn=value.DistortionMotion;DistortionSpeedSlider.Value=value.DistortionSpeed;DistortionCenterXSlider.Value=value.DistortionCenterX*100;DistortionCenterYSlider.Value=value.DistortionCenterY*100;
        ScanWidthSlider.Value=value.ScanlineWidth;ScanSoftnessSlider.Value=value.ScanlineSoftness*100;ScanAngleSlider.Value=value.ScanlineAngle;
        AudioReactiveSwitch.IsOn=value.AudioReactive;AudioSourcePicker.SelectedIndex=value.AudioSource;AudioBandPicker.SelectedIndex=value.AudioBand;
        AudioSensitivitySlider.Value=value.AudioSensitivity;AudioSmoothingSlider.Value=value.AudioSmoothing*1000;
        AudioBeatThresholdSlider.Value=value.AudioBeatThreshold*100;AudioAmountSlider.Value=value.AudioAmount*100;
        taskbarSwitch.IsOn=effectSession.Collection.HideTaskbarEnabled;
        taskbarStylePicker.SelectedIndex=(int)effectSession.Collection.TaskbarStyle;SyncTaskbarSettings();
        LargeGlitchRandomizeSwitch.IsOn=value.LargeGlitchRandomize;LargeGlitchVariationSlider.Value=value.LargeGlitchVariation*100;
        SyncExtendedEffects(value);RefreshAudioRules();
        loadingEffects=false;UpdateEffectControlStates();RefreshEffectCollection();
    }
    private void LoadEffectDraft(bool force=false,bool preserveView=false)
    {
        if(!effectsReady)return;
        if(effectTargetAssetId!=null&&EffectTargetAsset==null)effectTargetAssetId=null;
        string? key=SelectedScreen==null?null:SelectedScreen.DeviceName+"|"+(effectTargetAssetId??"screen")+"|"+SelectedScreen.Bounds.Size;
        if(!force&&key==effectSession.Key)return;
        if(navigationPage=="display")RefreshEffectsContext();
        bool keepView=preserveView&&key==effectSession.Key&&navigationPage=="display"&&selectedEffectKind!=null;
        var previousEffect=selectedEffectKind;double previousScroll=EffectsParameterScroll.VerticalOffset;
        if(keepView)CaptureEffectDisclosures();
        EndEffectEditing();if(!keepView)ResetEffectsNavigation();effectSession.ChangeTarget(key);
        WallpaperProfile? saved=null;draftOverrides=0;
        try {
            if(SelectedScreen!=null) {
                if(effectTargetAssetId is { } assetId) {
                    var assetProfile=modules.Effects.AssetProfile(assetId);draftOverrides=assetProfile.Overrides;
                }
                saved=modules.Effects.EditingProfile(SelectedScreen.DeviceName,effectTargetAssetId);
            }
        } catch(Exception ex) {ShowError(UiLanguage.Text("读取画面方案失败：")+ex.Message,ex);}
        SyncHomeFill(saved?.Fill??FillMode.Cover);
        SetEffectControls(saved?.Effects??VideoEffects.Original);
        effectSession.AdoptOriginal(DraftEffects);
        if(keepView&&selectedEffectKind==previousEffect) {
            RestoreEffectDisclosures();EffectsParameterScroll.UpdateLayout();EffectsParameterScroll.ChangeView(null,previousScroll,null,true);
        } else if(keepView)EffectsParameterScroll.ChangeView(null,0,null,true);
        SetEffectsSaveStatus("");
    }
    private void UpdateEffectControlStates()
    {
        bool[] flags=[AudioReactiveSwitch.IsOn,ScanlineSwitch.IsOn,RgbSwitch.IsOn,false,VignetteSwitch.IsOn,GlowSwitch.IsOn,NoiseSwitch.IsOn,
            LargeGlitchSwitch.IsOn,LargeGlitchRandomizeSwitch.IsOn,LargeGlitchSmallCountSlider.Value>0,GlitchSwitch.IsOn,GlitchColorShiftSwitch.IsOn,GlitchRandomHueSwitch.IsOn,
            LargeGlitchColorShiftSwitch.IsOn,LargeGlitchRandomHueSwitch.IsOn,BlurSwitch.IsOn,BlurMotionSwitch.IsOn,DistortionSwitch.IsOn,DistortionMotionSwitch.IsOn,
            ScanMotionSwitch.IsOn,GlitchClusterSwitch.IsOn,DotSwitch.IsOn,DotGlowSwitch.IsOn,DotMotionSwitch.IsOn,GlitchRgbSwitch.IsOn,ScanBendSwitch.IsOn,DotRippleSwitch.IsOn,RippleDotGlowSwitch.IsOn];
        ulong state=((ulong)(DistortionModePicker.SelectedIndex+1)<<40)|((ulong)(1+(int)Math.Round(ClusterSmallCountSlider.Value))<<48);
        for(int i=0;i<flags.Length;i++)if(flags[i])state|=1UL<<i;
        if(effectControlState==state)return;effectControlState=state;
        ScanlineSlider.IsEnabled=DensitySlider.IsEnabled=ScanlineSwitch.IsOn;RgbSlider.IsEnabled=RgbSwitch.IsOn;
        VignetteSlider.IsEnabled=VignetteSwitch.IsOn;GlowSlider.IsEnabled=ThresholdSlider.IsEnabled=GlowSwitch.IsOn;NoiseSlider.IsEnabled=NoiseSwitch.IsOn;
        LargeGlitchRandomizeSwitch.IsEnabled=LargeGlitchSwitch.IsOn;LargeGlitchVariationSlider.IsEnabled=LargeGlitchSwitch.IsOn&&LargeGlitchRandomizeSwitch.IsOn;
        LargeGlitchModePicker.IsEnabled=LargeGlitchAreaSlider.IsEnabled=LargeGlitchStrengthSlider.IsEnabled=LargeGlitchRateSlider.IsEnabled=LargeGlitchBlendSlider.IsEnabled=LargeGlitchRgbSlider.IsEnabled=LargeGlitchSwitch.IsOn;
        LargeGlitchIntervalSlider.IsEnabled=LargeGlitchDurationSlider.IsEnabled=LargeGlitchSmallCountSlider.IsEnabled=LargeGlitchSwitch.IsOn;
        LargeGlitchSmallStrengthSlider.IsEnabled=LargeGlitchSwitch.IsOn&&LargeGlitchSmallCountSlider.Value>0;
        GlitchColorShiftSwitch.IsEnabled=GlitchSwitch.IsOn||LargeGlitchSwitch.IsOn;
        GlitchRandomHueSwitch.IsEnabled=(GlitchSwitch.IsOn||LargeGlitchSwitch.IsOn)&&GlitchColorShiftSwitch.IsOn;
        GlitchHueSlider.IsEnabled=(GlitchSwitch.IsOn||LargeGlitchSwitch.IsOn)&&GlitchColorShiftSwitch.IsOn&&!GlitchRandomHueSwitch.IsOn;
        LargeGlitchColorShiftSwitch.IsEnabled=LargeGlitchSwitch.IsOn;
        LargeGlitchRandomHueSwitch.IsEnabled=LargeGlitchSwitch.IsOn&&LargeGlitchColorShiftSwitch.IsOn;
        LargeGlitchHueSlider.IsEnabled=LargeGlitchSwitch.IsOn&&LargeGlitchColorShiftSwitch.IsOn&&!LargeGlitchRandomHueSwitch.IsOn;
        BlurRadiusSlider.IsEnabled=BlurStrengthSlider.IsEnabled=BlurSwitch.IsOn;
        BlurMotionSwitch.IsEnabled=BlurSwitch.IsOn;
        BlurMotionModePicker.IsEnabled=BlurMotionSpeedSlider.IsEnabled=BlurCoverageSlider.IsEnabled=BlurScaleSlider.IsEnabled=BlurFeatherSlider.IsEnabled=BlurAngleSlider.IsEnabled=BlurSwitch.IsOn&&BlurMotionSwitch.IsOn;
        DistortionModePicker.IsEnabled=DistortionStrengthSlider.IsEnabled=DistortionCenterXSlider.IsEnabled=DistortionCenterYSlider.IsEnabled=DistortionSwitch.IsOn;
        DistortionScaleSlider.IsEnabled=DistortionSwitch.IsOn&&DistortionModePicker.SelectedIndex==0;DistortionMotionSwitch.IsEnabled=DistortionSwitch.IsOn&&DistortionModePicker.SelectedIndex!=1;
        DistortionSpeedSlider.IsEnabled=DistortionSwitch.IsOn&&DistortionModePicker.SelectedIndex!=1&&DistortionMotionSwitch.IsOn;
        ScanWidthSlider.IsEnabled=ScanSoftnessSlider.IsEnabled=ScanAngleSlider.IsEnabled=ScanlineSwitch.IsOn;
        ScanMotionSwitch.IsEnabled=ScanlineSwitch.IsOn;ScanSpeedSlider.IsEnabled=ScanlineSwitch.IsOn&&ScanMotionSwitch.IsOn;
        GlitchStrengthSlider.IsEnabled=GlitchRateSlider.IsEnabled=GlitchCoverageSlider.IsEnabled=GlitchSwitch.IsOn&&!GlitchClusterSwitch.IsOn;
        DotStrengthSlider.IsEnabled=DotSpacingSlider.IsEnabled=DotSizeSlider.IsEnabled=DotGlowSwitch.IsEnabled=DotMotionSwitch.IsEnabled=DotSwitch.IsOn;
        DotGlowSlider.IsEnabled=DotSwitch.IsOn&&DotGlowSwitch.IsOn;DotSpeedSlider.IsEnabled=DotPulseSlider.IsEnabled=DotSwitch.IsOn&&DotMotionSwitch.IsOn;
        GlitchClusterSwitch.IsEnabled=GlitchSwitch.IsOn;GlitchRgbSwitch.IsEnabled=GlitchSwitch.IsOn&&!GlitchClusterSwitch.IsOn;
        GlitchRgbSlider.IsEnabled=GlitchSwitch.IsOn&&!GlitchClusterSwitch.IsOn&&GlitchRgbSwitch.IsOn;
        for(int i=0;i<glitchLayers.Length;i++) {if(effectsPageReady)ParameterInputGate.Apply(glitchLayers[i],GlitchSwitch.IsOn&&GlitchClusterSwitch.IsOn,refresh:false);glitchLayers[i].Visibility=i<=(int)Math.Round(ClusterSmallCountSlider.Value)?Visibility.Visible:Visibility.Collapsed;}
        ClusterBlendSlider.IsEnabled=ClusterSizeSlider.IsEnabled=ClusterSmallCountSlider.IsEnabled=GlitchSwitch.IsOn&&GlitchClusterSwitch.IsOn;
        ScanBendSwitch.IsEnabled=ScanlineSwitch.IsOn;ScanBendAmplitudeSlider.IsEnabled=ScanBendSpanSlider.IsEnabled=ScanBendDensitySlider.IsEnabled=ScanlineSwitch.IsOn&&ScanBendSwitch.IsOn;
        RippleSpeedSlider.IsEnabled=RippleWidthSlider.IsEnabled=RippleCountSlider.IsEnabled=RippleDotStrengthSlider.IsEnabled=RippleDotSpacingSlider.IsEnabled=RippleDotSizeSlider.IsEnabled=RippleDotGlowSwitch.IsEnabled=DotRippleSwitch.IsOn;
        RippleStrengthSlider.IsEnabled=DotRippleSwitch.IsOn&&RippleDotGlowSwitch.IsOn;
    }
    private long tracedEffectGesture=-1;
    private string? tracedEffectTarget;
    private OperationContext? tracedEffectOperation;
    private void DraftChanged()
    {
        if(!effectsReady||loadingEffects)return;
        if(SelectedScreen is not { } screen) {effectSession.NotifyDraftChanged();return;}
        string traceTarget=screen.DeviceName+":"+effectTargetAssetId;
        if(tracedEffectGesture!=store.CurrentUndoGesture||tracedEffectTarget!=traceTarget||tracedEffectOperation==null) {
            tracedEffectGesture=store.CurrentUndoGesture;tracedEffectTarget=traceTarget;
            tracedEffectOperation=OperationTrace.Current??OperationTrace.Begin("display","effect_edit",selectedEffectKind?.ToString()??"effects");
        }
        OperationTrace.Record(tracedEffectOperation,"effect_draft","changed",$"revision={effectSession.Revision+1}; gesture={store.CurrentUndoGesture}",sampledEvent:true);
        effectSession.Queue(screen.DeviceName,effectTargetAssetId,DraftFill,DraftEffects,applying>0,store.CurrentUndoGesture,tracedEffectOperation);
        SetEffectsSaveStatus(UiLanguage.Text("正在同步画面参数…"));
    }

    private void RefreshEffectDraftUi()
    {
        if(!effectSession.ConsumeUiChange())return;
        UpdateEffectControlStates();
        GlitchBasicSection.Visibility=GlitchClusterSwitch.IsOn?Visibility.Collapsed:Visibility.Visible;
        GlitchClusterSection.Visibility=GlitchClusterSwitch.IsOn?Visibility.Visible:Visibility.Collapsed;
        syncingEffectInstance=true;
        try {glitchMode.SelectedIndex=GlitchClusterSwitch.IsOn?1:0;}finally {syncingEffectInstance=false;}
        if(selectedEffectKind==EffectKind.AudioReactive)RefreshAudioResponseDefaults();
        if(selectedEffectKind==EffectKind.Particles)RefreshParticleFields();
        if(selectedEffectKind==EffectKind.LightFlare)RefreshLightFlareFields();

        // The values already update in their own controls. Only the current
        // effect's enable/strength controls need synchronizing during a drag.
        if(selectedEffectKind is { } kind) {
            var definition=effectDefinitions.First(d=>d.Kind==kind);
            syncingEffectInstance=true;
            try {effectEnabled.IsOn=EffectIsEnabled(definition);effectStrength.Value=effectSession.Collection.Strength(kind)*100;RefreshEffectParameterAccess(definition,refresh:false);}
            finally {syncingEffectInstance=false;}
        } else RefreshEffectCollection();
    }
    private void PreviewEffectChanges()
    {
        effectSession.StopPreview();RefreshEffectDraftUi();
        if(!effectSession.NeedsPreview||effectSession.Pending is not { } update)return;
        // Preparing material overrides reads saved profiles. Do not turn a
        // throttled preview into a synchronous join of a disk transaction.
        // The completion callback resumes the latest preview.
        if(store.HasPendingEffectWrite)return;
        using var trace=OperationTrace.Enter(update.Operation);
        try {
            wallpapers?.PreviewEffectDraft(modules.Effects.Prepare(update.ScreenId,update.AssetId,new(update.Fill,update.Effects),update.Before));
            effectSession.MarkPreviewed(update);
        }catch(Exception ex) {ShowError(UiLanguage.Text("画面预览未能同步：")+ex.Message,ex);}
    }
    private void FlushEffectChanges(bool background=false,bool capturePending=true)
    {
        if(capturePending) {effectSession.StopSchedule();effectSession.QueuePendingSave();}
        if(background&&!effectSession.HasQueuedSaves)return;
        if(background) {if(!store.TryCompleteEffectWrite())return;}
        else store.CompleteEffectWrite();
        RefreshEffectDraftUi();
        while(effectSession.TakeQueuedSave() is { } update) {
          using var trace=OperationTrace.Enter(update.Operation);
          OperationTrace.Record(update.Operation,"effect_save","started",$"revision={update.Revision}; gesture={update.Gesture}");
          try {
                var prepared=modules.Effects.Prepare(update.ScreenId,update.AssetId,new(update.Fill,update.Effects),update.Before);
                if(background&&effectSession.Revision==update.Revision) {wallpapers?.PreviewEffectDraft(prepared);effectSession.MarkPreviewed(update);}
                var write=store.QueueEffectWrite(prepared,update.Gesture);
                ++effectWriteSerial;
                AdoptSavedEffectMasks(update,prepared.Editing);
                if(background) {
                    SetEffectsSaveStatus(UiLanguage.Text("正在保存画面参数…"));
                    _=FinishEffectWrite(write,update);
                    return;
                }
            store.CompleteEffectWrite(write);
            OperationTrace.Record(update.Operation,"effect_save","completed",$"revision={update.Revision}; persistence only, rendering tracked separately");
            wallpapers?.RefreshLiveEffects(update.AssetId==null?EffectChange.Screen(update.ScreenId):EffectChange.Asset(update.AssetId));
            if(effectSession.Revision==update.Revision&&effectTargetAssetId==update.AssetId&&SelectedScreen?.DeviceName==update.ScreenId) {
                draftOverrides=update.AssetId==null?0:modules.Effects.AssetProfile(update.AssetId).Overrides;
                var saved=modules.Effects.EditingProfile(update.ScreenId,update.AssetId);
                string? previousMask=selectedMaskId;
                var remapped=update.Effects.EffectMasks.FirstOrDefault(p=>p.Value==selectedMaskId);
                if(remapped.Key!=0&&saved.Effects.EffectMasks.TryGetValue(remapped.Key,out var replacement))selectedMaskId=replacement;
                effectSession.ExtendedDraft=effectSession.ExtendedDraft with {Masks=saved.Effects.Masks,EffectMasks=saved.Effects.EffectMasks};
                effectSession.Collection=effectSession.Collection with {Layers=saved.Effects.GetCollection().Layers};
                if(previousMask!=selectedMaskId)RefreshMaskLibrary();
                effectSession.AdoptOriginal(DraftEffects);
                bool visible=update.AssetId==null?wallpapers?.Running.ContainsKey(update.ScreenId)==true:wallpapers?.Running.Values.Any(p=>p.Asset.Id==update.AssetId)==true;
                SetEffectsSaveStatus(visible?UiLanguage.Text("已应用并保存"):update.AssetId==null?UiLanguage.Text("已保存显示器效果 · 应用壁纸后生效"):UiLanguage.Text("已保存素材效果 · 应用此素材后生效"));
                RefreshEffectCollection();
            }
            RefreshStatus();
          } catch(Exception ex) {OperationTrace.Fault(update.Operation,"effect_save",ex.ToString());effectSession.RestoreQueuedSave(update);SetEffectsSaveStatus(UiLanguage.Text("参数同步失败，请重新调整后重试。"));ShowError(UiLanguage.Text("画面参数未能同步：")+ex.Message,ex);return;}
        }
    }
    private void AdoptSavedEffectMasks(EffectUpdate update,WallpaperProfile saved)
    {
        if(effectTargetAssetId!=update.AssetId||SelectedScreen?.DeviceName!=update.ScreenId||effectSession.Revision!=update.Revision)return;
        string? previousMask=selectedMaskId;
        var remapped=update.Effects.EffectMasks.FirstOrDefault(p=>p.Value==selectedMaskId);
        if(remapped.Key!=0&&saved.Effects.EffectMasks.TryGetValue(remapped.Key,out var replacement))selectedMaskId=replacement;
        effectSession.ExtendedDraft=effectSession.ExtendedDraft with {Masks=saved.Effects.Masks,EffectMasks=saved.Effects.EffectMasks};
        effectSession.Collection=effectSession.Collection with {Layers=saved.Effects.GetCollection().Layers};
        effectSession.AdoptOriginal(DraftEffects);
        if(previousMask!=selectedMaskId)RefreshMaskLibrary();
    }
    private async Task FinishEffectWrite(Task write,EffectUpdate update)
    {
        bool failed=false;
        using var trace=OperationTrace.Enter(update.Operation);
        try {
            await write;
            if(libraryShutdown)return;
            store.CompleteEffectWrite(write);
            OperationTrace.Record(update.Operation,"effect_save","completed",$"revision={update.Revision}; persistence only, rendering tracked separately");
            // A newer gesture may already have started. Its preview must win.
            if(effectSession.Revision!=update.Revision||effectTargetAssetId!=update.AssetId||SelectedScreen?.DeviceName!=update.ScreenId)return;
            draftOverrides=update.AssetId==null?0:modules.Effects.AssetProfile(update.AssetId).Overrides;
            bool visible=update.AssetId==null?wallpapers?.Running.ContainsKey(update.ScreenId)==true:wallpapers?.Running.Values.Any(p=>p.Asset.Id==update.AssetId)==true;
            SetEffectsSaveStatus(visible?UiLanguage.Text("已应用并保存"):update.AssetId==null?UiLanguage.Text("已保存显示器效果 · 应用壁纸后生效"):UiLanguage.Text("已保存素材效果 · 应用此素材后生效"));RefreshEffectCollection();
        }catch(Exception ex) {
            if(libraryShutdown)return;
            try {store.CompleteEffectWrite(write);}catch { }
            // Later material deltas may use this draft as their baseline. Keep
            // the failed gesture ahead of them, and wait for another explicit
            // edit/flush rather than automatically retrying a failing worker.
            OperationTrace.Fault(update.Operation,"effect_save",ex.ToString());failed=true;effectSession.RestoreQueuedSave(update);effectSession.StopSchedule();
            SetEffectsSaveStatus(UiLanguage.Text("参数保存失败，请重新调整后重试。"));ShowError(UiLanguage.Text("画面参数未能保存：")+ex.Message,ex);
        } finally {
            if(!libraryShutdown&&!failed) {
                if(effectSession.HasQueuedSaves)FlushEffectChanges(background:true,capturePending:false);
                else if(effectSession.NeedsPreview)PreviewEffectChanges();
            }
        }
    }
    private async Task FlushEffectChangesAsync()
    {
        effectSession.StopSchedule();effectSession.QueuePendingSave();
        while(store.HasPendingEffectWrite||effectSession.HasQueuedSaves) {
            await store.CompleteEffectWriteAsync();
            if(libraryShutdown)return;
            if(effectSession.HasQueuedSaves) {
                long before=effectWriteSerial;
                FlushEffectChanges(background:true,capturePending:false);
                if(effectSession.HasQueuedSaves&&before==effectWriteSerial)
                    throw new InvalidOperationException(UiLanguage.Text("尚有未保存的画面参数。"));
            }
        }
        await store.CompleteEffectWriteAsync();
    }
    public void EndEffectEditing() {ClearEffectLayerDrag();StopEffectImage();FlushEffectChanges();wallpapers?.ClearEffectSolo();CloseEffectsAdd(false);CloseEffectsTarget(false);RefreshEffectCollection();}
    private void EffectSlider_Changed(object sender,RangeBaseValueChangedEventArgs e)=>DraftChanged();
    private void Effect_Toggled(object sender,RoutedEventArgs e) {
        if(effectsReady&&!loadingEffects&&ReferenceEquals(sender,NoiseSwitch)&&NoiseSwitch.IsOn&&NoiseSlider.Value==0)NoiseSlider.Value=.5;
        DraftChanged();
    }
    private void DistortionMode_Changed(object sender,SelectionChangedEventArgs e)=>DraftChanged();
    private void BlurMotionMode_Changed(object sender,SelectionChangedEventArgs e)=>DraftChanged();
    private void LargeGlitchMode_Changed(object sender,SelectionChangedEventArgs e)=>DraftChanged();
    private void AudioSource_Changed(object sender,SelectionChangedEventArgs e)=>DraftChanged();
    private void AudioBand_Changed(object sender,SelectionChangedEventArgs e)=>DraftChanged();
    private void Fill_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(!effectsReady||loadingEffects||SelectedScreen is not { } screen)return;
        try {
            if(FillPicker.SelectedIndex==2) {SyncHomeFill();homePositionFlyout?.ShowAt(HomePositionButton);return;}
            var fill=DraftFill;FlushEffectChanges();FlushHomePosition();
            using var history=store.BeginUndoBatch("修改画面填充");
            var baseline=modules.Effects.ScreenProfile(screen.DeviceName)??new WallpaperProfile(FillMode.Cover,VideoEffects.Original);
            modules.Effects.SaveScreenProfile(screen.DeviceName,baseline with {Fill=fill});SaveHomePosition(screen.DeviceName,new());
            wallpapers?.RefreshLiveEffects(EffectChange.Screen(screen.DeviceName));
        }catch(Exception ex) {ShowError(UiLanguage.Text("保存填充方式失败：")+ex.Message,ex);}
    }
    private void ResetColor_Click(object sender,RoutedEventArgs e) {SetEffectControls(DraftEffects with {Brightness=0,Contrast=1,Saturation=1});DraftChanged();}
    public void OpenEffectsForScreen(string device)
    {
        SelectScreen(device);
        if(wallpapers?.Running.TryGetValue(device,out var current)==true) {ClearLibraryFilter();RefreshLibrary(current.Asset.Id);}
        EndEffectEditing();LoadEffectDraft(true);Navigate("display");
    }
}
