namespace LumaWall.Automation;

// Independent from transport limits: checkpoints stay inside this application
// instance and include its embedded image/media resources.
public static class ConfigurationCheckpointBudget
{
    public const int MaxBytes=64*1024*1024;
    public static void Validate(long encodedBytes)
    {
        if(encodedBytes<0||encodedBytes>MaxBytes)
            throw new OperationFault("invalid_state","检查点超过 64 MiB 限制",null);
    }
}
