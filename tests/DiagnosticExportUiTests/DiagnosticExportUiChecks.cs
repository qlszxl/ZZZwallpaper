using System.IO.Compression;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using LumaWall.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private static IEnumerable<FrameworkElement> DiagnosticVisuals(DependencyObject root)
    {
        if(root is FrameworkElement element)yield return element;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in DiagnosticVisuals(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    internal async void CheckDiagnosticExport(string output)
    {
        try {
            await ReorderSettle();ReorderCheckIsolation();
            ReorderCheck(Appearance.Pattern&&Appearance.Motion,"Fresh profile enables brand pattern and motion");
            var operation=ChooseDiagnosticExportOptions();await ReorderSettle();
            var dialog=VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).SelectMany(p=>DiagnosticVisuals(p.Child)).OfType<ContentDialog>().Single();
            var choices=DiagnosticVisuals((FrameworkElement)dialog.Content).OfType<CheckBox>().ToArray();
            ReorderCheck(choices.Length==3&&choices[0].IsChecked==true&&choices[1].IsChecked==true&&choices[2].IsChecked==false,"Actual diagnostic dialog displays the three intended default selections");
            var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(dialog);
            byte[] pixels=(await bitmap.GetPixelsAsync()).ToArray();
            using(var file=File.Create(Path.Combine(output,"diagnostic-dialog.png"))) {
                var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,file.AsRandomAccessStream());
                encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels);await encoder.FlushAsync();
            }
            foreach(var choice in choices)choice.IsChecked=false;
            ReorderCheck(!dialog.IsPrimaryButtonEnabled,"Deselecting everything disables the actual primary button");
            choices[2].IsChecked=true;
            ReorderCheck(dialog.IsPrimaryButtonEnabled,"Configuration-only selection enables the actual primary button");
            var primary=DiagnosticVisuals(dialog).OfType<Button>().Single(b=>b.Name=="PrimaryButton");
            ((IInvokeProvider)new ButtonAutomationPeer(primary).GetPattern(PatternInterface.Invoke)).Invoke();
            var selected=await operation;
            ReorderCheck(selected is {IncludeLogs:false,IncludeEnvironment:false,IncludeConfiguration:true},"Confirm returns the selected configuration-only options");
            var cancel=ChooseDiagnosticExportOptions();await ReorderSettle();
            var cancelDialog=VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).SelectMany(p=>DiagnosticVisuals(p.Child)).OfType<ContentDialog>().Single();
            cancelDialog.Hide();ReorderCheck(await cancel==null,"Cancelling returns no export selection");
            ReorderCheck(!Directory.EnumerateFiles(output,"*.zip",SearchOption.AllDirectories).Any(),"Choosing or cancelling options does not create a bundle");
            for(int mask=1;mask<8;mask++) {
                var args=ToolCatalog.Json(new {format="bundle",includeLogs=(mask&1)!=0,includeEnvironment=(mask&2)!=0,includeConfiguration=(mask&4)!=0});
                ToolCatalog.Validate("diagnostics_export",args);
                var reply=await AutomationDiagnostics(new("diagnostics_export",args),CancellationToken.None);
                ReorderCheck(!reply.IsError,$"Actual MCP bundle handler succeeds for selection {mask}");
                using var archive=ZipFile.OpenRead(reply.Data.GetProperty("path").GetString()!);
                ReorderCheck(archive.Entries.Any(e=>e.FullName.StartsWith("logs/"))==((mask&1)!=0)
                    &&(archive.GetEntry("system-info.json")!=null)==((mask&2)!=0)
                    &&(archive.GetEntry("configuration.json")!=null)==((mask&4)!=0),$"MCP handler produces exactly the selected categories {mask}");
            }
            ReorderCheckIsolation();
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {passed=true,checks=reorderChecks,foregroundActivated=false,productionDataAccessed=false,desktopHostsStarted=false},new JsonSerializerOptions {WriteIndented=true}));
        } catch(Exception error) {File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());Environment.ExitCode=1;}
        finally {
            effectSession.StopSchedule();ShutdownImports();ShutdownPerformanceMonitor();ShutdownWindowPlacement();
            App.ReorderIsolation?.WriteReport();Close();store.Dispose();App.ChildProcesses.Dispose();Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }
}
