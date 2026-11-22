using LumaWall.Contracts.Sharing;

namespace LumaWall.ControlCenter;

// Shared by the text and image adapters. Epochs prevent late background work
// from publishing results after a selection or input has changed.
internal sealed class SharingState
{
    public ShareSelection Preferences { get; set; }=new();
    public bool Busy { get; set; }
    public bool ImageBusy { get; set; }
    public int ExportEpoch { get; private set; }
    public int ImportEpoch { get; private set; }
    public string? GeneratedCode { get; set; }
    public byte[]? GeneratedQr { get; set; }
    public SharedConfiguration? Decoded { get; set; }
    public int BeginExport() {GeneratedCode=null;GeneratedQr=null;return ++ExportEpoch;}
    public int BeginImport() {Decoded=null;return ++ImportEpoch;}
}
