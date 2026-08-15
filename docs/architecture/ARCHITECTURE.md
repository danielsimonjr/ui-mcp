# ui-mcp — Architecture

Why this is built the way it is. For *what* each part is, see [COMPONENTS.md](COMPONENTS.md).

## The problem

An LLM produces the UI tree. That makes the renderer a **prompt-injection surface** unless the
output space is closed. And the display shows machine telemetry, where the expensive failure is
not a crash — it is a value that could not be measured being drawn as a confident zero.

Two principles follow, and almost every decision below is one of them applied somewhere.

## Principle 1 — the output space is CLOSED

Model output is validated against a fixed catalog at the boundary. Anything it emits is either
schema-valid or **refused** — nothing in between.

- **Adding a component is a deliberate act.** If it is not in `CatalogValidator.Catalog`, it
  cannot render.
- **Unknown props are refused, not ignored.** A silent ignore hides a typo, and a typo in a
  security boundary is how things get through.
- **Deliberately absent, each an injection vector and none needed for telemetry:** raw HTML,
  model-supplied `href`/`src`, event handlers, style strings, class overrides.
- **Element types come from the renderer only.** The tree names a *component*; `TreeRenderer`
  chooses the WPF type. Text reaches the UI through `TextBlock.Text`, which parses no markup.
  Colours come from a closed `tone` enum — the tree never supplies a colour.

**`ValidatedNode` is a distinct type from raw JSON.** This is the whole safety model in one
signature: "has this been validated?" is a question the *compiler* answers, not one you answer
by reading call sites and hoping. `IUiSurface.Render` accepts only `ValidatedNode`, and there
is no public path from JSON to `ValidatedNode` that bypasses `CatalogValidator`.

**Validate the whole tree, then draw — never interleaved.** A partially rendered invalid tree
is worse than none: it looks like a working display while silently omitting whatever failed.

### Defence in depth, on purpose

The prototype guard (`__proto__`, `constructor`, `prototype`) exists in **both** `PropTypes.Path`
and `PathResolver.Resolve`. That duplication is intentional: the resolver is reachable from the
renderer's own bindings, not only from validated props, and a guard present at one of two entry
points is a door left open.

In `PropTypes.Path` the prototype check runs **before** the charset check. `__proto__` is
charset-legal, so checking the charset first would let a prototype path through on some future
regex edit. Ordering the security-bearing rule first makes that class of regression harder.

## Principle 2 — UNKNOWN, never zero

A section that could not be measured, displayed as a green zero, reads as health. The system
keeps "no value" and "the value zero" in visibly different shapes at every layer:

- `PathResolver.Resolve` returns `null` for **every** failure — absent key, index out of range,
  indexing a non-array, a refused path, no scope for `$item`. It never substitutes a default.
- `PathResolver.Display(null)` is `"UNKNOWN"`. JSON `null` also formats as `UNKNOWN`: a key
  present but null carries no more information than an absent key.
- `RenderRules.MetricText` appends a unit **only** when there is a value. `"UNKNOWN live"`
  reads as a measured quantity in some unit, when there is no quantity at all.
- `RenderRules.DeltaText` returns `null` rather than `0` for a non-numeric delta — "unchanged"
  and "not measured" are different claims.
- `UiSurfaceStatus` uses nullable fields throughout, and `ui_status` renders each as `UNKNOWN`.
  A status board that answers "0 nodes" when it means "I never rendered" is lying in the most
  convincing possible way.

`PathResolver` **never throws**. One bad binding in a sixty-node tree degrades to `UNKNOWN` in
place rather than taking the console down.

## Key decisions

### Two threads, one direction

MCP needs async stdio that never blocks. WPF needs an STA thread with a message pump. They
never touch: handlers call `UiThreadHost.InvokeAsync` and get a `Task`; **the UI thread never
waits on MCP.**

`UiThreadHost.Start` blocks until the `Dispatcher` is actually pumping. Returning earlier would
hand callers a host that accepts work and silently never runs it — far worse to diagnose than
a slow start.

### The supervisor, and why it is not optional

A window event handler that throws has **nobody awaiting it**. Without a
`Dispatcher.UnhandledException` handler, that exception terminates the **process**, taking MCP
tool serving down with the display.

> A dead display is a degraded service. A dead server is an outage. The two must not be the
> same failure.

The fault is **recorded** (`LastFault`) rather than swallowed, so `ui_status` can report a
degraded display. A supervisor that hides what it absorbed is a silent failure with extra steps.

