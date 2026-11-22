using System.Runtime.InteropServices;
using LumaWall.Automation;

namespace LumaWall.ControlCenter;

// Shared by the WinUI editor and desktop timer buttons. Keep the native pointer
// fields: an explicit union size alone does not give INPUT its x64 alignment.
internal static class AutomationInput
{
    [StructLayout(LayoutKind.Sequential)] internal struct MouseData
    {
        public int X,Y;
        public uint Data,Flags,Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyData
    {
        public ushort Key,Scan;
        public uint Flags,Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct HardwareData {public uint Message;public ushort Low,High;}
    [StructLayout(LayoutKind.Explicit)] internal struct Payload
    {
        [FieldOffset(0)] public MouseData Mouse;
        [FieldOffset(0)] public KeyData Keyboard;
        [FieldOffset(0)] public HardwareData Hardware;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Input {public uint Type;public Payload Data;}
    internal readonly record struct SendResult(uint Count,int Error);
    [DllImport("user32.dll",SetLastError=true)] private static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    internal static int Size=>Marshal.SizeOf<Input>();

    internal static Input Mouse(uint flags,int x,int y,System.Drawing.Rectangle desktop,int delta=0)=>new() {
        Data=new() {Mouse=new() {
            X=(int)((x-(long)desktop.X)*65535/Math.Max(1,desktop.Width-1)),
            Y=(int)((y-(long)desktop.Y)*65535/Math.Max(1,desktop.Height-1)),
            Data=unchecked((uint)delta),Flags=flags|0xC000}}
    };
    internal static Input Key(ushort key,bool up=false)=>new() {Type=1,Data=new() {Keyboard=new() {Key=key,Flags=(up?2u:0u)|(key is >=33 and <=40?1u:0u)}}};
    internal static Input Unicode(char value,bool up=false)=>new() {Type=1,Data=new() {Keyboard=new() {Scan=value,Flags=up?6u:4u}}};
    private static SendResult Send(Input[] inputs)
    {
        uint sent=SendInput((uint)inputs.Length,inputs,Size);
        int error=Marshal.GetLastPInvokeError(); // Capture before cleanup or any other native call.
        return new(sent,error);
    }
    internal static uint Dispatch(IReadOnlyList<Input> inputs,Func<Input[],SendResult>? send=null,Func<int,bool>? pressed=null)
    {
        int expected=IntPtr.Size==8?40:28;
        if(Size!=expected||Marshal.OffsetOf<Input>(nameof(Input.Data)).ToInt32()!=(IntPtr.Size==8?8:4))
            throw new InvalidOperationException("原生输入结构布局无效");
        if(inputs.Count==0)throw new OperationFault("invalid_argument","输入序列不能为空","arguments");
        send??=Send;pressed??=key=>(GetAsyncKeyState(key)&0x8000)!=0;
        // Do not turn a permitted key into a system shortcut, or release a
        // mouse button / modifier that the user was already holding.
        var keys=new HashSet<int> {1,2,4,16,17,18,91,92};
        foreach(var input in inputs)if(input.Type==1&&(input.Data.Keyboard.Flags&4)==0)keys.Add(input.Data.Keyboard.Key);
        if(keys.Any(pressed))throw new OperationFault("invalid_state","请先释放鼠标按键和修饰键，再执行界面操作",recovery:"reread_ui_tree");
        var batch=inputs.ToArray();var result=send(batch);
        if(result.Count==batch.Length)return result.Count;
        var releases=PendingReleases(batch,(int)Math.Min(result.Count,(uint)batch.Length));
        var cleanup=releases.Length==0?new SendResult(0,0):send(releases);
        throw new OperationFault("io_error","UI 输入未完整发送，请读取当前状态",recovery:"reread_state",details:new {
            requestedCount=batch.Length,sentCount=result.Count,nativeError=result.Error,inputSize=Size,
            cleanupRequested=releases.Length,cleanupSent=cleanup.Count,cleanupError=cleanup.Error,
            requiresReadback=true
        });
    }
    internal static Input[] PendingReleases(Input[] inputs,int sent)
    {
        var pending=new List<Input>();
        foreach(var input in inputs.Take(sent)) {
            if(input.Type==0) {
                if((input.Data.Mouse.Flags&2)!=0)pending.Add(new() {Data=new() {Mouse=new() {Flags=4}}});
                if((input.Data.Mouse.Flags&4)!=0)pending.RemoveAll(x=>x.Type==0);
            } else if(input.Type==1) {
                var key=input.Data.Keyboard;
                pending.RemoveAll(x=>x.Type==1&&x.Data.Keyboard.Key==key.Key&&x.Data.Keyboard.Scan==key.Scan&&(x.Data.Keyboard.Flags&4)==(key.Flags&4));
                if((key.Flags&2)==0) {var release=input;release.Data.Keyboard.Flags|=2;pending.Add(release);}
            }
        }
        pending.Reverse();return pending.ToArray();
    }
}
