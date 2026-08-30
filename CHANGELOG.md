# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **`visible` prop support on all catalog components.** Ported from `danielsimonjr/JSON-UI`
  (`packages/core/src/visibility.ts`, `packages/core/src/types.ts`). Every node in the tree
  now accepts an optional top-level `visible` property (a sibling of `type`, `props`, and
  `children`) that controls whether the component is rendered.
  - Accepted shapes: `true` / `false` (literal), `{"path":"..."}` (truthy path check),
    `{"and":[...]}`, `{"or":[...]}`, `{"not":{...}}`, `{"eq":[a,b]}`, `{"neq":[a,b]}`,
    `{"gt":[a,b]}`, `{"gte":[a,b]}`, `{"lt":[a,b]}`, `{"lte":[a,b]}`.
  - Absent `visible` means always-visible (the JSON-UI default).
  - An unresolvable path is treated as falsy (hidden), so a visibility condition that cannot be
    evaluated never silently shows content that should be hidden.
  - Auth-based conditions (`{"auth":"signedIn"}`) have no meaning in ui-mcp (no session model)
    and evaluate to hidden rather than visible, consistent with a safe-failure policy.
  - Visibility is evaluated against the ROOT data, not the current `$item` scope: conditions
    reference the same shared data the rest of the tree reads.
  - Hidden nodes collapse to `Visibility.Collapsed` in WPF — no space is consumed, so invisible
    siblings do not leave gaps in the layout.
  - New files: `VisibilityCondition.cs`, `VisibilityEvaluator.cs` in `UiMcp.Abstractions`.
  - `ValidatedNode` carries an optional `VisibilityCondition? Visible` (defaulted `null` so all
    existing call sites are unchanged).
  - **53 new tests** in `VisibilityTests.cs` covering Parse, IsVisible, and the CatalogValidator
    round-trip. Every rejection case is paired with a positive control.

### Changed

- **All source references updated from `AdminLTE/JSON-UI` to `danielsimonjr/JSON-UI`** (the
  GitHub repo where the catalog and renderer are published). Comments and test summaries in
  `.cs` files and documentation in `.md` files updated throughout.

### Fixed

- **Every per-file LOC figure in the architecture documents was wrong, and had been from the
  start.** `FILE_INVENTORY.md` states that its per-file numbers come from `file-inventory.json`'s
  `loc` field. Not one of the 21 matched it, and all were low, so the per-file tables never
  summed to their own project totals — 2,769 against a stated 3,279. The tables and the totals
  were two sources of truth for the same fact, presented identically, and the gate reads only
  the totals. Every figure is now taken from `file-inventory.json`, and the three per-project
  tables now sum to 3,375 exactly. `TEST_COVERAGE.md`'s per-component LOC column carried the
  same stale numbers and is corrected with them.
- **Stale counts after `64b7008` changed the renderer.** `totalLinesOfCode` 3,279 → **3,375**
  (the drift gate caught this one), and with it the prose the gate cannot read: **217 → 223
  tests**, `~6 s` → `~10 s`, test lines 1,840 → 1,899, `Program.cs` 46 → 54 lines, and the
  window-test share re-measured as ~8 s of the ~10 s run rather than ~5.6 s of ~6 s. Test count
  and duration verified by running `dotnet test`, not by inference.
- **Seven ambiguous references across four documents**, found when the STE checker gained a
  merged clause-anchored rule that sees mid-sentence demonstratives. Each is fixed by naming the
  referent. No measurement changed.

## [0.1.2] - 2026-08-15

### Fixed

