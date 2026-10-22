<!-- repo-map:no-verification -->
<!-- GENERATED FILE -- do not edit by hand.
     Regenerate with `repo-tools map`. -->

# Complete File Inventory

This report lists each source file that the map census finds, with a disposition. The census reads the files that git tracks. Outside a git work tree, it walks the folder. The census skips tool state, dependency folders and build output (for example `.git/`, `node_modules/`, `dist/` and `target/`), and `obj/` and `bin/` for C#.

**Language**: csharp

**Total files**: 25

## Disposition counts

| Disposition | Count | Meaning |
| --- | --: | --- |
| `reachable` | 12 | A `src`-area file that an entry root reaches. |
| `build-entry` | 1 | A `src`-area file that is an entry root. |
| `test-only` | 1 | A `src`-area file that only a test reaches. |
| `orphan` | 0 | A `src`-area file that nothing reaches. Delete it, or wire it to a root. |
| `test` | 11 | A file in a `tests/` folder, or a `*.test.ts` or `*.spec.ts` file. |
| `tool` | 0 | A file in a `tools/` or `scripts/` folder outside `src/`. |
| `config` | 0 | A `*.config.*` file of TypeScript or JavaScript. |
| `example` | 0 | A file in `examples/` or `docs/`. |
| `bench` | 0 | A file in `bench/` or `benchmarks/`, or a `*.bench.*` file. |
| **Total** | **25** | |

## Per-area counts

| Area | Files |
| --- | --: |
| `src` | 14 |
| `tests` | 11 |

## Per-package counts

| Package | Files |
| --- | --: |
| `UiMcp` | 6 |
| `UiMcp.Abstractions` | 8 |
| `UiMcp.Tests` | 11 |

## All files

| File | Package | Area | Disposition | Lines |
| --- | --- | --- | --- | --: |
| `src/UiMcp.Abstractions/CatalogValidator.cs` | UiMcp.Abstractions | src | reachable | 205 |
| `src/UiMcp.Abstractions/PathResolver.cs` | UiMcp.Abstractions | src | reachable | 127 |
| `src/UiMcp.Abstractions/PropTypes.cs` | UiMcp.Abstractions | src | reachable | 131 |
| `src/UiMcp.Abstractions/RenderRules.cs` | UiMcp.Abstractions | src | reachable | 121 |
| `src/UiMcp.Abstractions/UiValidationException.cs` | UiMcp.Abstractions | src | reachable | 15 |
| `src/UiMcp.Abstractions/ValidatedNode.cs` | UiMcp.Abstractions | src | reachable | 28 |
| `src/UiMcp.Abstractions/VisibilityCondition.cs` | UiMcp.Abstractions | src | reachable | 232 |
| `src/UiMcp.Abstractions/VisibilityEvaluator.cs` | UiMcp.Abstractions | src | reachable | 112 |
| `src/UiMcp/Hosting/IUiSurface.cs` | UiMcp | src | reachable | 43 |
| `src/UiMcp/Hosting/UiSurface.cs` | UiMcp | src | reachable | 158 |
| `src/UiMcp/Hosting/UiThreadHost.cs` | UiMcp | src | reachable | 150 |
| `src/UiMcp/Program.cs` | UiMcp | src | build-entry | 67 |
| `src/UiMcp/Rendering/TreeRenderer.cs` | UiMcp | src | reachable | 321 |
| `src/UiMcp/Tools/UiTools.cs` | UiMcp | src | test-only | 165 |
| `tests/UiMcp.Tests/CatalogRendererSeamTests.cs` | UiMcp.Tests | tests | test | 184 |
| `tests/UiMcp.Tests/CatalogValidatorTests.cs` | UiMcp.Tests | tests | test | 319 |
| `tests/UiMcp.Tests/PathResolverTests.cs` | UiMcp.Tests | tests | test | 166 |
| `tests/UiMcp.Tests/ProgramMetadataTests.cs` | UiMcp.Tests | tests | test | 45 |
| `tests/UiMcp.Tests/RenderRulesTests.cs` | UiMcp.Tests | tests | test | 215 |
| `tests/UiMcp.Tests/StaFixture.cs` | UiMcp.Tests | tests | test | 31 |
| `tests/UiMcp.Tests/TreeRendererTests.cs` | UiMcp.Tests | tests | test | 510 |
| `tests/UiMcp.Tests/UiSurfaceTests.cs` | UiMcp.Tests | tests | test | 241 |
| `tests/UiMcp.Tests/UiThreadHostTests.cs` | UiMcp.Tests | tests | test | 173 |
| `tests/UiMcp.Tests/UiToolsTests.cs` | UiMcp.Tests | tests | test | 333 |
| `tests/UiMcp.Tests/VisibilityTests.cs` | UiMcp.Tests | tests | test | 738 |

## Skipped links

Links (symbolic links and junctions) that the census did not follow. A link can point to another repository, so no file behind a link is in this inventory or in the graph.

_None._

## Warnings

_None._
