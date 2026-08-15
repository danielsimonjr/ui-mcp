# ui-mcp — Dependency Graph

> **Derived from `dependency-graph.json`.** ui-mcp has no Markdown-emitting analyser of its
> own, so this file is authored from that artifact rather than generated. Refresh it by
> re-running `repo_map.py map` and updating both the tables and the Verification block.

## Read this first — C# edges are NAMESPACE-granular

A C# `using` names a **namespace**, not a file. `using UiMcp.Abstractions;` therefore produces
an edge to **every one of the six files declaring that namespace**, whether or not the importing
file uses anything from each.

This is not a defect in the analysis; it is what the language expresses. But it means the raw
edge counts below **over-state coupling at file granularity**, and a reader must not conclude
from the table that, say, `IUiSurface.cs` depends on `PathResolver.cs` — it does not, it merely
imports the namespace they share.

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

`UiMcp.Abstractions` depends on **nothing in this repo** and on no WPF assembly. That is the
architectural boundary, enforced by its `net9.0` target framework.

## External dependencies

| Package | Used by | For |
|---|---|---|
| `ModelContextProtocol.Server` | `Program.cs`, `UiTools.cs` | MCP server, stdio transport, `[McpServerTool]` |
| `Microsoft.Extensions.Hosting` | `Program.cs` | Generic host |
| `Microsoft.Extensions.DependencyInjection` | `Program.cs` | `AddSingleton<IUiSurface, UiSurface>` |
| `Microsoft.Extensions.Logging` | `Program.cs` | Logging, pinned to stderr |
| `FluentAssertions` | all 5 test files | Assertions |

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

`UiThreadHost.cs` having **zero** internal dependencies is worth noting: the concurrency
primitive knows nothing about the catalog, the renderer or MCP. It is the most reusable file in
the repository and the easiest to reason about in isolation.

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
**This is the namespace-granularity caveat in its other direction:** same-namespace coupling is
invisible to the graph, so this project's internal structure must be read from
[COMPONENTS.md](COMPONENTS.md), not inferred from zero edges.

### Tests

| File | Depends on |
|---|---|
| `CatalogValidatorTests.cs` | 6 × Abstractions |
| `PathResolverTests.cs` | 6 × Abstractions |
| `RenderRulesTests.cs` | 6 × Abstractions |
| `UiThreadHostTests.cs` | `IUiSurface.cs`, `UiSurface.cs`, `UiThreadHost.cs` |
| `UiToolsTests.cs` | 6 × Abstractions + 3 × Hosting + `UiTools.cs` |

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

`Program.cs` registers tools via `.WithToolsFromAssembly()`, which discovers `[McpServerTool]`
methods with a **source generator**. There is no `using UiMcp.Tools;` anywhere in `UiMcp`, so
no static edge exists for the analysis to follow, and the only in-repo importer is
`UiToolsTests.cs`.

Verified live rather than assumed: `tools/list` against the shipped binary returns all four
tools, and an end-to-end session drove a real window that an independent process observed.

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
| totalSourceFiles | 17 | dependency-graph.json |
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