- **A `Gauge` whose `maxPath` did not resolve drew a bar against a default maximum of 100.** A
  value of 50 against an unreadable maximum showed as **half full** — a confident measurement
  against a scale nobody supplied, which is the green-zero failure wearing a progress bar and
  precisely the class of defect this server exists to prevent.
  - Root cause: *"no maximum was asked for"* (default 100, correct) and *"a maximum **was** asked
    for and could not be resolved"* (there is no scale, and nothing honest to draw) both arrived at
    `RenderRules.GaugePercent` as a `null` max, and only the caller could tell them apart. It now
    takes `maxWasRequested`, defaulted to `false` so every existing two-argument call is unchanged.
  - Confirmed against `danielsimonjr/JSON-UI/render.js`, which every ported file names as the source of
    truth: an unresolvable `maxPath` there yields `undefined`, fails the
    `typeof max === 'number'` test, and the bar stays at 0.

- **A tone outside the closed set rendered as Amber — the *attention* colour.** It contradicted
  `Tone()`'s own summary (*"a tone the renderer does not know is muted"*) and the JS
  (`TONE_CLASS[t] || 'stx-muted'`), and it manufactures urgency out of a value the renderer had
  simply failed to understand. The two fallthrough cases are now distinct: **no tone supplied**
  keeps the default accent, a tone **outside the set** is muted. Unreachable from a validated tree
  — the catalog's `tone` is a closed enum — but `Render` is public and `ValidatedNode` is
  constructible in-process, so it is reachable, and an unreachable branch that behaves wrongly is
  a trap for whoever makes it reachable.

### Changed

- **`ComponentSpec` and `PropRule` are now `internal`.** Both describe how the catalog is *built*,
  and all 15 occurrences of either are inside `CatalogValidator.cs`; a consumer handed a
  `ComponentSpec` could do nothing with it, because `Validate()` is the only entry point and takes
  JSON. Surfaced by `unused-analysis.md`, which classified both as "referenced only within their
  own file" — correctly, and that is what a needlessly public type looks like from outside.

### Added

- **`TreeRendererTests` — 26 Fact + 3 Theory over the previously untested renderer** (268 lines,
  the largest source file). Pins specifically what reading cannot settle: that the nine-way switch
  reaches the right builder, that the 64/200 truncation caps are **applied** rather than merely
  declared as constants, that `$item` scope actually reaches a `Repeat`'s children and a `Table`'s
  cells, and that a missing value is *visually* distinct and not only textually.
- **`CatalogRendererSeamTests` — the validator↔renderer seam**, exhaustive over the catalog rather
  than sampled. Both defects ever found by running this system lived in a seam, and a suite
  organised per component tests each side of every seam and none of the seams themselves.
  `EveryCatalogComponentIsCovered` fails if a tenth component is added without a case, so the
  suite cannot quietly stop being comprehensive.
- **`UiSurfaceTests` — 12 Fact over the previously untested WPF surface**, split into a group
  needing no window (runs anywhere) and `UiSurfaceWindowTests` which briefly shows real ones. The
  two fail for different reasons — logic versus environment — and a suite that cannot tell those
  apart teaches you to ignore it.
- **`StaFixture`** — a shared STA thread built from the same `UiThreadHost` production uses, not a
  bespoke test thread. WPF objects have thread affinity, so every query runs *inside* the STA call
  and returns plain values; the first version of the renderer suite ignored that and failed all 34
  tests for one reason that looked like 34.

**132 → 217 tests.** Every new guard is mutation-proven, each mutation reverted to a
SHA-256-identical file: reverting the gauge fix, the tone fix, the `Repeat` cap, `$item` scope
propagation, `NodeCount`'s null, and the structural hash each killed exactly the intended test —
and `$item` scope killed two, from different angles.

## [Unreleased]

### Fixed

- **Table column paths are now ROW-relative by default.** A column path was resolved against the
  data ROOT unless it began with `$item`, so the natural path `"name"` looked for a top-level `name`,
  found nothing, and rendered UNKNOWN. Found on the HTML console that shares these semantics: every
  row of every table read UNKNOWN **while the row counts were correct** — `fromPath` resolved and the
  column paths did not, which is the diagnostic combination.
  The default was the defect, not the views: of the ten column paths written for that console, **nine
  were bare and one carried the prefix**. When the author reaches for the "wrong" form nine times in
  ten, the surprising form is what needs changing. An explicit `$item.` prefix still works and means
  the same thing. Scoped to Table columns only — inside a `Repeat`, a bare path resolving against the
  root is meaningful and is untouched. **223 tests**, including an end-to-end case proving a bare path
  reaches the row (asserting only the string rewrite would pass even if the resolver ignored it) and
  a regression witness pinning the old root-resolving behaviour.

