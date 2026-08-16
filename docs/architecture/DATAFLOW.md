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

**No UI thread exists yet.** `UiSurface` constructs `UiThreadHost` as a field. The STA thread
does not start until the first `Open` call. A host with no desktop therefore fails when a
caller asks for a window, and not at launch. The error can then name what went wrong.

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

**Step 3 completes before step 4 begins.** The system never mixes validation with drawing. A
part-rendered invalid tree looks like a working display, and it quietly leaves out whatever
failed. That result is the same "silence reads as health" defect that `UNKNOWN` prevents
elsewhere.

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

Two things never cross this boundary. **The tree never supplies a colour.** It supplies a
`tone` name, and the renderer maps that name to a brush. **The tree never supplies an element
type.** It supplies a component name, and the `switch` maps that name to a WPF type.

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
| totalSourceFiles | 21 | dependency-graph.json |

**Claims that the gate cannot hold.** A reading of `UiTools.Render`, `UiSurface.Render`,
`TreeRenderer.Render` and `UiThreadHost` gives every sequence above. A dependency graph shows
*that* these files depend on each other. It never shows *the order* of their calls. The order
carries the safety property, and a reading of the source gives it.
