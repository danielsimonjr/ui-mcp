# ui-mcp — Components

Every module, with real signatures. Signatures are copied from source; if one here disagrees
with the code, the code is right and this file is stale.

---

## `UiMcp.Abstractions` — no WPF, testable anywhere

Targets plain `net9.0`. That target framework is the enforcement: this project *cannot*
reference WPF, so the guarantee is a compiler error rather than a code-review habit.

### `ValidatedNode.cs` — the safety model in one type

```csharp
public sealed record ValidatedNode(
    string Type,                                  // guaranteed a catalog key
    IReadOnlyDictionary<string, object> Props,    // coerced, never raw input
    IReadOnlyList<ValidatedNode> Children);       // never null

public sealed record ValidatedColumn(string Header, string ValuePath);
```

`Props` values are already safe: a string here is length-checked, a path is charset-checked and
prototype-guarded, a tone is a member of the closed set. There is deliberately no public
construction path from JSON that skips `CatalogValidator`.

### `UiValidationException.cs`

```csharp
public sealed class UiValidationException : Exception
{
    public UiValidationException(string message);
    public UiValidationException(string message, Exception inner);
}
```

Its own type so the MCP layer can distinguish a deliberate refusal from an internal fault and
report the refusal verbatim.

### `PropTypes.cs` — prop validators

Each returns a **coerced safe value** or throws. Never the raw input.

```csharp
public const int MaxTextLength  = 500;
public const int MaxTableColumns = 8;

public static readonly IReadOnlyList<string> Tones =
    new[] { "nominal", "attention", "critical", "degraded", "muted", "info" };
public static readonly IReadOnlyList<int> RowColumnCounts = new[] { 1, 2, 3, 4 };

public static object Text(JsonElement v);     // string, ≤500 chars
public static object Num(JsonElement v);      // finite double
public static object Bool(JsonElement v);
public static object Path(JsonElement v);     // charset + prototype guarded, $item aware
public static object Tone(JsonElement v);     // must be in Tones
public static object RowCols(JsonElement v);  // must be in RowColumnCounts
public static object Columns(JsonElement v);  // ≤8 ValidatedColumn, each path guarded
```

The path charset is `^[A-Za-z0-9_.\[\]]+$` — anchored and deliberately narrow. Quotes, slashes,
semicolons and whitespace are absent **by construction**, not blocked by a denylist.

`$item` is handled as a **literal prefix**, not by adding `$` to the charset. Widening the
charset would have been one character of diff and would have legalised every other use of `$`
at the same time; `$item.drive` is legal while `$other`, `$` and `a$b` stay refused.

`Columns` routes each column's `valuePath` through `Path`, so the prototype guard reaches
*inside* the array — a nested path is exactly where a boundary check tends to be forgotten.

### `CatalogValidator.cs` — the vocabulary and the boundary

```csharp
// INTERNAL: both describe how the catalog is BUILT, and every use is inside this file.
internal sealed record PropRule(Func<JsonElement, object> Validate, bool Optional = false);
internal sealed record ComponentSpec(IReadOnlyDictionary<string, PropRule> Props, bool AcceptsChildren);

public const int MaxDepth    = 12;
public const int MaxChildren = 64;

public static IReadOnlyCollection<string> ComponentNames { get; }
public static ValidatedNode Validate(JsonElement node);   // throws UiValidationException
```

The nine components and their props:

| Component | Required | Optional | Children |
|---|---|---|---|
| `StatusBanner` | `label`, `tone` | `detail` | no |
| `Panel` | `title` | `tone` | **yes** |
| `Row` | `cols` (1–4) | — | **yes** |
| `Metric` | `label`, `valuePath` | `unit`, `deltaPath`, `tone` | no |
| `Field` | `label`, `valuePath` | `tone` | no |
| `Gauge` | `label`, `valuePath` | `maxPath`, `tone` | no |
| `Repeat` | `fromPath` | `emptyText` | **yes** |
| `Table` | `fromPath`, `columns` | `emptyText` | no |
| `Note` | `text` | `tone` | no |

