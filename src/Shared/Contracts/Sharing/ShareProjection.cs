using System.Text.Json;
using System.Text.Json.Nodes;

namespace LumaWall.Contracts.Sharing;

public sealed record ShareSelection
{
    public EffectKind Effects {get;init;}
    public string[] WidgetIds {get;init;}=[];
    public bool Layout {get;init;}=true;
    public bool Parameters {get;init;}=true;
    public bool Visibility {get;init;}=true;
    public bool Text {get;init;}
    public bool Targets {get;init;}
    public bool Images {get;init;}
}

public static class ShareProjection
{
    // Explicit membership is intentional: future user-editable fields must be
    // classified here before export. Never serialize an original widget wholesale.
    public static readonly IReadOnlyDictionary<EffectKind,string> EffectFields=new Dictionary<EffectKind,string> {
        [EffectKind.Color]="Brightness Contrast Saturation",
        [EffectKind.RgbSplit]="RgbSplit RgbOffset",
        [EffectKind.Glow]="Glow GlowStrength GlowThreshold",
        [EffectKind.Vignette]="Vignette VignetteStrength",
        [EffectKind.Noise]="Noise NoiseStrength ColoredNoise",
        [EffectKind.Scanlines]="Scanlines ScanlineStrength ScanlineDensity ScanlineMotion ScanlineSpeed ScanlineBends ScanBendAmplitude ScanBendSpan ScanBendDensity ScanlineWidth ScanlineSoftness ScanlineAngle",
        [EffectKind.Glitch]="Glitch GlitchStrength GlitchRate GlitchCoverage GlitchColorShift GlitchHueShift GlitchRandomHue GlitchRgbSplit GlitchRgbOffset GlitchCluster Layer1 Layer2 Layer3 Layer4 Layer5 Layer6 ClusterBlend ClusterSize ClusterSmallCount",
        [EffectKind.LargeGlitch]="LargeGlitch LargeGlitchRandomize LargeGlitchVariation LargeGlitchMode LargeGlitchArea LargeGlitchStrength LargeGlitchRate LargeGlitchBlend LargeGlitchRgbOffset LargeGlitchInterval LargeGlitchDuration LargeGlitchSmallCount LargeGlitchSmallStrength LargeGlitchColorShift LargeGlitchHueShift LargeGlitchRandomHue",
        [EffectKind.Dots]="DotShape DotRotation Dots DotStrength DotSpacing DotSize DotGlow DotGlowStrength DotMotion DotSpeed DotPulse",
        [EffectKind.DotRipple]="RippleDotShape RippleDotRotation DotRipple DotRippleStrength DotRippleSpeed DotRippleWidth DotRippleCount RippleDotStrength RippleDotSpacing RippleDotSize RippleDotGlow RippleColor",
        [EffectKind.Blur]="Blur BlurRadius BlurStrength BlurMethod BlurDirection BlurCenterX BlurCenterY BlurSamples",
        [EffectKind.Distortion]="Distortion DistortionMode DistortionStrength DistortionScale DistortionMotion DistortionSpeed DistortionCenterX DistortionCenterY",
        [EffectKind.AudioReactive]="AudioReactive AudioSource AudioBand AudioSensitivity AudioSmoothing AudioBeatThreshold AudioAmount AudioAttack AudioRules",
        [EffectKind.MaskLibrary]="",
        [EffectKind.HideTaskbar]="",
        [EffectKind.ZoomTunnel]="ZoomTunnel",
        [EffectKind.Particles]="Particles RainRipples",
        [EffectKind.WaterWave]="WaterWave",
        [EffectKind.EdgeOutline]="EdgeOutline",
        [EffectKind.Pixelate]="Pixelate",
        [EffectKind.SunPrism]="SunPrism",
        [EffectKind.LightFlare]="LightFlare",
        [EffectKind.Transform]="Transform AudioZoomAmount"
    };
    public static bool Supports(EffectKind kind)=>EffectFields.ContainsKey(kind);
    private static JsonObject Node<T>(T value)=>(JsonObject)JsonSerializer.SerializeToNode(value)!;
    private static void Copy(JsonObject from,JsonObject to,string fields) {
        foreach(string field in fields.Split(' ',StringSplitOptions.RemoveEmptyEntries)) {
            if(!from.TryGetPropertyValue(field,out var value))throw new InvalidOperationException("分享字段尚未注册："+field);
            to[field]=value?.DeepClone();
        }
    }
    private static T Project<T>(T source,T baseline,string fields) {var target=Node(baseline);Copy(Node(source),target,fields);return target.Deserialize<T>()!;}

