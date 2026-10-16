# ui-mcp — Test Coverage

**290 tests, 0 failures, 0 skipped** (`dotnet test`). The test project holds 11 files (10 test
files and 1 fixture) and 2,374 lines. The 2,374 lines are 57% of the 4,173 lines in the
repository's `.cs` files. CI runs the suite on every push to `main` and on every pull request,
with `dotnet test UiMcp.sln --no-build -c Release` on `windows-latest`.

No line-coverage instrumentation is configured, so this file reports coverage **by component and
by behaviour**, which is the honest thing to report. This document gives no percentage, because
nobody measured one.

## By component

| Component | LOC | Test file | Tests | Assessment |
|---|---|---|---|---|
| `CatalogValidator` + `PropTypes` | 326 | `CatalogValidatorTests.cs` | 24 Fact + 7 Theory | Thorough |
| `PathResolver` | 127 | `PathResolverTests.cs` | 26 Fact + 1 Theory | Thorough |
| `RenderRules` | 121 | `RenderRulesTests.cs` | 31 Fact | Thorough |
| `VisibilityCondition` + `VisibilityEvaluator` | 284 | `VisibilityTests.cs` | 64 Fact | Good; see gap 2 |
| `UiThreadHost` | 150 | `UiThreadHostTests.cs` | 13 Fact | Thorough |
| `UiTools` | 159 | `UiToolsTests.cs` | 16 Fact | Thorough |
| `TreeRenderer` | 321 | `TreeRendererTests.cs` | 26 Fact + 3 Theory | Good; the `visible` gate has no test (gap 1) |
| `UiSurface` | 158 | `UiSurfaceTests.cs` | 12 Fact | Good; window paths need a desktop |
| *validator ↔ renderer seam* | — | `CatalogRendererSeamTests.cs` | 3 Fact + 3 Theory | Exhaustive over the catalog |
| `Program` | 67 | `ProgramMetadataTests.cs` | 2 Fact | Metadata only; the wiring runs in every end-to-end run |

`VisibilityTests.cs` also covers the `visible` key of the validator. The table counts `Fact` and
`Theory` attributes. A `Theory` runs once for each data row, so the executed count (290) is
higher than the attribute count (231).

`StaFixture.cs` (31 lines) is shared infrastructure, not a test: one STA thread, built from the
same `UiThreadHost` production uses.

## What is tested well

**Every rejection path is paired with a positive control**, and boundaries are tested on both
sides — 500/501 characters, 64/65 children, depth 11/13, 8/9 table columns. A rejection test that
never proves the accepting case still works is only half a test.

**The tests treat the missing-against-zero rule as a pair**, at every layer where it appears.
A real `0` must display as `"0"`. A missing value must never display as `"0"`. A surface that
has never rendered must report `null` for its node count, and never `0`.

**The most important tool test needs no desktop.** `UiToolsTests` uses a `SpySurface` to assert
the negative — that `Render` was *never called* when a tree is invalid. No screenshot can prove
that.

**The renderer's truncation caps are asserted as APPLIED, not merely declared.** `RenderRules`
tests the constants; `TreeRendererTests` supplies 70 `Repeat` items and 250 `Table` rows and
counts what actually came out. The two are different claims, and only the second can fail
without a warning.

**The validator↔renderer seam is covered exhaustively over the catalog**, not sampled.
`EveryCatalogComponentIsCovered` fails if a tenth component is ever added without a seam case —
without it, a "comprehensive" suite quietly stops being one.

**The visibility evaluator is tested as a rule set.** `VisibilityTests` pins that the number
`1` and the string `"1"` differ, and that an ordering condition is false when an operand is not
a number. An unresolved path is falsy in the same file.

### The mutation record

A green suite that has never been shown to fail is not evidence. The table lists the mutations
that were run on the catalog, the path resolver, the renderer and the surface. It holds none
for the visibility code. Every mutation below was reverted and the file verified
**byte-identical by SHA-256**.

| Mutation | Result |
|---|---|
| `ForbiddenPathTokens` emptied | exactly 5 failures — 4 prototype-path cases + the nested table-column case |
| Blind spot made to render `"0"` | exactly 2 failures |
| `e.Handled = false` in the supervisor | **the test host process crashed mid-run** |
| Gauge `maxWasRequested` reverted to `false` | exactly 1 — `AGaugeWhoseRequestedMaxDidNotResolveShowsAnEmptyBar` |
| Unknown tone reverted to Amber | exactly 1 — `AToneOutsideTheClosedSetIsMutedRatherThanAlarming` |
| `Repeat`'s `.Take(MaxRepeatItems)` removed | exactly 1 — `RepeatRendersAtMost64Items` |
| `Repeat` stops passing the item as scope | 2 — the targeted renderer test **and** the seam test, from different angles |
| `NodeCount` defaulted to `0` instead of `null` | exactly 1 — `ASurfaceThatHasNeverRenderedReportsNulls_NotZeros` |
| `Describe` returns a constant | exactly 1 — `ADifferentTreeStructureHashesDifferently` |

