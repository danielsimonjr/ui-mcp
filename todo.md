# ui-mcp — todo

Format: `- [ ] (YYYY-MM-DD) task`. 🟢 READY means unblocked and next.

## Now

- [x] (2026-08-14) Specification written — `docs/SPEC.md`. Architecture, tool contract, catalog,
      safety invariants, testing strategy, v0.1 definition of done, risks.

## Done 2026-08-15

- [x] (2026-08-15) **Desktop assumption — PARTLY retired, and the split matters.** Taken OUT OF
      ORDER, ahead of the scaffold: its own title said EARLY, and a failure would have changed the
      hosting model and therefore the scaffold. Probe: `tools/probe-desktop.ps1`.
      **RETIRED for the production path** (host started by `ResumeStarship`, `LogonType:
      Interactive`): a real WPF window created inside `WindowsMcp`'s process tree returned
      `HWND 1770336` with the OS reporting `IsWindowVisible=True`; control from a `claude.exe` child
      gave a different handle (`3868598`), so neither is a cached result; all 59 live MCP processes
      are in session 1; and a human confirmed seeing the window. Three independent methods.
      **NOT retired for S4U** — the case the risk originally named. `ResumeStarship` is
      `InteractiveToken`, which requires a logged-on session by definition, so a desktop is expected
      there. No task that starts `claude.exe` uses S4U today, so it does not arise; if the host ever
      moves to an S4U trigger, re-run the probe first. See SPEC 10.1.
      **Residual:** the probe proves a *descendant* of an MCP server can draw. ui-mcp will draw
      *in-process*. Children inherit session/window-station/desktop so the gap is small, but it does
      not fully close until `ui_open` puts a real window up.

- [x] (2026-08-15) **Scaffold the solution.** `src/UiMcp` (net9.0-windows10.0.19041.0, `UseWPF`,
      Exe), `src/UiMcp.Abstractions`, `tests/UiMcp.Tests` (xunit + FluentAssertions + Moq),
      `global.json` pinned to 9.0.314 `rollForward: latestFeature` (resolves 9.0.317 here — verified,
      not assumed), `Directory.Build.props` as the single version source, `UiMcp.sln`.
      **Abstractions targets plain `net9.0`, NOT `net9.0-windows`** — the spec says it must not
      reference WPF, and a non-Windows TFM makes that a compiler guarantee instead of a convention.
      `dotnet build`: **0 warnings, 0 errors**. Proven to SERVE, not merely compile: the built exe
      answered a real MCP `initialize` with `{name: ui-mcp, version: 0.1.0, protocol: 2024-11-05}`,
      and that version arrived via Directory.Build.props -> assembly -> `ServerVersion`, so the
      no-hardcoded-version wiring is verified too.

## 🟢 READY — next session, in order
- [x] (2026-08-15) **Catalog + validator, TDD.** All nine components ported from
      `AdminLTE/JSON-UI/catalog.js`. **44 tests, 44 pass, 0 warnings.** Every rejection path is
      paired with a positive control, and boundaries are tested on both sides (500/501 chars,
      64/65 children, depth 11/13, 8/9 table columns).
      **Proven to have teeth by mutation, not by passing:** with `ForbiddenPathTokens` emptied the
      suite failed exactly 5 tests — the 4 prototype-path cases plus the nested table-column case —
      then the file was restored byte-identical (SHA256 verified). A green suite that has never been
      shown to fail is not evidence.
      Two deliberate hardenings over the JS: the prototype check runs **before** the charset check
      (`__proto__` is charset-legal, so a future regex edit could otherwise let it through), and
      `ValidatedNode` is a distinct type from raw JSON so "was this validated?" is a compiler
      question, not a call-site reading exercise.