    public static ShareDocument Create(VideoEffects source,WidgetConfiguration state,ShareSelection selection)
    {
        source.Validate();state.Validate();
        var sections=new List<ShareSection>();
        var original=source.GetCollection();var included=selection.Effects&original.Added;
        var effects=Node(VideoEffects.Original);var collection=new EffectCollection();var sourceNode=Node(source);
        foreach(var kind in Enum.GetValues<EffectKind>()) {
            if((included&kind)==0)continue;
            if(!EffectFields.TryGetValue(kind,out var fields))throw new ArgumentException("所选效果尚未支持分享，请升级软件。");
            Copy(sourceNode,effects,fields);
            if(kind==EffectKind.Particles&&effects["Particles"] is JsonObject particles) {
                void FilterLayer(JsonObject layer) {
                    if(!selection.Images) {
                        layer["Image"]="";
                        var choices=layer["RandomStyles"]?.Deserialize<int[]>()??[];
                        choices=choices.Where(id=>id!=7).ToArray();
                        layer["RandomStyles"]=JsonSerializer.SerializeToNode(choices.Length==0?new[]{0}:choices);
                    }
                    if(!selection.Text)layer["ImageName"]="";
                }
                FilterLayer(particles);if(particles["SubLayer"] is JsonObject sub)FilterLayer(sub);
            }
            collection=collection.WithStrength(kind,original.Strength(kind)) with {Added=collection.Added|kind};
            if(kind==EffectKind.Color)collection=collection with {ColorEnabled=original.ColorEnabled};
            if(kind==EffectKind.HideTaskbar)collection=collection with {HideTaskbarEnabled=original.HideTaskbarEnabled,ReleaseTaskbarSpace=original.ReleaseTaskbarSpace,TaskbarStyle=original.TaskbarStyle,TaskbarVisual=original.TaskbarVisual};
        }
        if(included!=0) {
            if(original.HasCustomOrder&&(included&EffectOrder.VisualKinds)!=0)
                collection=collection with {Layers=EffectOrder.Default.Where(kind=>(included&kind)!=0).ToDictionary(kind=>kind,original.GetLayer)};
            var masks=source.Masks.Where(m=>(included&EffectKind.MaskLibrary)!=0||source.EffectMasks.Any(p=>(included&p.Key)!=0&&p.Value==m.Id)).ToArray();
            // Mask names and uploaded images follow the existing privacy selections.
            masks=masks.Select((m,i)=>m with {Name=selection.Text?m.Name:"遮罩 "+(i+1),Image=selection.Images?m.Image:""}).ToArray();
            var ids=masks.Select(m=>m.Id).ToHashSet();
            var links=source.EffectMasks.Where(p=>(included&p.Key)!=0&&ids.Contains(p.Value)).ToDictionary(p=>p.Key,p=>p.Value);
            effects["Masks"]=JsonSerializer.SerializeToNode(masks);effects["EffectMasks"]=JsonSerializer.SerializeToNode(links);
            if(masks.Length>0)collection=collection with {Added=collection.Added|EffectKind.MaskLibrary};
            effects["Collection"]=Node(collection);effects["Intensity"]=source.Intensity;
            sections.Add(new("effects",1,Node(effects.Deserialize<VideoEffects>()!.Validate())));
        }
        var chosen=state.Widgets.Where(w=>selection.WidgetIds.Contains(w.Id,StringComparer.Ordinal)).ToArray();
        if(chosen.Length>0)sections.Add(new("widgets",1,Node(Widgets(state,chosen,selection))));
        return new(1,sections.ToArray());
    }

    private static SpectrumOptions Spectrum(SpectrumOptions source,bool parameters,bool layout)
    {
        var result=new SpectrumOptions();
        if(parameters)result=Project(source,result,"Shape CenterEmphasis StrokeWidth RingRadius Enabled Opacity Bands Sensitivity SmoothingMs Mirror Background Color ColorEnd Gradient Glow GlowIntensity GlowRadius GlowFollowColor GlowColor Gap Radius SegmentHeight SegmentGap MinimumHeight Padding Direction Reverse Peaks");
        if(layout)result=Project(source,result,"Scale OffsetX OffsetY Rotation Width Height");
        if(!parameters)return result;
        return result with {
            Weighting=new() {Enabled=source.Weighting.Enabled,SyncWithMain=source.Weighting.SyncWithMain,Smooth=source.Weighting.Smooth,
                Points=source.Weighting.Points.Select(p=>new SpectrumWeightPoint(p.Position,p.Weight)).ToArray()},
            SubSpectra=source.SubSpectra.Select(s=>Spectrum(s,parameters,layout)).ToArray()
        };
    }