- **`ui_open` now activates the window on CREATION, not only on the idempotent re-open.** Reported as
  "no window is displayed" while `ui_status` correctly said `windowAlive: true` — the window existed
  with a real HWND the whole time, sitting behind the terminal. `Show()` alone places a window
  wherever the z-order puts it, which for a background-spawned process is underneath whatever has
  focus. The status was honest; the FIRST open was the one least likely to be seen, which is
  backwards. `Activate()` is best-effort by design — Windows refuses foreground to a process that
  does not own it — so the tool description now states that `topmost:true` is the only guarantee
  rather than leaving callers to discover it.

### Known

- **The running server locks its own binary, so `dotnet publish` fails while the plugin is loaded.**
  `.mcp.json` points at `${CLAUDE_PLUGIN_ROOT}/bundle/UiMcp.exe`; the marketplace junction resolves
  that to this working tree, so the live process holds the build output open and publish dies with
  `IOException: being used by another process`. Rebuilding requires stopping the `UiMcp.exe`
  processes first (match on **ExecutablePath**, never a name sweep), then publishing, then
  `/reload-plugins`. The Node-based plugins do not hit this because they ship source, not a locked exe.

- **Verify a rebuild by HASH, never by file size.** The single-file bundle is 29,799,584 bytes at
  0.1.0, 0.1.1, 0.1.2 **and** 0.1.3 — four builds, four distinct SHA256 values, one identical size.
  The payload dominates and version strings are the same length, so size never moves. A size check
  written here produced a false positive (concluding three earlier versions had shipped identical
  binaries — they had not) and then a false negative (reporting no change on a rebuild that did
  happen). Hash both sides or the check is theatre.

### Added

- **Architecture documentation — all ten canonical documents** in `docs/architecture/`
  (OVERVIEW, ARCHITECTURE, COMPONENTS, DATAFLOW, API, FILE_INVENTORY, TEST_COVERAGE,
  DEPENDENCY_GRAPH, unused-analysis, duplicate-symbols), plus a Documentation index in the
  README. Every numeric claim carries a `## Verification` block derived from a real parse of
  the source, and `repo_map.py check` exits non-zero when the code moves away from them.
  - **The gate was mutation-proven, not trusted.** Changing `totalLinesOfCode` from 2376 to
    9999 in OVERVIEW.md made `check` exit 1 naming both the file and the claim; the file was
    then restored byte-identical (SHA-256 verified). A gate that has never been shown to fail
    is not evidence.
  - Claims a graph metric cannot hold — the four tools, the nine components, the 132-test
    figure — are written with their actual basis stated, so a reader can tell a gate-enforced
    number from a hand-verified one.
  - This was **unblocked by teaching `repo_map` to parse C#** (`skills` `7264f55`, `45c27ec`).
    Before that, every .NET repo scanned to an empty graph and could not be documented against
    a drift gate at all.

### Fixed

- **The README declared the project unimplemented.** Its status banner still read
  *"specification only. No implementation yet."* on a repository that is released at v0.1.1,
  installed as a plugin, and serving four tools — the most misleading single line in the repo.

### Notes — real gaps surfaced by writing the documentation

Recorded in `todo.md`, not fixed here, because documenting is not the same act as changing:

- **`TreeRenderer` has no unit tests** — 248 lines, the largest source file. Its truncation
  caps (64 `Repeat` items / 200 `Table` rows) are tested as *constants* while nothing asserts
  the renderer applies them, and `$item` scope propagation is likewise unverified. Both defects
  ever found by running this system lived in exactly this gap.
