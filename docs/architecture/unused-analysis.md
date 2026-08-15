# ui-mcp — Unused Analysis

> **Derived from `unused-analysis.json`.** ui-mcp has no Markdown-emitting analyser of its own,
> so this file is authored from that artifact rather than generated. Refresh it by re-running
> `repo_map.py map` and updating both the tables and the Verification block.

## Result

| | |
|---|---|
| Files with no in-repo importer | **0** |
| Exports flagged | **0** |
| — of which deletion candidates (`unreferencedAnywhere`) | **0** |
| — referenced only within their own file | **0** |
| — unclassifiable | **0** |

Nothing is flagged.

## What changed, and why it is not merely a quieter report

The previous run flagged **2** exports: `ComponentSpec` and `PropRule`, both in
`CatalogValidator.cs`, both classified `referencedInModule` — used, but only inside the file that
declares them.

That classification was correct, and it was a **real finding**: both were `public` while every
one of their 15 occurrences was in one file. A consumer handed a `ComponentSpec` could do nothing
with it, because `Validate()` is the only entry point and it takes JSON. They described how the
catalog is *built*, which is not a fact `UiMcp.Abstractions` needs to publish.

**Both are now `internal`.** The count went 2 → 0 because the surface actually narrowed, not
because the analysis was relaxed. `dotnet build` reports 0 warnings and 0 errors, and all 217
tests pass — nothing outside the file was relying on them, exactly as the analysis said.

This is the intended use of this artifact: a `referencedInModule` entry is not a bug report, it
is a question — *does this need to be public?* — and here the answer was no.

## How usage is determined for C#, and what that limits

This matters enough to state prominently, because the method here is **weaker** than for the
other languages `repo_map` supports.

A C# `using` names a **namespace**, never a symbol. `using UiMcp.Abstractions;` says nothing
about *which* types are used, so the import edges carry no symbol names, and "who imports this
symbol?" is unanswerable from the dependency graph.

Usage is therefore determined by a **whole-identifier text search across every scanned file** —
the same class of text-level heuristic used to split `referencedInModule` from
`unreferencedAnywhere`. The consequences, stated rather than discovered later:

- **It can over-count.** A name appearing in another file's comment or string literal counts as a
  use. The analysis therefore **under-reports dead code rather than inventing it**, which is the
  safe direction for a list whose entries read as deletion candidates.
- **Reflection is invisible.** A type reached only via `Activator.CreateInstance`,
  DI-by-convention, or attribute discovery is invisible either way. `UiTools` is exactly such a
  type — discovered by source generator from `[McpServerToolType]`, it appears `test-only` in the
  graph while being fully live. See [DEPENDENCY_GRAPH.md](DEPENDENCY_GRAPH.md).
- **Names in non-scanned files do not count.** A type referenced only from a `.csproj`, XAML, or
  a JSON manifest would look unused.

For context on why this method exists at all: before it, the analysis assumed imports name
symbols — true for TypeScript and Python, false for C#. Run against this repository under that
assumption it flagged **15 of 20 exports, 9 as genuine deletion candidates**, including
`IUiSurface`, `PathResolver` and `TreeRenderer` — three of the most-used types here. Every count
was internally consistent and every gate passed, which is what made it dangerous.

## Files with no in-repo importer

None. The entry root (`Program.cs`) is excluded from this measure by definition — a root is what
nothing imports.

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
| unusedExportCount | 0 | unused-analysis.json |
| unreferencedAnywhereCount | 0 | unused-analysis.json |
| referencedInModuleCount | 0 | unused-analysis.json |
| unclassifiedExportCount | 0 | unused-analysis.json |
| noImporterFileCount | 0 | unused-analysis.json |
| unusedExportsCount | 0 | dependency-graph.json |

**Claims the gate cannot hold:** that `ComponentSpec` and `PropRule` were the two previously
flagged exports, and that all 15 of their occurrences sit in `CatalogValidator.cs`, was confirmed
by grep across the repository — a second method, not a restatement of the artifact. The
"15 of 20 / 9 deletion candidates" figures are from running the prior version of the analysis
against this repository during the work that fixed it.
