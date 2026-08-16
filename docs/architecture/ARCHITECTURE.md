# ui-mcp — Architecture

Why this is built the way it is. For *what* each part is, see [COMPONENTS.md](COMPONENTS.md).

## The problem

An LLM produces the UI tree. The renderer is therefore a **prompt-injection surface**, unless
the output space is closed. The display also shows machine telemetry. There the expensive
failure is not a crash. The expensive failure is a value that nobody could measure, drawn as a
confident zero.

Two principles follow, and almost every decision below is one of them applied somewhere.

## Principle 1 — the output space is CLOSED

Model output is validated against a fixed catalog at the boundary. Anything it emits is either
schema-valid or **refused** — nothing in between.

- **Adding a component is a deliberate act.** If it is not in `CatalogValidator.Catalog`, it
  cannot render.
- **The catalog refuses an unknown prop, and never ignores one.** A silent ignore hides a
  typo, and a typo in a security boundary is how bad input gets through.
- **Deliberately absent, each an injection vector and none needed for telemetry:** raw HTML,
  model-supplied `href`/`src`, event handlers, style strings, class overrides.
- **Element types come from the renderer only.** The tree names a *component*; `TreeRenderer`
  chooses the WPF type. Text reaches the UI through `TextBlock.Text`, which parses no markup.
  Colours come from a closed `tone` enum — the tree never supplies a colour.
- **`Tone()` has two different fallthrough cases**, and one earlier defect merged them. An
  absent tone gets the default accent, and the JS emits no tone class there at all. A tone
  *outside* the closed set gets **muted**. Both cases used to return Amber, the *attention*
  colour. An unrecognised tone therefore rendered as alarm. That result manufactures urgency
  from a value that the renderer simply failed to understand. The closed enum keeps the second
  case out of reach from a validated tree. `Render` is public, and code in the same process can
  construct a `ValidatedNode`, so the case *is* reachable. An unreachable branch that behaves
  wrongly is a trap for whoever makes it reachable.

**`ValidatedNode` is a different type from raw JSON.** That difference is the whole safety
model in one signature. The *compiler* answers the question "did anything validate this?". You
do not answer it by reading each call site and hoping. `IUiSurface.Render` accepts only
`ValidatedNode`, and no public path goes from JSON to `ValidatedNode` around
`CatalogValidator`.

**Validate the whole tree, then draw — never interleaved.** A partially rendered invalid tree
is worse than none: it looks like a working display while silently omitting whatever failed.

### Defence in depth, on purpose

The prototype guard (`__proto__`, `constructor`, `prototype`) exists in **both** `PropTypes.Path`
and `PathResolver.Resolve`. The duplication is deliberate. The renderer's own bindings reach
the resolver, and not only the validated props do. A guard at one of two entry points is a door
left open.

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

### The gauge is where this principle was actually being broken

`GaugePercent` takes a `maxWasRequested` flag. The flag looks like clutter until you see what
it prevents. Two different claims arrived at the method as one `null` max. The first claim is
"no maximum was asked for", and a default of 100 is correct for it. The second claim is "a
maximum **was** asked for and could not be resolved". For the second there is no scale, and
nothing honest to draw. Only the caller could tell the two apart.

While the two claims stayed together, a `Gauge` with an unresolvable `maxPath` drew its bar
against a default of 100. A value of 50 against an unreadable maximum therefore showed as
**half full**. That bar is a confident measurement against a scale that nobody supplied. The
bar is the green-zero failure wearing a progress bar. The JS original is explicit about this case: an
unresolvable `maxPath` gives `undefined`, fails the `typeof max === 'number'` test, and the bar
stays at 0.

The bar is now empty in that case, and the label still carries `UNKNOWN` — which is what
distinguishes "empty bar" from "no reading".

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

A writer almost missed this distinction. The first two fault tests passed on a *framework*
guarantee. `Dispatcher.InvokeAsync` captures an exception into the returned `Task`, so awaited
work was never going to kill anything. `Post()` exists to model the unawaited path that the
spec really means.

### The UI thread starts lazily

The thread starts on the first `Open()` call, and not at startup. A server that starts an STA
pump early pays for a window that nobody asked for. Worse, such a server fails *at launch* on a
host with no desktop. The failure should instead happen when a caller asks for a window,
because the error can then name what went wrong.

### `IUiSurface` exists for testability, and it earns its keep

The interface lets the most important test in the suite run **anywhere**. That test proves that
the system refuses an invalid tree *before* anything touches the window. A real window would
need a desktop, and it would prove *less*. A spy can assert the negative: "Render was never
called". No screenshot can assert that.

### Judgement and drawing are separated

Everything that decides what a value *means* lives in `RenderRules`, inside `Abstractions`,
with no WPF. That includes unit suppression, delta existence, gauge clamping, empty text and
the 64/200 caps. `TreeRenderer` only assembles elements, so a reader can verify it. That split
makes the interesting half testable without a desktop.

### STDOUT is the transport

Anything on stdout that is not a JSON-RPC frame corrupts the protocol. The failure then shows
as an unhelpful parse error in the client, and not as "your log line broke it". `Program.cs`
therefore clears every logging provider and sends the console logger to **stderr**. That rule
is a correctness constraint, and not a style preference.

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

Both shapes are real. An agent naturally sends an object. Any caller written against the
earlier `string` signature sends a string. While the parameters were `string`, an object-shaped
call failed inside **SDK parameter binding, before the method ran**. No refusal path could
report anything, so the caller saw only "An error occurred invoking 'ui_render'". A run of the
deployed plugin found this defect. No unit test found it.

### `UiValidationException` carries its own type

The MCP layer must tell a **deliberate refusal** from an internal fault. The layer must then report
the refusal word for word. Without its own type, the SDK turns every non-MCP exception into
"An error occurred invoking '&lt;tool&gt;'". That message replaces an actionable
`Note: unknown prop "onclick"` with nothing useful, and it hides the guard that just did its
job.

## Deployment

The build is framework-dependent, single-file and `win-x64`, at **28.42 MB**. A measurement
chose it over the self-contained build at 153.7 MB. Both target machines already hold
`WindowsDesktop.App 9.0.19`. The repository also commits the bundle, and the cache clones it
for each version.

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
| totalSourceFiles | 21 | dependency-graph.json |
| runtimeCircularDeps | 0 | dependency-graph.json |
| entryRoots | 1 | dependency-graph.json |

**Claims that the gate cannot hold.** A reading of the source and its comments gives every
architectural rule above. The files are `CatalogValidator.cs`, `PathResolver.cs`,
`RenderRules.cs`, `UiThreadHost.cs` and `Program.cs`. These rules are design properties, and
not graph metrics. The publish task measured the **28.42 MB** and **153.7 MB** figures, and
SPEC 10.2 records them.
