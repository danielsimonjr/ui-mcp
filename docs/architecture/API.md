# ui-mcp — API

Two surfaces: the **MCP tools** an agent calls over stdio, and the **exported .NET types**.
The first is the real product API; the second matters only to this repo and its tests.

---

## MCP tool surface

Server identity is published through MCP 2.0 `server/discover`: name `ui-mcp`, title
`ui-mcp`, version from `Directory.Build.props`, the repo description, and server instructions.
Transport is stdio. The server negotiates protocol `2026-07-28` for MCP 2.0 clients and still
accepts the legacy `initialize` handshake for older clients. Each tool returns an indented JSON
text payload.

`tools/list` also publishes human-readable tool titles plus safety hints where they are true:
`ui_status` is read-only; all four tools are idempotent.

### `ui_open`

Open the shared UI window, or focus it if already open. Idempotent — a second call does not
create a second window.

| Param | Type | Default | Meaning |
|---|---|---|---|
| `title` | string | `"Starship Console"` | Window title |
| `topmost` | bool | `false` | Keep above other windows |
| `width` | double | `1100` | Width in device-independent pixels |
| `height` | double | `800` | Height in device-independent pixels |

```json
{ "ok": true, "title": "Starship Console" }
```

### `ui_render`

Render a catalog-constrained JSON UI tree, replacing whatever is displayed. **The tree is
validated in full first** and drawn only if all of it passed; anything outside the catalog is
refused rather than ignored.

| Param | Type | Required | Meaning |
|---|---|---|---|
| `tree` | JSON object *or* a JSON string containing one | yes | The UI tree |
| `data` | JSON object *or* a JSON string containing one | no | What the tree's paths resolve against |

```json
{ "ok": true, "nodeCount": 19, "treeHash": "105e2c486c52" }
```

On refusal — and the UI is **not** touched:

```json
{ "ok": false, "rejected": "Note: unknown prop \"onclick\"" }
```

The tool accepts both parameter shapes on purpose. While the parameters were `string`, an
object-shaped call failed inside SDK parameter binding *before the method ran*. No refusal path
could run, so the caller saw only "An error occurred invoking 'ui_render'".

### `ui_status`

Report window state, the last render, and any absorbed UI fault. **Anything that cannot be
measured is `UNKNOWN`, never `0`.**

```json
{
  "windowAlive": true,
  "title": "Starship Console",
  "nodeCount": "19",
  "treeHash": "105e2c486c52",
  "lastRenderUtc": "2026-08-15T18:04:11.2280000+00:00",
  "lastFault": "none"
}
```

`windowAlive` comes live from the window, and not from a cached flag. `lastFault` is `"none"`
when the supervisor absorbed nothing. Any other value is the message of the last unawaited
fault on the UI thread. Treat the display as degraded when that happens.

### `ui_close`

Close the window. **The server stays up and keeps serving tools** — a closed display is not a
stopped service.

```json
{ "ok": true }
```

---

## The tree format

```json
{ "type": "<component>", "props": { … }, "children": [ … ] }
```

- `type` — required, must be a catalog component name.
- `props` — unknown props are **refused**, not ignored. Missing required props are refused.
- `children` — only on components that accept them; max 64; max tree depth 12.

### Components

| Component | Required props | Optional props | Children |
|---|---|---|---|
| `StatusBanner` | `label`, `tone` | `detail` | — |
| `Panel` | `title` | `tone` | ✔ |
| `Row` | `cols` ∈ {1,2,3,4} | — | ✔ |
| `Metric` | `label`, `valuePath` | `unit`, `deltaPath`, `tone` | — |
| `Field` | `label`, `valuePath` | `tone` | — |
| `Gauge` | `label`, `valuePath` | `maxPath`, `tone` | — |
| `Repeat` | `fromPath` | `emptyText` | ✔ |
| `Table` | `fromPath`, `columns` | `emptyText` | — |
| `Note` | `text` | `tone` | — |

### Prop types

