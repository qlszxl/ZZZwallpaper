using System.Globalization;
using System.Text.Json;

namespace LumaWall.Contracts;

public sealed record AudioRule
{
    public string Parameter {get;init;}="GlowStrength";
    public bool Enabled {get;init;}=true;
    public int Band {get;init;}
    public int Response {get;init;}
    public double Weak {get;init;}
    public double Strong {get;init;}=.8;
    public double? Sensitivity {get;init;}
    public double? Attack {get;init;}
    public double? Release {get;init;}
    public double? Threshold {get;init;}
    public bool ThresholdSwitch {get;init;}
    public double SwitchThreshold {get;init;}=.5;
    public bool TriggerLimitEnabled {get;init;}
    public double TriggerLimitWindow {get;init;}=1;
    public int TriggerLimitCount {get;init;}=1;
    public AudioRule Validate()
    {
        var p=AudioParameters.Find(Parameter);
        static void Range(double? value,double min,double max) {if(value is { } v&&(!double.IsFinite(v)||v<min||v>max))throw new ArgumentException("音频联动数值超出范围");}
        Range(Weak,p.Minimum,p.Maximum);Range(Strong,p.Minimum,p.Maximum);
        if(Band is <0 or >3||Response is <0 or >1)throw new ArgumentException("音频联动响应无效");
        Range(Sensitivity,.25,5);Range(Attack,.005,1);Range(Release,.02,2);Range(Threshold,.05,.95);
        Range(SwitchThreshold,.01,1);Range(TriggerLimitWindow,.1,60);
        if(TriggerLimitCount is <1 or >100)throw new ArgumentException("触发次数须为 1–100 次");
        return this;
    }
}

public sealed record AudioParameter(int Id,EffectKind Effect,string Key,string EffectName,string Name,double Minimum,double Maximum,double DisplayScale,string Unit,bool Scaled,double Neutral,bool Phase,bool Trigger=false)
{
    [System.Text.Json.Serialization.JsonIgnore] public string DisplayName => UiLanguage.Text(Name);
    [System.Text.Json.Serialization.JsonIgnore] public string DisplayEffectName => UiLanguage.Text(EffectName);
    [System.Text.Json.Serialization.JsonIgnore] public string DisplayUnit => UiLanguage.Text(Unit);
    public double Read(VideoEffects effects)=>AudioParameters.ReadValue(Id,effects);
    public bool IsEnabled(VideoEffects effects)
    {
        var collection=effects.GetCollection();
        if(!collection.Added.HasFlag(Effect)||collection.Strength(Effect)<=0)return false;
        return AudioParameters.Enabled(Effect,effects);
    }
}

public static partial class AudioParameters
{
    public static bool TriggerEffect(EffectKind effect)=>effect is EffectKind.Glitch or EffectKind.LargeGlitch or EffectKind.DotRipple or EffectKind.ZoomTunnel or EffectKind.Particles;
    private static AudioParameter[]? selectable;
    public static IReadOnlyList<AudioParameter> Selectable=>selectable??=All.Where(p=>p.Trigger||!TriggerEffect(p.Effect)).ToArray();
    // Canonicalize legacy parameter links once at the model boundary. Explicit
    // trigger rows take precedence; otherwise retain the first enabled old row.
    public static AudioRule[] Normalize(AudioRule[] rules)
    {
        if(rules is null)return null!;
        rules=rules.Where(r=>r?.Parameter!="SoftRadius").ToArray();
        Validate(rules);
        var result=new List<AudioRule>();
        foreach(var group in rules.GroupBy(r=> {var p=Find(r.Parameter);return TriggerEffect(p.Effect)?p.Effect+".Trigger":p.Key;})) {
            var parameter=Find(group.Key);
            var rule=group.FirstOrDefault(r=>r.Parameter==group.Key)??group.FirstOrDefault(r=>r.Enabled)??group.First();
            result.Add(parameter.Trigger?rule with {Parameter=group.Key,Response=1,Weak=0,Strong=1,ThresholdSwitch=false,Attack=null,Release=rule.Release??(parameter.Effect is EffectKind.Glitch or EffectKind.DotRipple or EffectKind.ZoomTunnel?.35:null)}:rule);
        }
        return result.OrderBy(r=>Find(r.Parameter).Id).ToArray();
    }

