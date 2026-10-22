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
    IReadOnlyList<ValidatedNode> Children,        // never null
    VisibilityCondition? Visible = null);         // null means always visible

public sealed record ValidatedColumn(string Header, string ValuePath);
```

`Props` values are already safe: a string here is length-checked, a path is charset-checked and
prototype-guarded, a tone is a member of the closed set. There is deliberately no public
construction path from JSON that skips `CatalogValidator`.

`Visible` holds the parsed `visible` condition of the node. A `null` value means that the node
is always visible.

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

The path charset is `^[A-Za-z0-9_.\[\]]+\z` — anchored and deliberately narrow. The end anchor is
`\z` because .NET `$` also matches before a final line feed. Quotes, slashes, semicolons and
whitespace are absent **by construction**, not blocked by a denylist.

`PropTypes.Path` handles `$item` as a **literal prefix**, and does not add `$` to the charset.
A wider charset would have been one character of difference, and it would have made every
other use of `$` legal at the same time. `$item.drive` is legal. `$other`, `$` and `a$b` stay
refused.

`Columns` sends each column's `valuePath` through `Path`. The prototype guard therefore
reaches *inside* the array. A nested path is where a writer most often forgets a boundary
check.

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

The validator throws on each rejection path below:

- a depth greater than 12,
- a node that is not an object,
- a `type` that is missing or is not a string,
- an unknown component,
- a node-level key other than `type`, `props`, `children` and `visible`,
- an **unknown prop**,
- a missing required prop,
- children on a component that accepts none,
- children that are not an array,
- more than 64 children,
- a `visible` value that is not `true`, `false` or a valid condition object.

A failing prop validator becomes `"{type}.{key}: {message}"`. The message `"expected string"`
alone tells a reader nothing useful in a tree of 60 nodes.

The validator reads four node-level keys: `type`, `props`, `children` and `visible`. It refuses
any other node-level key, before it reads the props, with `"{type}: unknown node key \"{key}\""`.
It reads `visible` after the props and the children. A failure in the condition becomes
`"{type}.visible: {message}"`. A `visible` value of `null` has the same result as an absent key.

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

`GaugePercent` returns `0` for a missing value, because a bar must have *some* length. The
**label** carries the difference between "empty bar" and "no reading", and it shows `UNKNOWN`.
A bar of zero length with no `UNKNOWN` beside it is the green-zero failure again.

### `VisibilityCondition.cs` — the closed condition union

```csharp
public abstract record VisibilityCondition
{
    private VisibilityCondition() { }

    public sealed record Always : VisibilityCondition;
    public sealed record Never : VisibilityCondition;
    public sealed record WhenPath(string Path) : VisibilityCondition;
    public sealed record And(IReadOnlyList<VisibilityCondition> Conditions) : VisibilityCondition;
    public sealed record Or(IReadOnlyList<VisibilityCondition> Conditions) : VisibilityCondition;
    public sealed record Not(VisibilityCondition Condition) : VisibilityCondition;
    public sealed record Eq(VisibilityOperand Left, VisibilityOperand Right) : VisibilityCondition;
    public sealed record Neq(VisibilityOperand Left, VisibilityOperand Right) : VisibilityCondition;
    public sealed record Gt(VisibilityOperand Left, VisibilityOperand Right) : VisibilityCondition;
    public sealed record Gte(VisibilityOperand Left, VisibilityOperand Right) : VisibilityCondition;
    public sealed record Lt(VisibilityOperand Left, VisibilityOperand Right) : VisibilityCondition;
    public sealed record Lte(VisibilityOperand Left, VisibilityOperand Right) : VisibilityCondition;

    public static VisibilityCondition Parse(JsonElement el);   // throws UiValidationException
}

public abstract record VisibilityOperand
{
    private VisibilityOperand() { }

    public sealed record Literal(JsonElement Value) : VisibilityOperand;
    public sealed record PathRef(string Path) : VisibilityOperand;

