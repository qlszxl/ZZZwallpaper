using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private sealed record PresetUiaValue(bool Supported,bool IsReadOnly,string Value);

    private async Task<PresetUiaValue> ReadPresetNameUia(TextBox box,string? input,string stage)
    {
        // WinUI's TextBox value pattern is native and is not returned as the
        // managed Microsoft.UI.Xaml.Automation.Provider.IValueProvider.
        // An external client also keeps its cross-thread COM call off the UI
        // dispatcher. Restrict lookup to this isolated HWND and a test-only ID.
        string id=AutomationProperties.GetAutomationId(box);
        if(string.IsNullOrEmpty(id)) {id="preset-name-test-"+Guid.NewGuid().ToString("N");AutomationProperties.SetAutomationId(box,id);}
        string script=Path.Combine(AppContext.BaseDirectory,"PresetNameUia.ps1");
        var start=new ProcessStartInfo {
            FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
        };
        foreach(string argument in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",script,
            "-WindowHandle",WinRT.Interop.WindowNative.GetWindowHandle(this).ToInt64().ToString(),
            "-TestProcessId",Environment.ProcessId.ToString(),"-AutomationId",id,"-Operation",input==null?"Inspect":"Set",
            "-InputBase64",Convert.ToBase64String(Encoding.UTF8.GetBytes(input??""))})start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new InvalidOperationException("Cannot start the native UIA test client.");
        var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try {await process.WaitForExitAsync(timeout.Token);}
        catch(OperationCanceledException) {if(!process.HasExited)process.Kill(true);throw new TimeoutException("Native UIA name input timed out: "+stage);}
        string json=await output,error=await errors;
        if(process.ExitCode!=0)throw new InvalidOperationException("Native UIA name input failed at "+stage+": "+json+" "+error);
        var result=JsonSerializer.Deserialize<PresetUiaValue>(json,new JsonSerializerOptions {PropertyNameCaseInsensitive=true})
            ??throw new InvalidOperationException("Native UIA name input returned no result: "+stage);
        File.AppendAllText(Path.Combine(EffectReorderRegressionProgram.Output,"name-uia.jsonl"),
            JsonSerializer.Serialize(new {stage,result.Supported,result.IsReadOnly,result.Value})+Environment.NewLine);
        return result;
    }
}
