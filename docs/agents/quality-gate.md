# Quality gate commands

This file gives the commands for the `/quality-gate` skill in this repository. The skill checks the files that a
branch changes, before a review. Run every command from the repository root.

Verified on .NET SDK 10.0.113. On another SDK version, repeat the validation of check 1 before you trust it.

## 1. Unused usings (C#)

```
dotnet build server/Tuigether.Server.Specs --no-incremental --artifacts-path <temp folder> -nologo \
  -p:GenerateDocumentationFile=true -p:AnalysisModeStyle=All -p:TreatWarningsAsErrors=false
```

Keep the lines with `warning IDE0005` in a changed file. The spec project references the other two C# projects,
so this one build covers all three.

- `GenerateDocumentationFile=true`: without it, the compiler does not report IDE0005 in a build, whatever its
  severity.
- `AnalysisModeStyle=All`: it raises IDE0005 from hidden to warning. It raises the other style rules too, so filter
  for IDE0005.
- `TreatWarningsAsErrors=false`: the extra warnings would stop the build.
- `--artifacts-path`: the normal `bin/` and `obj/` stay unchanged.
- `--no-incremental`: a project that is up to date skips the compiler, and with it the analyzer.

The normal build does not report IDE0005.

Validation: in a folder outside the repository, copy `Directory.Build.props`, `.editorconfig` and `global.json`.
Add an empty SDK project with one file. In that file, put each planted using between two usings that the code
needs: `using System;` (a duplicate of an implicit using), `using System.Text;` (unused) and
`using Foo = System.Text.StringBuilder;` (an unused alias). The command must report all three lines. IDE0005 reports
a block of unused usings that follow each other as one warning, on the first line of the block.

The F# client has no validated detector for unused `open` declarations. Report this part of the check as
unavailable.

## 2. Formatter

- C#: `dotnet format server/<project> --verify-no-changes --include <file> ...` for each changed `.cs` file.
- F#: `dotnet fantomas --check <file> ...` for each changed `.fs` file. Run `dotnet tool restore` first on a fresh
  clone.

Apply the fixes per file: drop `--verify-no-changes` and `--check`. Some files on `main` are not formatted, so a run
on a whole folder puts their changes into the diff.

## 3. New warnings

```
dotnet build Tuigether.slnx
```

`Directory.Build.props` sets `TreatWarningsAsErrors` for the C# projects, so a new C# warning fails the build. The
F# client must also stay at 0 warnings.

## 4. and 5. Debug leftovers, TODO and FIXME

The skill greps the added lines of the diff. This repository adds no command.

## 6. Cognitive complexity (C#, local trial)

This check runs only when `tools/cognitive-complexity/` exists. The folder is a local trial, and
`.git/info/exclude` keeps it out of the repository. Without the folder, report the check as unavailable.

```
dotnet fsi tools/cognitive-complexity/measure.fsx Tuigether.slnx <base-ref>
```

Every new or touched member in `server/` scores 5 or less. A member above 5 is a finding. Flatten it as
`tools/cognitive-complexity/README.md` describes, or report it as open. A score that fell because the diff added a
forwarding method is a finding too.
