<#
  probe-desktop.ps1 -- retires the SPEC section 10 risk:
  "A stdio MCP server may have no desktop to draw on when started by a scheduled task under S4U."

  WHAT IT PROVES
    Session id alone is NOT proof. A process can sit in the interactive session and still fail to
    create a window if it has no window station or no desktop. So this probe does the real thing:
    it creates an actual WPF Window, resolves its native HWND, and asks the OS whether that HWND is
    visible. A non-zero HWND that the OS reports visible is the answer; anything less is inference.

  HOW TO USE IT (both runs are needed - one of them is the control)
    CONTROL : run from an ordinary interactive shell. It MUST report createdWindow=True.
              A probe that cannot produce a positive proves nothing when it later reports a
              negative. Run the control FIRST.
    TEST    : run from inside an MCP server's process tree. That is the measurement.
    Compare. Identical results mean the MCP host imposes no extra desktop restriction.

  WHY THE STA RUNSPACE, learned the hard way 2026-08-15
    pwsh runs its main thread as MTA; WPF requires STA. But a raw [System.Threading.Thread] running
    a PowerShell ScriptBlock throws "There is no Runspace available to run scripts in this thread" -
    the scriptblock never executes, so the probe dies BEFORE it ever asks the desktop question, and
    that failure looks exactly like "no desktop". The correct construct is a Runspace with
    ApartmentState = STA, which carries both the apartment and an execution context.
    The control run exists to catch precisely this class of mistake before it yields a false
    negative. It did, on the first run.

    This is also the constraint SPEC section 2 cites as the reason ui-mcp is C# rather than
    PowerShell, so the probe doubles as a working demonstration of that argument.

  ASCII only. A non-BOM em-dash has parse-bombed PowerShell on this machine before.
#>
[CmdletBinding()]
param(
    [string]$OutFile = (Join-Path $env:TEMP 'ui-mcp-desktop-probe.json'),

    # How long the window stays up. Long enough for an INDEPENDENT observer in another process to
    # see it, short enough that a forgotten probe cannot litter the desktop.
    [int]$HoldSeconds = 6,

    # Unique enough that an outside observer can match on it without ambiguity.
    [string]$Title = 'ui-mcp-desktop-probe'
)

$ErrorActionPreference = 'Stop'

# Written by the STA runspace, read by the caller after Invoke() returns.
$R = [hashtable]::Synchronized(@{
    probePid        = $PID
    sessionId       = (Get-Process -Id $PID).SessionId
    parentPid       = $null
    parentName      = $null
    apartment       = $null
    createdWindow   = $false
    hwnd            = 0
    isWindowVisible = $false
    error           = $null
})

# Parent chain, so the TEST run can prove it really executed under the MCP server rather than
# somewhere else that happened to have a desktop.
try {
    $me = Get-CimInstance Win32_Process -Filter "ProcessId=$PID"
    $R.parentPid = $me.ParentProcessId
    $par = Get-Process -Id $me.ParentProcessId -ErrorAction SilentlyContinue
    if ($par) { $R.parentName = $par.ProcessName }
} catch { }

$work = {
    try {
        $R.apartment = [System.Threading.Thread]::CurrentThread.GetApartmentState().ToString()

        Add-Type -AssemblyName PresentationFramework
        Add-Type -AssemblyName PresentationCore
        Add-Type -AssemblyName WindowsBase

        # IsWindowVisible is the OS's own answer. WPF's $win.IsVisible is the framework's opinion of
        # itself and would report true even if nothing reached a desktop, so it is not sufficient.
        Add-Type -Namespace UiMcpProbe -Name Native -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool IsWindowVisible(System.IntPtr hWnd);
'@

        $win = New-Object System.Windows.Window
        $win.Title  = $Title
        $win.Width  = 460
        $win.Height = 170
        $win.WindowStartupLocation = 'CenterScreen'
        $win.Topmost = $true
        $tb = New-Object System.Windows.Controls.TextBlock
        $tb.Text = "ui-mcp desktop probe`nIf you can read this, a window reached the desktop.`nCloses itself."
        $tb.Margin = '16'
        $win.Content = $tb

        # Show() is non-blocking, unlike ShowDialog(). The window still needs the dispatcher to pump
        # before it is realised, which is what running the Dispatcher below provides.
        $win.Show()

        $R.hwnd = (New-Object System.Windows.Interop.WindowInteropHelper $win).Handle.ToInt64()
        $R.createdWindow = ($R.hwnd -ne 0)

        $timer = New-Object System.Windows.Threading.DispatcherTimer
        $timer.Interval = [TimeSpan]::FromSeconds($HoldSeconds)
        $timer.Add_Tick({
            # Sampled at close time, after the window has had real time on screen.
            $R.isWindowVisible = [UiMcpProbe.Native]::IsWindowVisible([IntPtr]$R.hwnd)
            $timer.Stop()
            $win.Close()
            [System.Windows.Threading.Dispatcher]::CurrentDispatcher.InvokeShutdown()
        })
        $timer.Start()

        [System.Windows.Threading.Dispatcher]::Run()
    } catch {
        $R.error = $_.Exception.Message
    }
}

$rs = [runspacefactory]::CreateRunspace()
$rs.ApartmentState = [System.Threading.ApartmentState]::STA
$rs.ThreadOptions  = [System.Management.Automation.Runspaces.PSThreadOptions]::ReuseThread
$rs.Open()
$rs.SessionStateProxy.SetVariable('R', $R)
$rs.SessionStateProxy.SetVariable('Title', $Title)
$rs.SessionStateProxy.SetVariable('HoldSeconds', $HoldSeconds)

$ps = [powershell]::Create()
$ps.Runspace = $rs
[void]$ps.AddScript($work.ToString())
try   { [void]$ps.Invoke() }
catch { $R.error = $_.Exception.Message }
finally { $ps.Dispose(); $rs.Close(); $rs.Dispose() }

$out = [ordered]@{}
foreach ($k in 'probePid','sessionId','parentPid','parentName','apartment','createdWindow','hwnd','isWindowVisible','error') { $out[$k] = $R[$k] }
($out | ConvertTo-Json -Compress) | Set-Content -Path $OutFile -Encoding ASCII

'PROBE RESULT'
foreach ($k in $out.Keys) { '  {0,-16}: {1}' -f $k, $out[$k] }
'  written to     : ' + $OutFile
