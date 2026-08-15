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

## Build

```powershell
dotnet build
dotnet test

# The publish recipe is load-bearing — all four flags matter.
dotnet publish src/UiMcp -c Release -o bundle `
  -r win-x64 --self-contained false -p:PublishSingleFile=true
```

`bundle/UiMcp.exe` is committed — it is the artifact the plugin runs.

**Do not drop the flags.** A bare `dotnet publish -c Release -o bundle` produces a 156 KB
launcher plus **45 loose DLLs** scattered into `bundle/`, not the single 29,799,584-byte
executable `.mcp.json` points at. The recipe was previously undocumented and reconstructing it
cost a cleanup; the byte size is the check that it is right.

`--self-contained false` is the measured choice (SPEC 10.2): framework-dependent 28.42 MB
against self-contained 153.7 MB, because `WindowsDesktop.App 9.0.x` is present on both target
machines. Switching back is one flag.

Publish also emits `.pdb` files; only `UiMcp.exe` is committed. **Re-publish after any change to
`src/`, or the plugin keeps serving the previous build** — and re-run
`claude plugin install ui-mcp@local-marketplace` so the plugin cache picks it up.

## Install as a plugin

Registered in `~/Github/skills/.claude-plugin/marketplace.json` as a local source
(`./ui-mcp`, a symlink to this repo) and enabled in `~/.claude/settings.json`.

```powershell
claude plugin marketplace update local-marketplace
claude plugin install ui-mcp@local-marketplace
```

Then `/reload-plugins` in the session that should use it. **Installed is not serving** — the
tools only appear once the session binds the MCP server. Confirm by looking for
`mcp__plugin_ui-mcp_ui-mcp__ui_open` and calling it, not by seeing the plugin listed.

To check the deployed artifact directly, drive it over stdio:

```powershell
# answers {"name":"ui-mcp","version":"0.1.0"} and advertises ui_open/ui_render/ui_status/ui_close
& "$env:USERPROFILE\.claude\plugins\cache\local-marketplace\ui-mcp\0.1.0\bundle\UiMcp.exe"
```

## License

MIT
