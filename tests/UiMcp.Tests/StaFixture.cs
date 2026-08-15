using UiMcp.Hosting;

namespace UiMcp.Tests;

/// <summary>
/// A shared STA thread for tests that build real WPF objects.
///
/// It is deliberately the SAME <see cref="UiThreadHost"/> production uses, not a bespoke
/// `new Thread(...) { STA }` inside the test project. A renderer test running on a differently
/// constructed thread would be exercising a runtime the server never uses - the same class of
/// mistake as testing a dev build instead of the shipped artifact.
///
/// One host per test class, because starting an STA pump per test would dominate the run and prove
/// nothing extra: <see cref="UiThreadHostTests"/> already covers start and shutdown.
///
/// CALLERS MUST INSPECT INSIDE <see cref="On{T}"/>, not after it. WPF objects have thread affinity,
/// so reading `Panel.Children` back on the test thread throws "The calling thread cannot access
/// this object because a different thread owns it". Return plain values - strings, ints, doubles -
/// which cross the boundary safely.
/// </summary>
public sealed class StaFixture : IDisposable
{
    private readonly UiThreadHost _host = new();

    public StaFixture() => _host.Start(TimeSpan.FromSeconds(15));

    /// <summary>Run work on the UI thread and return its (thread-safe) result.</summary>
    public T On<T>(Func<T> work) => _host.InvokeAsync(work).GetAwaiter().GetResult();

    public void Dispose() => _host.Dispose();
}
