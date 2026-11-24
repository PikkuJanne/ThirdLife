[CmdletBinding()]
param()

# Small direct-host observer for the compiled shell. Run after a Release build.
# It starts/closes only its owned UI process and never changes Windows settings.
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$binaryRoot = Join-Path $repositoryRoot "src\ThirdLife.UI\bin\Release\net10.0-windows"
$apphost = Join-Path $binaryRoot "ThirdLife.UI.exe"
$assembly = Join-Path $binaryRoot "ThirdLife.UI.dll"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    if (([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run the shell observation from a normal, unelevated terminal."
    }
}
finally { $identity.Dispose() }
$result = [ordered]@{
    schema_version = 1
    scenario = "shell_startup_external_uia_navigation"
    result = "not_run"
    source_commit = (& git -C $repositoryRoot rev-parse HEAD)
    tracked_source_clean = [string]::IsNullOrWhiteSpace((& git -C $repositoryRoot status --porcelain=v1 --untracked-files=no | Out-String))
    apphost_sha256 = (Get-FileHash -LiteralPath $apphost -Algorithm SHA256).Hash.ToLowerInvariant()
    assembly_sha256 = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash.ToLowerInvariant()
    standard_user_token = $true
    rendering = "default_wpf"
    cleanup = "pending"
    claim = "Automated active-machine observation; no human screen-reader or cross-hardware claim."
}
$timer = [Diagnostics.Stopwatch]::StartNew()
$ownedShell = Start-Process -FilePath $apphost -WindowStyle Hidden -PassThru
try {
    if (-not $ownedShell.WaitForInputIdle(10000)) { throw "Shell input-idle timeout." }
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        $ownedShell.Refresh()
        if ($ownedShell.HasExited -or $ownedShell.MainWindowHandle -ne [IntPtr]::Zero) { break }
        [Threading.Thread]::Sleep(100)
    }
    if ($ownedShell.HasExited -or $ownedShell.MainWindowHandle -eq [IntPtr]::Zero) { throw "Shell window unavailable." }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($ownedShell.MainWindowHandle)
    $inspectCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "StageInspect")
    $inspect = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $inspectCondition)
    if ($null -eq $inspect -or -not $inspect.Current.IsEnabled) { throw "Inspection review unavailable." }
    $inspect.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $statusCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, "Inspect selected. No job is open. Device readiness is unknown.")
    $status = $null
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $status = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $statusCondition)
        if ($null -ne $status) { break }
        [Threading.Thread]::Sleep(100)
    }
    if ($null -eq $status) { throw "Changed review status unavailable." }
    $ownedShell.Refresh()
    $result.result = "passed"
    $result.elapsed_to_observation_ms = $timer.ElapsedMilliseconds
    $result.peak_working_set_bytes = $ownedShell.PeakWorkingSet64
    $result.cpu_ms = $ownedShell.TotalProcessorTime.TotalMilliseconds
}
catch {
    $result.result = "failed"
    $result.failure_type = $_.Exception.GetType().Name
    $result.failure_hresult = $_.Exception.HResult
}
finally {
    if (-not $ownedShell.HasExited) {
        $null = $ownedShell.CloseMainWindow()
        if (-not $ownedShell.WaitForExit(5000)) {
            $ownedShell.Kill()
            $null = $ownedShell.WaitForExit(5000)
            $result.cleanup = "owned_process_terminated"
            $result.result = "failed"
        }
        else { $result.cleanup = "normal_exit" }
    }
    else { $result.cleanup = "already_exited" }
    $result.total_elapsed_ms = $timer.ElapsedMilliseconds
    $ownedShell.Dispose()
}
$result | ConvertTo-Json -Depth 3
if ($result.result -ne "passed") { exit 1 }
