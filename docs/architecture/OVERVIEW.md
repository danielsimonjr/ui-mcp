# ui-mcp — Overview

An MCP server that gives any connected agent a **shared Windows desktop window** to draw
structured status displays into. The agent sends a JSON tree; ui-mcp validates it against a
closed component catalog and renders it with WPF. There is one window, and every agent draws
to the same one.

## What it is for

Agents on this machine produce status that is hard to read as scrolling text — roster state,
disk headroom, job progress. `ui-mcp` gives them a persistent visual surface without giving
them the ability to execute arbitrary UI. The tree is **data to validate, never instructions
to obey**.

## Capabilities

Four MCP tools on stdio, served through MCP `2026-07-28` (`server/discover`) with legacy
`initialize` backward compatibility. Server identity, instructions, and tool safety hints are
published at discovery; each tool carries a human-readable title.

| Tool | Does |
|---|---|
| `ui_open` | Opens the shared window, or focuses it. Idempotent — a second call never opens a second window. |
| `ui_render` | Validates a catalog-constrained JSON tree **in full**, then replaces the display. Refuses anything outside the catalog. |
| `ui_status` | Reports window state, last render, and any absorbed UI fault. Anything unmeasured reports `UNKNOWN`, never `0`. |
| `ui_close` | Closes the window. **The server stays up** — a closed display is not a stopped service. |

Nine catalog components: `StatusBanner`, `Panel`, `Row`, `Metric`, `Field`, `Gauge`,
`Repeat`, `Table`, `Note`.

Any node can also carry a `visible` condition, which hides the node when the condition is
false. The condition is one of twelve closed forms (see [API.md](API.md)).

## Layout

Three projects. The split is a compiler-enforced boundary, not a convention.

| Project | TFM | Holds | Files / LOC |
|---|---|---|---|
| `src/UiMcp.Abstractions` | `net9.0` | The catalog, validator, path resolver, render *rules*, visibility conditions. **No WPF.** | 8 / 933 |
| `src/UiMcp` | `net9.0-windows10.0.19041.0` | MCP host, tool surface, STA thread host, WPF renderer | 6 / 899 |
| `tests/UiMcp.Tests` | `net9.0-windows10.0.19041.0` | xunit + FluentAssertions + Moq | 11 / 2795 |

**`Abstractions` targets plain `net9.0` deliberately.** The spec requires that the project
does not reference WPF. A non-Windows target framework makes that requirement a compiler
guarantee instead of a habit. The same choice lets a runner with no desktop test every
judgement in the system.

```
Program.cs ──▶ UiTools ──▶ IUiSurface ──▶ UiSurface ──▶ UiThreadHost (STA)
                  │                            │
                  └──▶ CatalogValidator        └──▶ TreeRenderer ──▶ PathResolver
                            │                                            │
                            └──▶ PropTypes ──▶ ValidatedNode        RenderRules
```

`CatalogValidator` parses the `visible` key into a `VisibilityCondition`, and `ValidatedNode`
holds it. `TreeRenderer` asks `VisibilityEvaluator` whether to draw each node.

## The numbers

25 source files, 4,955 lines, 42 exported types, 1 entry root, **0 circular dependencies**,
**0 duplicate symbols**, **0 orphaned files**, **0 unused exports**. 474 tests, all passing.

The counts include the 11 test files. They **exclude `obj/` and `bin/`**. A build writes
compiler-generated `.cs` files (`*.AssemblyInfo.cs`, `*.GlobalUsings.g.cs`) into those
directories. A count of every `.cs` file in a built working tree is therefore larger than this
table. A count taken by hand will not match this table, and this table is the one that is right.

## Documentation

| Document | Answers |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Why it is built this way — principles and key decisions |
| [COMPONENTS.md](COMPONENTS.md) | Each module, with real signatures |
| [DATAFLOW.md](DATAFLOW.md) | How a `ui_render` call travels end to end |
| [API.md](API.md) | The full public surface |
| [FILE_INVENTORY.md](FILE_INVENTORY.md) | Every file, its disposition, per-project counts |
| [TEST_COVERAGE.md](TEST_COVERAGE.md) | What is tested, what is not, and which gaps matter |
| [DEPENDENCY_GRAPH.md](DEPENDENCY_GRAPH.md) | Who depends on whom |
| [unused-analysis.md](unused-analysis.md) | Exports with no external user |
| [duplicate-symbols.md](duplicate-symbols.md) | Names defined more than once |

Design intent and the v0.1 definition of done live in [../SPEC.md](../SPEC.md).

## Verification

Generated 2026-10-22 by `repo-tools map`.
Regenerate: `repo-tools map <repo> --out=<dir>` · Check: `repo-tools check <repo> --docs=docs/architecture`

| Claim | Value | Source |
|---|---|---|
| totalSourceFiles | 25 | dependency-graph.json |
| totalLinesOfCode | 4955 | dependency-graph.json |
| totalExports | 42 | dependency-graph.json |
| totalModules | 2 | dependency-graph.json |
| entryRoots | 1 | dependency-graph.json |
| runtimeCircularDeps | 0 | dependency-graph.json |
| typeOnlyCircularDeps | 0 | dependency-graph.json |
| orphanedFiles | 0 | dependency-graph.json |
| duplicateCount | 0 | duplicate-symbols.json |

**Claims that the gate cannot hold.** Each claim below gives its basis. A direct reading of
`UiTools.cs` and `CatalogValidator.cs` confirms the **four MCP tools** and the **nine catalog
components**. No `repo-tools` metric gives them: `repo-tools` counts exported *types*, and a tool
is a method. The **474 tests** figure is the result of `dotnet test UiMcp.sln -c Release`, which CI
also runs. A graph metric does not give this figure.