- [x] (2026-08-15) **Path resolver, TDD.** Ported from `render.js`. Nested keys, array indices,
      consecutive indices (`grid[1][0]`), `$item` scope, prototype refusal. **74 tests total, all
      passing.** Never throws - one bad binding degrades to UNKNOWN in place rather than taking the
      render down.
      **The missing-vs-zero invariant is tested as a PAIR**, because each half alone is worthless:
      a real `0` must display as `"0"`, and a missing value must never display as `"0"`.
      **Mutation-proven:** making a blind spot render as `"0"` failed exactly 2 tests, then restored
      byte-identical (SHA256). I predicted 3 and got 2 — the third (`JsonNull_DisplaysAsUnknown`)
      resolves to a real `JsonElement` of kind `Null` and takes a different switch arm than the
      `v is null` branch I mutated, so 2 is correct. The count corroborated the reading of the code.
      Extra over the JS: the prototype guard is repeated in the resolver even though the catalog
      already refuses those paths - the resolver is reachable from the renderer's own bindings, and
      a guard present at only one of two entry points is a door left open.
- [ ] (2026-08-15) **Re-check the desktop assumption IF the host ever moves to an S4U trigger.**
      Not currently reachable - every task that starts `claude.exe` is `InteractiveToken` today.
      `tools/probe-desktop.ps1` answers it in one run. See SPEC 10.1.
- [x] (2026-08-15) **WPF host on an STA thread** (`UiMcp.Hosting.UiThreadHost`) with `Dispatcher`
      marshalling and a real supervisor. **87 tests, all passing.** Covers: STA apartment, single UI
      thread for all work, 50 concurrent marshals without deadlock (SPEC 7 names this), fail-fast
      after shutdown rather than queueing onto a dead pump, idempotent shutdown, refused double-start.
      **The supervisor test nearly passed for the wrong reason, and finding that was the point.**
      The first two fault tests passed on a FRAMEWORK guarantee - `Dispatcher.InvokeAsync` captures
      exceptions into the returned Task, so awaited work was never going to kill anything. The case
      the spec actually means is a window event handler throwing with nobody awaiting it: that
      reaches `Dispatcher.UnhandledException` and terminates the PROCESS. Added `Post()` to model
      that path, plus a `UnhandledException` handler that marks it handled and records `LastFault`.
      **Mutation-proven, and unusually starkly:** with `e.Handled = false` the TEST HOST PROCESS
      CRASHED mid-run (`Unhandled exception ... window handler blew up`). Restored byte-identical.
      The fault is recorded rather than swallowed so `ui_status` can report a degraded display -
      a supervisor that hides what it absorbed is a silent failure with extra steps.
- [x] (2026-08-15) **Tools:** `ui_open`, `ui_render`, `ui_status`, `ui_close`. **98 tests, all
      passing, 0 warnings on a FULL rebuild.**
      **Verified over the wire, not assumed from the attribute compiling:** `tools/list` returns all
      four with exactly the spec names, and `ui_render`'s schema exposes `tree` + `data`.
      **End-to-end proven:** `ui_open` -> `ui_render` -> `ui_status` driven over stdio produced a
      real window that an INDEPENDENT process observed (`MainWindowTitle: "ui-mcp e2e"`), with
      `ui_status` reporting `windowAlive: true, nodeCount: 3, treeHash: 105e2c486c52, lastFault: none`.
      The most important test is a NEGATIVE and needs no desktop: an invalid tree must be refused
      BEFORE anything touches the window. `IUiSurface` exists so a spy can assert "Render was never
      called", which no screenshot can. Covered: unknown component, unknown prop, malformed tree
      JSON, malformed data JSON, and a partially-invalid tree that must render NOTHING.
      Caveat carried forward: `UiSurface.Render` currently draws a labelled PLACEHOLDER, not the
      catalog visual tree. That is the next item and the placeholder says so in the window itself.
