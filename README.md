# ui-mcp

An MCP server that hosts a native Windows window and renders **catalog-constrained JSON UI trees**.
Any agent connected to the host can draw to it; nothing else can.

> **Status: specification only. No implementation yet.** See [`docs/SPEC.md`](docs/SPEC.md).

## What it is for

Agents on this machine can measure the system and report in text. They cannot *show* anything.
ui-mcp gives them one shared display: an agent emits a JSON tree, the server validates it against a
fixed component catalog, and a WPF window renders it. Anything outside the catalog is refused.

## Why an MCP server rather than a script

A WPF window needs an STA thread with a message pump. An MCP server needs async stdio that never
blocks. A PowerShell script cannot do both — `ShowDialog()` blocks its only thread, so it can run
timers but cannot serve an agent. A C# server runs the UI on a dedicated STA thread and marshals
every update through the `Dispatcher`.

## Safety model

The tree is **data to validate, never instructions to obey**. Unknown components and props are
refused rather than ignored. No value from the JSON becomes a type name, a member name, or
executable text. A value that cannot be resolved renders as **UNKNOWN**, never as `0` — a blind
spot displayed as a green zero reads as health.

## Catalog

`StatusBanner` · `Panel` · `Row` · `Metric` · `Field` · `Gauge` · `Repeat` · `Table` · `Note`

## Relationship to other work here

| Repo / file | Relationship |
|---|---|
| `Windows-mcp` | Structural template: C#, bundled exe, plugin + `.mcp.json` wiring |
| `json-render` | Source of the constrained-catalog idea. No shared code — that is React/Next; this is WPF |
| `AdminLTE/JSON-UI/` | The same catalog and renderer in vanilla JS, rendering to HTML |
| `~/.claude/scripts/starship-console.ps1` | Reference implementation of this renderer against WPF |

## Build (once implemented)

```powershell
dotnet build
dotnet test
dotnet publish src/UiMcp -c Release -o bundle
```

## License

MIT
