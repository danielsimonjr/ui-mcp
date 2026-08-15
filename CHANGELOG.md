# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Specification** (`docs/SPEC.md`) for an MCP server that hosts a native WPF window and renders
  catalog-constrained JSON UI trees. Covers architecture, the five-tool contract, the nine-component
  catalog, seven safety invariants, testing strategy, a v0.1 definition of done, and four named
  risks with mitigations.
- **README** describing purpose, the threading rationale for C# over PowerShell, the safety model,
  and the relationship to `Windows-mcp`, `json-render`, and the existing HTML/PowerShell consoles.
- **`todo.md`** with the ordered build sequence.

### Notes

- No implementation yet. This release is specification only.
- The design is not greenfield: a working JS renderer (`AdminLTE/JSON-UI/`) and a working WPF
  renderer (`~/.claude/scripts/starship-console.ps1`) already exist and are proven against live
  data from two machines. The C# port is transcription, not design. Where the spec and that code
  disagree, **the code is right and the spec is stale**.
- The desktop-availability risk (can a stdio MCP server create a visible window?) is scheduled to
  be retired **before** the UI is built. It is the cheapest risk to test and the most expensive to
  discover late.
