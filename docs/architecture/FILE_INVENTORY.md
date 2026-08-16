# ui-mcp — File Inventory

Every scanned file, its project, area and disposition.

> **Derived from `file-inventory.json`.** ui-mcp has no Markdown-emitting analyser of its own,
> so this file is authored from that artifact rather than generated. Refresh it by re-running
> `repo_map.py map` and updating both the table and the Verification block.

## Scope — what is counted

**21 files.** `obj/` and `bin/` are excluded as build output. The whole working tree contains
**39** `.cs` files; the other 18 are compiler-generated (`*.AssemblyInfo.cs`,
`*.GlobalUsings.g.cs`, and copies under `bin/`). Counting them would credit more than half the
repository to the compiler and add namespaces no developer declared.

## By project

| Project | Files | LOC |
|---|---|---|
| `UiMcp` | 6 | 878 |
| `UiMcp.Abstractions` | 6 | 598 |
| `UiMcp.Tests` | 9 | 1899 |
| **Total** | **21** | **3375** |

The owning `.csproj` gives the attribution. In a .NET repository, the project file does the
work that a `package.json` does elsewhere. It declares one compilation, its target framework
and its dependencies.

## By area and disposition

| Area | Files |
|---|---|
| `src` | 12 |
| `tests` | 9 |

| Disposition | Files |
|---|---|
| `reachable` | 10 |
| `build-entry` | 1 |
| `test-only` | 1 |
| `orphan` | **0** |
| `test` | 9 |

## Every file

### `UiMcp.Abstractions` — 6 files, 598 LOC

| File | LOC | Disposition | Holds |
|---|---|---|---|
| `src/UiMcp.Abstractions/CatalogValidator.cs` | 182 | reachable | The nine-component catalog and `Validate` |
| `src/UiMcp.Abstractions/PropTypes.cs` | 130 | reachable | Prop validators; charset and prototype guards |
| `src/UiMcp.Abstractions/PathResolver.cs` | 127 | reachable | Path binding and `Display`; the UNKNOWN rule |
| `src/UiMcp.Abstractions/RenderRules.cs` | 121 | reachable | Render judgement with no WPF |
| `src/UiMcp.Abstractions/ValidatedNode.cs` | 23 | reachable | `ValidatedNode`, `ValidatedColumn` |
| `src/UiMcp.Abstractions/UiValidationException.cs` | 15 | reachable | The refusal type |

### `UiMcp` — 6 files, 878 LOC

| File | LOC | Disposition | Holds |
|---|---|---|---|
| `src/UiMcp/Rendering/TreeRenderer.cs` | 314 | reachable | `ValidatedNode` → WPF `UIElement` |
| `src/UiMcp/Tools/UiTools.cs` | 159 | **test-only** ⚠ | The four MCP tools |
| `src/UiMcp/Hosting/UiSurface.cs` | 158 | reachable | The real WPF surface |
| `src/UiMcp/Hosting/UiThreadHost.cs` | 150 | reachable | STA thread, dispatcher, supervisor |
| `src/UiMcp/Program.cs` | 54 | **build-entry** | Composition root, `Main` |
| `src/UiMcp/Hosting/IUiSurface.cs` | 43 | reachable | `IUiSurface`, `UiSurfaceStatus` |

> ⚠ **`UiTools.cs` is NOT dead code.** Its `test-only` disposition is an artifact of static
> analysis, and the docs would be lying if they repeated it without saying so. `Program.cs`
> registers tools with `.WithToolsFromAssembly()`, which discovers `[McpServerTool]` methods by
> **source generator** — so no `using UiMcp.Tools;` exists for a static scan to follow, and the
> only in-repo edge into the file comes from `UiToolsTests.cs`.
>
> Verified live, not inferred: a `tools/list` against the shipped binary returns all four tools,
> and an end-to-end `ui_open` → `ui_render` → `ui_status` session drove a real window that an
> independent process observed. Any file reachable only through reflection or attribute
> discovery will show this way.

### `UiMcp.Tests` — 9 files, 1899 LOC

| File | LOC | Disposition | Covers |
|---|---|---|---|
| `tests/UiMcp.Tests/TreeRendererTests.cs` | 419 | test | The nine-way switch, truncation caps, `$item` scope, tone map, inert text |
| `tests/UiMcp.Tests/UiToolsTests.cs` | 261 | test | The four tools, refusal ordering |
| `tests/UiMcp.Tests/CatalogValidatorTests.cs` | 259 | test | Catalog, props, caps, boundaries |
| `tests/UiMcp.Tests/RenderRulesTests.cs` | 215 | test | Units, deltas, gauge clamp, empty text |
| `tests/UiMcp.Tests/UiSurfaceTests.cs` | 213 | test | Window lifetime, idempotent open, structural hash, null-not-zero status |
| `tests/UiMcp.Tests/UiThreadHostTests.cs` | 173 | test | STA, marshalling, supervisor, shutdown |
| `tests/UiMcp.Tests/PathResolverTests.cs` | 166 | test | Paths, indices, `$item`, UNKNOWN |
| `tests/UiMcp.Tests/CatalogRendererSeamTests.cs` | 162 | test | Validator ↔ renderer agreement, exhaustive over the catalog |
| `tests/UiMcp.Tests/StaFixture.cs` | 31 | test | Shared STA thread — infrastructure, not a test |

## Not scanned

| Path | Why |
|---|---|
| `**/obj/`, `**/bin/` | Build output — 18 generated `.cs` files |
| `bundle/UiMcp.exe` | The 28.42 MB shipped binary |
| `*.csproj`, `*.sln`, `Directory.Build.props`, `global.json` | Build configuration, not `.cs` source |
| `.mcp.json`, `.claude-plugin/plugin.json` | Plugin manifests |
| `tools/probe-desktop.ps1` | PowerShell probe |
| `examples/starship-view.json` | Sample tree |
| `docs/` | This documentation |

`Directory.Build.props` is worth naming despite not being scanned. The file is the **single
source** of the version, which `Program.ServerVersion` reads off the assembly rather than
duplicating.

## Verification

Generated 2026-08-15 by `repo_map.py map`.
Regenerate: `python repo_map.py map <repo> --out <dir>` · Check: `python repo_map.py check <repo> --docs docs/architecture`

| Claim | Value | Source |
|---|---|---|
| totalFiles | 21 | file-inventory.json |
| totalSourceFiles | 21 | dependency-graph.json |
| totalLinesOfCode | 3375 | dependency-graph.json |
| orphanedFiles | 0 | dependency-graph.json |
| testOnlyFiles | 1 | dependency-graph.json |
| entryRoots | 1 | dependency-graph.json |
| noImporterFileCount | 0 | unused-analysis.json |

**Claims that the gate cannot hold.** The split of files and lines for each project comes from
the `files` array in `file-inventory.json`. Each entry there carries a `package` value and a
`loc` value, and this document adds them. The gate checks the totals. The gate does not check
the breakdown. A count of the `.cs` files in the working tree, with and without `obj` and
`bin`, gave the **39 against 21** split. A reading of the source gave the "Holds" column and
the "Covers" column.
