using System.Threading;
using FluentAssertions;
using UiMcp.Hosting;

namespace UiMcp.Tests;

/// <summary>
/// SPEC section 3: two threads, one direction. Tool handlers never touch WPF objects directly; the
/// UI thread never blocks on MCP. Every cross-boundary call is an explicit marshal.
///
/// Most of this is testable WITHOUT a window, which is the point - a Dispatcher needs an STA thread
/// with a message pump, not a desktop.
///
/// These tests are async throughout and never block on a Task (xUnit1031). That is not cosmetic
/// here: this suite exists to prove a marshalling boundary does not deadlock, so a test that itself
/// blocks a thread while waiting on that boundary is the one place the shortcut could manufacture
/// the very failure it claims to rule out.
/// </summary>
public class UiThreadHostTests
{
    private static UiThreadHost Started()
    {
        var host = new UiThreadHost();
        host.Start(TimeSpan.FromSeconds(10));
        return host;
    }

    [Fact]
    public void Start_BringsTheHostAlive()
    {
        using var host = Started();
        host.IsAlive.Should().BeTrue();
    }

    [Fact]
    public async Task Work_RunsOnAnStaThread()
    {
        using var host = Started();
        var state = await host.InvokeAsync(() => Thread.CurrentThread.GetApartmentState());
        state.Should().Be(ApartmentState.STA);
    }

    [Fact]
    public async Task Work_RunsOnADifferentThreadFromTheCaller()
    {
        using var host = Started();
        var uiThreadId = await host.InvokeAsync(() => Environment.CurrentManagedThreadId);
        uiThreadId.Should().NotBe(Environment.CurrentManagedThreadId);
    }

    [Fact]
    public async Task AllWork_LandsOnTheSameUiThread()
    {
        using var host = Started();
        var ids = new List<int>();
        for (var i = 0; i < 20; i++)
            ids.Add(await host.InvokeAsync(() => Environment.CurrentManagedThreadId));

        ids.Distinct().Should().HaveCount(1, "a single UI thread is the whole point of the marshal");
    }

    // ---- faults on work the caller IS awaiting --------------------------------------------------

    [Fact]
    public async Task ExceptionInDispatchedWork_SurfacesToTheCaller()
    {
        using var host = Started();
        var act = async () => await host.InvokeAsync<int>(() => throw new InvalidOperationException("boom"));
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("boom");
    }

    [Fact]
    public async Task ExceptionInDispatchedWork_DoesNotKillTheHost()
    {
        using var host = Started();
        try { await host.InvokeAsync<int>(() => throw new InvalidOperationException("boom")); }
        catch (InvalidOperationException) { /* expected */ }

        host.IsAlive.Should().BeTrue("a window fault must never take the MCP server down");
        (await host.InvokeAsync(() => 42)).Should().Be(42, "the host must still accept work afterwards");
    }

    /// <summary>
    /// THE CASE THE SPEC ACTUALLY MEANS. The two tests above pass on a framework guarantee:
    /// Dispatcher.InvokeAsync captures faults into the returned Task, so awaited work was never
    /// going to kill anything. A real window crash is different - it is raised ON the UI thread by
    /// an event handler with nobody awaiting it, reaches Dispatcher.UnhandledException, and
    /// terminates the process if unhandled. Removing the supervisor crashes the test host outright,
    /// which is how this test was verified to be load-bearing rather than decorative.
    /// </summary>
    [Fact]
    public async Task UnobservedFaultOnTheUiThread_DoesNotKillTheHost()
    {
        using var host = Started();

        host.Post(() => throw new InvalidOperationException("window handler blew up"));

        // Recorded asynchronously; poll briefly rather than sleeping a fixed span.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (host.LastFault is null && DateTime.UtcNow < deadline) await Task.Delay(25);

        host.LastFault.Should().NotBeNull("an unobserved UI fault must be recorded, not swallowed");
        host.LastFault!.Message.Should().Be("window handler blew up");
        host.IsAlive.Should().BeTrue("a dead window is a degraded display, not an outage");
        (await host.InvokeAsync(() => 7)).Should().Be(7, "tool serving must continue");
    }

    [Fact]
    public async Task NoFaultRecorded_WhenNothingThrows_PositiveControl()
    {
        using var host = Started();
        host.Post(() => { /* well-behaved handler */ });
        (await host.InvokeAsync(() => 1)).Should().Be(1);   // ordering barrier: Post ran first
        host.LastFault.Should().BeNull("a clean run must not report a fault");
    }

    // ---- concurrency: SPEC section 7 names deadlock-under-concurrent-render explicitly -----------

    [Fact]
    public async Task ConcurrentInvokes_AllComplete_WithoutDeadlock()
    {
        using var host = Started();
        var tasks = Enumerable.Range(0, 50).Select(i => host.InvokeAsync(() => i * 2)).ToArray();

        var all = Task.WhenAll(tasks);
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(20)));

        finished.Should().BeSameAs(all, "concurrent marshals must not deadlock");
        (await all).Should().BeEquivalentTo(Enumerable.Range(0, 50).Select(i => i * 2));
    }

    // ---- lifecycle -------------------------------------------------------------------------------

    [Fact]
    public void Shutdown_EndsTheHost()
    {
        var host = Started();
        host.Shutdown(TimeSpan.FromSeconds(10));
        host.IsAlive.Should().BeFalse();
    }

    [Fact]
    public void InvokeAfterShutdown_FailsFast_RatherThanHanging()
    {
        var host = Started();
        host.Shutdown(TimeSpan.FromSeconds(10));

        // Deliberately NOT awaited: the contract is that the call throws SYNCHRONOUSLY rather than
        // returning a Task that never completes. Awaiting would pass either way and prove less.
        // Discarding the Task keeps `act` an Action, so this asserts a SYNCHRONOUS throw. Typed as
        // Func<Task<int>> it would bind to ThrowAsync, which would also pass for a returned faulted
        // task - a weaker claim than the one being made here.
        var act = () => { _ = host.InvokeAsync(() => 1); };
        act.Should().Throw<InvalidOperationException>("a dead host must refuse work rather than block forever");
    }

    [Fact]
    public void Shutdown_IsIdempotent()
    {
        var host = Started();
        host.Shutdown(TimeSpan.FromSeconds(10));
        var act = () => host.Shutdown(TimeSpan.FromSeconds(10));
        act.Should().NotThrow();
    }

    [Fact]
    public void DoubleStart_IsRefused_RatherThanSilentlyLeakingASecondThread()
    {
        using var host = Started();
        var act = () => host.Start(TimeSpan.FromSeconds(10));
        act.Should().Throw<InvalidOperationException>();
    }
}
