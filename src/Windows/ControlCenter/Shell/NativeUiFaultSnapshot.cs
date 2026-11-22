using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LumaWall.ControlCenter;

// Read-only retained WinUI error/warning contexts, captured only on a fault/stall.
// Offsets were verified against the PDB of the bundled Windows App SDK 1.6 binary.
// Never interpret another binary using these offsets. No debugger, symbols, thread
// suspension, injection, memory dump, or per-frame observer is used at runtime.
internal sealed class NativeUiFaultSnapshot
{
    private const uint ImageTimestamp=3828430910,ImageSize=14405632;
    private const string CodeView="52534453B0981EABF02C6005D4A4229B7D28518E01000000";
    private readonly nint module,teb;
    private readonly bool supported;
    private readonly uint threadId;
    private readonly object gate=new();
    internal bool IsSupported=>supported;
    [StructLayout(LayoutKind.Sequential)]
    private struct ThreadBasic {public int ExitStatus;public nint Teb,ProcessId,ThreadId;public nuint Affinity;public int Priority,BasePriority;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandle(string name);
    [DllImport("kernel32.dll")]private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll")]private static extern nint GetCurrentThread();
    [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")]private static extern bool ReadProcessMemory(nint process,nint address,byte[] buffer,nuint size,out nuint read);
    [DllImport("ntdll.dll")]private static extern int NtQueryInformationThread(nint thread,int kind,out ThreadBasic data,int size,out int returned);
    public NativeUiFaultSnapshot()
    {
        threadId=GetCurrentThreadId();module=GetModuleHandle("Microsoft.UI.Xaml.dll");
        if(NtQueryInformationThread(GetCurrentThread(),0,out var basic,Marshal.SizeOf<ThreadBasic>(),out _)>=0)teb=basic.Teb;
        if(module==0||teb==0||IntPtr.Size!=8)return;
        int pe=(int)U32(module+60);if(pe<64||pe>4096)return;
        if(U32(module+pe)!=0x4550||U32(module+pe+8)!=ImageTimestamp||U32(module+pe+24+56)!=ImageSize)return;
        uint debug=U32(module+pe+24+160),length=U32(module+pe+24+164);
        if(debug>=ImageSize||length>4096)return;
        for(int offset=0;offset+28<=length;offset+=28) {
            nint entry=module+(int)debug+offset;if(U32(entry+12)!=2)continue;
            uint rva=U32(entry+20);if(rva>ImageSize-24)continue;
            if(Read(module+(int)rva,24) is { } bytes&&Convert.ToHexString(bytes)==CodeView) {supported=true;break;}
        }
    }
    private static byte[]? Read(nint address,int length)
    {
        if(address<0x10000||length<0||length>16384)return null;
        var data=new byte[length];return ReadProcessMemory(GetCurrentProcess(),address,data,(nuint)length,out var count)&&count==(nuint)length?data:null;
    }
    private static uint U32(nint address)=>Read(address,4) is { } bytes?BitConverter.ToUInt32(bytes):uint.MaxValue;
    private static nint Pointer(nint address)=>Read(address,8) is { } bytes?(nint)BitConverter.ToInt64(bytes):0;
    private nint ContextHead(int indexRva)
    {
        uint index=U32(module+indexRva);if(index>=1088)return 0;
        if(index<64)return Pointer(teb+0x1480+(int)index*8);
        nint slots=Pointer(teb+0x1780);return slots==0?0:Pointer(slots+((int)index-64)*8);
    }
    public string Capture()
    {
        if(!supported)return "Native WinUI layout context unavailable: bundled binary identity/architecture did not match; unsafe offset reads skipped.";
        if(!Monitor.TryEnter(gate))return "Native context capture already in progress.";
        try {
            var text=new StringBuilder(8192);
            text.AppendLine($"WinUI retained native contexts; uiThread={threadId}; moduleBase=0x{module:X}; timestamp={ImageTimestamp}; imageSize={ImageSize}; codeView={CodeView}; non-atomic read-only snapshot; retained timestamps may precede trigger.");
            CaptureList(text,ContextHead(0xcac20c),true);
            CaptureList(text,ContextHead(0xcac210),false);
            // Addresses are symbolized offline. A module list makes ASLR-independent resolution possible.
            using var process=Process.GetCurrentProcess();
            foreach(ProcessModule item in process.Modules) {
                if(text.Length>46*1024)break;
                text.AppendLine($"MODULE {item.ModuleName} base=0x{item.BaseAddress:X} size={item.ModuleMemorySize}");
            }
            return text.Length>48*1024?text.ToString(0,48*1024):text.ToString();
        } catch(Exception error) {return "Native snapshot incomplete: "+error.GetType().Name+"; "+error.Message;}
        finally {Monitor.Exit(gate);}
    }
    private static void CaptureList(StringBuilder text,nint head,bool warning)
    {
        var seen=new HashSet<nint>();int limit=warning?12:5;
        for(int i=0;head!=0&&i<limit&&text.Length<36*1024&&seen.Add(head);i++) {
            if(Read(head,warning?4216:7512) is not { } context) {text.AppendLine("Context unreadable/expired");break;}
            ushort count=BitConverter.ToUInt16(context,12);if(count>512) {text.AppendLine("Invalid context length");break;}
            long captured=BitConverter.ToInt64(context,0);
            text.AppendLine($"CONTEXT {(warning?"warning":"error")} {i}; fileTimeUtc={captured}; thread={BitConverter.ToUInt32(context,8)}; frames={count}"+
                (warning?"":$"; HRESULT=0x{BitConverter.ToUInt32(context,4204):X8}"));
            nint begin=(nint)BitConverter.ToInt64(context,4112),end=(nint)BitConverter.ToInt64(context,4120);
            if(begin>=0x10000&&end>=begin&&end-begin<=32*64)for(nint item=begin;item+32<=end;item+=32) {
                if(Read(item,32) is not { } value)break;
                ulong length=BitConverter.ToUInt64(value,16),capacity=BitConverter.ToUInt64(value,24);
                if(length>2048||length>capacity)continue;
                nint chars=capacity<8?item:(nint)BitConverter.ToInt64(value,0);
                if(Read(chars,(int)length*2) is { } bytes)text.AppendLine("INFO "+Encoding.Unicode.GetString(bytes));
                if(text.Length>36*1024)break;
            }
            for(int frame=0;frame<Math.Min((int)count,64);frame++)text.AppendLine($"FRAME 0x{BitConverter.ToUInt64(context,16+frame*8):X16}");
            head=(nint)BitConverter.ToInt64(context,warning?4208:7504);
        }
    }
}
