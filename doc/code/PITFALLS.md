# Pitfalls

Mistakes made in this project and the rules that came out of them. Add an entry when something
costs more than ten minutes.

---

## 1. Unity and C#

**The editor compiles C# 9.** Unity 6.3 passes `-langversion:9.0`. `record struct` is C# 10 and
fails with `CS8773`. Unity's bundled Roslyn is 4.3 and accepts C# 10 by default, so probing the
compiler directly gives the wrong answer; the authority is Unity's generated `Sim.csproj`.

→ The `dotnet/` shadow projects pin `netstandard2.1` / `LangVersion 9.0`. Record *classes* are
C# 9 and available.

**`init` and records need an `IsExternalInit` shim.** netstandard2.1 does not define it.
`Assets/Sim/Engine/Compat/IsExternalInit.cs` does. It must stay in
`System.Runtime.CompilerServices` — the compiler looks it up by exact name. Sole case where a
namespace may disagree with its folder.

---

## 2. Determinism

Saves are a seed plus a command log (§6.1). Everything here is save corruption, not test flake.

**`System.Random` is not stable across runtimes.** .NET Framework/Mono and .NET Core 3.0+
produce different sequences from the same seed; the implementation is documented as subject to
change. `Sim` compiles under both Unity's runtime and .NET.

→ `Rng` implements PCG-XSH-RR explicitly. `RngGoldenVectors` is asserted headless, in Unity, and
on Linux in CI. Changing that table is a save-format break.

**`Enum.TryParse` accepts more than enum names**, past an `Enum.IsDefined` guard:

```
"Tick,DayBoundary" -> DayBoundary    comma lists parse on a non-[Flags] enum
"1"                -> DayBoundary    numeric strings accepted
"0"                -> Tick
```

Writing `1` works, then reordering the enum silently repoints every such row.

→ Config columns match enum names exactly. `RowReader.Enum<T>` does this and reports the valid
names.

**Floats must be digested by bit pattern.** Formatting rounds, and rounding hides the one-ulp
drift the gate exists to catch. `HashStateWriter` folds `SingleToInt32Bits`.

**Parse and format with `InvariantCulture`.** `float.Parse("0.5")` on a French-locale machine
returns `5` — a wrong number, not an error. This machine runs a French SDK.

**Dictionary iteration order is not deterministic.** `ComponentStore`, `ConfigRegistry` and
`World` keep dictionaries as lookup indexes only and iterate packed arrays. §7.1.

**No static mutable state in `Sim`.** The second run sees what the first left behind. The one
exception is `Log`, permitted because logging may not influence sim state — and that exception
is tested.

---

## 3. Configuration

**Report every problem, not the first.** `ValidationReport`,
`PipelineConfigurationException` and `ContentValidationException` accumulate.

**Check the header once.** A missing column reported per row produces N identical problems and
buries the rest. `ValidationReport.RequireColumns` runs before any row.

**A silently skipped thing is worse than a loud failure.** Same shape, five times:

| Thing | Would silently vanish | Now |
|---|---|---|
| System missing from `pipeline.csv` | never runs | load fails |
| Command with no handler | accepted, does nothing | throws at submission |
| Fixture with no `[Category]` | runs in neither filtered suite | convention test fails |
| Component without `IComponentData` | excluded from the digest | does not compile |
| Channel misspelled in `logging.csv` | phantom channel | load fails |

→ When something can be absent, check what absence looks like. If it looks like success, make
it loud.

**Ambiguous ordering is non-determinism.** Two systems claiming the same `(phase, order)` are
rejected, not tie-broken.

---

## 4. Testing

**A gate that cannot fail is not a gate.** Each has been broken on purpose once and restored:
asmdef boundary (`using UnityEngine;`), determinism digest (leak state between runs), category
convention (untagged fixture), state schema (rename a key), shipped files (hide `pipeline.csv`).

**A test proving determinism must itself be deterministic.** The negative gate test used
`DateTime.UtcNow.Ticks % 7`; two runs could coincidentally agree, ~1 in 50. It failed on `main`
minutes after merge. Flakiness in the mechanism is a bug in the test, not a use for the `Flaky`
category — that is for inherently environment-dependent signal.

**`nameof()` is wrong for serialisation keys.** They are a file format, not identifiers.
`nameof(_lastEntityId)` ties the save format to a private field name, so a rename silently
changes the digest. Keys are literals; `StateSchemaTests` pins the shape and says whether to
restore it or bump `SchemaVersion`.

**Do not write an API before its caller.** `ComponentStore.Set` silently upserted with no caller
anywhere. It became a strict `Add`: loosening later breaks nothing, tightening later breaks
everything.

---

## 5. Tooling on this machine

**Batch files must be CRLF.** `cmd.exe` misparses LF-only `.cmd` — symptom was
`'M' is not recognized`, first characters of lines eaten. `.gitattributes` forces `eol=crlf`.

**Unity's CLI reports success wrongly in two ways:**

- `unity cmd recompile` answers `up_to_date` while the editor has not built new files. Force
  with `CompilationPipeline.RequestScriptCompilation(CleanBuildCache)`, then poll.
- `unity cmd eval` returns `400 ... Main thread operation timed out after 5000ms` for slow
  calls **but the call still applies**. Re-query before assuming failure.

→ Confirm editor-side changes via `Library/ScriptAssemblies/*.dll` timestamps or a type probe.

**Do not generate C# through shell heredocs or string surgery.** A heredoc carrying 200 lines
died with `unexpected EOF` and silently skipped an earlier file in the same command. Inline
Python turned `\n` inside a C# string literal into a real newline twice, producing `CS1010`.
Use a file-writing tool; keep heredocs for short data files with no escapes.

---

## 6. CI

**Windows costs double on a private repo.** Metered minutes, 2× multiplier, and roughly twice
the wall clock: ~4 billed minutes against ~1. Linux is also stricter — case-sensitive paths, LF
endings.

→ Ubuntu on pull requests; Windows added on pushes to `main`, where the cross-runtime
determinism check earns it.

**Warnings accumulate unless they are errors.** 18 nullable warnings built up before CI existed
to show them. `TreatWarningsAsErrors` is on for all three shadow projects.
