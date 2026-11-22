using LumaWall.Contracts;
using System.Text.Json;
internal static class SpectrumChecks
{
    internal static void Run(Action<bool,string> check)
    {
        WeightingChecks(check);
        ResponseChecks(check);
        check(new SpectrumOptions().MinimumHeight==0,"new spectra default to invisible silence");
        var segmented=new WidgetConfiguration {Widgets=[new() {Kind=WidgetKind.Spectrum,Spectrum=new() {Shape=SpectrumShape.SegmentedBars,SegmentHeight=8,SegmentGap=2,Glow=true,SubSpectra=[new() {Shape=SpectrumShape.SegmentedBars,SegmentHeight=4,SegmentGap=1}]}}]};
        check(WidgetConfiguration.FromJson(segmented.ToJson()).ToJson()==segmented.ToJson(),"main and child segmented shapes and cell dimensions roundtrip independently");
        var oldCells=WidgetConfiguration.FromJson("{\"Version\":12,\"Widgets\":[{\"Kind\":3,\"Spectrum\":{\"Shape\":4,\"SubSpectra\":[{\"Shape\":2}]}}]}");
        check(oldCells.Version==18&&oldCells.Widgets[0].Spectrum is {Shape:SpectrumShape.RadialBars,SegmentHeight:6,SegmentGap:3}&&oldCells.Widgets[0].Spectrum.SubSpectra[0].Shape==SpectrumShape.FilledWave,"v12 migration preserves existing shapes and supplies dormant cell defaults");
        foreach(var invalidCell in new[]{new SpectrumOptions {SegmentHeight=0},new SpectrumOptions {SegmentHeight=41},new SpectrumOptions {SegmentHeight=double.NaN},new SpectrumOptions {SegmentGap=-1},new SpectrumOptions {SegmentGap=21},new SpectrumOptions {SubSpectra=[new() {SegmentGap=double.PositiveInfinity}]}}) {
            bool rejected=false;try {invalidCell.Validate();}catch(ArgumentException) {rejected=true;}check(rejected,"invalid cell dimensions rejected in all layers");
        }
        foreach(bool mirror in new[]{false,true})foreach(bool forward in new[]{false,true}) {
            check(SpectrumSegments.Layout(100,0,6,3,mirror,forward)==default,"zero amplitude never lights a segmented cell");
            foreach(double level in new[]{.01,5.99,6,8.99,15,50,100}) {
                var spans=SpectrumSegments.Layout(100,level,6,3,mirror,forward);
                foreach(var span in new[]{spans.First,spans.Second}) {
                    check(span.Length==0||(span.Start>=0&&span.Start+span.Length<=100&&Math.Abs((span.Length+3)/9-Math.Round((span.Length+3)/9))<1e-9),"segmented geometry contains only whole cells within the canvas");
                }
                if(mirror&&spans.First.Length>0)check(spans.First.Length==spans.Second.Length&&Math.Abs(spans.First.Start+spans.Second.Start+spans.Second.Length-100)<1e-9,"mirror cells remain anchored symmetrically about the centre");
            }
        }
        var firstStep=SpectrumSegments.Layout(100,6,6,3,false,false);
        var sameStep=SpectrumSegments.Layout(100,14.99,6,3,false,false);
        var nextStep=SpectrumSegments.Layout(100,15,6,3,false,false);
        check(firstStep==sameStep&&firstStep.First==new SpectrumSegmentSpan(94,6)&&nextStep.First==new SpectrumSegmentSpan(85,15),"bars step by one whole cell without moving the existing grid");
        check(SpectrumSegments.Layout(100,15,6,3,false,true).First==new SpectrumSegmentSpan(0,15),"downward and rightward bars grow from the leading edge");
        check(SpectrumSegments.Layout(100,100,1,0,false,false).First==new SpectrumSegmentSpan(0,100),"zero gap and full amplitude cover the complete span");
        foreach(int count in new[]{1,2,32,256}) {
            var quiet=new double[count];
            check(!SpectrumMotion.HasGeometry(quiet,0),"silent zero-minimum layers have no geometry for any shape");
            check(SpectrumMotion.HasGeometry(quiet,2),"an explicitly nonzero minimum retains its resting appearance");
            quiet[count/2]=.001;check(SpectrumMotion.HasGeometry(quiet,0),"one active band is sufficient to show its layer");
        }
        foreach(double release in new[]{.05,.18,.8}) {
            double level=1;for(int i=0;i<1000;i++)level=SpectrumMotion.Step(level,0,.02,release);
            check(level==0&&!SpectrumMotion.HasGeometry(new[]{level},0),"silent release ends at exact zero instead of an infinite baseline");
            double resumed=SpectrumMotion.Step(level,.7,.02,release);
            check(resumed>0&&resumed<.7,"audio resumes naturally after complete silence");
        }
        check(SpectrumMotion.Step(.000001,.000001,.02,.18)>0,"small but active input is not discarded by silent-release snapping");
        var explicitMinimum=WidgetConfiguration.FromJson("{\"Version\":12,\"Widgets\":[{\"Kind\":3,\"Spectrum\":{\"MinimumHeight\":2,\"SubSpectra\":[{\"MinimumHeight\":0}]}}]}");
        check(explicitMinimum.Widgets[0].Spectrum.MinimumHeight==2&&explicitMinimum.Widgets[0].Spectrum.SubSpectra[0].MinimumHeight==0,"existing explicit main and child minimum heights stay independent");
        foreach(int count in new[]{1,2,3,32,117,256}) {
            var input=Enumerable.Range(0,count).Select(i=>(i*37%101)/100d).ToArray();var output=new double[count];
            SpectrumEnvelope.Shape(input,output,true);
            check(output.All(double.IsFinite)&&output.All(v=>v is >=0 and <=1),"center profile remains bounded");
            check(Enumerable.Range(0,count).All(i=>output[i]<=SpectrumEnvelope.HeightLimit(i,count)+1e-12),"audio stays under every position's Gaussian height ceiling");
            SpectrumEnvelope.Shape(input,output,false);check(input.SequenceEqual(output),"disabled profile preserves natural frequency order");
            SpectrumEnvelope.Shape(new double[count],output,true);check(output.All(v=>v==0),"center profile never invents audio in silence");
        }
        // A flat signal must give the characteristic normal-distribution ratios,
        // including its curved shoulders and small tails, rather than a parabola.
        var flat=new double[9];SpectrumEnvelope.Shape(Enumerable.Repeat(.8,9).ToArray(),flat,true);
        check(Math.Abs(flat[4]-.8)<1e-12,"Gaussian centre retains uniform signal amplitude");
        check(Math.Abs(flat[3]/flat[4]-(Math.Pow(.05,.0625)-.05)/.95)<1e-12,"quarter-distance Gaussian shoulder");
        check(Math.Abs(flat[2]/flat[4]-(Math.Pow(.05,.25)-.05)/.95)<1e-12,"half-distance Gaussian shoulder");
        check(flat[0]==0&&flat[^1]==0,"Gaussian endpoints are exactly zero");
        foreach(int count in new[]{4,32,117,256}) {
            var bell=new double[count];SpectrumEnvelope.Shape(Enumerable.Repeat(.8,count).ToArray(),bell,true);
            check(Math.Abs(bell.Max()-.8)<1e-12,"odd and even band counts keep equal peak height");
            check(bell[0]==0&&bell[^1]==0,"both tails are zero for odd and even counts");
            check(Enumerable.Range(1,count/2).All(i=>bell[i]>=bell[i-1]),"steady Gaussian rises smoothly towards centre");
            check(Enumerable.Range(count/2,count-count/2-1).All(i=>bell[i]>=bell[i+1]),"steady Gaussian falls smoothly towards edge");
        }
        // Regression for the over-smoothed bell: one active frequency must not
        // energize 255 other bins, and edits must stay local to their own bin.
        var isolated=new double[256];isolated[57]=.75;var isolatedResult=new double[256];
        SpectrumEnvelope.Shape(isolated,isolatedResult,true);
        check(isolatedResult.Count(v=>v>0)==1&&isolatedResult[57]>0,"a single active bin stays a single active bin among 256");
        var detailed=Enumerable.Range(0,256).Select(i=>i%2==0?.1:.9).ToArray();var detailedResult=new double[256];
        SpectrumEnvelope.Shape(detailed,detailedResult,true);
        check(Enumerable.Range(1,254).All(i=>Math.Abs(detailedResult[i]/SpectrumEnvelope.HeightLimit(i,256)-detailed[i])<1e-12)&&detailedResult[0]==0&&detailedResult[^1]==0,"all 256 independent amplitudes survive positional weighting");
        var changed=detailed.ToArray();changed[131]=.25;var changedResult=new double[256];SpectrumEnvelope.Shape(changed,changedResult,true);
        check(Enumerable.Range(0,256).Where(i=>i!=131).All(i=>detailedResult[i]==changedResult[i])&&detailedResult[131]!=changedResult[131],"changing one frequency cannot change another output frequency");
        var glowing=new WidgetConfiguration {Widgets=[new() {Kind=WidgetKind.Spectrum,Spectrum=new() {Glow=true,GlowIntensity=.4,GlowRadius=24,GlowFollowColor=false,GlowColor="#55AAFF",SubSpectra=[new() {Glow=true,GlowIntensity=.9,GlowRadius=8,GlowFollowColor=false,GlowColor="#FF33CC"},new() {Glow=false}]}}]};
        string glowJson=glowing.ToJson();var glowRoundtrip=WidgetConfiguration.FromJson(glowJson);
        check(glowRoundtrip.ToJson()==glowJson,"independent main and child glow settings roundtrip");
        var oldGlow=WidgetConfiguration.FromJson("{\"Version\":11,\"Widgets\":[{\"Kind\":3,\"Spectrum\":{\"SubSpectra\":[{}]}}]}");
        check(oldGlow.Version==18&&!oldGlow.Widgets[0].Spectrum.Glow&&!oldGlow.Widgets[0].Spectrum.SubSpectra[0].Glow,"v11 migration keeps glow disabled on every existing layer");
        foreach(var invalidGlow in new[]{new SpectrumOptions {GlowIntensity=double.NaN},new SpectrumOptions {GlowIntensity=1.1},new SpectrumOptions {GlowRadius=81},new SpectrumOptions {GlowRadius=-1},new SpectrumOptions {GlowColor="red"},new SpectrumOptions {SubSpectra=[new() {GlowRadius=double.PositiveInfinity}]}}) {
            bool rejected=false;try {invalidGlow.Validate();}catch(ArgumentException) {rejected=true;}check(rejected,"invalid glow settings rejected in main and child layers");
        }
        var skewed=new[]{.15,.9,.25,.4,.05};var skewedResult=new double[5];SpectrumEnvelope.Shape(skewed,skewedResult,true);
        check(skewedResult[1]>skewedResult[3],"opposite shoulders preserve their own audio instead of mirroring");
        var reversedResult=new double[5];SpectrumEnvelope.Shape(skewed.Reverse().ToArray(),reversedResult,true);
        check(skewedResult.Zip(reversedResult.Reverse()).All(p=>Math.Abs(p.First-p.Second)<1e-12),"reversing frequency order reverses independent motion");
        var bad=new[]{double.NaN,double.PositiveInfinity,-1,2};var clean=new double[4];SpectrumEnvelope.Shape(bad,clean,true);check(clean.All(v=>double.IsFinite(v)&&v is >=0 and <=1),"malformed samples cannot poison geometry");
        var options=new SpectrumOptions {CenterEmphasis=true,SubSpectra=Enum.GetValues<SpectrumShape>().Select((shape,i)=>new SpectrumOptions {Shape=shape,Bands=117,Color="#55AAFF",Opacity=.4,Scale=.8,OffsetX=.1,Rotation=i*30,CenterEmphasis=true}).ToArray()};
        var state=new WidgetConfiguration {Widgets=[new() {Kind=WidgetKind.Spectrum,Spectrum=options,Layer=7,Locked=true}],LayoutGroups=[]};
        string json=state.ToJson();check(WidgetConfiguration.FromJson(json).ToJson()==json,"composite shapes, order, transforms and colors roundtrip");
        var legacy=WidgetConfiguration.FromJson("{\"Version\":9,\"Widgets\":[{\"Id\":\"s\",\"Kind\":3,\"Layer\":7,\"Locked\":true,\"Spectrum\":{\"Bands\":117,\"Mirror\":true}}]}");
        check(legacy.Version==18&&legacy.Widgets.Single() is {Layer:7,Locked:true,Spectrum:{Bands:117,Mirror:true,Shape:SpectrumShape.Bars,CenterEmphasis:false,SubSpectra.Length:0}},"v9 migration retains the previous spectrum appearance");
        var sized=options with {Width=400,Height=120,SubSpectra=[new() {Width=900,Height=70},new() {Width=240,Height=360,Enabled=false}]};
        check(SpectrumLayout.Size(sized)==(900,360),"independent dimensions determine a stable compound footprint");
        var resized=sized with {Width=300};check(resized.SubSpectra[0].Width==900&&resized.SubSpectra[1].Height==360,"resizing the main layer does not mutate child dimensions");
        var oldComposite=new WidgetConfiguration {Version=10,Widgets=[new() {Kind=WidgetKind.Spectrum,Spectrum=options with {Width=700,Height=200}}]};
        var migrated=WidgetConfiguration.FromJson(JsonSerializer.Serialize(oldComposite));
        check(migrated.Version==18&&migrated.Widgets[0].Spectrum.SubSpectra.All(s=>s.Width==700&&s.Height==200),"v10 children migrate from shared canvas to matching independent dimensions");
        foreach(var invalid in new[]{options with {Opacity=double.NaN},options with {Shape=(SpectrumShape)99},options with {Scale=0},options with {OffsetY=1.1},options with {SubSpectra=new SpectrumOptions[9]},options with {SubSpectra=[new() {SubSpectra=[new()]}]},options with {SubSpectra=[null!]}}) {
            bool rejected=false;try {invalid.Validate();}catch(ArgumentException) {rejected=true;}check(rejected,"invalid or recursive spectrum layers rejected");
        }
    }
    private static void ResponseChecks(Action<bool,string> check)
    {
        foreach(double gain in new[]{.25,1d,1.5,4d}) {
            check(SpectrumMotion.Target(0,gain)==0&&SpectrumMotion.Target(.0001,gain)==0,"silence and the capture noise floor produce exact zero");
            double previous=0;
            for(int step=1;step<=4096;step++) {
                double amplitude=.0001*Math.Pow(80000d,step/4096d);
                double value=SpectrumMotion.Target(amplitude,gain);
                check(double.IsFinite(value)&&value>previous&&value<1,"every stronger in-range input stays distinct, below the ceiling at all supported gains");
                previous=value;
            }
            double knee=.0001+.36/gain;
            check(Math.Abs(SpectrumMotion.Target(knee,gain)-.6)<1e-12,"the soft knee has no height discontinuity");
            double epsilon=1e-8;
            double below=(SpectrumMotion.Target(knee,gain)-SpectrumMotion.Target(knee-epsilon,gain))/epsilon;
            double above=(SpectrumMotion.Target(knee+epsilon,gain)-SpectrumMotion.Target(knee,gain))/epsilon;
            check(Math.Abs(below-above)<1e-5,"the transition to headroom has a continuous slope");
        }
        // These all became exactly 1 in the old logarithmic clamp at gain 4.
        var loud=new[]{.25,.5,.75,1d}.Select(a=>SpectrumMotion.Target(a,4)).ToArray();
        check(loud.Zip(loud.Skip(1)).All(p=>p.Second>p.First)&&loud[^1]-loud[0]>.1,"formerly clipped loud audio retains visible range instead of a flat ceiling");
        foreach(double limit in new[]{0d,.05,.5,1d}) {
            var weighted=new double[loud.Length];SpectrumEnvelope.Shape(loud,weighted,Enumerable.Repeat(limit,loud.Length).ToArray(),false);
            check(weighted.All(v=>v>=0&&v<=limit),"every loud response respects its configured positional height limit");
            check(limit==0?weighted.All(v=>v==0):weighted.Zip(weighted.Skip(1)).All(p=>p.Second>p.First),"scaling the available height preserves loud-input motion, while zero weights stay hidden");
        }
        var isolated=new double[256];isolated[91]=SpectrumMotion.Target(.75,4);var result=new double[256];
        SpectrumEnvelope.Shape(isolated,result,SpectrumWeightCurve.Sample(new() {Enabled=true},256),false);
        check(result.Count(v=>v>0)==1,"headroom processing does not invent motion in neighbouring silent bins");
        check(SpectrumMotion.Step(loud[^1],loud[0],.033,.18)<loud[^1],"a falling loud input immediately releases from its former clipped plateau");
        foreach(double invalid in new[]{-1d,double.NaN,double.PositiveInfinity,double.NegativeInfinity}) {
            check(SpectrumMotion.Target(invalid,1)==0&&SpectrumMotion.Target(.5,invalid)==0,"invalid input cannot poison the response");
        }
        check(SpectrumMotion.Target(.5,0)==0&&double.IsFinite(SpectrumMotion.Target(double.MaxValue,double.MaxValue)),"zero gain and out-of-contract finite values are safely bounded");
    }
    private static void WeightingChecks(Action<bool,string> check)
    {
        var linear=new SpectrumWeighting {Enabled=true,Smooth=false,Points=[new(0,0),new(.25,1),new(.7,.2),new(1,.6)]};
        check(SpectrumWeightCurve.Evaluate(linear,.125)==.5,"linear weighting follows the user keyframes");
        var smooth=linear with {Smooth=true};
        foreach(var curve in new[]{linear,smooth}) {
            curve.Validate();
            foreach(var point in curve.Points)check(Math.Abs(SpectrumWeightCurve.Evaluate(curve,point.Position)-point.Weight)<1e-12,"each keyframe percentage is reached exactly");
            for(int i=0;i<curve.Points.Length-1;i++)for(int step=0;step<=100;step++) {
                var a=curve.Points[i];var b=curve.Points[i+1];double value=SpectrumWeightCurve.Evaluate(curve,a.Position+(b.Position-a.Position)*step/100);
                check(value>=Math.Min(a.Weight,b.Weight)-1e-12&&value<=Math.Max(a.Weight,b.Weight)+1e-12,"interpolation never overshoots adjacent percentages");
            }
        }
        var gaussian=new SpectrumWeighting {Enabled=true};
        check(SpectrumWeightCurve.Evaluate(gaussian,0)==0&&SpectrumWeightCurve.Evaluate(gaussian,1)==0&&SpectrumWeightCurve.Evaluate(gaussian,.5)==1,"normal preset has exact zero ends and a 100 percent centre");
        foreach(int count in new[]{1,2,32,117,256}) {
            var weights=SpectrumWeightCurve.Sample(linear,count);var values=Enumerable.Repeat(.8,count).ToArray();var output=new double[count];
            SpectrumEnvelope.Shape(values,output,weights,false);
            check(Enumerable.Range(0,count).All(i=>Math.Abs(output[i]-.8*weights[i])<1e-12),"all bands use their own screen-position percentage");
            SpectrumEnvelope.Shape(new double[count],output,weights,false);check(output.All(v=>v==0),"weighting never creates audio in silent bins");
            check(SpectrumWeightCurve.Sample(linear with {Enabled=false},count).All(v=>v==1),"disabled weighting yields 100 percent at all positions");
        }
        var reverse=new double[3];SpectrumEnvelope.Shape(new[]{.2,.5,.9},reverse,new[]{1d,.5,0d},true);
        check(reverse.SequenceEqual(new[]{.9,.25,0d}),"reversed audio is weighted at its final display position, not mirrored with the curve");
        var single=new double[256];single[83]=.8;var isolated=new double[256];
        SpectrumEnvelope.Shape(single,isolated,SpectrumWeightCurve.Sample(linear,256),false);
        check(isolated.Count(v=>v>0)==1,"an edited curve cannot mix neighbouring audio bins");
        var child=new SpectrumOptions {Bands=117,Color="#44AAFF",Width=600,Weighting=linear with {SyncWithMain=true}};
        var main=new SpectrumOptions {Bands=32,Weighting=gaussian,SubSpectra=[child]};
        check(SpectrumWeightCurve.Resolve(main,child,true)==gaussian,"synchronized children read the main curve");
        var mutedMain=main with {Weighting=gaussian with {Enabled=false}};
        check(SpectrumWeightCurve.Sample(SpectrumWeightCurve.Resolve(mutedMain,child,true),117).All(v=>v==1),"children follow the main weighting enable state");
        var independent=child with {Weighting=child.Weighting with {SyncWithMain=false}};
        check(SpectrumWeightCurve.Resolve(mutedMain,independent,true)==linear&&independent.Color==child.Color&&independent.Width==600,"disabling sync restores the saved independent curve without changing appearance");
        check(SpectrumWeightCurve.Sample(gaussian,117)[58]==1&&SpectrumWeightCurve.Sample(gaussian,32).Length==32,"different child band counts sample the same normalized curve");
        var saved=new WidgetConfiguration {Widgets=[new() {Kind=WidgetKind.Spectrum,Spectrum=main}]};
        check(WidgetConfiguration.FromJson(saved.ToJson()).ToJson()==saved.ToJson(),"custom curve points, interpolation and sync roundtrip");
        var legacy=WidgetConfiguration.FromJson("{\"Version\":13,\"Widgets\":[{\"Kind\":3,\"Spectrum\":{\"CenterEmphasis\":true,\"Bands\":256,\"SubSpectra\":[{\"CenterEmphasis\":false},{\"CenterEmphasis\":true}]}}]}");
        var migrated=legacy.Widgets[0].Spectrum;
        check(legacy.Version==18&&migrated.Weighting.Enabled&&!migrated.CenterEmphasis&&migrated.Bands==256&&!migrated.SubSpectra[0].Weighting.Enabled&&migrated.SubSpectra[1].Weighting.Enabled&&migrated.SubSpectra.All(s=>!s.Weighting.SyncWithMain),"legacy main and child Gaussian switches migrate independently");
        foreach(var invalid in new[]{linear with {Points=null!},linear with {Points=[]},linear with {Points=[new(.1,0),new(1,1)]},linear with {Points=[new(0,0),new(.5,1),new(.5,.2),new(1,0)]},linear with {Points=[new(0,0),new(.7,1),new(.2,.3),new(1,0)]},linear with {Points=[new(0,double.NaN),new(1,1)]},linear with {Points=[new(0,0),new(1,1.1)]},linear with {Points=[new(0,0),null!,new(1,1)]},linear with {Points=Enumerable.Range(0,33).Select(i=>new SpectrumWeightPoint(i/32d,1)).ToArray()}}) {
            bool rejected=false;try {invalid.Validate();}catch(ArgumentException) {rejected=true;}check(rejected,"invalid or oversized weight keyframes are rejected");
        }
        bool rejectedSync=false;try {(main with {Weighting=gaussian with {SyncWithMain=true}}).Validate();}catch(ArgumentException) {rejectedSync=true;}check(rejectedSync,"main spectrum cannot synchronize with itself");
        check(gaussian==new SpectrumWeighting {Enabled=true},"curves compare by point values rather than array identity");
    }
}
