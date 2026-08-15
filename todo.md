# ui-mcp — todo

Format: `- [ ] (YYYY-MM-DD) task`. 🟢 READY means unblocked and next.

## Now

- [x] (2026-08-14) Specification written — `docs/SPEC.md`. Architecture, tool contract, catalog,
      safety invariants, testing strategy, v0.1 definition of done, risks.

## 🟢 READY — next session, in order

- [ ] (2026-08-14) **Scaffold the solution.** `src/UiMcp` (net9.0-windows, `<UseWPF>true</UseWPF>`,
      Exe), `src/UiMcp.Abstractions` (**no WPF reference** — that is the seam that keeps the
      validator testable), `tests/UiMcp.Tests` (xunit + FluentAssertions), `global.json` pinned to
      SDK 9.0.314 `rollForward: latestFeature`, mirroring Windows-mcp.
- [ ] (2026-08-14) **Catalog + validator, TDD.** Port the nine components from
      `AdminLTE/JSON-UI/catalog.js`. Failing test first for each rejection path.
      **Every rejection test needs a paired positive control** — a validator that refuses
      everything passes a naive rejection suite.
- [ ] (2026-08-14) **Path resolver, TDD.** Port from `render.js`: nested objects, array indices,
      `$item` scope, prototype-access refusal. Missing path returns **null**, never 0 or "".
- [ ] (2026-08-14) **Verify the desktop assumption EARLY, before building the UI.** Can a stdio MCP
      server started by the host actually create a visible window? If not, everything above still
      holds but the hosting model changes. This is the cheapest risk to retire and the most
      expensive to discover late.
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