- **`dotnet test` does not run in CI** — the 132 tests are a local habit, not a gate.
- **`ComponentSpec` and `PropRule` are `public`** while every use is confined to
  `CatalogValidator.cs`.

## [0.1.1] - 2026-08-15

### Fixed

- **`ui_render` refused an object-shaped `tree`, and could not say why.** `tree` and `data` were
  declared `string`, so a caller passing an actual JSON **object** — the natural reading of the
  tool's own description, *"The UI tree as JSON"* — failed inside the MCP SDK's parameter binding
  **before the method ran**. Every refusal path in `Render` was therefore unreachable: the caller
  got only `"An error occurred invoking 'ui_render'."`, while the real cause
  (`System.Text.Json: The JSON value could not be converted to System.String`) went to stderr,
  where an agent calling the tool cannot read it. Refusing *with a reason* is this server's whole
  posture, and this was the one path that could not.
  - Both parameters are now `JsonElement` and accept either shape: the payload itself, or a JSON
    string containing it. SPEC 4 calls `tree` "catalog JSON" and never required a stringified
    form, so this is the contract rather than a loosening of it.
  - **Found by driving the DEPLOYED plugin over stdio, not by any unit test** — the same way the
    `$item` and `treeHash` defects were found. 129 → 132 tests; both shapes are pinned, because
    either alone would let the other regress.
  - **Proven against the artifact, and the proof discriminates:** the identical object-shaped
    `ui_render` call returns `isError: true` on the previous binary and `isError: false` on the
    rebuilt one.

- **The plugin cache is keyed on VERSION, so a fix at an unchanged version never deploys.**
  After rebuilding with the `ui_render` fix, `claude plugin install ui-mcp@local-marketplace`
  answered *"already installed"* and did nothing — the cache kept the previous binary and the
  object-shaped call still failed there. `install` asks whether the plugin is installed, not which
  version. `claude plugin update ui-mcp@local-marketplace` is the command that re-fetches, and it
  only has something to fetch once the version moves. Hence 0.1.1: `Directory.Build.props` (the
  single version source), `.claude-plugin/plugin.json`, and the marketplace entry.
  Verified on the deployed copy: `serverInfo.version` is `0.1.1` and the object-shaped render
  returns `isError: false`.

- **The publish recipe was undocumented, and the obvious command produces the wrong artifact.**
  README said `dotnet publish src/UiMcp -c Release -o bundle`. That yields a 156 KB launcher plus
  **45 loose DLLs** in `bundle/`, not the single 29,799,584-byte executable `.mcp.json` points at
  — found by running it and having to clean up after. The full recipe
  (`-r win-x64 --self-contained false -p:PublishSingleFile=true`) is now in the README, reproduces
  the committed artifact byte-for-byte in size, and says why each flag is load-bearing.

### Added

- **Installed as a plugin, and the deployed artifact is verified serving.** Registered in the
  local marketplace as `./ui-mcp` — a symlink to this repo, matching the `episodic-memory`
  precedent, because this repo has no git remote and a `url` source would have nothing to fetch.
  - **The last two steps did not need the user-only slash commands.** `todo.md` recorded them as
    `/plugin marketplace update` + `/reload-plugins`, both user-invoked. The equivalent CLI exists
    and is not gated: `claude plugin marketplace update local-marketplace` followed by
    `claude plugin install ui-mcp@local-marketplace`. The update step also *validates* the
    manifest, which is the "re-run the consumer that parses it" check that note asks for.
  - **Verified against the DEPLOYED exe, not the dev build.** The binary in
    `~/.claude/plugins/cache/local-marketplace/ui-mcp/0.1.0/bundle/UiMcp.exe` (29,799,584 bytes)
    answered a real MCP `initialize` with `{"name":"ui-mcp","version":"0.1.0"}` and advertised
    `ui_open`, `ui_render`, `ui_status`, `ui_close`.
  - **Still not serving in a live session, and that distinction is the point.** The tools are
    absent from the current session: the plugin was registered at 13:43 while the last reload ran
    at ~11:25. Binding needs `/reload-plugins`, which genuinely is user-only. Installed is not
    serving.
  - README gained Build and Install sections; the Build heading no longer says "once implemented".

