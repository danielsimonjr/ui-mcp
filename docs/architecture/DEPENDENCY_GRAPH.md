# ui-mcp — Dependency Graph

> **Derived from `dependency-graph.json`.** ui-mcp has no Markdown-emitting analyser of its
> own, so this file is authored from that artifact rather than generated. Refresh it by
> re-running `repo_map.py map` and updating both the tables and the Verification block.

## Read this first — C# edges are NAMESPACE-granular

A C# `using` names a **namespace**, not a file. `using UiMcp.Abstractions;` therefore produces
an edge to **every one of the six files declaring that namespace**, whether or not the importing
file uses anything from each.

The behaviour is not a defect in the analysis. The behaviour is what the language expresses.
The raw edge counts below therefore **over-state the coupling between files**. A reader must
not conclude from the table that `IUiSurface.cs` depends on `PathResolver.cs`. No such
dependency exists. `IUiSurface.cs` only imports the namespace that the two files share.

Consequences visible in this graph:

- Every file importing `UiMcp.Abstractions` shows 6 internal dependencies.
- `reachableFiles` is 11 of 12 `src` files partly because of this fan-out.
- Cycle detection is correspondingly conservative: **0 runtime cycles** is a strong result
  precisely because namespace-level edges would make cycles *easier*, not harder, to produce.

## Project-level dependencies

```
UiMcp.Tests ──▶ UiMcp ──▶ UiMcp.Abstractions
     │                          ▲
     └──────────────────────────┘
```

`UiMcp.Abstractions` depends on **nothing in this repository** and on no WPF assembly. That
independence is the architectural boundary. The `net9.0` target framework enforces it.

## External dependencies

| Package | Used by | For |
|---|---|---|
| `ModelContextProtocol.Server` | `Program.cs`, `UiTools.cs` | MCP server, stdio transport, `[McpServerTool]` |
| `Microsoft.Extensions.Hosting` | `Program.cs` | Generic host |
| `Microsoft.Extensions.DependencyInjection` | `Program.cs` | `AddSingleton<IUiSurface, UiSurface>` |
| `Microsoft.Extensions.Logging` | `Program.cs` | Logging, pinned to stderr |
| `FluentAssertions` | all 8 test classes | Assertions |

Only **one** external package reaches beyond the composition root: `ModelContextProtocol.Server`,
in `UiTools.cs`, for the tool attributes. Nothing in `UiMcp.Abstractions` has any external
dependency at all.

Runtime framework namespaces (`System.*`) are classified separately from packages. `Microsoft.*`
is correctly counted as **external**, not framework: `Microsoft.Extensions.*` arrives as NuGet
packages.

## Per-file edges

### `UiMcp` project

| File | Internal deps | External | Framework |
|---|---|---|---|
| `Program.cs` | `IUiSurface.cs`, `UiSurface.cs`, `UiThreadHost.cs` | 4 `Microsoft.*` / `ModelContextProtocol.Server` | — |
| `Tools/UiTools.cs` | 6 × Abstractions + `IUiSurface.cs`, `UiSurface.cs`, `UiThreadHost.cs` | `ModelContextProtocol.Server` | `System.ComponentModel`, `System.Security.Cryptography`, `System.Text`, `System.Text.Json` |
| `Hosting/UiSurface.cs` | 6 × Abstractions + `TreeRenderer.cs` | — | `System.Security.Cryptography`, `System.Text`, `System.Text.Json`, `System.Windows{,.Controls,.Media}` |
| `Hosting/IUiSurface.cs` | 6 × Abstractions | — | `System.Text.Json` |
| `Hosting/UiThreadHost.cs` | **none** | — | `System.Windows.Threading` |
| `Rendering/TreeRenderer.cs` | 6 × Abstractions | — | `System.Text.Json`, `System.Windows{,.Controls,.Controls.Primitives,.Media}` |

`UiThreadHost.cs` has **zero** internal dependencies, and that fact is worth note. The
concurrency primitive knows nothing about the catalog, the renderer or MCP. `UiThreadHost.cs`
is therefore the most reusable file in the repository, and also the easiest file to
understand alone.

### `UiMcp.Abstractions` project

Every file has **zero internal dependencies** and **zero external dependencies**.

| File | Framework only |
|---|---|
| `CatalogValidator.cs` | `System.Text.Json` |
| `PropTypes.cs` | `System.Text.Json`, `System.Text.RegularExpressions` |
| `PathResolver.cs` | `System.Globalization`, `System.Text.Json`, `System.Text.RegularExpressions` |
| `RenderRules.cs` | `System.Globalization`, `System.Text.Json` |
| `ValidatedNode.cs` | — |
| `UiValidationException.cs` | — |

