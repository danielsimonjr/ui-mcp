using System.Windows.Threading;

namespace UiMcp.Hosting;

/// <summary>
/// Owns the single STA thread that WPF requires, and is the ONLY way tool handlers reach the UI.
///
/// SPEC section 3: two threads, one direction. MCP needs async stdio that never blocks; WPF needs an
/// STA thread with a message pump. They never touch. Handlers call <see cref="InvokeAsync{T}"/> and
/// get a Task back; the UI thread never waits on MCP.
///
/// WHY THIS IS A CLASS AND NOT A SCRIPT (SPEC section 2, demonstrated the hard way 2026-08-15):
/// the PowerShell desktop probe needed a hand-built STA Runspace, and the first attempt died with
/// "no Runspace available to run scripts in this thread" - PowerShell cannot hold an STA pump and
/// serve stdio at once without exactly this construct. In C# it is thirty lines.
///
/// SUPERVISOR CONTRACT: a fault inside dispatched work surfaces to the CALLER and leaves the host
/// alive. A window that crashes must never take tool serving down with it - a dead display is a
/// degraded service, a dead server is an outage, and the two must not be the same failure.
/// </summary>
public sealed class UiThreadHost : IDisposable
{
    private readonly object _gate = new();
    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private volatile bool _shutdown;

    /// <summary>
    /// The most recent fault raised ON the UI thread with nobody awaiting it - a window event
    /// handler throwing, typically. Recorded rather than swallowed so <c>ui_status</c> can report a
    /// degraded display honestly; a supervisor that hides the fault it absorbed is just a silent
    /// failure with extra steps.
    /// </summary>
    public Exception? LastFault { get; private set; }

    /// <summary>True once the STA thread is pumping and before shutdown completes.</summary>
    public bool IsAlive
    {
        get
        {
            var d = _dispatcher;
            return !_shutdown && d is not null && !d.HasShutdownFinished && (_thread?.IsAlive ?? false);
        }
    }

    /// <summary>
    /// Starts the UI thread and blocks until its Dispatcher is pumping. Blocking here is
    /// deliberate: returning before the pump exists would hand callers a host that accepts work and
    /// silently never runs it, which is far worse to diagnose than a slow start.
    /// </summary>
    public void Start(TimeSpan timeout)
    {
        lock (_gate)
        {
            if (_thread is not null) throw new InvalidOperationException("UI thread already started");

            // Signalled by the UI thread once its Dispatcher exists.
            using var ready = new ManualResetEventSlim(false);

            _thread = new Thread(() =>
            {
                var d = Dispatcher.CurrentDispatcher;

                // THE SUPERVISOR. Without this, an exception thrown by a window event handler -
                // nobody awaiting it - reaches here unhandled and terminates the PROCESS, taking
                // MCP tool serving down with the display. Marking it handled keeps the pump alive.
                // The fault is recorded rather than discarded so the degraded state is reportable:
                // a supervisor that hides what it absorbed is a silent failure with extra steps.
                d.UnhandledException += (_, e) =>
                {
                    LastFault = e.Exception;
                    e.Handled = true;
                };

                _dispatcher = d;
                ready.Set();
                Dispatcher.Run();
            })
            {
                // Foreground would keep the process alive after MCP shutdown, turning a clean exit
                // into a hang that looks like the server ignoring SIGTERM.
                IsBackground = true,
                Name = "ui-mcp UI thread"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            if (!ready.Wait(timeout))
            {
                _thread = null;
                throw new TimeoutException($"UI thread did not start within {timeout}");
            }
        }
    }

    /// <summary>Marshal work onto the UI thread and await its result.</summary>
    public Task<T> InvokeAsync<T>(Func<T> work)
    {
        var d = _dispatcher;

        // Fail FAST rather than queueing onto a dispatcher that will never run again. A hang here
        // would present as "the tool call never returns", which is the least diagnosable failure
        // this server could have.
        if (_shutdown || d is null || d.HasShutdownStarted)
            throw new InvalidOperationException("UI thread is not running");

        return d.InvokeAsync(work).Task;
    }

    /// <summary>Marshal work with no result onto the UI thread.</summary>
    public Task InvokeAsync(Action work) => InvokeAsync<object?>(() => { work(); return null; });

    /// <summary>
    /// Fire-and-forget work on the UI thread. This models what a window event handler actually is:
    /// nobody holds the resulting operation, so a fault here goes to the supervisor rather than to
    /// a caller. Used for teardown and for anything the tool layer must not wait on.
    /// </summary>
    public void Post(Action work)
    {
        var d = _dispatcher;
        if (_shutdown || d is null || d.HasShutdownStarted)
            throw new InvalidOperationException("UI thread is not running");

        d.BeginInvoke(work);
    }

    /// <summary>
    /// Stops the UI thread. Idempotent: shutdown is reached from both an orderly MCP exit and an
    /// error path, and a teardown that throws when called twice turns cleanup into a new fault.
    /// </summary>
    public void Shutdown(TimeSpan timeout)
    {
        lock (_gate)
        {
            if (_shutdown) return;
            _shutdown = true;

            var d = _dispatcher;
            if (d is not null && !d.HasShutdownStarted)
            {
                try { d.InvokeShutdown(); }
                catch (Exception) { /* already going down; nothing left to salvage */ }
            }

            _thread?.Join(timeout);
        }
    }

    public void Dispose() => Shutdown(TimeSpan.FromSeconds(5));
}