Rejection paths, each throwing with the component and prop named: depth > 12, non-object node,
missing/non-string `type`, unknown component, **unknown prop**, missing required prop, children
on a component that accepts none, non-array children, > 64 children. A failing prop validator
is re-wrapped as `"{type}.{key}: {message}"` — `"expected string"` alone is unactionable in a
60-node tree.

### `PathResolver.cs` — binding, and the UNKNOWN rule

```csharp
public const string Unknown = "UNKNOWN";

public static JsonElement? Resolve(JsonElement data, string? path, JsonElement? scope = null);
public static string Display(JsonElement? v);
```

`Resolve` returns `null` for **every** failure mode and never throws. Handles nested keys,
array indices, consecutive indices (`grid[1][0]`), bare `[2]`, and `$item` scope.

`Display` mapping — the one place a value becomes visible text:

| Kind | Renders as |
|---|---|
| `null` / JSON `null` / `Undefined` | `UNKNOWN` |
| `True` / `False` | `YES` / `NO` |
| `Number` | `"R"` round-trip format, `InvariantCulture` |
| `String` | the string (or `UNKNOWN` if null) |
| `Array` | its length |
| `Object` | `OBJ` |

`"R"` round-trips, so `258.5` stays `258.5` and acquires no trailing noise. `InvariantCulture`
matters: a comma decimal separator on a differently-configured machine would silently change
every number on the display.

### `RenderRules.cs` — judgement without drawing

```csharp
public const int MaxRepeatItems = 64;   // matches the proven renderer's arr.slice(0, 64)
public const int MaxTableRows   = 200;  // matches arr.slice(0, 200)

public static bool    IsUnknown(JsonElement? v);
public static string  MetricText(JsonElement? value, string? unit);
public static string? DeltaText(JsonElement? delta);          // null when not numeric
public static double  GaugePercent(JsonElement? value, JsonElement? max);  // 0–100, clamped
public static string  EmptyText(string? supplied);            // "none" when blank
```

`GaugePercent` returns `0` for a missing value because a bar must have *some* length. The
distinction between "empty bar" and "no reading" is carried by the **label**, which shows
`UNKNOWN` — a zero-length bar with no `UNKNOWN` beside it is the green-zero failure again.

---

## `UiMcp` — the host, tools and WPF

### `Program.cs` — composition root

```csharp
internal static string ServerVersion { get; }        // off the assembly, never a literal
public static async Task<int> Main(string[] args);
```

Clears every logging provider and pins the console logger to **stderr** (stdout is the MCP
transport). Registers `IUiSurface → UiSurface` as a **singleton** — there is one shared
surface, which is the entire point. Then `.AddMcpServer(…).WithStdioServerTransport()
.WithToolsFromAssembly()`.

> `WithToolsFromAssembly()` discovers `[McpServerTool]` methods by **source generator**, so
> there is no `using UiMcp.Tools;` here. That is why static analysis reports `UiTools.cs` as
> test-only — see [DEPENDENCY_GRAPH.md](DEPENDENCY_GRAPH.md).

### `Tools/UiTools.cs` — the MCP surface

```csharp
[McpServerToolType]
public sealed class UiTools
{
    public UiTools(IUiSurface surface);

    [McpServerTool(Name = "ui_open")]
    public string Open(string title = "Starship Console", bool topmost = false,
                       double width = 1100, double height = 800);

    [McpServerTool(Name = "ui_render")]
    public string Render(JsonElement tree, JsonElement? data = null);

    [McpServerTool(Name = "ui_status")]
    public string Status();

    [McpServerTool(Name = "ui_close")]
    public string Close();
}
```

**The order of operations in `Render` is the safety property.** Unwrap → validate the whole
tree → *only then* `_surface.Render(...)`. Every failure returns `{ok: false, rejected: …}`
with the reason, rather than throwing.

`TryUnwrap` accepts a JSON value that is either the payload itself or a JSON string containing
it, returning `false` with a reason instead of throwing so the caller gets a readable rejection.

### `Hosting/IUiSurface.cs` — the testability seam