    public static VisibilityOperand Parse(JsonElement el);
}
```

The private constructor stops C# source outside the type from adding a case. The protected copy
constructor lets a derived type exist at run time, and a test builds such a type. `Parse` builds
only the twelve nested records, so no validated tree reaches another subtype. A new case is a
deliberate edit.

`VisibilityCondition.Parse` maps the JSON shapes as follows:

- `true` gives `Always`. `false` gives `Never`.
- Any other value that is not an object throws `"visible must be true, false, or an object"`.
- For an object, the parser tests these keys in order: `path`, `and`, `or`, `not`, `eq`, `neq`,
  `gt`, `gte`, `lt`, `lte`. The first key that the object holds decides the form. An object
  holds exactly one form key, so the parser refuses every other key with
  `visible: unknown key "<key>" next to "<form>"`.
- `and` and `or` take an array of conditions. `not` takes one condition. The parser reads each
  nested condition with the same rules.
- `eq`, `neq`, `gt`, `gte`, `lt` and `lte` take an array of exactly two operands.
- An object with none of these keys throws `"visible: unrecognised condition shape"`.

An operand is a `PathRef` when it is an object with a string `path` key. An operand object holds
exactly one key: the parser refuses a second key with
`visible.<form>[<index>]: unknown key "<key>" next to "path"`. Every other operand is a `Literal`.
That includes an object without a string `path`.

The parser checks the path of a `WhenPath` and the path of a `PathRef` with one method,
`VisibilityCondition.ParsePath`. `ParsePath` applies the `PropTypes.Path` rule and then refuses
`$item`. A refusal throws `"{position}: {message}"`. The position is `visible.path` for the path form
and `visible.<form>[<index>].path` for an operand, for example `visible.gt[0].path`. The position
does not name an enclosing `and`, `or` or `not`.

### `VisibilityEvaluator.cs` — the decision

```csharp
public static class VisibilityEvaluator
{
    public static bool IsVisible(VisibilityCondition? condition, JsonElement data);
}
```

`IsVisible` returns `true` for a `null` condition. `IsVisible` does not throw. The rules are:

| Condition | Result |
|---|---|
| `Always` / `Never` | `true` / `false` |
| `WhenPath` | `true` when the path resolves to a truthy value |
| `And` / `Or` | all / any of the sub-conditions; an empty `And` is `true` and an empty `Or` is `false` |
| `Not` | the negation of the sub-condition |
| `Eq` / `Neq` | equality / inequality of the two canonical operand values |
| `Gt`, `Gte`, `Lt`, `Lte` | the numeric order of the two operands; `false` unless both are numbers |

A path resolves against the root `data`, and never against a `Repeat` scope. The validator
therefore refuses a `$item` path in a condition. The evaluator leaves a `$item` path unresolved
when a caller builds the condition without the validator.

Truthiness follows JavaScript. `false`, `null`, `0` and the empty string are falsy. `true`, a
non-zero number, a non-empty string, an array and an object are truthy. An unresolved path is
falsy, so a `path` condition hides its node.

An operand reduces to a canonical value. A number becomes a `double`. A string stays a string.
`true` and `false` stay booleans. `null`, an unresolved path, an array and an object become
`null`. `Eq` compares two canonical values with `object.Equals`, so the number `1` and the string
`"1"` are different values, and two `null` values are equal.

The `switch` ends with a default arm that returns `false`, so a condition of an unknown subtype
is hidden. No JSON input reaches that arm, because `Parse` builds only the twelve forms. `Not`
negates the result, so a `Not` of an unknown subtype shows the node. A test table lists the twelve
subtypes. A new nested subtype fails the build until the table has a row for it. A row that only the
default arm decides fails too.

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

The `AddMcpServer` options set the server information and a short instruction text. The server
information holds the name and title `ui-mcp`, a description, the repository address and the
version. `ServerVersion` reads the assembly version to three parts, so no literal in the code
carries the version. The assembly version comes from `<Version>` in `Directory.Build.props`. The
instruction text names the four tools in one short paragraph.

> `WithToolsFromAssembly()` discovers `[McpServerTool]` methods by **reflection at run time**, so
> there is no `using UiMcp.Tools;` here. That is why static analysis reports `UiTools.cs` as
> test-only — see [unused-analysis.md](unused-analysis.md).

### `Tools/UiTools.cs` — the MCP surface

```csharp
[McpServerToolType]
public sealed class UiTools
{
    public UiTools(IUiSurface surface);

