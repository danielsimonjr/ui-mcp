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
- [ ] (2026-08-14) **Path resolver, TDD.** Port from `render.js`: nested objects, array indices,
      `$item` scope, prototype-access refusal. Missing path returns **null**, never 0 or "".
- [ ] (2026-08-15) **Re-check the desktop assumption IF the host ever moves to an S4U trigger.**
      Not currently reachable - every task that starts `claude.exe` is `InteractiveToken` today.
      `tools/probe-desktop.ps1` answers it in one run. See SPEC 10.1.
- [ ] (2026-08-14) **WPF host on an STA thread** with `Dispatcher` marshalling; supervisor so a
      window crash does not take tool serving down.
- [ ] (2026-08-14) **Tools:** `ui_open`, `ui_render`, `ui_status`, `ui_close`.
- [ ] (2026-08-14) **Renderer** for the nine components; validated tree in, WPF visual tree out.
- [ ] (2026-08-14) **Publish + wire:** `bundle/UiMcp.exe`, `.claude-plugin/plugin.json`,
      `.mcp.json` with `${CLAUDE_PLUGIN_ROOT}`, marketplace entry.
      **Confirm it SERVES TOOLS in a live session** — installed is not serving.

## Deferred

- [ ] `ui_update` — patch one bound value without a full re-render (v0.2).
- [ ] Multi-agent arbitration. v0.1 is last-write-wins with `ui_status` naming the last renderer.
- [ ] Decide whether ui-mcp replaces the HTML console or sits beside it. Two surfaces rendering one
      tree is fine; two surfaces with different data is the two-sources-of-truth failure.

## Blocked

- [ ] **Publish to GitHub — needs the owner's approval.** The repo is local-only
      (`git init`, no remote). Creating a remote repository is an outward-facing act.
