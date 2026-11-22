namespace LumaWall.Contracts;

public sealed record StartupDelayOptions(bool Enabled=false,int Seconds=10)
{
    public StartupDelayOptions Validate()
    {
        if(Seconds is <1 or >3600)throw new ArgumentException("启动延时须为 1–3600 秒");
        return this;
    }
    public int EffectiveSeconds=>Enabled?Seconds:0;
}