    public static WidgetConfiguration Widgets(WidgetConfiguration state,WidgetInstance[] chosen,ShareSelection options)
    {
        var groups=state.LayoutGroups.Where(g=>options.Layout&&chosen.Any(w=>w.LayoutGroupId==g.Id)).ToArray();
        var groupIds=groups.Select((g,i)=>(g.Id,NewId:"layout-"+(i+1))).ToDictionary(p=>p.Id,p=>p.NewId);
        var cards=state.Shortcuts.Where(c=>options.Targets&&chosen.Any(w=>w.Kind==WidgetKind.Shortcuts&&w.ShortcutId==c.Id)).ToArray();
        var cardIds=cards.Select((c,i)=>(c.Id,NewId:"card-"+(i+1))).ToDictionary(p=>p.Id,p=>p.NewId);
        var shortcutGroups=state.Groups.Where(g=>g.Id=="common"||cards.Any(c=>c.Group==g.Id)||chosen.Any(w=>w.Group==g.Id)).ToArray();
        var shortcutGroupIds=shortcutGroups.Select((g,i)=>(g.Id,NewId:g.Id=="common"?"common":"group-"+(i+1))).ToDictionary(p=>p.Id,p=>p.NewId);
        var widgets=new List<WidgetInstance>();
        foreach(var w in chosen) {
            var item=new WidgetInstance {Id="widget-"+(widgets.Count+1),Kind=w.Kind,Group=shortcutGroupIds[w.Group]};
            if(options.Layout)item=item with {X=w.X,Y=w.Y,Width=w.Width,Height=w.Height,Layer=w.Layer,LayoutOrder=w.LayoutOrder,
                LayoutGroupId=w.LayoutGroupId is { } id&&groupIds.TryGetValue(id,out var mapped)?mapped:null};
            if(options.Visibility)item=item with {Enabled=w.Enabled,Locked=w.Locked,InteractionLocked=w.InteractionLocked};
            var visual=new VisualWidgetOptions {Text=""};
            if(options.Parameters) {
                visual=Project(w.Visual,visual,"FontSize Bold Alignment TextColor FillColor Background FillOpacity Radius Padding BorderWidth BorderColor StickerShape ImageFit FlipHorizontal MaskShape Gradient ClickThrough TimeStyle TimeAppearance");
                item=item with {Opacity=w.Opacity,IconOnly=w.IconOnly,RefreshSeconds=w.RefreshSeconds,Metrics=w.Metrics,Performance=w.Performance};
            }
            if(options.Layout)visual=visual with {Width=w.Visual.Width,Height=w.Visual.Height};
            var search=new SearchWidgetOptions();
            if(options.Layout)search=search with {Width=w.Search.Width,Height=w.Search.Height};
            if(options.Parameters)search=search with {Radius=w.Search.Radius,Engine=w.Search.Engine==SearchEngine.Custom?SearchEngine.Bing:w.Search.Engine};
            if(options.Targets)search=search with {Engine=w.Search.Engine,CustomUrl=w.Search.CustomUrl};
            item=item with {Search=search,Spectrum=Spectrum(w.Spectrum,options.Parameters,options.Layout)};
            if(options.Layout)item=item with {GridOptions=w.GridOptions};
            if(options.Parameters)item=item with {Clock=w.Clock,Countdown=new() {ShowSeconds=w.Countdown.ShowSeconds,CountUpAfterEnd=w.Countdown.CountUpAfterEnd},Focus=new() {FocusMinutes=w.Focus.FocusMinutes,BreakMinutes=w.Focus.BreakMinutes,LongBreakMinutes=w.Focus.LongBreakMinutes,SessionsPerLongBreak=w.Focus.SessionsPerLongBreak,AutoStart=w.Focus.AutoStart,RemainingSeconds=w.Focus.FocusMinutes*60}};
            if(options.Text)item=item with {Countdown=item.Countdown with {Title=w.Countdown.Title,Target=w.Countdown.Target}};
            if(options.Text)visual=visual with {Text=w.Visual.Text};
            if(options.Images) {visual=visual with {Image=w.Visual.Image};item=item with {Icon=w.Icon};}
            if(options.Targets&&w.ShortcutId is { } card&&cardIds.TryGetValue(card,out var newCard))item=item with {ShortcutId=newCard};
            widgets.Add(item with {Visual=visual});
        }
        return new WidgetConfiguration {
            Widgets=widgets.ToArray(),
            LayoutGroups=groups.Select((g,i)=>new WidgetLayoutGroup {Id=groupIds[g.Id],Name=options.Text?g.Name:"组件组 "+(i+1),
                X=g.X,Y=g.Y,Flow=g.Flow,HorizontalAlignment=g.HorizontalAlignment,VerticalAlignment=g.VerticalAlignment,Gap=g.Gap,Columns=g.Columns,Locked=options.Visibility&&g.Locked}).ToArray(),
            Groups=shortcutGroups.Select((g,i)=>new ShortcutGroup(shortcutGroupIds[g.Id],options.Text?g.Name:g.Id=="common"?"常用":"分组 "+(i+1),g.Order)).ToArray(),
            Shortcuts=cards.Select((c,i)=>new Shortcut(cardIds[c.Id],options.Text?c.Name:"快捷卡片 "+(i+1),c.Url,shortcutGroupIds[c.Group],c.Order,
                options.Images?c.Icon:null,c.Kind,c.WorkingDirectory,c.ShowCommandWindow)).ToArray()
        }.Validate();
    }
}
