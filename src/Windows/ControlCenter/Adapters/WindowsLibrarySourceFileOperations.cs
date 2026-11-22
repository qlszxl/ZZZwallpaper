using System.Diagnostics;
using System.Runtime.InteropServices;
using LumaWall.Application;

namespace LumaWall.ControlCenter;

internal sealed class WindowsLibrarySourceFileOperations : ILibrarySourceFileOperations
{
    internal bool RecycleVerified { get; private set; }
    public void Reveal(string path)
    {
        string full = LibrarySourceFiles.Normalize(path);
        Marshal.ThrowExceptionForHR(SHParseDisplayName(full, 0, out nint item, 0, out _));
        try { Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(item, 0, 0, 0)); }
        finally { Marshal.FreeCoTaskMem(item); }
    }

    public void ManageSteam(string path, string? workshopId)
    {
        string uri = !string.IsNullOrEmpty(workshopId) && workshopId.All(char.IsAsciiDigit)
            ? "steam://url/CommunityFilePage/" + workshopId : "steam://nav/games/details/431960";
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    public void Delete(LibrarySourceFile source, bool permanently)
    {
        RecycleVerified = false;
        LibrarySourceFiles.RequireUnchanged(source);
        if (permanently) { File.Delete(source.Path); return; }
        // Never silently substitute a permanent delete when a volume does not
        // support the recycle bin. The progress sink also rejects a nuke pass.
        string root = Path.GetPathRoot(source.Path)!;
        if (root.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(root).DriveType != DriveType.Fixed)
            throw new IOException("此位置不能安全使用回收站。请在文件管理器中处理，或明确勾选永久删除。");
        var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new("3AD05575-8857-4850-9277-11B85BDB8E09"), true)!)!;
        nint item = 0;
        try
        {
            var iid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(source.Path, 0, ref iid, out item));
            // SILENT | NOCONFIRMATION | NOERRORUI | NO_CONNECTED_ELEMENTS |
            // WANTNUKEWARNING | RECYCLEONDELETE | EARLYFAILURE | ADDUNDORECORD.
            operation.SetOperationFlags(0x4 | 0x10 | 0x400 | 0x2000 | 0x4000 | 0x80000 | 0x100000 | 0x20000000);
            var sink = new RecycleSink(source);
            operation.DeleteItem(item, sink);
            operation.PerformOperations();
            operation.GetAnyOperationsAborted(out bool aborted);
            GC.KeepAlive(sink);
            if (aborted || !sink.Deleted || sink.Error < 0)
                throw new IOException("未能确认文件已移到回收站，请检查源文件和回收站。", sink.Error < 0 ? Marshal.GetExceptionForHR(sink.Error) : null);
            RecycleVerified = true;
        }
        finally
        {
            if (item != 0) Marshal.Release(item);
            Marshal.FinalReleaseComObject(operation);
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class RecycleSink(LibrarySourceFile source) : IFileOperationProgressSink
    {
        public bool Deleted; public int Error;
        public int StartOperations() => 0;
        public int FinishOperations(int result) { if (result < 0) Error = result; return 0; }
        public int PreDeleteItem(uint flags, nint item)
        {
            if ((flags & 0x80) == 0) return unchecked((int)0x80004004);
            try { LibrarySourceFiles.RequireUnchanged(source); return 0; }
            catch { return unchecked((int)0x80004004); }
        }
        // The Shell documents a null newly-created item as a full deletion.
        // Success is reported only when it returns the actual recycle-bin item.
        public int PostDeleteItem(uint flags, nint item, int result, nint created) { Error = result; Deleted = result >= 0 && created != 0; return 0; }
        public int PreRenameItem(uint flags,nint item,nint name) => 0;
        public int PostRenameItem(uint flags,nint item,nint name,int result,nint created) => 0;
        public int PreMoveItem(uint flags,nint item,nint destination,nint name) => 0;
        public int PostMoveItem(uint flags,nint item,nint destination,nint name,int result,nint created) => 0;
        public int PreCopyItem(uint flags,nint item,nint destination,nint name) => 0;
        public int PostCopyItem(uint flags,nint item,nint destination,nint name,int result,nint created) => 0;
        public int PreNewItem(uint flags,nint destination,nint name) => 0;
        public int PostNewItem(uint flags,nint destination,nint name,nint template,uint attributes,int result,nint created) => 0;
        public int UpdateProgress(uint total,uint current) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IFileOperationProgressSink sink,out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(nint dialog);
        void SetProperties(nint properties);
        void SetOwnerWindow(nint owner);
        void ApplyPropertiesToItem(nint item);
        void ApplyPropertiesToItems(nint items);
        void RenameItem(nint item,nint name,nint sink);
        void RenameItems(nint items,nint name);
        void MoveItem(nint item,nint destination,nint name,nint sink);
        void MoveItems(nint items,nint destination);
        void CopyItem(nint item,nint destination,nint name,nint sink);
        void CopyItems(nint items,nint destination);
        void DeleteItem(nint item,IFileOperationProgressSink sink);
        void DeleteItems(nint items);
        void NewItem(nint destination,uint attributes,nint name,nint template,nint sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
    [ComImport, Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags,nint item,nint name);
        [PreserveSig] int PostRenameItem(uint flags,nint item,nint name,int result,nint created);
        [PreserveSig] int PreMoveItem(uint flags,nint item,nint destination,nint name);
        [PreserveSig] int PostMoveItem(uint flags,nint item,nint destination,nint name,int result,nint created);
        [PreserveSig] int PreCopyItem(uint flags,nint item,nint destination,nint name);
        [PreserveSig] int PostCopyItem(uint flags,nint item,nint destination,nint name,int result,nint created);
        [PreserveSig] int PreDeleteItem(uint flags,nint item);
        [PreserveSig] int PostDeleteItem(uint flags,nint item,int result,nint created);
        [PreserveSig] int PreNewItem(uint flags,nint destination,nint name);
        [PreserveSig] int PostNewItem(uint flags,nint destination,nint name,nint template,uint attributes,int result,nint created);
        [PreserveSig] int UpdateProgress(uint total,uint current);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path,nint context,ref Guid iid,out nint item);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name,nint context,out nint item,uint attributes,out uint actualAttributes);
    [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(nint item,uint count,nint children,uint flags);
}
