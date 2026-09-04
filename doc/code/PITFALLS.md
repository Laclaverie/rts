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

**Prose in the command log is a determinism and localisation bug.** Command rejections were a
free-form `string reason`, and that string is serialised into saves and folded into the replay
digest. Three problems: English text a French player's UI would have to display; numbers
formatted with the machine's locale, so the same command log digests differently on two
machines; and tests that could only substring-match it. Now a `CommandRejection` code, with the
human detail moved to the log file. `Command.ToString()` was digested for the same reason and is
now the type name.

**Casting an int to an enum is unchecked.** C# forbids the *implicit* conversion — except the
literal `0` — but `(LogChannel)99` always compiles and produces a value no `switch` handles. No
compiler setting forbids it. `NoIntToEnumCastTests` scans the source instead; a Roslyn analyser
was rejected because Unity does not run NuGet analysers, so the rule would hold in the shadow
build and quietly not in the editor.

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

**Same-typed parameters are the real hazard, not long parameter lists.** A survey found 15
signatures with more than four parameters; five of them take all-distinct types, so the compiler
catches any misordering and the length is harmless. The dangerous ones were the runs:
`StratumRules` took six consecutive `float`s, `Building` four consecutive `int`s, `PortReport`
three `int`s then three `float`s, and `BalanceTables.Load` had a `ValidationReport` wedged
fourth among six `CsvTable`s.

A transposition in any of those compiles, loads, and produces plausible numbers in the right
columns. That is the worst failure mode this project has, because the numbers are exactly what
nobody can check by eye — swapping goods and buildings gives you farms that cost four coin a day
to eat, and nothing looks wrong until a balance pass makes no sense.

→ Every such call site now passes **named arguments**. Where the parameters are one coherent
group, a struct instead: `BalanceSources`. Prefer named arguments over object initialisers
here — the editor compiles C# 9, which has no `required`, so an initialiser that forgets a
property silently yields `0f`, trading a misordering risk for a missing-value one. Named
arguments keep the compiler's "you must supply every parameter".

→ Count parameters to find candidates; count *consecutive same-typed* parameters to decide which
ones matter. `tools/` has no script for this — it was a one-off; re-derive it if the question
comes up again.

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
- Polling immediately after requesting a compile reads the *previous* run's `completed` and
  exits at once. Wait for the editor to pick the request up before polling.
- `unity cmd eval` returns `400 ... Main thread operation timed out after 5000ms` for slow
  calls **but the call still applies**. Re-query before assuming failure.

→ Confirm editor-side changes via `Library/ScriptAssemblies/*.dll` timestamps or a type probe.

**`File.ReadAllText` cannot read a file that is still being written.** It opens with
`FileShare.Read`, which forbids a concurrent writer, so it fails with a sharing violation
against a live log. The *reader* must share `ReadWrite`, not just the writer. A tail tool
follows a log with `FileStream(path, Open, Read, FileShare.ReadWrite)`.

**Do not generate C# through shell heredocs or string surgery.** A heredoc carrying 200 lines
died with `unexpected EOF` and silently skipped an earlier file in the same command. Inline
Python turned `\n` inside a C# string literal into a real newline twice, producing `CS1010`.
Use a file-writing tool; keep heredocs for short data files with no escapes.

**It corrupts prose too, and there the compiler cannot catch it.** `EDITOR_SETTINGS.md`
carried `Logs\rts_<utc>.log` for three phases with a real carriage return where the `\r`
should have been, because a Windows path went through a non-raw Python string. It rendered as
`Logsts_<utc>.log` and nobody noticed, since no build step reads a markdown file.

→ Any Windows path, regex, or escape sequence goes through a file written with the editing
tools, or through a Python script written to a file with `r'...'` literals — never through an
inline heredoc. If a doc must contain a backslash, read it back after writing.

---

## 6. CI

**Windows costs double on a private repo.** Metered minutes, 2× multiplier, and roughly twice
the wall clock: ~4 billed minutes against ~1. Linux is also stricter — case-sensitive paths, LF
endings.

→ Ubuntu on pull requests; Windows added on pushes to `main`, where the cross-runtime
determinism check earns it.

**Warnings accumulate unless they are errors.** 18 nullable warnings built up before CI existed
to show them. `TreatWarningsAsErrors` is on for all three shadow projects.
