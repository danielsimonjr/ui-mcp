<!-- repo-map:no-verification -->
<!-- GENERATED FILE -- do not edit by hand.
     Regenerate with `repo-tools map`. -->

# Unused Files and Exports Analysis

## Summary

- **Files with no in-repo importer**: 0
- **Dormant files**: 1
  - **Orphaned (reachable from nothing)**: 0
  - **Test-only (only a test reaches them)**: 1
- **Potentially unused exports**: 0
  - **Unreferenced anywhere**: 0
  - **Referenced in their own module**: 0
  - **Not classified**: 0

## Caveats

- A literal relative `import(...)` is a graph edge. A runtime call is a namespace use (`*`), so the exports of that file count as used. A type-position `import(...).Name` counts only `Name`, and `typeof import(...)` counts none. A dynamic import built from a variable or a template, and other runtime loads (e.g. `new Worker(path)`), are still invisible -- a file or export reached ONLY through one of those is reported as no-importer/unreferenced even though it may be live.
- `noImporterFiles` is NOT a deletion-candidate list. A file with zero in-repo importers is expected, not suspicious, for: a standalone script invoked directly (e.g. a smoketest run via `node script.mjs`, never `import`ed by anything), or a build/lint config file loaded by its own tool rather than by source code (e.g. `eslint.config.mjs`, read by eslint itself). Cross-check against package.json scripts / tool configs before treating any entry here as dead.
- Only NAMED exports are analysed (`FileNode.exports` carries named exports only) -- `export default` usage is not checked here.
- The `referencedInModule` vs `unreferencedAnywhere` split for exports is a TEXT-LEVEL heuristic (whole-identifier occurrence count within the defining file's own source, minus the declaration site itself), not an AST reference count -- it can over-count a name that also appears in a string literal or comment. When the source text can't be read at all (no `RepoGraph.root_path`, or the file is missing) the export lands in `unclassifiedExports` instead of being guessed into either bucket.
- `unreferencedAnywhereNotes`'s dynamic-import check (`_dynamic_import_referrers`) only sees `import(...)` calls with a LITERAL string specifier. A dynamic import built from a variable or template literal (e.g. `import(`./cmds/${name}.js`)`, a command-dispatcher pattern) is invisible to it -- 'found none' in a note means no LITERAL match was found, NOT that nothing imports the file.
- This language's imports name a namespace, not the symbols they bring in (C#'s `using App.Hosting;`), so per-symbol usage cannot be read off the import edges at all. Usage here is a whole-identifier text search across every scanned file instead -- the same class of TEXT-LEVEL heuristic as the `referencedInModule` split above, and subject to the same limit: a name appearing in another file's comment or string literal counts as a use. That makes this direction conservative -- it UNDER-reports dead code rather than inventing it -- which is the safe direction for a list whose entries read as deletion candidates. A type reached only by reflection (`Activator.CreateInstance`, DI-by-convention) or named solely in a .csproj/XAML file is invisible either way.

## Dormant files: orphaned

Source files that no root and no test reaches. Verify each one before you delete it.

_None._

## Dormant files: test-only

Source files that only a test reaches. They ship nothing, but a test uses them.

- `src/UiMcp/Tools/UiTools.cs`

## Files with no in-repo importer

No file of this repository imports these files. This is not a deletion list.

_None._

## Exports unreferenced anywhere

No other file imports these names, and their own module does not use them.

_None._

## Exports referenced in their own module

No other file imports these names, but their own module uses them.

_None._

## Exports not classified

The analysis could not read the source of these names.

_None._