- **Plugin packaging** — `bundle/UiMcp.exe` (28.42 MB, single file), `.claude-plugin/plugin.json`,
  `.mcp.json` with `${CLAUDE_PLUGIN_ROOT}`.
  - **Deployment model decided by measurement, not preference** (SPEC 10.2). Both were built:
    self-contained **153.7 MB** vs framework-dependent **28.42 MB**. Chose framework-dependent
    because `WindowsDesktop.App 9.0.19` is installed on **both** the ZBOOK and the EVO (verified via
    `dotnet --list-runtimes` on each), and the bundle is committed and cache-cloned per version, so
    125 MB of avoidable payload would be paid on every version on every machine forever. The cost is
    stated in the spec rather than buried: a machine without the runtime cannot run this server.
  - **Tested the artifact that SHIPS.** `bundle/UiMcp.exe` answered a real MCP `initialize` with
    `ui-mcp v0.1.0`. A dev build passing proves nothing about the published one — different runtime,
    different failure modes.
  - **Caught a trap that would have shipped a silently useless plugin.** `~/.gitignore_global`
    line 3 ignores `.mcp.json` **everywhere**. A normal commit would have omitted it, the plugin
    would have installed and served **nothing**, and the symptom would have looked like a broken
    server rather than a missing file. Fixed structurally with a `!.mcp.json` negation in the repo
    `.gitignore` (repo rules take precedence over `core.excludesFile`) rather than with
    `git add -f`, which works once and rots the moment anyone commits normally.

- **The renderer** — `RenderRules` (Abstractions) + `TreeRenderer` (WPF), all nine components.
  **129 tests, 0 warnings.** Rendered live: `examples/starship-view.json` bound to real
  `briefing.ps1 -Json` output, 19 nodes, window confirmed by an independent process.
  - **Judgement and drawing are separated on purpose.** Unit suppression on a missing value, delta
    only when numeric, gauge clamping, empty text, the 64/200 caps — all in `RenderRules`, testable
    with no window. `TreeRenderer` is assembly only, thin enough to verify by reading.
  - `MetricText` omits the unit when the value is missing: *"UNKNOWN live"* implies a measured
    quantity in some unit when there is no quantity at all.
  - `DeltaText` returns **null** for a missing or non-numeric delta rather than `0` — "unchanged"
    and "not measured" are different claims.
  - A missing gauge reads 0% because a bar must have some length, so the **label** carries UNKNOWN.
    A zero-length bar with no UNKNOWN beside it is the green-zero failure again.
  - Security posture carried over intact: element types come from the renderer only, text reaches
    the UI via `TextBlock.Text` (inert — WPF parses no markup from it, the structural equivalent of
    "textContent, never innerHTML"), colours come from the closed tone enum, lookups go through the
    guarded resolver.

### Fixed

- **`$item` scope paths were refused by the validator while the resolver implemented them.**
  `PropTypes.Path`'s charset excluded `$`; `PathResolver` documented and implemented a `$item`
  prefix. Each component was correct in isolation and no unit test covered the seam between them —
  it surfaced only on the first real end-to-end render. Fixed by accepting `$item` as a **literal
  prefix**, not by adding `$` to the charset, which would have legalised every other use of it in
  one character of diff. `$other`, `$`, `a$b` and `$items.x` stay refused, and `$item.__proto__`
  still hits the prototype guard because that check runs against the whole string first.
  **The JS original carries the identical bug:** `danielsimonjr/JSON-UI/catalog.js` refuses `$` while
  `render.js` implements `$item`, and `view.json:332` uses `"valuePath": "$item"` — so the HTML
  console refuses its own view tree. Tracked separately; not fixed here.

