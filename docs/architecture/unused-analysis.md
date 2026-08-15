# ui-mcp — Unused Analysis

> **Derived from `unused-analysis.json`.** ui-mcp has no Markdown-emitting analyser of its own,
> so this file is authored from that artifact rather than generated. Refresh it by re-running
> `repo_map.py map` and updating both the tables and the Verification block.

## Result

| | |
|---|---|
| Files with no in-repo importer | **0** |
| Exports flagged | **2** |
| — of which deletion candidates (`unreferencedAnywhere`) | **0** |
| — referenced only within their own file | 2 |
| — unclassifiable | 0 |

**Nothing here is a deletion recommendation.** Both flagged exports are used; they are flagged
because their use is confined to the file that declares them.

## The two flagged exports

| File | Export | What it actually is |
|---|---|---|
| `src/UiMcp.Abstractions/CatalogValidator.cs` | `ComponentSpec` | The record describing what one catalog component accepts |
| `src/UiMcp.Abstractions/CatalogValidator.cs` | `PropRule` | The record describing one prop's validator and optionality |

Both are used extensively — every one of the nine catalog entries is a `ComponentSpec` built
from `PropRule` values — but **only inside `CatalogValidator.cs`**, which is where the catalog
lives. Verified by grep: every occurrence of either name across the repository is in that one
file.

**This is a real finding, correctly classified.** Both are `public` while their entire use is
private to one file. Making them `internal` would narrow the public surface of
`UiMcp.Abstractions` at no cost to any caller. That is a tidy-up, not a bug, and it is
deliberately not being done as part of writing documentation.

## How usage was determined for C#, and what that limits

This matters enough to state prominently, because the method here is **weaker** than for the
other languages `repo_map` supports.

A C# `using` names a **namespace**, never a symbol. `using UiMcp.Abstractions;` says nothing
about *which* types are used, so the import edges carry no symbol names, and the question "who
imports this symbol?" is unanswerable from the dependency graph.

Usage is therefore determined by a **whole-identifier text search across every scanned file** —
the same class of text-level heuristic used to split `referencedInModule` from
`unreferencedAnywhere`. The consequences, stated rather than discovered later:

- **It can over-count.** A name appearing in another file's comment or string literal counts as
  a use. The analysis therefore **under-reports dead code rather than inventing it**, which is
  the safe direction for a list whose entries read as deletion candidates.
- **Reflection is invisible.** A type reached only via `Activator.CreateInstance`,
  DI-by-convention, or attribute discovery is invisible either way. `UiTools` is exactly such a
  type — it is discovered by source generator from `[McpServerToolType]` and appears `test-only`
  in the graph while being fully live. See [DEPENDENCY_GRAPH.md](DEPENDENCY_GRAPH.md).
- **Names in non-scanned files do not count.** A type referenced only from a `.csproj`, XAML, or
  a JSON manifest would look unused.

For context on why this method exists at all: before it, the analysis assumed imports name
symbols — true for TypeScript and Python, false for C#. Run against this repository under that
assumption it flagged **15 of 20 exports, 9 of them as genuine deletion candidates**, including
`IUiSurface`, `PathResolver` and `TreeRenderer` — three of the most-used types in the codebase.
Every count was internally consistent and every gate passed, which is what made it dangerous.

## Files with no in-repo importer

None. Every one of the 12 `src` files is imported by something, and the entry root
(`Program.cs`) is excluded from this measure by definition — a root is what nothing imports.

## Before deleting anything

Read the caveats above, then confirm with a second method: grep the identifier across the whole
working tree including `.csproj`, XAML and JSON files, and check whether the type is reached by
attribute or reflection. A `public` type that only *looks* unused is common in this repository
precisely because MCP tool discovery is attribute-driven.

## Verification

Generated 2026-08-15 by `repo_map.py map`.
Regenerate: `python repo_map.py map <repo> --out <dir>` · Check: `python repo_map.py check <repo> --docs docs/architecture`

| Claim | Value | Source |
|---|---|---|
| unusedExportCount | 2 | unused-analysis.json |
| unreferencedAnywhereCount | 0 | unused-analysis.json |
| referencedInModuleCount | 2 | unused-analysis.json |
| unclassifiedExportCount | 0 | unused-analysis.json |
| noImporterFileCount | 0 | unused-analysis.json |
| unusedExportsCount | 2 | dependency-graph.json |

**Claims the gate cannot hold:** the identification of the two flagged exports as
`ComponentSpec` and `PropRule` comes from `unused-analysis.json`'s `referencedInModule` map,
which the gate does not check entry-by-entry. That every occurrence of both names is confined to
`CatalogValidator.cs` was confirmed by grep across the repository — a second method, not a
restatement of the artifact. The "15 of 20 / 9 deletion candidates" figures are from running the
prior version of the analysis against this repository during the work that fixed it.
