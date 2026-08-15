using System.Text.Json;
using UiMcp.Abstractions;

namespace UiMcp.Hosting;

/// <summary>
/// What <c>ui_status</c> reports. Nullable fields mean NOT MEASURED, and the tool renders them as
/// UNKNOWN rather than as a zero or an empty string.
///
/// SPEC section 4 requires status to "report UNKNOWN for anything it cannot measure". That is the
/// same rule as the path resolver's, applied to the server's own introspection: a status board that
/// answers "0 nodes" when it actually means "I never rendered" is lying in the most convincing
/// possible way.
/// </summary>
public sealed record UiSurfaceStatus(
    bool WindowAlive,
    string? Title,
    int? NodeCount,
    string? TreeHash,
    DateTimeOffset? LastRenderUtc,
    string? LastFault);

/// <summary>
/// The UI surface, behind an interface so the tool layer is testable WITHOUT a desktop.
///
/// This seam is what lets the most important test in the suite run anywhere: that an invalid tree
/// is refused BEFORE anything touches the window. Proving that with a real window would require a
/// desktop and would prove less, because a spy can assert the negative - "Render was never called" -
/// which no screenshot can.
/// </summary>
public interface IUiSurface
{
    UiSurfaceStatus Status { get; }

    /// <summary>Idempotent. A second call focuses the existing window rather than opening another.</summary>
    void Open(string title, bool topmost, double width, double height);

    /// <summary>Full replace. Must only ever be called with an already-validated tree.</summary>
    void Render(ValidatedNode tree, JsonElement data);

    /// <summary>Closes the window. The SERVER stays up - a closed display is not a stopped service.</summary>
    void Close();
}