- **`ui_render` and `ui_status` reported different `treeHash` values for the same render**
  (`7d4ef2048c4b` vs `febd32fc836e`): the tool hashed the raw JSON text, the surface hashed the
  structure. One field name, two functions, two answers — the second-source-of-truth defect.
  `ui_render` now reads the hash **back from the surface**, so they cannot disagree. Deleting the
  duplicate rather than syncing it, because syncing re-arms the drift. Verified equal live.

### Added

- **The four MCP tools** — `ui_open`, `ui_render`, `ui_status`, `ui_close` — plus `IUiSurface` and
  the WPF `UiSurface`. **98 tests, all passing, 0 warnings on a full rebuild.**
  - **`ui_render` validates the WHOLE tree before anything touches the window.** A partially
    rendered invalid tree is worse than none: it looks like a working display while silently
    omitting what failed. Tested with a partially-invalid tree that must render **nothing at all**.
  - **`IUiSurface` exists so the most important test is a negative that needs no desktop.** A spy
    can assert "Render was never called"; a screenshot cannot. Covers unknown component, unknown
    prop, malformed tree JSON, and malformed data JSON — all refusals, never thrown faults.
  - **`ui_status` reports UNKNOWN for anything unmeasured**, never a fabricated zero, and surfaces
    an absorbed UI fault rather than hiding it. It reads the LIVE window state instead of a cached
    flag, because the user can close the window at any moment and a status reporting our last
    *intention* would be confidently wrong.
  - **Verified over the wire, not inferred from the attribute compiling:** `tools/list` returns all
    four with exactly the spec names and `ui_render`'s schema exposes `tree` + `data`. An end-to-end
    drive over stdio produced a real window an **independent process** observed
    (`MainWindowTitle: "ui-mcp e2e"`), with `ui_status` reporting `windowAlive: true, nodeCount: 3,
    treeHash: 105e2c486c52, lastFault: none`.
  - `UiSurface` starts the UI thread **lazily on first open**, so a host with no desktop fails where
    a window was actually requested and can say so, rather than at launch.
  - **Known and labelled:** `UiSurface.Render` currently draws a placeholder that says
    "renderer pending" in the window itself. The catalog visual tree is the next task; the
    placeholder announces what it is rather than implying a finished renderer.

- **Fixed 7 `xUnit1031` warnings** (blocking task operations in tests) by making the host tests
  async throughout. Not cosmetic: that suite exists to prove a marshalling boundary does not
  deadlock, so a test that blocks a thread waiting on that boundary is the one place the shortcut
  could manufacture the failure it claims to rule out. **These had been reported as "0 warnings"
  earlier — incorrectly.** Incremental builds were not recompiling the test project;
  `--no-incremental` surfaced them.

- **`UiMcp.Hosting.UiThreadHost`** — the single STA thread WPF requires, and the only route from a
  tool handler to the UI. **87 tests, all passing, 0 warnings.** SPEC section 3's "two threads, one
  direction": handlers marshal and get a Task; the UI thread never waits on MCP.
  - Verified: STA apartment, all work on one UI thread, **50 concurrent marshals with no deadlock**
    (SPEC section 7 names this case), fail-fast after shutdown instead of queueing onto a dead pump,
    idempotent shutdown, refused double-start.
  - **The supervisor almost passed for the wrong reason.** The first fault tests were satisfied by a
    framework guarantee — `Dispatcher.InvokeAsync` captures exceptions into the returned Task, so
    awaited work was never at risk. The case the spec means is a **window event handler throwing
    with nobody awaiting it**, which reaches `Dispatcher.UnhandledException` and terminates the
    process. Added `Post()` to model that path and a handler that marks it handled.
  - **Mutation-proven, starkly:** with `e.Handled = false` the **test host process crashed** mid-run
    (`Unhandled exception ... window handler blew up`) and the run aborted. Restored byte-identical.
    That is precisely the failure the supervisor exists to prevent: a dead window is a degraded
    display, a dead process is an outage, and they must not be the same event.
  - `LastFault` records what was absorbed so `ui_status` can report a degraded display honestly.
    A supervisor that hides the fault it caught is a silent failure with extra steps.
  - The UI thread is a **background** thread: a foreground one would keep the process alive after
    MCP shutdown, turning a clean exit into a hang that looks like an ignored SIGTERM.

