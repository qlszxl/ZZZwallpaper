param(
    [Parameter(Mandatory=$true)][long]$WindowHandle,
    [Parameter(Mandatory=$true)][int]$TestProcessId,
    [Parameter(Mandatory=$true)][string]$AutomationId,
    [ValidateSet('Inspect','Set')][string]$Operation='Inspect',
    [string]$InputBase64=''
)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $window=[System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$WindowHandle)
    if($null -eq $window -or $window.Current.ProcessId -ne $TestProcessId) { throw 'The target is not the isolated test process.' }
    $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$AutomationId)
    $element=$null
    for($attempt=0;$attempt -lt 20 -and $null -eq $element;$attempt++) {
        $element=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
        if($null -eq $element) { Start-Sleep -Milliseconds 100 }
    }
    if($null -eq $element) { throw 'The isolated name editor was not found by its test-only AutomationId.' }
    if($element.Current.ProcessId -ne $TestProcessId) { throw 'The name editor is outside the isolated test process.' }
    $pattern=$null
    $supported=$element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern,[ref]$pattern)
    if(-not $supported) { throw 'The isolated name editor does not expose native UIA ValuePattern.' }
    $readOnly=$pattern.Current.IsReadOnly
    if($Operation -eq 'Set') {
        if($readOnly) { throw 'Native UIA reports the name editor as read-only.' }
        $text=[System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($InputBase64))
        $pattern.SetValue($text)
    }
    [pscustomobject]@{ Supported=$supported; IsReadOnly=$readOnly; Value=$pattern.Current.Value } | ConvertTo-Json -Compress
} catch {
    [pscustomobject]@{ Error=$_.Exception.Message } | ConvertTo-Json -Compress
    exit 1
}
