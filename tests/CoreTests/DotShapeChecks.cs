using LumaWall.Contracts;
internal static class DotShapeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(var shape in Enum.GetValues<DotShape>()) {
            var fx=new VideoEffects {Collection=new() {Added=EffectKind.Dots|EffectKind.DotRipple|EffectKind.AudioReactive},Dots=true,DotRipple=true,DotShape=shape,DotRotation=137,RippleDotShape=(DotShape)(7-(int)shape),RippleDotRotation=281,AudioReactive=true,AudioRules=[new() {Parameter="DotStrength",Strong=.7}]};
            var restored=VideoEffects.FromJson(fx.ToJson());check(restored.ToJson()==fx.ToJson(),"independent regular and ripple shapes/angles persist");
            check(restored.ForRendering().DotShape==shape&&restored.ForRendering().RippleDotRotation==281&&restored.ForRendering().AudioRules.Single().Parameter=="DotStrength","audio and effect intensity retain configured silhouettes");
            var fields=restored.ToWire().Split(' ');check(fields[0]=="37"&&fields[151]==((int)shape).ToString()&&fields[152]=="137.000000"&&fields[153]==(7-(int)shape).ToString()&&fields[154]=="281.000000","native wire preserves main and ripple shape/angle in the compatible prefix");
        }
        var old=VideoEffects.FromJson("{\"Version\":15,\"Dots\":true,\"DotRipple\":true,\"DotSize\":0.6,\"RippleDotSize\":0.9}");
        check(old.Version==VideoEffects.Original.Version&&old.DotShape==DotShape.Circle&&old.RippleDotShape==DotShape.Circle&&old.DotRotation==0&&old.RippleDotRotation==0&&old.DotSize==.6&&old.RippleDotSize==.9,"v15 migrates to circular dots without changing dimensions");
        foreach(var bad in new[]{new VideoEffects {DotShape=(DotShape)8},new VideoEffects {RippleDotShape=(DotShape)(-1)},new VideoEffects {DotRotation=double.NaN},new VideoEffects {DotRotation=361},new VideoEffects {RippleDotRotation=-1},new VideoEffects {RippleDotRotation=double.PositiveInfinity}}) {
            bool rejected=false;try {bad.Validate();}catch(ArgumentException) {rejected=true;}check(rejected,"invalid dot silhouette and angles rejected before rendering");
        }
    }
}
