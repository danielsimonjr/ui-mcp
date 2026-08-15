using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UiMcp.Abstractions;
using UiMcp.Rendering;

namespace UiMcp.Hosting;

/// <summary>
/// The real WPF surface. Every member marshals through <see cref="UiThreadHost"/>; no caller ever
/// touches a WPF object on its own thread.
///
/// The UI thread starts LAZILY on first <see cref="Open"/>. A server that spins up an STA pump at
/// startup pays for a window nobody asked for, and - more importantly - would fail at launch on a
/// host with no desktop instead of failing at the moment a window is actually requested, where the
/// error can name what went wrong.
/// </summary>
public sealed class UiSurface : IUiSurface, IDisposable
{
    private readonly UiThreadHost _host = new();
    private readonly object _gate = new();

    private Window? _window;
    private ContentControl? _content;

    private string? _title;
    private int? _nodeCount;
    private string? _treeHash;
    private DateTimeOffset? _lastRenderUtc;

    public UiSurfaceStatus Status
    {
        get
        {
            // Read the live window state rather than a cached flag: the user can close the window
            // with the X at any moment, and a status that reports our last INTENTION instead of the
            // current reality is exactly the kind of confident-wrong answer this server must not give.
            var alive = false;
            if (_host.IsAlive && _window is not null)
            {
                try { alive = _host.InvokeAsync(() => _window is not null && _window.IsVisible).Result; }
                catch (Exception) { alive = false; }
            }

            return new UiSurfaceStatus(
                WindowAlive: alive,
                Title: _title,
                NodeCount: _nodeCount,
                TreeHash: _treeHash,
                LastRenderUtc: _lastRenderUtc,
                LastFault: _host.LastFault?.Message);
        }
    }

    public void Open(string title, bool topmost, double width, double height)
    {
        lock (_gate)
        {
            if (!_host.IsAlive) _host.Start(TimeSpan.FromSeconds(15));

            _host.InvokeAsync(() =>
            {
                if (_window is null)
                {
                    _content = new ContentControl();
                    _window = new Window
                    {
                        Title = title,
                        Width = width,
                        Height = height,
                        Topmost = topmost,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        Background = new SolidColorBrush(Color.FromRgb(0x0a, 0x0a, 0x0a)),
                        Content = _content
                    };

                    // The user closing the window must not leave a dangling reference that later
                    // reads as "alive". Clearing here keeps ui_status honest without polling.
                    _window.Closed += (_, _) => { _window = null; _content = null; };
                    _window.Show();
                }
                else
                {
                    // Idempotent: focus, do not open a second window.
                    _window.Title = title;
                    _window.Topmost = topmost;
                    _window.Activate();
                }
            }).GetAwaiter().GetResult();

            _title = title;
        }
    }

    public void Render(ValidatedNode tree, JsonElement data)
    {
        // Auto-open rather than refusing: an agent that renders without opening first meant to
        // display something, and failing on a ceremony step would be pedantry, not safety.
        if (_window is null) Open(_title ?? "Starship Console", topmost: false, width: 1100, height: 800);

        _host.InvokeAsync(() =>
        {
            if (_content is null) return;

            // Scrollable: a dashboard outgrows the window, and content silently clipped off the
            // bottom is a blind spot wearing a layout.
            _content.Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(18),
                Content = TreeRenderer.Render(tree, data)
            };
        }).GetAwaiter().GetResult();

        _nodeCount = CountNodes(tree);
        _treeHash = HashOf(tree);
        _lastRenderUtc = DateTimeOffset.UtcNow;
    }

    public void Close()
    {
        if (!_host.IsAlive) return;

        // Fire-and-forget: closing must not block a tool call, and the window may already be gone.
        // The SERVER stays up either way - a closed display is not a stopped service.
        try { _host.Post(() => _window?.Close()); }
        catch (InvalidOperationException) { /* host already down; nothing to close */ }
    }

    private static int CountNodes(ValidatedNode n) => 1 + n.Children.Sum(CountNodes);

    private static string HashOf(ValidatedNode n) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Describe(n))))[..12].ToLowerInvariant();

    private static string Describe(ValidatedNode n) =>
        n.Type + "(" + string.Join(",", n.Props.Keys.OrderBy(k => k, StringComparer.Ordinal)) + ")"
        + string.Concat(n.Children.Select(Describe));

    public void Dispose() => _host.Dispose();
}
