# ui-mcp

An MCP server that hosts a native Windows window and renders **catalog-constrained JSON UI trees**.
Any agent connected to the host can draw to it; nothing else can.

> **Status: implemented.** Four tools, nine components, a `visible` condition on every node, and a
> test suite that CI runs on every change. Design intent is in [`docs/SPEC.md`](docs/SPEC.md); the
> as-built architecture is in [`docs/architecture/`](docs/architecture/OVERVIEW.md).

## What it is for

Agents on this machine can measure the system and report in text. They cannot *show* anything.
ui-mcp gives them one shared display. An agent emits a JSON tree. The server validates the tree
against a fixed component catalog, and a WPF window renders it. The server refuses anything
outside the catalog.

## Why an MCP server rather than a script

A WPF window needs an STA thread with a message pump. An MCP server needs async stdio that never
blocks. A PowerShell script cannot do both. `ShowDialog()` blocks the only thread of the script.
The script can run timers but cannot serve an agent. A C# server runs the UI on a dedicated STA
thread and marshals every update through the `Dispatcher`.

## Safety model

The tree is **data to validate, never instructions to obey**. The server refuses unknown components,
props and node-level keys instead of ignoring them. No value from the JSON becomes a type name,
a member name, or executable text. A value that cannot be resolved renders as **UNKNOWN**,
never as `0` — a blind spot displayed as a green zero reads as health.

## Catalog

`StatusBanner` · `Panel` · `Row` · `Metric` · `Field` · `Gauge` · `Repeat` · `Table` · `Note`

## Documentation

The counts in these documents come from a parse of the source, and a gate re-checks them. The
gate exits non-zero when the code moves (`repo-tools check . --docs=docs/architecture`). A claim
that the gate cannot measure names its source in the document.

| Document | Answers |
|---|---|
| [OVERVIEW.md](docs/architecture/OVERVIEW.md) | What this is, what it does, how it is laid out |
| [ARCHITECTURE.md](docs/architecture/ARCHITECTURE.md) | Why it is built this way — principles and decisions |
| [COMPONENTS.md](docs/architecture/COMPONENTS.md) | Each module, with real signatures |
| [DATAFLOW.md](docs/architecture/DATAFLOW.md) | How a `ui_render` call travels end to end |
| [API.md](docs/architecture/API.md) | The MCP tool surface and the tree format |
| [FILE_INVENTORY.md](docs/architecture/FILE_INVENTORY.md) | Every file, its project and disposition |
| [TEST_COVERAGE.md](docs/architecture/TEST_COVERAGE.md) | What is tested, and the gaps that matter |
| [DEPENDENCY_GRAPH.md](docs/architecture/DEPENDENCY_GRAPH.md) | Who depends on whom |
| [unused-analysis.md](docs/architecture/unused-analysis.md) | Exports with no external user |
| [duplicate-symbols.md](docs/architecture/duplicate-symbols.md) | Names defined more than once |

## Relationship to other work here

| Repo / file | Relationship |
|---|---|
| `Windows-mcp` | Structural template: C#, bundled exe, plugin + `.mcp.json` wiring |
| `json-render` | Source of the constrained-catalog idea. No shared code — that is React/Next; this is WPF |
| `danielsimonjr/JSON-UI/` | The same component catalog as a TypeScript library (React renderer + headless renderer). The source of truth for component props, visibility conditions, and path resolution semantics. |
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
launcher plus **45 loose DLLs** scattered into `bundle/`. `.mcp.json` points at one single-file
executable of about 30 MB instead. The recipe was previously undocumented and reconstructing it
cost a cleanup; the file size is the check that it is right.

`--self-contained false` is the measured choice (SPEC 10.2): framework-dependent 28.42 MB
against self-contained 153.7 MB, because `WindowsDesktop.App 9.0.x` is present on both target
machines. Switching back is one flag.

Publish also emits `.pdb` files; only `UiMcp.exe` is committed. **Re-publish after any change to
`src/`, or the plugin keeps serving the previous build.** Commit the rebuilt `bundle/UiMcp.exe`
and push it. The plugin cache holds a clone of the remote repository, so a change that is not
pushed does not deploy. Then run `claude plugin marketplace update local-marketplace` and
`claude plugin update ui-mcp@local-marketplace`. The updater reads the version in
`.claude-plugin/plugin.json`, so a new bundle needs a new version there.

## Install as a plugin

Registered in `.claude-plugin/marketplace.json` of the `local-marketplace` marketplace
(`danielsimonjr/skills`) with a `url` source that points at this repository, and enabled in
`~/.claude/settings.json`.

```powershell
claude plugin marketplace update local-marketplace
claude plugin install ui-mcp@local-marketplace
```

Then `/reload-plugins` in the session that should use it. **Installed is not serving** — the
tools only appear once the session binds the MCP server. Confirm by looking for
`mcp__plugin_ui-mcp_ui-mcp__ui_open` and calling it, not by seeing the plugin listed.

To check the deployed artifact directly, drive it over stdio:

```powershell
# MCP 2.0 clients should send server/discover first; legacy clients can still initialize.
& "$env:USERPROFILE\.claude\plugins\cache\local-marketplace\ui-mcp\<version>\bundle\UiMcp.exe"
```

## License

MIT