- [x] (2026-08-15) **Renderer** for the nine components. **129 tests, 0 warnings.**
      Split deliberately: every JUDGEMENT lives in `RenderRules` (Abstractions, no WPF, unit-tested
      without a window) - unit suppression on a missing value, delta only when numeric, gauge
      clamping, empty text, the 64/200 caps. `TreeRenderer` is assembly only, thin enough to check
      by reading. Security posture carried over intact: element types come from the renderer only,
      text reaches the UI through `TextBlock.Text` (inert - WPF parses no markup from it), colours
      come from the closed tone enum, lookups go through the guarded resolver.
      **PROVEN AGAINST LIVE DATA:** `examples/starship-view.json` rendered with real
      `briefing.ps1 -Json` output over MCP - 19 nodes, window observed by an independent process.
      **TWO REAL DEFECTS FOUND BY RUNNING IT, neither visible to any unit test:**
      (1) `$item` scope paths were REFUSED by the validator while the resolver implemented them.
          Each component was correct alone; nobody tested the seam. Fixed by accepting `$item` as a
          literal prefix - NOT by adding `$` to the charset, which would have legalised every other
          use of it. **The JS original still has this bug** (see the C:\ tracker entry).
      (2) `ui_render` and `ui_status` reported DIFFERENT `treeHash` values for one render
          (`7d4ef2048c4b` vs `febd32fc836e`) - the tool hashed raw JSON, the surface hashed
          structure. Same name, two functions. Fixed by deleting the duplicate and reading the
          value back from the surface, so they cannot disagree. Now verified equal live.
- [x] (2026-08-15) **Publish + wire — repo side DONE.** `bundle/UiMcp.exe` (28.42 MB, one file),
      `.claude-plugin/plugin.json`, `.mcp.json` with `${CLAUDE_PLUGIN_ROOT}`.
      **Deployment model decided by measurement** (SPEC 10.2): framework-dependent 28.42 MB over
      self-contained 153.7 MB, because `WindowsDesktop.App 9.0.19` is present on BOTH machines and
      the bundle is committed and cache-cloned per version.
      **Tested the artifact that SHIPS, not the dev build:** `bundle/UiMcp.exe` answered a real MCP
      `initialize` with `ui-mcp v0.1.0`.
      **Trap caught before it bit:** `~/.gitignore_global` line 3 ignores `.mcp.json` everywhere, so
      a normal commit would have shipped a plugin that installs and serves NOTHING - and the failure
      would have looked like a broken server, not a missing file. Fixed structurally with a `!` rule
      in the repo `.gitignore` (which takes precedence over `core.excludesFile`) rather than with
      `git add -f`, because a force-add rots the moment anyone commits normally.

- [x] (2026-08-15) **Marketplace registration + install — DONE, and steps 3-4 did NOT need the
      slash commands after all.** Registered as `./ui-mcp` (a symlink to this repo, matching the
      `episodic-memory` precedent) and enabled in settings — both already in place at 13:43.
      **The `/plugin ... update` + install steps were done from the CLI instead**, which is not
      user-only: `claude plugin marketplace update local-marketplace` (which also *validated* the
      manifest — the `owner`-field check this note warns about) then
      `claude plugin install ui-mcp@local-marketplace`. Cache now holds
      `.../local-marketplace/ui-mcp/0.1.0/` including `bundle/UiMcp.exe` (29,799,584 bytes).
      **Verified against the DEPLOYED artifact, not the dev build:** the exe in the plugin cache
      answered a real MCP `initialize` with `{"name":"ui-mcp","version":"0.1.0"}` and advertised
      all four tools — `ui_open`, `ui_render`, `ui_status`, `ui_close`.

- [x] (2026-08-15) **`ui_render` accepts an object-shaped tree; publish recipe documented.**
      Found by driving the DEPLOYED plugin, not by a unit test: `tree`/`data` were `string`, so a
      caller sending a JSON OBJECT failed in SDK parameter binding BEFORE the method ran, making
      every refusal path unreachable - the caller saw only "An error occurred invoking
      'ui_render'". Both are now `JsonElement` and take either shape. 129 -> 132 tests, both
      shapes pinned. **The proof discriminates:** the same object-shaped call is `isError: true`
      on the old binary and `isError: false` on the rebuilt one.
      Also: the README publish command was missing `-r win-x64 --self-contained false
      -p:PublishSingleFile=true`, so it emitted a 156 KB launcher plus 45 loose DLLs instead of the
      single 29,799,584-byte exe. Documented with the reason each flag is load-bearing.

- [ ] 🟢 **READY — the in-session bind is the only part left, and it IS user-only.**
      `/reload-plugins` in a live session, then confirm `mcp__plugin_ui-mcp_ui-mcp__ui_open`
      exists and CALL it. Installed is not serving: this session was checked and the tools are
      absent, because the plugin was registered at 13:43 and the last reload ran at ~11:25.

## Deferred