```csharp
public sealed record UiSurfaceStatus(
    bool WindowAlive, string? Title, int? NodeCount,
    string? TreeHash, DateTimeOffset? LastRenderUtc, string? LastFault);

public interface IUiSurface
{
    UiSurfaceStatus Status { get; }
    void Open(string title, bool topmost, double width, double height);  // idempotent
    void Render(ValidatedNode tree, JsonElement data);                    // full replace
    void Close();                                                          // server stays up
}
```

Every nullable field means **NOT MEASURED**, and `ui_status` renders it as `UNKNOWN`.

### `Hosting/UiThreadHost.cs` — the STA thread

```csharp
public sealed class UiThreadHost : IDisposable
{
    public Exception? LastFault { get; }
    public bool IsAlive { get; }
    public void     Start(TimeSpan timeout);     // blocks until the Dispatcher pumps
    public Task<T>  InvokeAsync<T>(Func<T> work);
    public Task     InvokeAsync(Action work);
    public void     Post(Action work);           // fire-and-forget; faults go to the supervisor
    public void     Shutdown(TimeSpan timeout);  // idempotent
    public void     Dispose();
}
```

The thread is `IsBackground = true` and `ApartmentState.STA`. Foreground would keep the process
alive after MCP shutdown, turning a clean exit into a hang that looks like the server ignoring
SIGTERM.

`InvokeAsync` and `Post` **fail fast** when the host is down rather than queueing onto a
dispatcher that will never run again — a hang there would present as "the tool call never
returns", the least diagnosable failure this server could have.

### `Hosting/UiSurface.cs` — the real WPF surface

```csharp
public sealed class UiSurface : IUiSurface, IDisposable
```

Every member marshals through `UiThreadHost`; no caller ever touches a WPF object on its own
thread. The UI thread starts **lazily** on first `Open`.

`Status` reads the **live** window state (`_window.IsVisible` on the UI thread) rather than a
cached flag — the user can close the window with the X at any moment, and a status reporting
our last *intention* instead of current reality is exactly the confident-wrong answer this
server must not give. `Window.Closed` clears `_window`/`_content` so a dangling reference never
later reads as alive.

`Render` **auto-opens** if there is no window: an agent that renders without opening meant to
display something, and failing on a ceremony step would be pedantry, not safety. Content is
wrapped in a `ScrollViewer` — a dashboard outgrows the window, and content silently clipped off
the bottom is a blind spot wearing a layout.

`Close` is **fire-and-forget** via `Post`: closing must not block a tool call, and the window
may already be gone.

`TreeHash` is SHA-256 over a **structural** description (`Type(propKeys…)` recursively), first
12 hex chars — not over the raw JSON.

### `Rendering/TreeRenderer.cs` — assembly only

```csharp
public static class TreeRenderer
{
    public static UIElement Render(ValidatedNode node, JsonElement data, JsonElement? scope = null);
}
```

One `switch` over the nine component names, one private builder each (`StatusBanner`,
`PanelBox`, `RowBox`, `Metric`, `Field`, `Gauge`, `Repeat`, `Table`, `Note`). All colours are
frozen `SolidColorBrush` constants selected by the closed `tone` set; unresolved values use a
distinct brush so `UNKNOWN` is visually loud. Text goes through a single `Text(...)` helper onto
`TextBlock.Text`, which parses no markup.

## Verification

Generated 2026-08-15 by `repo_map.py map`.
Regenerate: `python repo_map.py map <repo> --out <dir>` · Check: `python repo_map.py check <repo> --docs docs/architecture`

| Claim | Value | Source |
|---|---|---|
| totalSourceFiles | 21 | dependency-graph.json |
| totalExports | 23 | dependency-graph.json |
| totalSymbols | 13 | duplicate-symbols.json |

**Claims the gate cannot hold:** every signature above was copied from the named source file.
`totalExports` (23) counts exported *types* across all 21 files including tests;
`totalSymbols` (13) is the distinct-symbol count duplicate-symbols.json analyses. Neither is a
count of methods, so the per-method signatures here are source-read, not gate-enforced.
