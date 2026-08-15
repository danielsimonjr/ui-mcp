using System.Threading;
using FluentAssertions;
using UiMcp.Hosting;

namespace UiMcp.Tests;

/// <summary>
/// SPEC section 3: two threads, one direction. Tool handlers never touch WPF objects directly; the
/// UI thread never blocks on MCP. Every cross-boundary call is an explicit marshal.
///
/// Most of this is testable WITHOUT a window, which is the point - a Dispatcher needs an STA thread
/// with a message pump, not a desktop. Only the tests that create an actual Window need one, and
/// those are isolated in <see cref="UiWindowTests"/> so the bulk of the suite runs anywhere.
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
    public void Work_RunsOnAnStaThread()
    {
        using var host = Started();
        var state = host.InvokeAsync(() => Thread.CurrentThread.GetApartmentState()).Result;
        state.Should().Be(ApartmentState.STA);
    }

    [Fact]
    public void Work_RunsOnADifferentThreadFromTheCaller()
    {
        using var host = Started();
        var uiThreadId = host.InvokeAsync(() => Environment.CurrentManagedThreadId).Result;
        uiThreadId.Should().NotBe(Environment.CurrentManagedThreadId);
    }

    [Fact]
    public void AllWork_LandsOnTheSameUiThread()
    {
        using var host = Started();
        var ids = Enumerable.Range(0, 20)
            .Select(_ => host.InvokeAsync(() => Environment.CurrentManagedThreadId).Result)
            .Distinct()
            .ToList();
        ids.Should().HaveCount(1, "a single UI thread is the whole point of the marshal");
    }

    // ---- the supervisor: a failure inside dispatched work must not take tool serving down --------

    [Fact]
    public void ExceptionInDispatchedWork_SurfacesToTheCaller()
    {
        using var host = Started();
        var act = () => host.InvokeAsync(() => throw new InvalidOperationException("boom")).GetAwaiter().GetResult();
        act.Should().Throw<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public void ExceptionInDispatchedWork_DoesNotKillTheHost()
    {
        using var host = Started();
        try { host.InvokeAsync(() => throw new InvalidOperationException("boom")).GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { /* expected */ }

        host.IsAlive.Should().BeTrue("a window fault must never take the MCP server down");
        host.InvokeAsync(() => 42).Result.Should().Be(42, "the host must still accept work afterwards");
    }

    /// <summary>
    /// THE CASE THE SPEC ACTUALLY MEANS. The two tests above pass on a framework guarantee:
    /// Dispatcher.InvokeAsync captures faults into the returned Task, so awaited work was never
    /// going to kill anything. A real window crash is different - it is raised ON the UI thread by
    /// an event handler with nobody awaiting it, reaches Dispatcher.UnhandledException, and
    /// terminates the process if unhandled. That is the path that turns a dead display into a dead
    /// server, and it is what the supervisor exists for.
    /// </summary>
    [Fact]
    public void UnobservedFaultOnTheUiThread_DoesNotKillTheHost()
    {
        using var host = Started();

        host.Post(() => throw new InvalidOperationException("window handler blew up"));

        // The fault is recorded asynchronously; poll briefly rather than sleeping a fixed span.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (host.LastFault is null && DateTime.UtcNow < deadline) Thread.Sleep(25);

        host.LastFault.Should().NotBeNull("an unobserved UI fault must be recorded, not swallowed");
        host.LastFault!.Message.Should().Be("window handler blew up");
        host.IsAlive.Should().BeTrue("a dead window is a degraded display, not an outage");
        host.InvokeAsync(() => 7).Result.Should().Be(7, "tool serving must continue");
    }

    [Fact]
    public void NoFaultRecorded_WhenNothingThrows_PositiveControl()
    {
        using var host = Started();
        host.Post(() => { /* well-behaved handler */ });
        host.InvokeAsync(() => 1).Result.Should().Be(1);   // ordering barrier: Post ran first
        host.LastFault.Should().BeNull("a clean run must not report a fault");
    }

    // ---- concurrency: SPEC section 7 names deadlock-under-concurrent-render explicitly -----------

    [Fact]
    public void ConcurrentInvokes_AllComplete_WithoutDeadlock()
    {
        using var host = Started();
        var tasks = Enumerable.Range(0, 50).Select(i => host.InvokeAsync(() => i * 2)).ToArray();

        Task.WaitAll(tasks, TimeSpan.FromSeconds(20)).Should().BeTrue("concurrent marshals must not deadlock");
        tasks.Select(t => t.Result).Should().BeEquivalentTo(Enumerable.Range(0, 50).Select(i => i * 2));
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

        var act = () => host.InvokeAsync(() => 1).GetAwaiter().GetResult();
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
