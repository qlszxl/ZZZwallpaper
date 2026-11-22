using System.Runtime.InteropServices;

namespace LumaWall.DesktopIntegration;

/// <summary>Connects the running window, launcher and installed shortcuts.</summary>
public sealed class TaskbarIdentity : IDisposable
{
    public const string AppId="ZZZwallpaper.Desktop";
    private IPropertyStore? properties;
    private static readonly Guid PropertySet=new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [DllImport("shell32.dll",ExactSpelling=true)]
    private static extern int SHGetPropertyStoreForWindow(nint window,in Guid iid,out IPropertyStore store);

    public static void InitializeProcess() => Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));

    public TaskbarIdentity(nint window,string launcher)
    {
        Guid iid=typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window,in iid,out var store));
        properties=store;
        try {
            Set(2,"\""+launcher+"\"");
            Set(3,launcher+",-101");
            Set(4,"ZZZwallpaper");
            // Set identity last, after all information needed by Explorer exists.
            Set(5,AppId);
        } catch {Dispose();throw;}
    }
    private void Set(uint id,string text)
    {
        var key=new PropertyKey {Format=PropertySet,Id=id};
        var value=new PropVariant {Type=31,Pointer=Marshal.StringToCoTaskMemUni(text)}; // VT_LPWSTR
        try {Marshal.ThrowExceptionForHR(properties!.SetValue(in key,in value));}
        finally {Marshal.FreeCoTaskMem(value.Pointer);}
    }
    public void Dispose()
    {
        var store=properties;if(store==null)return;properties=null;
        try {
            // Window property stores do not release their values automatically.
            var empty=new PropVariant();
            foreach(uint id in new uint[] {5,2,3,4}) {
                var key=new PropertyKey {Format=PropertySet,Id=id};store.SetValue(in key,in empty);
            }
        } finally {Marshal.ReleaseComObject(store);}
    }
    [StructLayout(LayoutKind.Sequential,Pack=4)]
    private struct PropertyKey {public Guid Format;public uint Id;}
    // The product is x64; PROPVARIANT includes a 16-byte union after its header.
    [StructLayout(LayoutKind.Explicit,Size=24)]
    private struct PropVariant {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint Pointer;
    }
    [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index,out PropertyKey key);
        [PreserveSig] int GetValue(in PropertyKey key,out PropVariant value);
        [PreserveSig] int SetValue(in PropertyKey key,in PropVariant value);
        [PreserveSig] int Commit();
    }
}
