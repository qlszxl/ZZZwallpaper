using LumaWall.Contracts;
using LumaWall.Diagnostics;
using Microsoft.Data.Sqlite;

namespace LumaWall.Storage;

public sealed partial class LibraryStore
{
    private Task? effectWrite;
    private IDisposable? effectWriteUndo;
    private string? effectWriteOperation;

    // Reading this flag never joins the worker or touches the UI connection.
    public bool HasPendingEffectWrite=>effectWrite is {IsCompleted:false};
    public bool TryCompleteEffectWrite()
    {
        if(HasPendingEffectWrite)return false;
        CompleteEffectWrite();return true;
    }

    public Task QueueEffectWrite(PreparedEffectWrite prepared,long? gesture=null)
    {
        CompleteEffectWrite();
        prepared.Baseline.Effects.Validate();
        if(prepared.Specific is { } specific) {
            specific.Resolve(prepared.Baseline.Effects).Validate();
            if(!Assets().Any(a=>a.Id==prepared.Asset))throw new ArgumentException("素材不存在。");
        }
        var change=prepared.Asset==null?EffectChange.Screen(prepared.Screen):EffectChange.Asset(prepared.Asset);
        var bindings=Bindings().Where(binding=>change.Affects(binding.ScreenId,binding.AssetId)).Select(binding=> {
            var baseline=binding.ScreenId==prepared.Screen?prepared.Baseline:ScreenProfile(binding.ScreenId)??new(FillMode.Cover,VideoEffects.Original);
            var own=binding.AssetId==prepared.Asset?prepared.Specific!:AssetProfile(binding.AssetId);
            return binding with {Fill=baseline.Fill,Effects=own.Resolve(baseline.Effects)};
        }).ToArray();
        effectWriteUndo=prepared.Asset==null
            ?CaptureUndo(gesture,"修改显示器效果","screen:"+prepared.Screen,new UndoScope("screen_profile","screen",prepared.Screen))
            :CaptureUndo(gesture,"修改素材效果","asset:"+prepared.Asset,new UndoScope("screen_profile","screen",prepared.Screen),new UndoScope("asset_effects","asset",prepared.Asset));
        string connectionString=database.ConnectionString;
        effectWriteOperation=AutomationOperationId;
        // At most one pending transaction. No worker touches the UI connection,
        // undo list, controls, or mutable runtime bindings.
        var operation=OperationTrace.Current;
        return effectWrite=Task.Run(()=> {
            using var trace=OperationTrace.Enter(operation);
            OperationTrace.Record(operation,"effect_transaction","started");
            try {WritePreparedEffects(connectionString,prepared,bindings);OperationTrace.Record(operation,"effect_transaction","committed");}
            catch(Exception error) {OperationTrace.Fault(operation,"effect_transaction",error.ToString());throw;}
        });
    }

    public void CompleteEffectWrite(Task? expected=null)
    {
        var pending=effectWrite;
        if(pending==null||expected!=null&&!ReferenceEquals(expected,pending))return;
        effectWrite=null;var undo=effectWriteUndo;effectWriteUndo=null;
        string? operation=effectWriteOperation;effectWriteOperation=null;
        try {pending.GetAwaiter().GetResult();}
        finally {
            var current=AutomationOperationId;AutomationOperationId=operation;
            try {undo?.Dispose();}finally {AutomationOperationId=current;}
        }
    }

    private static void WritePreparedEffects(string connectionString,PreparedEffectWrite prepared,ScreenBinding[] bindings)
    {
        var resources=new Dictionary<string,string>(StringComparer.Ordinal);
        string Encode(VideoEffects effects)=>EncodeEffectResources(effects,(id,image)=>resources[id]=image);
        string baseline=Encode(prepared.Baseline.Effects);
        string? specific=prepared.Specific==null?null:Encode(prepared.Specific.Effects);
        var live=bindings.Select(binding=>(Binding:binding,Effects:Encode(binding.Effects))).ToArray();
        using var writer=new SqliteConnection(connectionString);writer.Open();
        using var transaction=writer.BeginTransaction();
        using var command=writer.CreateCommand();command.Transaction=transaction;
        foreach(var resource in resources) {
            command.CommandText="INSERT OR IGNORE INTO effect_resource VALUES($id,$data)";
            command.Parameters.AddWithValue("$id",resource.Key);command.Parameters.AddWithValue("$data",resource.Value);
            command.ExecuteNonQuery();command.Parameters.Clear();
        }
        command.CommandText="INSERT INTO screen_profile VALUES($screen,$fill,$effects) ON CONFLICT(screen) DO UPDATE SET fill=excluded.fill,effects=excluded.effects";
        if(prepared.Asset==null)command.CommandText+="; UPDATE binding SET fill=$fill,effects=$effects WHERE screen=$screen";
        command.Parameters.AddWithValue("$screen",prepared.Screen);command.Parameters.AddWithValue("$fill",(int)prepared.Baseline.Fill);command.Parameters.AddWithValue("$effects",baseline);
        command.ExecuteNonQuery();command.Parameters.Clear();
        if(prepared.Asset!=null) {
            command.CommandText="INSERT INTO asset_effects VALUES($asset,$overrides,$effects) ON CONFLICT(asset) DO UPDATE SET overrides=excluded.overrides,effects=excluded.effects";
            command.Parameters.AddWithValue("$asset",prepared.Asset);command.Parameters.AddWithValue("$overrides",(long)prepared.Specific!.Overrides);command.Parameters.AddWithValue("$effects",specific!);
            command.ExecuteNonQuery();
        }
        foreach(var item in live) {
            command.Parameters.Clear();
            command.CommandText="UPDATE binding SET fill=$fill,effects=$effects WHERE screen=$screen AND asset=$asset AND revision=$revision";
            command.Parameters.AddWithValue("$fill",(int)item.Binding.Fill);command.Parameters.AddWithValue("$effects",item.Effects);
            command.Parameters.AddWithValue("$screen",item.Binding.ScreenId);command.Parameters.AddWithValue("$asset",item.Binding.AssetId);command.Parameters.AddWithValue("$revision",item.Binding.Revision);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