One mutation **survived**, and that result is worth a record. The mutation dropped the type
name from `Describe`, and changed `n.Type + "("` to `"" + "("`. It broke nothing, because the
two trees under comparison still differ by their child count. The mutation is weak, and the
test is not: the mutation never broke the property under assertion. A constant `Describe`
killed the test immediately. To tell those two cases apart is the whole discipline. A mutation
that survives is not automatically a finding.

## Two defects were found by writing these tests

Both were confirmed against `danielsimonjr/JSON-UI/render.js`, which every ported file names as the
source of truth: *"where the two disagree, the JS is right."*

1. **A `Gauge` whose `maxPath` did not resolve drew a bar against a default maximum of 100.** A
   value of 50 against an unreadable maximum showed as **half full**. That bar is a confident
   measurement against a scale that nobody supplied. The JS returns 0 for this case. The root
   cause is a lost difference. "No max requested" and "a max was requested and could not be
   resolved" both arrived at `GaugePercent` as a `null`. Only the caller could tell them apart.

2. **A tone outside the closed set rendered as Amber, the *attention* colour.** That result
   contradicted the method's own summary, which says that an unknown tone is muted. It also
   contradicted the JS (`TONE_CLASS[t] || 'stx-muted'`). The colour manufactures urgency from a
   value that the renderer simply failed to understand. A validated tree cannot reach the case,
   but `Render` is public. An unreachable branch that behaves wrongly is a trap for whoever
   makes it reachable.

Both are fixed. A mutation-proven test pins each fix. Note that the *absent* tone
case is pinned separately, so the muted fix cannot later be over-applied and repaint every
untoned panel grey.

## The gaps that remain

### 1. The renderer's `visible` gate has no test

`TreeRenderer.Render` evaluates `node.Visible` before the `switch`. No test outside
`VisibilityTests.cs` reads `Visible`, and `VisibilityTests.cs` tests the parser, the evaluator
and the validator, not the renderer. Removing the gate from `Render` breaks no test. Every test
that does not open a window still passes with the gate deleted, and no window test reads
`Visible`. The statement that the seam is covered exhaustively holds for the components, and
does not hold for `visible`.

### 2. Some visibility behaviours have no test

No test pins these behaviours, which [COMPONENTS.md](COMPONENTS.md) and [API.md](API.md) state.
Six belong to the evaluator and the parser:

- an empty `and` is true and an empty `or` is false;
- the first form key of an object wins;
- a `$item` path is unresolved;
- `[]`, `{}` and the string `"0"` are truthy;
- two unresolved operands are equal;
- an object or an array literal reduces to `null`.

Two belong to the surface:

- `treeHash` ignores the `visible` condition;
- `nodeCount` counts hidden nodes.

### 3. The window-showing tests need an interactive desktop

`UiSurfaceWindowTests` briefly opens and closes real windows. Its 8 tests need an interactive
desktop. On a host with no desktop they fail at `UiSurface.Open` — which is the **correct and
informative** failure, not something to paper over with a skip. See SPEC 10.1: the desktop
assumption is retired for the interactive path and explicitly *not* for S4U.

The split between `UiSurfaceTests` and `UiSurfaceWindowTests` is deliberate. `UiSurfaceTests`
needs no window and runs anywhere. The two groups fail for different reasons: one for logic,
the other for the environment. A suite that cannot tell those two apart teaches you to ignore
it.

### 4. `Program.cs` has metadata tests only

67 lines of composition root. `ProgramMetadataTests` pins `ServerVersion` against
`Directory.Build.props`, and pins that `ServerInstructions` names the four tools. Wire-level
`server/discover` is gated in CI against the shipped binary. Every end-to-end run of the shipped
binary covers the rest of the host wiring. The shipped binary is the only place where that wiring
is observable.

## Verification

Generated 2026-10-16 by `repo-tools map`.
Regenerate: `repo-tools map <repo> --out=<dir>` · Check: `repo-tools check <repo> --docs=docs/architecture`

**This file is hand-written.** `repo-tools map` writes its own `TEST_COVERAGE.md`. Run the map
into a staging folder and copy the other reports. A map run into `docs/architecture` overwrites
this file. A C# `using` names a namespace. The generated report therefore counts a test as
covering every file in each namespace that the test imports. This file does not use that report.
The committed `test-coverage.json` holds the data of that report. Its counts use the same
namespace rule, so do not read a coverage percentage from it.

| Claim | Value | Source |
|---|---|---|
| totalSourceFiles | 25 | dependency-graph.json |
| totalLinesOfCode | 4173 | dependency-graph.json |
| testOnlyFiles | 1 | dependency-graph.json |

**Claims the gate cannot hold:** the **290 tests / 0 failures** figure is `dotnet test` output.
The test count comes from the CI run on `main` and from a local `dotnet test --list-tests` run.
The failure count comes from the CI run. A count of the `Fact` and `Theory` attributes in each
test file gives the per-file numbers. Note that the attribute
count and the executed-test count differ, because a `Theory` runs one time for each `InlineData`
row or `MemberData` row. The 2,374 test lines and the per-component LOC come from
`file-inventory.json`'s per-file `loc`. Every mutation result in the table above was produced by
applying that mutation, running the suite, and restoring the file to a verified identical hash.
