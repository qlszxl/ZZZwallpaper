namespace LumaWall.Contracts;
public sealed record PackageEffectSetting(string Id,bool Enabled=true,double Strength=1);
public sealed partial record VideoEffects
{
    public PackageEffectSetting[] PackageEffects {get;init;}=[];
    private void ValidatePackageEffects(){
        if(PackageEffects is {Length:0})return;
        if(PackageEffects==null||PackageEffects.Length>16||PackageEffects.Any(p=>p==null||string.IsNullOrWhiteSpace(p.Id)||p.Id.Length>129
            ||p.Id.Split('/').Length!=2||!double.IsFinite(p.Strength)||p.Strength<0||p.Strength>1)
            ||PackageEffects.Select(p=>p.Id).Distinct().Count()!=PackageEffects.Length)throw new ArgumentException("扩展效果配置无效。");
    }
    private string PackageWire(){
        if(PackageEffects.Length==0||Intensity<=0)return "";
        var installed=FeaturePacks.Effects.ToDictionary(p=>p.Key);
        var tokens=new List<string>();
        foreach(var setting in PackageEffects){
            if(!setting.Enabled||setting.Strength<=0||!installed.TryGetValue(setting.Id,out var item))continue;
            tokens.Add(FormattableString.Invariant($"{item.Generation} {item.Entry.Implementation} {setting.Strength:F6} {(item.Entry.Animated?1:0)}"));
        }
        return tokens.Count==0?"":" "+tokens.Count+" "+string.Join(" ",tokens);
    }
}