The intra-project references (`CatalogValidator` → `PropTypes` → `ValidatedNode`) do not appear
as edges because they share the single `UiMcp.Abstractions` namespace and need no `using`.
**The namespace granularity works in the other direction here.** Coupling inside one namespace
is invisible to the graph. Read the internal structure of this project from
[COMPONENTS.md](COMPONENTS.md). Do not read it from the zero edges.

### Tests

| File | Depends on |
|---|---|
| `CatalogValidatorTests.cs` | 6 × Abstractions |
| `PathResolverTests.cs` | 6 × Abstractions |
| `RenderRulesTests.cs` | 6 × Abstractions |
| `UiThreadHostTests.cs` | `IUiSurface.cs`, `UiSurface.cs`, `UiThreadHost.cs` |
| `UiToolsTests.cs` | 6 × Abstractions + 3 × Hosting + `UiTools.cs` |
| `TreeRendererTests.cs` | 6 × Abstractions + `TreeRenderer.cs` + `StaFixture.cs` |
| `CatalogRendererSeamTests.cs` | 6 × Abstractions + `TreeRenderer.cs` + `StaFixture.cs` |
| `UiSurfaceTests.cs` | 6 × Abstractions + 3 × Hosting |
| `StaFixture.cs` | `UiThreadHost.cs` (shared infrastructure, not a test) |

## Entry point and reachability

| | |
|---|---|
| Entry root | `src/UiMcp/Program.cs` |
| Reachable from it | 11 files |
| Orphaned | **0** |
| Test-only | 1 — `src/UiMcp/Tools/UiTools.cs` |
| Dormant | 1 |

The root was found from `src/UiMcp/UiMcp.csproj` declaring `<OutputType>Exe</OutputType>`,
then the file in that project declaring `Main`.

### `UiTools.cs` is test-only in the graph and live in production

`Program.cs` registers the tools with `.WithToolsFromAssembly()`. That call finds each
`[McpServerTool]` method with a **source generator**. There is no `using UiMcp.Tools;` anywhere in `UiMcp`, so
no static edge exists for the analysis to follow, and the only in-repo importer is
`UiToolsTests.cs`.

A live test confirms this, and no part of it is an assumption. A `tools/list` call against the
shipped binary returns all four tools. An end-to-end session then drew a real window, and an
independent process observed that window.

**Any file reached only through reflection, attribute discovery or DI-by-convention will look
this way.** Treat a `test-only` or `orphan` disposition on such a file as a question, not a
verdict.

## Cycles

| | |
|---|---|
| Runtime circular dependencies | **0** |
| Type-only circular dependencies | **0** |
| Truncated | no |

Zero cycles is a genuinely strong result here, because namespace-granular edges make cycles
*more* likely to appear, not less. The layering — `Abstractions` ← `Hosting`/`Rendering` ←
`Tools` ← `Program` — holds strictly, with `IUiSurface` as the one inversion point that keeps
`UiTools` independent of `UiSurface`'s WPF.

## Verification

Generated 2026-08-15 by `repo_map.py map`.
Regenerate: `python repo_map.py map <repo> --out <dir>` · Check: `python repo_map.py check <repo> --docs docs/architecture`

| Claim | Value | Source |
|---|---|---|
| totalSourceFiles | 21 | dependency-graph.json |
| totalModules | 2 | dependency-graph.json |
| entryRoots | 1 | dependency-graph.json |
| reachableFiles | 11 | dependency-graph.json |
| orphanedFiles | 0 | dependency-graph.json |
| testOnlyFiles | 1 | dependency-graph.json |
| dormantFiles | 1 | dependency-graph.json |
| runtimeCircularDeps | 0 | dependency-graph.json |
| typeOnlyCircularDeps | 0 | dependency-graph.json |
| totalTypeOnlyImports | 0 | dependency-graph.json |
| circularDepsTruncated | False | dependency-graph.json |

**Claims the gate cannot hold:** the per-file edge tables are read from
`dependency-graph.json`'s `modules` section, which the gate does not check claim-by-claim. The
namespace-granularity explanation is a property of the C# language and of `repo_map`'s C#
resolver, not a metric. `UiTools.cs`'s production liveness was confirmed by driving the shipped
binary, which no static artifact can show.