This distinction was nearly missed. The first two fault tests passed on a *framework*
guarantee — `Dispatcher.InvokeAsync` captures exceptions into the returned `Task`, so awaited
work was never going to kill anything. `Post()` was added specifically to model the unawaited
path the spec actually means.

### The UI thread starts lazily

On first `Open()`, not at startup. A server that spins up an STA pump eagerly pays for a window
nobody asked for and — more importantly — would fail *at launch* on a host with no desktop,
instead of failing at the moment a window is requested, where the error can name what happened.

### `IUiSurface` exists for testability, and it earns its keep

The interface is what lets the most important test in the suite run **anywhere**: that an
invalid tree is refused *before* anything touches the window. Proving that with a real window
would require a desktop and would prove *less* — a spy can assert the negative, "Render was
never called", which no screenshot can.

### Judgement and drawing are separated

Everything that decides what a value *means* lives in `RenderRules`, in `Abstractions`, with no
WPF: unit suppression, delta existence, gauge clamping, empty text, the 64/200 caps.
`TreeRenderer` is assembly only — thin enough to verify by reading. This is what makes the
interesting half testable without a desktop.

### STDOUT is the transport

Anything written to stdout that is not a JSON-RPC frame corrupts the protocol, and the failure
surfaces as an unhelpful client-side parse error rather than "your log line broke it". Every
logging provider is cleared and the console logger is pinned to **stderr**. This is a
correctness constraint, not a style preference.

### The version is read off the assembly

`Program.ServerVersion` reads `Assembly.GetName().Version`; the single source is `<Version>` in
`Directory.Build.props`. A hardcoded version in Windows-mcp rotted through three releases, and
a server that misreports its own version is precisely what makes a stale-bundle deploy
invisible.

### One hash, not two

`ui_render` reads `treeHash` **back from the surface** rather than computing its own. The first
version hashed the raw JSON string while the surface hashed the structure, so the two tools
reported different values under the same name. Two functions for one fact is the drift defect;
deleting one beats syncing them, because syncing re-arms it.

### `ui_render` accepts an object *or* a string

Both shapes are real: an agent naturally sends an object, while any caller written against the
earlier `string` signature sends a string. When the parameters were typed `string`, an
object-shaped call failed inside **SDK parameter binding, before the method ran**, so none of
the refusal paths could report anything — the caller saw only "An error occurred invoking
'ui_render'". Found by driving the deployed plugin, not by a unit test.

### `UiValidationException` carries its own type

The MCP layer must tell a **deliberate refusal** apart from an internal fault and report the
refusal verbatim. The SDK otherwise flattens every non-MCP exception into "An error occurred
invoking '&lt;tool&gt;'", which turns an actionable `Note: unknown prop "onclick"` into an
opaque shrug — and hides the guard that just did its job.

## Deployment

Framework-dependent single-file, `win-x64`, **28.42 MB** — chosen over self-contained
(153.7 MB) by measurement, because `WindowsDesktop.App 9.0.19` is present on both target
machines and the bundle is committed and cache-cloned per version.

## Known limitations

- **Last-write-wins.** Two agents rendering different trees is a race; `ui_status` names only
  the last render. Multi-agent arbitration is deferred.
- **No partial update.** `ui_render` is a full replace; `ui_update` is deferred to v0.2.
- **The desktop assumption is retired only for the interactive path.** An S4U-triggered host
  has no desktop by definition. No task that starts the host uses S4U today; if that changes,
  re-run `tools/probe-desktop.ps1` first. See SPEC 10.1.

## Verification

Generated 2026-08-15 by `repo_map.py map`.
Regenerate: `python repo_map.py map <repo> --out <dir>` · Check: `python repo_map.py check <repo> --docs docs/architecture`

| Claim | Value | Source |
|---|---|---|
| totalSourceFiles | 17 | dependency-graph.json |
| runtimeCircularDeps | 0 | dependency-graph.json |
| entryRoots | 1 | dependency-graph.json |

**Claims the gate cannot hold:** the architectural rules above are read off the source and its
comments (`CatalogValidator.cs`, `PathResolver.cs`, `RenderRules.cs`, `UiThreadHost.cs`,
`Program.cs`) — they are design properties, not graph metrics. The **28.42 MB / 153.7 MB**
deployment figures were measured during the publish task and are recorded in SPEC 10.2.
