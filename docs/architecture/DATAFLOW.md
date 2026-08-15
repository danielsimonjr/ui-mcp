# ui-mcp — Dataflow

How a request travels end to end. The ordering here **is** the safety model, so this file
describes sequence, not just participants.

## Startup

```
launch UiMcp.exe
  │
  ├─ Console encodings → UTF-8
  ├─ Logging.ClearProviders()          ← stdout is the MCP transport
  ├─ Logging.AddConsole(→ stderr)
  ├─ AddSingleton<IUiSurface, UiSurface>()   ← ONE shared surface
  └─ AddMcpServer(name: "ui-mcp", version: <from assembly>)
       .WithStdioServerTransport()
       .WithToolsFromAssembly()        ← source generator finds [McpServerTool] methods
  │
  └─ RunAsync()  → serving on stdio
```

**No UI thread exists yet.** `UiThreadHost` is constructed as a field of `UiSurface`, but the
STA thread does not start until the first `Open`. A host with no desktop therefore fails at the
moment a window is requested — where the error can name what went wrong — rather than at launch.

## `ui_render` — the path that matters

```
agent ──JSON-RPC──▶ stdio ──▶ MCP SDK ──▶ UiTools.Render(tree, data)
                                              │
                              ┌───────────────┴───────────────┐
                              │ 1. TryUnwrap(tree)            │  object OR JSON string
                              │    fail → {ok:false, rejected}│  ◀── returns here
                              ├───────────────────────────────┤
                              │ 2. TryUnwrap(data) if present │
                              │    fail → {ok:false, rejected}│  ◀── returns here
                              ├───────────────────────────────┤
                              │ 3. CatalogValidator.Validate  │  WHOLE tree, recursively
                              │    UiValidationException      │
                              │      → {ok:false, rejected}   │  ◀── returns here
                              └───────────────┬───────────────┘
                                              │
                    ══════ NOTHING ABOVE THIS LINE TOUCHED THE UI ══════
                                              │
                                     4. _surface.Render(validated, data)
                                              │
                        ┌─────────────────────┴─────────────────────┐
                        │ UiSurface.Render                          │
                        │   auto-Open if no window                  │
                        │   host.InvokeAsync(…) ──▶ STA UI thread   │
                        │       ScrollViewer                        │
                        │         └─ TreeRenderer.Render(tree,data) │
                        │   then: _nodeCount, _treeHash, _lastRender│
                        └─────────────────────┬─────────────────────┘
                                              │
                    5. read treeHash BACK from _surface.Status
                                              │
                       {ok:true, nodeCount, treeHash} ──▶ agent
```

**Step 3 completes before step 4 begins.** Validation is never interleaved with drawing. A
partially rendered invalid tree looks like a working display while silently omitting whatever
failed — which is the same "silence reads as health" defect `UNKNOWN` exists to prevent.

**Step 5 re-reads rather than recomputing.** An earlier version hashed the raw JSON here while
the surface hashed the structure, so `ui_render` and `ui_status` reported different values under
the same name.

## Inside `TreeRenderer.Render` — binding and formatting

For each node, recursively:

```
ValidatedNode ──▶ switch (node.Type)
                     │
                     ├─ leaf with a path (Metric / Field / Gauge)
                     │     PathResolver.Resolve(data, valuePath, scope)
                     │            │
                     │            ├─ found  → JsonElement
                     │            └─ ANY failure → null
                     │                 (absent key · index out of range · index on
                     │                  a non-array · key on a scalar · refused
                     │                  path · $item with no scope)
                     │            │
                     │     RenderRules.MetricText / DeltaText / GaugePercent
                     │            │
                     │     PathResolver.Display(v)  →  "UNKNOWN" when null
                     │            │
                     │     TextBlock.Text = <string>      ← inert; parses no markup
                     │     Brush          = Tone(tone)    ← closed set, never from the tree
                     │
                     ├─ Repeat  → Resolve(fromPath) → take ≤64 → recurse per item
                     │              with scope = that item ($item resolves against it)
                     │              empty → RenderRules.EmptyText(...)
                     │
                     ├─ Table   → Resolve(fromPath) → take ≤200 rows
                     │              per column: Resolve(row, col.ValuePath)
                     │              empty → RenderRules.EmptyText(...)
                     │
                     └─ container (Panel / Row) → recurse over Children
```

Two things never cross this boundary: **the tree never supplies a colour** (only a `tone` name,
mapped to a brush by the renderer), and **the tree never supplies an element type** (only a
component name, mapped to a WPF type by the `switch`).

## `ui_status`

```
UiTools.Status() ──▶ _surface.Status
                         │
                         ├─ WindowAlive: host.InvokeAsync(() => _window.IsVisible).Result
                         │     ← LIVE read, not a cached flag; the user can hit X anytime
                         │     ← any exception → false
                         ├─ Title, NodeCount, TreeHash, LastRenderUtc  (nullable = NOT MEASURED)
                         └─ LastFault: _host.LastFault?.Message
                         │
              each null ──▶ "UNKNOWN"   (LastFault → "none")
```

## Fault paths

| Where it happens | What the caller sees | What happens to the server |
|---|---|---|
| Malformed `tree`/`data` JSON | `{ok:false, rejected: "tree is not valid JSON: …"}` | unaffected |
| Anything the catalog refuses | `{ok:false, rejected: "<Type>.<prop>: <reason>"}` | unaffected; **UI untouched** |
| Unresolvable binding path | renders `UNKNOWN` in place | unaffected; render completes |
| Awaited UI work throws | exception surfaces on the caller's `Task` | host stays alive |
| **Unawaited** UI work throws (window event handler) | nothing — it had no caller | `Dispatcher.UnhandledException` marks it handled, records `LastFault`; **`ui_status` reports it** |
| User closes the window with X | — | `Window.Closed` clears the refs; `ui_status` reports `windowAlive: false` |
| `ui_close` | `{ok:true}` | **server keeps serving** |

The unawaited case is the one that matters. Without the supervisor, that exception terminates
the process and takes MCP tool serving down with the display.

## Verification

Generated 2026-08-15 by `repo_map.py map`.
Regenerate: `python repo_map.py map <repo> --out <dir>` · Check: `python repo_map.py check <repo> --docs docs/architecture`

| Claim | Value | Source |
|---|---|---|
| entryRoots | 1 | dependency-graph.json |
| runtimeCircularDeps | 0 | dependency-graph.json |
| totalSourceFiles | 17 | dependency-graph.json |

**Claims the gate cannot hold:** the sequences above are traced by reading `UiTools.Render`,
`UiSurface.Render`, `TreeRenderer.Render` and `UiThreadHost`. A dependency graph shows *that*
these files depend on each other, never *in what order* their calls run — the ordering is the
part that carries the safety property, and it is source-read.