- [ ] `ui_update` — patch one bound value without a full re-render (v0.2).
- [ ] Multi-agent arbitration. v0.1 is last-write-wins with `ui_status` naming the last renderer.
- [ ] Decide whether ui-mcp replaces the HTML console or sits beside it. Two surfaces rendering one
      tree is fine; two surfaces with different data is the two-sources-of-truth failure.

- [x] (2026-08-15) **Published to GitHub** — approved and done. Public, MIT, `main`, issues
      disabled, matching the Windows-mcp shape. Bundle present at 29,799,584 bytes.

- [x] (2026-08-15) **Architecture documentation — all ten canonical documents**, in
      `docs/architecture/`, plus a Documentation index in the README.
      **Unblocked by teaching `repo_map` to parse C#** (`skills` `7264f55`, `45c27ec`): before
      that, every .NET repo scanned to an empty graph and could not be documented against a
      drift gate at all.
      **The gate exits 0, and was mutation-proven rather than trusted:** changing
      `totalLinesOfCode` from 2376 to 9999 in OVERVIEW.md made `check` exit 1 naming the file
      and the claim; restored byte-identical (SHA256 verified). A gate that has never been
      shown to fail is not evidence.
      Also corrected a stale README banner that still read *"specification only, no
      implementation yet"* on a released, installed v0.1.1.

## Done 2026-08-15 (v0.1.2) — the gaps the docs surfaced, closed

- [x] **`TreeRenderer` tested — 26 Fact + 3 Theory.** Pins what reading cannot settle: the
      nine-way switch, the 64/200 caps **applied** (not just declared), `$item` scope reaching a
      `Repeat`'s children and a `Table`'s cells, the tone→brush map, and that a missing value is
      *visually* distinct. **Two real defects found by writing them**, both confirmed against
      `AdminLTE/JSON-UI/render.js` (the stated source of truth):
      (1) a `Gauge` whose `maxPath` did not resolve drew its bar against a **default max of 100**,
          so 50 against an unreadable maximum showed **half full** — a confident measurement
          against a scale nobody supplied. Root cause: "no max asked for" and "max asked for and
          unresolvable" both arrived at `GaugePercent` as `null`, and only the caller could tell
          them apart. Now takes `maxWasRequested` (defaulted `false`, so every existing call is
          unchanged).
      (2) a tone **outside** the closed set rendered as Amber — the *attention* colour —
          contradicting `Tone()`'s own summary and the JS. It manufactures alarm out of a value
          the renderer failed to understand. Absent tone and unknown tone are now distinct cases,
          and the absent case is pinned separately so the fix cannot be over-applied.
- [x] **Seam test — `CatalogRendererSeamTests`**, exhaustive over the catalog rather than sampled.
      `EveryCatalogComponentIsCovered` fails if a tenth component is added without a case, so the
      suite cannot quietly stop being comprehensive.
- [x] **`ComponentSpec` and `PropRule` are `internal`.** All 15 occurrences were in
      `CatalogValidator.cs`. `unusedExportsCount` 2 → 0; 0 warnings, 0 errors.
- [x] **`UiSurface` tested — 12 Fact**, split into a no-window group that runs anywhere and
      `UiSurfaceWindowTests` that briefly shows real windows (~5.6 s of the ~7 s run). The split
      is deliberate: logic failures and environment failures must be distinguishable.
- [x] **`bundle/*.pdb` now gitignored.** `!bundle/` un-ignored the whole directory, so publish's
      symbol files sat untracked-but-unignored — invisible until a `git add -A` committed them.

**132 → 217 tests.** Every new guard mutation-proven, each mutation reverted to a SHA-256-identical
file. One mutation deliberately **survived** and is recorded in TEST_COVERAGE.md: dropping the type
name from `Describe` broke nothing because the compared trees still differ by child count — a weak
*mutation*, not a weak test. Telling those two apart is the whole discipline.

## 🟢 READY

- [ ] **Add `dotnet test` to CI.** The workflow builds and gates bundle freshness plus an MCP
      `initialize` handshake, but never runs the 217 tests — so they are a local habit, not a
      gate. Deliberately left out of the v0.1.2 sweep at the owner's direction.

## Blocked