    [McpServerTool(Name = "ui_open", Title = "Open Shared Window", Idempotent = true)]
    public string Open(string title = "Starship Console", bool topmost = false,
                       double width = 1100, double height = 800);

    [McpServerTool(Name = "ui_render", Title = "Render Shared UI")]
    public string Render(JsonElement tree, JsonElement? data = null);

    [McpServerTool(Name = "ui_status", Title = "Get Window Status", ReadOnly = true, Idempotent = true)]
    public string Status();

    [McpServerTool(Name = "ui_close", Title = "Close Shared Window", Idempotent = true)]
    public string Close();
}
```

Three tools carry `Idempotent = true`: `ui_open`, `ui_status` and `ui_close`. `ui_status` also
carries `ReadOnly = true`. `ui_render` carries neither hint.

**The order of operations in `Render` is the safety property.** Unwrap → validate the whole
tree → *only then* `_surface.Render(...)`. Every failure returns `{ok: false, rejected: …}`
with the reason, rather than throwing.

`TryUnwrap` accepts a JSON value in two shapes: the payload itself, or a JSON string that holds
the payload. The method returns `false` with a reason instead of throwing, so the caller gets a
rejection that it can read.

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

`InvokeAsync` and `Post` **fail fast** when the host is down. Neither queues work onto a
dispatcher that will never run again. A hang there would show as "the tool call never returns",
which is the hardest failure to diagnose that this server could have.

### `Hosting/UiSurface.cs` — the real WPF surface

```csharp
public sealed class UiSurface : IUiSurface, IDisposable
```

Every member marshals through `UiThreadHost`; no caller ever touches a WPF object on its own
thread. The UI thread starts **lazily** on first `Open`.

`Status` reads the **live** window state with `_window.IsVisible` on the UI thread, and never
reads a cached flag. The user can close the window with the X at any moment. A status that
reports the last *intention* instead of the current reality is the confident wrong answer that
this server must never give. `Window.Closed` clears `_window` and `_content`, so a stale
reference never later reads as alive.

`Render` **opens a window** when none exists. An agent that renders without an open window
still meant to display something. A failure on a ceremony step would be pedantry, not safety. A `ScrollViewer` wraps the content, because a dashboard grows past the window.
Content that the layout quietly cuts off the bottom is a blind spot.

`Close` uses `Post`, so the call is **fire-and-forget**. A close must not block a tool call, and the
window may already be gone.

`TreeHash` is SHA-256 over a **structural** description (`Type(propKeys…)` recursively), first
12 hex chars — not over the raw JSON.

### `Rendering/TreeRenderer.cs` — assembly only

```csharp
public static class TreeRenderer
{
    public static UIElement Render(ValidatedNode node, JsonElement data, JsonElement? scope = null);
}
```

`Render` first asks `VisibilityEvaluator.IsVisible(node.Visible, data)`. A hidden node returns a
`Border` with `Visibility.Collapsed`, so the node takes no space and none of its children is
drawn. The condition reads the root `data` and never the `scope`.

Then one `switch` over the nine component names, one private builder each (`StatusBanner`,
`PanelBox`, `RowBox`, `Metric`, `Field`, `Gauge`, `Repeat`, `Table`, `Note`). All colours are
frozen `SolidColorBrush` constants selected by the closed `tone` set; unresolved values use a
distinct brush so `UNKNOWN` is visually loud. Text goes through a single `Text(...)` helper onto
`TextBlock.Text`, which parses no markup.

## Verification

Generated 2026-10-18 by `repo-tools map`.
Regenerate: `repo-tools map <repo> --out=<dir>` · Check: `repo-tools check <repo> --docs=docs/architecture`

| Claim | Value | Source |
|---|---|---|
| totalSourceFiles | 25 | dependency-graph.json |
| totalExports | 42 | dependency-graph.json |
| totalSymbols | 30 | duplicate-symbols.json |

**Claims the gate cannot hold:** every signature above was copied from the named source file.
`totalExports` (42) counts exported *types* across all 25 files including tests;
`totalSymbols` (30) is the distinct-symbol count duplicate-symbols.json analyses. Neither is a
count of methods, so the per-method signatures here are source-read, not gate-enforced.
