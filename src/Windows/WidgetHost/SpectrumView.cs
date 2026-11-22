using LumaWall.Contracts;
using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;
using E=System.Windows.Media.Effects;
namespace LumaWall.WidgetHost;
internal sealed class SpectrumView : C.Panel
{
    private sealed class Layer(SpectrumView owner) : W.FrameworkElement
    {
        internal SpectrumOptions Options=new();
        internal double[] Levels=[],Peaks=[],Values=[],PeakValues=[],Weights=[];
        internal bool HasWeights;
        internal M.Brush Fill=M.Brushes.White;
        internal M.Pen Pen=new(M.Brushes.White,2);
        private M.DrawingBrush? segmentMask;
        private (double Width,double Height,double Gap,double Radius,bool Horizontal) segmentMaskKey;
        internal M.DrawingBrush SegmentMask(double width,bool horizontal) {
            var key=(width,Options.SegmentHeight,Options.SegmentGap,Options.Radius,horizontal);
            if(segmentMask!=null&&key==segmentMaskKey)return segmentMask;
            double height=Options.SegmentHeight,pitch=height+Options.SegmentGap,radius=Math.Min(Options.Radius,Math.Min(width,height)/2);
            var cell=horizontal?new W.Rect(0,0,height,width):new W.Rect(0,0,width,height);
            var tile=horizontal?new W.Rect(0,0,pitch,width):new W.Rect(0,0,width,pitch);
            // One cached tile per layer, not thousands of geometry objects for
            // tall spectra. Transparent tile spacing exposes the desktop below.
            segmentMask=new M.DrawingBrush(new M.GeometryDrawing(M.Brushes.White,null,new M.RectangleGeometry(cell,radius,radius))) {
                Viewbox=tile,ViewboxUnits=M.BrushMappingMode.Absolute,Viewport=tile,ViewportUnits=M.BrushMappingMode.Absolute,
                TileMode=M.TileMode.Tile,Stretch=M.Stretch.Fill,AlignmentX=M.AlignmentX.Left,AlignmentY=M.AlignmentY.Top
            };
            segmentMask.Freeze();segmentMaskKey=key;return segmentMask;
        }
        internal void Configure(SpectrumOptions next,SpectrumWeighting weighting) {
            Options=next;
            if(Levels.Length!=next.Bands) {Levels=new double[next.Bands];Peaks=new double[next.Bands];Values=new double[next.Bands];PeakValues=new double[next.Bands];}
            Weights=SpectrumWeightCurve.Sample(weighting,next.Bands);HasWeights=Weights.Any(w=>w>0);
            ApplyWeights();
            var first=(M.Color)M.ColorConverter.ConvertFromString(next.Color);var last=(M.Color)M.ColorConverter.ConvertFromString(next.ColorEnd);
            Fill=next.Gradient?new M.LinearGradientBrush(first,last,new W.Point(0,1),new W.Point(1,0)):new M.SolidColorBrush(first);Fill.Freeze();
            Pen=new M.Pen(Fill,next.StrokeWidth) {StartLineCap=M.PenLineCap.Round,EndLineCap=M.PenLineCap.Round,LineJoin=M.PenLineJoin.Round};Pen.Freeze();
            Opacity=next.Opacity;Visibility=next.Enabled&&next.Opacity>0?W.Visibility.Visible:W.Visibility.Collapsed;
            if(next.Glow&&next.GlowIntensity>0&&next.GlowRadius>0) {
                var effect=new E.DropShadowEffect {ShadowDepth=0,BlurRadius=next.GlowRadius,Opacity=next.GlowIntensity,Color=next.GlowFollowColor?first:(M.Color)M.ColorConverter.ConvertFromString(next.GlowColor),RenderingBias=E.RenderingBias.Performance};
                effect.Freeze();Effect=effect;
            } else Effect=null;
            InvalidateVisual();
        }
        protected override void OnRender(M.DrawingContext dc) {base.OnRender(dc);owner.RenderLayer(dc,this,ActualWidth,ActualHeight);}
        internal void Update(SpectrumSample sample,double dt) {
            for(int i=0;i<Levels.Length;i++) {
                double amplitude=0;int count=sample.Bands.Length;
                if(sample.Available&&count>0) {
                    if(Levels.Length<=count) {int start=i*count/Levels.Length,end=Math.Max(start+1,(i+1)*count/Levels.Length);for(int bin=start;bin<end;bin++)amplitude=Math.Max(amplitude,sample.Bands[bin]);}
                    else {double position=i*(count-1d)/Math.Max(1,Levels.Length-1);int start=(int)position;amplitude=sample.Bands[start]*(1-(position-start))+sample.Bands[Math.Min(count-1,start+1)]*(position-start);}
                }
                double target=SpectrumMotion.Target(amplitude,Options.Sensitivity);
                Levels[i]=SpectrumMotion.Step(Levels[i],target,dt,Options.SmoothingMs/1000.0);Peaks[i]=Math.Max(Levels[i],Peaks[i]-dt*.3);
            }
            ApplyWeights();
            InvalidateVisual();
        }
        private void ApplyWeights() {
            SpectrumEnvelope.Shape(Levels,Values,Weights,Options.Reverse);
            SpectrumEnvelope.Shape(Peaks,PeakValues,Weights,Options.Reverse);
        }
    }
    private SpectrumOptions? options;
    private readonly List<Layer> layers=[];
    private long previous=Environment.TickCount64;
    internal void Configure(SpectrumOptions next) {
        if(next==options)return;options=next;var all=new[]{next}.Concat(next.SubSpectra).ToArray();
        while(layers.Count<all.Length) {var layer=new Layer(this) {IsHitTestVisible=false};layers.Add(layer);Children.Add(layer);}
        while(layers.Count>all.Length) {int last=layers.Count-1;Children.Remove(layers[last]);layers.RemoveAt(last);}
        for(int i=0;i<all.Length;i++)layers[i].Configure(all[i],SpectrumWeightCurve.Resolve(next,all[i],i>0));InvalidateVisual();
    }
    internal void Update(SpectrumSample sample) {
        long now=Environment.TickCount64;double dt=Math.Clamp((now-previous)/1000.0,.001,.25);previous=now;
        foreach(var layer in layers)layer.Update(sample,dt);InvalidateVisual();
    }
    protected override W.Size MeasureOverride(W.Size availableSize) {foreach(var layer in layers)layer.Measure(availableSize);return new W.Size();}
    protected override W.Size ArrangeOverride(W.Size finalSize) {foreach(var layer in layers)layer.Arrange(new W.Rect(finalSize));return finalSize;}
    private void RenderLayer(M.DrawingContext dc,Layer layer,double width,double height) {
        if(width<=0||height<=0)return;
        dc.PushClip(new M.RectangleGeometry(new W.Rect(0,0,width,height)));
        var footprint=SpectrumLayout.Size(options??new());
        double insetX=Math.Max(0,footprint.Width-width),insetY=Math.Max(0,footprint.Height-height);
        {
            var o=layer.Options;var matrix=M.Matrix.Identity;
            matrix.ScaleAt(o.Scale,o.Scale,width/2,height/2);matrix.RotateAt(o.Rotation,width/2,height/2);matrix.Translate(o.OffsetX*width/2,o.OffsetY*height/2);
            dc.PushTransform(new M.MatrixTransform(matrix));
            double layerWidth=Math.Max(1,o.Width-insetX),layerHeight=Math.Max(1,o.Height-insetY);
            dc.PushTransform(new M.TranslateTransform((width-layerWidth)/2,(height-layerHeight)/2));
            DrawLayer(dc,layer,layerWidth,layerHeight);dc.Pop();dc.Pop();
        }
        dc.Pop();
    }
    private static void DrawLayer(M.DrawingContext dc,Layer layer,double width,double height) {
        var o=layer.Options;var values=layer.Values;int n=values.Length;
        // Skip the entire layer, including curves, peak markers and its glow.
        // Zero-height waves/rings/dots must not become a permanent baseline.
        if(!layer.HasWeights||!SpectrumMotion.HasGeometry(values,o.MinimumHeight))return;
        double Extent(int i,double span)=>Math.Min(span*layer.Weights[i],Math.Max(o.MinimumHeight,values[i]*span));
        if(o.Shape is SpectrumShape.Circle or SpectrumShape.RadialBars) {
            double radius=Math.Max(0,Math.Min(width,height)/2-o.StrokeWidth),baseRadius=radius*o.RingRadius,range=radius-baseRadius;var center=new W.Point(width/2,height/2);
            W.Point Polar(double angle,double r)=>new(center.X+Math.Cos(angle)*r,center.Y+Math.Sin(angle)*r);
            if(o.Shape==SpectrumShape.RadialBars) {
                for(int i=0;i<n;i++) {double a=-Math.PI/2+2*Math.PI*i/n,extent=Extent(i,range);double start=o.Mirror?Math.Max(0,baseRadius-extent):baseRadius;
                    if(extent<=0)continue;
                    dc.DrawLine(layer.Pen,Polar(a,start),Polar(a,Math.Min(radius,baseRadius+extent)));}
            } else {
                void Ring(bool inside) {var points=new W.Point[Math.Max(3,n)];for(int i=0;i<points.Length;i++) {double extent=Extent(Math.Min(n-1,i*n/points.Length),range);points[i]=Polar(-Math.PI/2+2*Math.PI*i/points.Length,Math.Clamp(baseRadius+(inside?-1:1)*extent,0,radius));}dc.DrawGeometry(null,layer.Pen,Curve(points,true,null));}
                Ring(false);if(o.Mirror)Ring(true);
            }
            return;
        }
        bool horizontal=o.Direction>=2;double cross=horizontal?height:width,length=horizontal?width:height,cell=cross/n,bar=Math.Max(.1,cell*(1-o.Gap));
        W.Point Point(double x,double y)=>horizontal?new(y,x):new(x,y);
        double Start(double extent)=>o.Mirror?(length-extent)/2:o.Direction is 1 or 2?0:length-extent;
        if(o.Shape is SpectrumShape.Bars or SpectrumShape.SegmentedBars) {
            var mask=o.Shape==SpectrumShape.SegmentedBars?layer.SegmentMask(bar,horizontal):null;
            for(int i=0;i<n;i++) {double extent=Extent(i,length),start=Start(extent),axis=i*cell+(cell-bar)/2;
                if(extent<=0)continue;
                if(mask!=null) {
                    var spans=SpectrumSegments.Layout(length,extent,o.SegmentHeight,o.SegmentGap,o.Mirror,o.Direction is 1 or 2);
                    if(spans.First.Length<=0)continue;
                    void DrawSpan(SpectrumSegmentSpan span) {
                        if(span.Length<=0)return;
                        dc.PushTransform(new M.TranslateTransform(horizontal?span.Start:axis,horizontal?axis:span.Start));
                        dc.PushOpacityMask(mask);
                        dc.DrawRectangle(layer.Fill,null,horizontal?new W.Rect(0,0,span.Length,bar):new W.Rect(0,0,bar,span.Length));
                        dc.Pop();dc.Pop();
                    }
                    DrawSpan(spans.First);DrawSpan(spans.Second);
                } else {
                    var rect=horizontal?new W.Rect(start,axis,extent,bar):new W.Rect(axis,start,bar,extent);
                    dc.DrawRoundedRectangle(layer.Fill,null,rect,Math.Min(o.Radius,bar/2),Math.Min(o.Radius,bar/2));
                }
                if(o.Peaks&&layer.PeakValues[i]>.005) {double peak=layer.PeakValues[i]*length;double pos=o.Mirror?(length-peak)/2:o.Direction is 1 or 2?peak:length-peak;
                    void Mark(double at) {at=Math.Clamp(at,0,Math.Max(0,length-2));dc.DrawRectangle(layer.Fill,null,horizontal?new W.Rect(at,axis,Math.Min(2,length),bar):new W.Rect(axis,at,bar,Math.Min(2,length)));}Mark(pos);if(o.Mirror)Mark(length-pos-2);}
            }return;
        }
        double baseline=o.Mirror?length/2:o.Direction is 1 or 2?o.StrokeWidth:length-o.StrokeWidth;
        void Wave(bool opposite) {
            var points=new W.Point[Math.Max(2,n)];
            for(int i=0;i<points.Length;i++) {double extent=Extent(Math.Min(n-1,i),length)*(o.Mirror?.5:1);double direction=o.Direction is 1 or 2?1:-1;if(opposite)direction=-direction;
                double y=Math.Clamp(baseline+direction*extent,o.StrokeWidth/2,Math.Max(o.StrokeWidth/2,length-o.StrokeWidth/2));points[i]=Point((i+.5)*cross/points.Length,y);}
            if(o.Shape==SpectrumShape.Dots) {for(int i=0;i<points.Length;i++) {if(Extent(Math.Min(n-1,i),length)<=0)continue;dc.DrawEllipse(layer.Fill,null,points[i],Math.Max(.5,o.StrokeWidth),Math.Max(.5,o.StrokeWidth));}}
            else {W.Point[]? basePoints=o.Shape==SpectrumShape.FilledWave?[Point(cross-cell/2,baseline),Point(cell/2,baseline)]:null;dc.DrawGeometry(basePoints==null?null:layer.Fill,layer.Pen,Curve(points,false,basePoints));}
        }
        Wave(false);if(o.Mirror)Wave(true);
    }
    private static M.StreamGeometry Curve(W.Point[] points,bool closed,W.Point[]? ending) {
        var geometry=new M.StreamGeometry();using(var context=geometry.Open()) {
            context.BeginFigure(points[0],ending!=null,closed||ending!=null);
            // Catmull–Rom converted to Beziers for a continuous wave/ring.
            for(int i=0;i<(closed?points.Length:points.Length-1);i++) {
                var p0=points[closed?(i-1+points.Length)%points.Length:Math.Max(0,i-1)];var p1=points[i];var p2=points[(i+1)%points.Length];var p3=points[closed?(i+2)%points.Length:Math.Min(points.Length-1,i+2)];
                context.BezierTo(new W.Point(p1.X+(p2.X-p0.X)/6,p1.Y+(p2.Y-p0.Y)/6),new W.Point(p2.X-(p3.X-p1.X)/6,p2.Y-(p3.Y-p1.Y)/6),p2,true,false);
            }
            if(ending!=null)foreach(var point in ending)context.LineTo(point,true,false);
        }geometry.Freeze();return geometry;
    }
}