    public static AudioParameter Find(string key)=>All.FirstOrDefault(p=>p.Key==key)??throw new ArgumentException("不支持的音频联动参数："+key);
    public static AudioRule[] Validate(AudioRule[] rules)
    {
        if(rules is null||rules.Length>128)throw new ArgumentException("音频联动规则最多 128 条");
        var keys=new HashSet<string>(StringComparer.Ordinal);
        foreach(var rule in rules) {
            if(rule is null||!keys.Add(rule.Parameter))throw new ArgumentException("音频联动参数重复");
            rule.Validate();
        }
        return rules;
    }
    public static AudioRule[] Merge(AudioRule[] screen,AudioRule[] asset)=>screen.Where(r=>!asset.Any(a=>a.Parameter==r.Parameter)).Concat(asset).OrderBy(r=>Find(r.Parameter).Id).ToArray();
    public static AudioRule[] Upsert(AudioRule[] rules,AudioRule rule)=>Merge(rules,Normalize([rule.Validate()]));
    // Store only edited rows; unrelated inherited rows remain linked to the screen.
    public static AudioRule[] ApplyDelta(AudioRule[] own,AudioRule[] before,AudioRule[] after)
    {
        var next=own.ToDictionary(r=>r.Parameter,StringComparer.Ordinal);
        foreach(var old in before)if(!after.Any(r=>r.Parameter==old.Parameter))next.Remove(old.Parameter);
        foreach(var row in after)if(before.FirstOrDefault(r=>r.Parameter==row.Parameter)!=row)next[row.Parameter]=row;
        return next.Values.OrderBy(r=>Find(r.Parameter).Id).ToArray();
    }
    public static AudioRule[] ForRendering(VideoEffects effects)=>effects.AudioRules.Where(r=>Find(r.Parameter).IsEnabled(effects)).Select(r=> {
        var p=Find(r.Parameter);double scale=p.Scaled?effects.GetCollection().Strength(p.Effect):1;
        if(scale==1)return r;
        double Scale(double value)=>Math.Clamp(p.Neutral+(value-p.Neutral)*scale,p.Minimum,p.Maximum);
        return r with {Weak=Scale(r.Weak),Strong=Scale(r.Strong)};
    }).ToArray();
}

public sealed partial record VideoEffects
{
    [System.Text.Json.Serialization.JsonIgnore] public bool AudioLegacy {get=>false;init {}}
    public double AudioAttack {get;init;}=.018;
    private AudioRule[] audioRules=[];
    public AudioRule[] AudioRules {
        get=>audioRules;
        init=>audioRules=AudioParameters.Normalize(value);
    }
    private void ValidateAudioRules()
    {
        if(!double.IsFinite(AudioAttack)||AudioAttack is <.005 or >1)throw new ArgumentException("音频响应速度超出范围");
        AudioParameters.Validate(AudioRules);
    }
    // Wire v37 adds an ID-keyed tail, preserving every earlier positional slot.
    private string AudioRuleBehaviorWire()=>" "+AudioRules.Length.ToString(CultureInfo.InvariantCulture)+string.Concat(AudioRules.Select(r=>FormattableString.Invariant(
        $" {AudioParameters.Find(r.Parameter).Id} {(r.ThresholdSwitch?1:0)} {r.SwitchThreshold:F6} {(r.TriggerLimitEnabled?1:0)} {r.TriggerLimitWindow:F6} {r.TriggerLimitCount}")));
    private string AudioRulesWire()=>FormattableString.Invariant($" {(AudioLegacy?1:0)} {AudioAttack:F6} {AudioRules.Length}")+string.Concat(AudioRules.Select(r=>FormattableString.Invariant(
        $" {AudioParameters.Find(r.Parameter).Id} {(r.Enabled?1:0)} {r.Band} {r.Response} {r.Weak:F6} {r.Strong:F6} {r.Sensitivity??AudioSensitivity:F6} {r.Attack??AudioAttack:F6} {r.Release??AudioSmoothing:F6} {r.Threshold??AudioBeatThreshold:F6}")));
}