- **Path resolver and display formatter** (`UiMcp.Abstractions.PathResolver`), ported from
  `danielsimonjr/JSON-UI/render.js`. Nested keys, array indices, consecutive indices (`grid[1][0]`),
  `$item` scope, prototype refusal. **74 tests total, all passing, 0 warnings.**
  - **An unresolvable path returns missing and formats as `UNKNOWN` — never `0`, never `""`.**
    SPEC section 6 names this the most expensive recurring failure on this machine: a section that
    could not be measured, shown as a green zero, reads as health. The tests assert it as a **pair**
    (a real `0` displays as `"0"`; a missing value never displays as `"0"`) because either half
    alone is satisfiable by a broken implementation.
  - **Mutation-proven.** Making a blind spot render as `"0"` failed exactly 2 tests; the file was
    then restored byte-identical (SHA256). The prediction was 3 — the third case resolves to a real
    `JsonElement` of kind `Null` and takes a different branch than the mutated one, so 2 is the
    correct number and the count corroborated the reading of the code rather than contradicting it.
  - **Never throws.** A missing value is a display problem, not a crash; one bad binding in a
    sixty-node tree degrades to `UNKNOWN` in place instead of taking the console down.
  - JSON `null` also formats as `UNKNOWN`: a key present but null carries no more information than
    an absent key, and treating them differently puts a confident-looking blank on a status board.
  - Numbers format with `"R"` under `InvariantCulture` — a comma decimal separator on a
    differently-configured machine would silently change every number on the display.
  - The prototype guard is repeated here even though the catalog already refuses such paths: the
    resolver is reachable from the renderer's own bindings, and a guard present at only one of two
    entry points is a door left open.

- **Catalog and validator** (`UiMcp.Abstractions`): all nine components ported from
  `danielsimonjr/JSON-UI/catalog.js` — `StatusBanner` · `Panel` · `Row` · `Metric` · `Field` · `Gauge` ·
  `Repeat` · `Table` · `Note`. Enforces the SPEC section 6 invariants: unknown component and unknown
  prop are **refused, not ignored**; tone is a closed set; paths are charset-restricted and refuse
  `__proto__` / `constructor` / `prototype`; depth capped at 12, children at 64, table columns at 8.
  **44 tests, 44 passing, 0 warnings.**
  - **Every rejection test is paired with a positive control**, and boundaries are tested on both
    sides (500 vs 501 chars, 64 vs 65 children, depth 11 vs 13, 8 vs 9 columns). A validator that
    refuses everything passes a naive rejection suite perfectly.
  - **The suite was proven able to fail, not merely observed passing.** Emptying
    `ForbiddenPathTokens` broke exactly 5 tests — the 4 prototype-path cases plus the nested
    table-column case — and the file was then restored byte-identical (SHA256 verified). A green
    suite that has never been shown to go red is not evidence that it checks anything.
  - Two deliberate hardenings over the JS original: the **prototype check runs before the charset
    check** (`__proto__` is charset-legal, so ordering the charset rule first would let a future
    regex edit quietly reopen the hole), and **`ValidatedNode` is a distinct type from raw JSON**, so
    "has this been validated?" is answered by the compiler rather than by reading call sites.
  - Column paths are validated through the same `Path` rule as top-level paths, so the prototype
    guard reaches *inside* the array — nested paths are exactly where a boundary check gets skipped.
  - `UiValidationException` is its own type so the MCP layer can report a deliberate refusal
    verbatim instead of letting the SDK flatten it to "An error occurred invoking '&lt;tool&gt;'",
    which would hide the guard at the moment it did its job.

