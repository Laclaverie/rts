# Toolchain

What this project is built with and how to drive it. Design lives in `ARCHITECTURE.md`;
mistakes these tools caused live in [PITFALLS.md](PITFALLS.md).

---

## Versions

| | Version | Note |
|---|---|---|
| Unity | `6000.3.23f1` (6.3 LTS) | Pinned in `ProjectSettings/ProjectVersion.txt` |
| Unity language | **C# 9**, `netstandard2.1` | Editor passes `-langversion:9.0`; the shadow projects match |
| .NET SDK | 9.0.x | Headless suites only; never compiled by Unity |
| IDE | Visual Studio Community 2026 (18.x) | 2022 is also installed; Unity must point at 18 |
| Test framework | NUnit | Same attribute set headless and in Unity |
| Build target | Windows standalone | Desktop only — see `BalanceFiles` on the Android/WebGL limit |

Unity solutions are `.slnx`, the newer XML solution format. VS 2026 reads it; VS 2022 does not.

---

## Layout

```
Assets/                     what Unity compiles
  Sim/        Content/      no UnityEngine reference — enforced by asmdef
  Game/                     the Unity side; may use UnityEngine
  StreamingAssets/
    Balance/                designer numbers (CSV)     — ARCHITECTURE §5.2
    Config/                 developer settings (CSV)   — e.g. logging.csv
dotnet/                     shadow projects: the same sources, compiled headlessly
tools/                      scripts, not a project
doc/                        specs, and these notes
```

**Shadow projects are a second gate, not a copy.** `dotnet/Sim` and `dotnet/Content` glob
`../../Assets/**/*.cs` at Unity's target framework and language version, so a `dotnet build`
fails on anything the editor would reject. Same files, compiled twice.

**`StreamingAssets` is the only Unity folder that survives into a build as ordinary files.**
`Content` has no `UnityEngine` reference, so it cannot read a `TextAsset` or ask Unity for a
path; one `System.IO` code path then works in the editor, a player and the test runner.

---

## Running tests

```
tools\test              Unit + Functional, headless      ~200 ms
tools\test -Unit        unit only
tools\test -Functional  functional only
tools\test -Flaky       opt-in, non-gating; never part of a default run
tools\test -All         + the Unity EditMode suite       (needs Unity running)
tools\test -Unity -Launch                                (starts Unity first)
```

Double-clicking `tools\test.cmd` works and holds the window open; a terminal or CI run does
not block. Exit code is non-zero if anything fails, and **a Unity editor that is not running
counts as a failure** rather than a silent skip.

Which suite answers what:

| | Where | Question |
|---|---|---|
| `dotnet/Sim.Tests` | outside Unity | Is the code right? The working loop. |
| `Assets/Game/Tests/EditMode` | inside Unity | Do Unity APIs, asset paths and the editor's own compiler agree? |

Categories are in `CONTRIBUTING` §2. A fixture with no `[Category]` runs in neither filtered
suite; a convention test fails the build if one appears.

---

## Driving the Unity editor from the terminal

`unity` (Unity CLI) talks to the **running** editor over a local port. Port and PID change
after a domain reload; that is normal.

```bash
unity status                    # connected editors
unity cmd recompile             # then poll:
unity cmd recompile_status
unity cmd console               # captured editor console
unity cmd run_tests --mode EditMode
unity cmd eval --code '<C#>'    # arbitrary editor-side C#
unity cmd list                  # the full command surface
```

To force a build the editor is convinced it does not need:

```bash
unity cmd eval --code 'UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
  UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(
    UnityEditor.Compilation.RequestScriptCompilationOptions.CleanBuildCache); return "ok";'
```

Poll `recompile_status` until `completed`, then confirm via `Library/ScriptAssemblies/<Asm>.dll`
timestamps. The status alone is not sufficient — PITFALLS §5.

---

## CI

`.github/workflows/ci.yml` runs `tools/Run-Tests.ps1`, the same entry point developers use, so
CI cannot drift from local.

- **Pull requests:** Ubuntu only.
- **Pushes to `main`:** Ubuntu **and** Windows.

Windows keeps a thread because the `Rng` golden vectors are one table asserted on Linux .NET,
Windows .NET and Unity's runtime. It stays off pull requests because the repo is private:
metered minutes, 2× multiplier.

The Unity EditMode suite is not in CI — it needs a licensed Unity runner, which currently would
cover eight tests. Revisit when the Unity side is load-bearing.

`.trx` results upload on success or failure.

---

## Git

- Branch and PR for everything, including infrastructure. Never push to `main`.
- `gh` is on PATH. The token has no `delete_repo` scope; `gh repo delete` fails.
- LFS is active for binary asset types via the Unity `.gitattributes` template. Free tier is
  1 GB storage and 1 GB/month bandwidth; every revision of an asset counts.
- `.gitattributes` forces `eol=crlf` on `*.cmd` and `*.bat` — PITFALLS §5.
