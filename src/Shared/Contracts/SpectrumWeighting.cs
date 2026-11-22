namespace LumaWall.Contracts;

public sealed record SpectrumWeightPoint(double Position,double Weight);

public sealed record SpectrumWeighting
{
    public bool Enabled {get;init;}
    public bool SyncWithMain {get;init;}
    public bool Smooth {get;init;}=true;
    public SpectrumWeightPoint[] Points {get;init;}=SpectrumWeightCurve.Gaussian();
    public bool Equals(SpectrumWeighting? other)=>ReferenceEquals(this,other)||other!=null&&Enabled==other.Enabled&&SyncWithMain==other.SyncWithMain&&Smooth==other.Smooth&&(ReferenceEquals(Points,other.Points)||Points!=null&&other.Points!=null&&Points.SequenceEqual(other.Points));
    public override int GetHashCode() {int hash=HashCode.Combine(Enabled,SyncWithMain,Smooth);if(Points!=null)foreach(var point in Points)hash=HashCode.Combine(hash,point);return hash;}
    public void Validate()
    {
        if(Points==null||Points.Length is <2 or >32)throw new ArgumentException("加权曲线需要 2–32 个关键点");
        double previous=-1;
        foreach(var point in Points) {
            if(point==null||!double.IsFinite(point.Position)||!double.IsFinite(point.Weight)||point.Position is <0 or >1||point.Weight is <0 or >1||point.Position-previous<.001-1e-9)
                throw new ArgumentException("加权关键点需从左到右排列，位置和权重须为 0–100%，相邻位置至少相隔 0.1%");
            previous=point.Position;
        }
        if(Points[0].Position!=0||Points[^1].Position!=1)throw new ArgumentException("加权曲线必须保留左右两个端点");
    }
}

public static class SpectrumWeightCurve
{
    public static SpectrumWeightPoint[] Flat()=>[new(0,1),new(1,1)];
    public static SpectrumWeightPoint[] Gaussian()=>Enumerable.Range(0,17).Select(i=> {
        double position=i/16d,x=position*2-1;
        return new SpectrumWeightPoint(position,i is 0 or 16?0:Math.Clamp((Math.Exp(Math.Log(.05)*x*x)-.05)/.95,0,1));
    }).ToArray();
    public static SpectrumWeighting Resolve(SpectrumOptions main,SpectrumOptions layer,bool child)=>child&&layer.Weighting.SyncWithMain?main.Weighting:layer.Weighting;
    public static double Evaluate(SpectrumWeighting curve,double position)
    {
        var points=curve.Points;position=Math.Clamp(position,0,1);
        if(position<=points[0].Position)return points[0].Weight;
        if(position>=points[^1].Position)return points[^1].Weight;
        int left=0;while(left+1<points.Length-1&&position>points[left+1].Position)left++;
        var a=points[left];var b=points[left+1];double width=b.Position-a.Position,t=(position-a.Position)/width;
        if(!curve.Smooth)return a.Weight+(b.Weight-a.Weight)*t;
        double Secant(int i)=>(points[i+1].Weight-points[i].Weight)/(points[i+1].Position-points[i].Position);
        double Slope(int i) {
            if(i==0)return Secant(0);if(i==points.Length-1)return Secant(i-1);
            double before=Secant(i-1),after=Secant(i);
            // Harmonic slopes preserve peaks/valleys without overshooting the
            // neighbouring keyframe percentages, including unequal intervals.
            return before*after<=0?0:2*before*after/(before+after);
        }
        double t2=t*t,t3=t2*t;
        return Math.Clamp((2*t3-3*t2+1)*a.Weight+(t3-2*t2+t)*width*Slope(left)+(-2*t3+3*t2)*b.Weight+(t3-t2)*width*Slope(left+1),Math.Min(a.Weight,b.Weight),Math.Max(a.Weight,b.Weight));
    }
    public static double[] Sample(SpectrumWeighting curve,int count)
    {
        if(count<1)return [];
        var weights=new double[count];for(int i=0;i<count;i++)weights[i]=curve.Enabled?Evaluate(curve,count==1?.5:i/(count-1d)):1;
        return weights;
    }
}