- **C# solution scaffold**, mirroring `Windows-mcp`: `src/UiMcp` (net9.0-windows10.0.19041.0,
  `UseWPF`, Exe), `src/UiMcp.Abstractions`, `tests/UiMcp.Tests` (xunit + FluentAssertions + Moq),
  `global.json`, `Directory.Build.props`, `UiMcp.sln`. Builds **0 warnings, 0 errors**.
  - `UiMcp.Abstractions` targets plain **`net9.0`**, not `net9.0-windows`. The spec requires it not
    to reference WPF; a non-Windows TFM makes that a **compiler guarantee** rather than a convention
    someone must remember. A stray `using System.Windows;` now fails the build.
  - **Version has exactly one source.** `Directory.Build.props` holds it; `Program.ServerVersion`
    reads it back off the assembly. Verified live, not assumed - the server reported `0.1.0` over
    the wire. Windows-mcp shipped a hardcoded `"0.4.1"` through three releases, and a server that
    misreports its version makes a stale-bundle deploy invisible.
  - Logging is pinned to **stderr**. Stdout is the MCP transport; a stray log line there corrupts
    the protocol and surfaces as an opaque client-side parse error.
  - **Proven to SERVE, not merely to compile.** The built exe answered a real MCP `initialize`:
    `{name: ui-mcp, version: 0.1.0, protocol: 2024-11-05, capabilities: present}`. Tool set is
    deliberately empty at this stage.

- **`tools/probe-desktop.ps1`** - retires the SPEC section 10 desktop risk by measurement.
  Creates a real WPF window, resolves its native `HWND`, and asks the OS `IsWindowVisible`.
  Ships with a mandatory control run, because a probe that cannot produce a positive proves
  nothing when it reports a negative. It earned that design on its first execution: a raw
  `System.Threading.Thread` running a PowerShell ScriptBlock throws *"no Runspace available to run
  scripts in this thread"*, which is **indistinguishable from "no desktop"**. The control caught it;
  the fix is an STA `Runspace`, which carries both the apartment and an execution context.

### Changed

- **SPEC section 10 risk 1 is now PARTLY RETIRED, with the split recorded rather than summarised**
  (new section 10.1). Retired for the production path: host started by `ResumeStarship`
  (`LogonType: Interactive`); window created inside `WindowsMcp`'s process tree, `HWND 1770336`,
  OS-confirmed visible, control from a `claude.exe` child gave a different handle (`3868598`), all
  59 live MCP processes in session 1, and a human confirmed seeing it. **Still open for S4U** - the
  case the risk actually named. `InteractiveToken` requires a logged-on session by definition, so a
  desktop is expected there; S4U runs with nobody logged on and is the case that plausibly has none.
  No task starting `claude.exe` uses S4U today, so it does not currently arise.
  **Residual:** the probe proves a *descendant* of an MCP server can draw; ui-mcp will draw
  in-process. Children inherit session, window station and desktop, so the gap is small - but it
  does not close entirely until `ui_open` shows a real window.

- **Specification** (`docs/SPEC.md`) for an MCP server that hosts a native WPF window and renders
  catalog-constrained JSON UI trees. Covers architecture, the five-tool contract, the nine-component
  catalog, seven safety invariants, testing strategy, a v0.1 definition of done, and four named
  risks with mitigations.
- **README** describing purpose, the threading rationale for C# over PowerShell, the safety model,
  and the relationship to `Windows-mcp`, `json-render`, and the existing HTML/PowerShell consoles.
- **`todo.md`** with the ordered build sequence.

### Notes

- No implementation yet. This release is specification only.
- The design is not greenfield: a working JS renderer (`danielsimonjr/JSON-UI/`) and a working WPF
  renderer (`~/.claude/scripts/starship-console.ps1`) already exist and are proven against live
  data from two machines. The C# port is transcription, not design. Where the spec and that code
  disagree, **the code is right and the spec is stale**.
- The desktop-availability risk (can a stdio MCP server create a visible window?) is scheduled to
  be retired **before** the UI is built. It is the cheapest risk to test and the most expensive to
  discover late.