| Type | Rule |
|---|---|
| text | string, ≤ 500 chars |
| tone | one of `nominal` · `attention` · `critical` · `degraded` · `muted` · `info` |
| path | `^[A-Za-z0-9_.\[\]]+$`, optionally prefixed `$item` or `$item.`; `__proto__`, `constructor`, `prototype` refused anywhere in the string |
| cols | one of 1, 2, 3, 4 |
| columns | array of ≤ 8 `{header, valuePath}`; each `valuePath` validated as a path |

### Paths and binding

Dotted, with array indices: `sections.roster.live`, `sections.disk.drives[0].freeGb`,
`grid[1][0]`, bare `[2]`. Inside a `Repeat` or `Table`, `$item` addresses the current element
(`$item` alone is the item itself; `$item.name` is a key on it).

**An unresolvable path renders `UNKNOWN`, never `0`.** The rule covers six cases:

- an absent key,
- an index out of range,
- an index on a value that is not an array,
- a key on a scalar,
- a refused path,
- `$item` with no scope.

JSON `null` also renders `UNKNOWN`. A key that is present but null carries no more information
than an absent key.

Value formatting: numbers round-trip in `InvariantCulture`; `true`/`false` become `YES`/`NO`;
an array becomes its length; an object becomes `OBJ`.

### Caps

| Cap | Value | Behaviour past it |
|---|---|---|
| Tree depth | 12 | refused |
| Children per node | 64 | refused |
| Text length | 500 | refused |
| Table columns | 8 | refused |
| `Repeat` items rendered | 64 | **truncated**, not refused |
| `Table` rows rendered | 200 | **truncated**, not refused |

The last two caps truncate because the *data* bounds them, and the agent may not control the
data. The other caps are properties of the tree, which the agent does control.

---

## Exported .NET types

Consumed by `UiMcp` and its tests. Not a published NuGet package — the shipped artifact is an
executable.

### `UiMcp.Abstractions`

| Type | Kind | Purpose |
|---|---|---|
| `ValidatedNode` | record | A node that passed validation. The renderer accepts only this. |
| `ValidatedColumn` | record | A validated table column. |
| `UiValidationException` | exception | A deliberate refusal, distinguishable from a fault. |
| `CatalogValidator` | static | `Validate`, `ComponentNames`, `MaxDepth`, `MaxChildren`. |
| `PropTypes` | static | The prop validators, `Tones`, `RowColumnCounts`, caps. |
| `PathResolver` | static | `Resolve`, `Display`, `Unknown`. |
| `RenderRules` | static | `IsUnknown`, `MetricText`, `DeltaText`, `GaugePercent`, `EmptyText`, caps. |

### `UiMcp`

| Type | Kind | Purpose |
|---|---|---|
| `IUiSurface` | interface | The display, behind a seam so tools are testable with no desktop. |
| `UiSurfaceStatus` | record | What `ui_status` reports. Nullable = NOT MEASURED. |
| `UiSurface` | class | The real WPF surface. |
| `UiThreadHost` | class | The single STA thread and its supervisor. |
| `TreeRenderer` | static | `ValidatedNode` → `UIElement`. |
| `UiTools` | class | The four MCP tools. |

`Program` is `internal` and exports nothing.

## Verification

Generated 2026-08-15 by `repo_map.py map`.
Regenerate: `python repo_map.py map <repo> --out <dir>` · Check: `python repo_map.py check <repo> --docs docs/architecture`

| Claim | Value | Source |
|---|---|---|
| totalExports | 23 | dependency-graph.json |
| totalSourceFiles | 21 | dependency-graph.json |
| duplicateCount | 0 | duplicate-symbols.json |

**Claims that the gate cannot hold.** A reading of `UiTools.cs` gives the **four tools**, their
parameters, their defaults and their response shapes. A reading of `CatalogValidator.cs` and
`PropTypes.cs` gives the **nine components** and every prop rule. repo_map counts exported
*types*. `totalExports` (23) therefore covers the 13 types listed above **and the test classes
and fixtures**. The number is not a count of tools, components or props. `ComponentSpec` and `PropRule` are deliberately absent from that
table: both were narrowed to `internal`, because every use of either is inside
`CatalogValidator.cs`. The tool list was additionally confirmed over the wire: a real `tools/list`
against the shipped binary returned exactly `ui_open`, `ui_render`, `ui_status`, `ui_close`.
