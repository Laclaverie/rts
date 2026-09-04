# Contributing

Audience: the maintainer, future collaborators, and AI agents working in this repo.
Solo today — written down anyway, because the rules below are the kind that get broken
silently and cost weeks to unpick.

Read [`doc/ARCHITECTURE.md`](doc/ARCHITECTURE.md) §1 first. The nine constraints there
are requirements, not style preferences.

---

Reference docs:

| | |
|---|---|
| `doc/ARCHITECTURE.md` | design and its constraints |
| `doc/BUILD_ORDER.md` | what gets built in what order |
| `doc/code/TOOLCHAIN.md` | versions, layout, running tests, CI, git |
| `doc/code/PITFALLS.md` | mistakes already made, and the rules from them |
| `doc/design/ECONOMY_FINDINGS.md` | what running the sim actually showed, and what changed because of it |
| `doc/editor/EDITOR_SETTINGS.md` | anything configured inside Unity |

---

## 1. Rules that are easy to break by accident

Each of these has a specific, non-obvious cost. Breaking one is a scope change to be
argued in a PR, never a convenience taken quietly.

| Rule | Why it exists |
|---|---|
| **No `UnityEngine` reference in `Sim` or `Content`** | Headless tests and console-speed iteration. Enforced by asmdef — if it compiles, you broke the asmdef. |
| **No coroutines, no `async`/`await` in `Sim`** | Their state lives outside `World` and cannot be snapshotted, which breaks fast loads and Decision Timeline fork points. (§7.2) |
| **No static mutable state, anywhere in `Sim`** | Destroys determinism and parallel test runs at once. |
| **No `UnityEngine.Random`, no `DateTime.Now` in `Sim`** | A save *is* a replay. Non-determinism is save corruption, not just a flaky test. (§7.1) |
| **No tuned constants in code** | All numbers live in `Content/Balance/*.csv`. One place, per C1. |
| **No ScriptableObjects for balance or content** | Unity-specific, and scatters tuning across one asset per entity. Permitted only for presentation-side prefab binding. (§1.1) |
| **Event subscribers never mutate the world** | They may only enqueue commands. Otherwise the event bus becomes implicit, unordered control flow. (§7) |
| **All sim state lives in `World`** | Anything else is invisible to saves, snapshots, the causal DAG, and the debugger. |

If a change genuinely needs one of these relaxed, say so explicitly in the PR body and
explain the cost. Silence is the failure mode.

---

## 2. Where code goes

Nothing loose in `Assets/`. Every script lives under an assembly folder, and inside it
under a folder named for its concern.

```
Assets/
  Sim/                        Sim.asmdef        — no UnityEngine reference
    Engine/                   reusable (C4): no ports, no crew, no goods
      Entities/               EntityId, ComponentStore<T>, World
      Pipeline/               Pipeline, phases, Context
      Events/                 EventQueue, CauseId
      Commands/               CommandDispatcher, command log
      Randomness/             seeded Rng
      Compat/                 language/runtime shims
    Components/               game component structs      (Phase 1+)
    Systems/                  game systems                (Phase 1+)
  Content/                    Content.asmdef    — no UnityEngine reference
    Loading/                  CSV and JSON readers
    Registries/               typed registries the sim reads
    Validation/               schema checks (§5.3)
  Game/                       Game.asmdef       — the Unity side; may use UnityEngine
    Boot/                     composition root, path resolution
    Tests/EditMode/           Unity-side tests: what dotnet test cannot reach

tools/                        scripts, not a project: test runner, later CI entry points
```

**The `Engine/` boundary is the one that matters.** ARCHITECTURE §2.1 lists what is
genuinely reusable — `ComponentStore<T>`, `Pipeline`, `EventQueue`, `CommandDispatcher`,
`ConfigRegistry`, `Rng`. If a type under `Engine/` mentions a port, a crew member or a
good, it is in the wrong folder.

**Namespaces mirror folders.** `Assets/Sim/Engine/Entities/` is `RTS.Sim.Engine.Entities`.
A namespace that disagrees with its path is misleading — it makes a type harder to find
and quietly breaks the IDE's assumption that the two match. ARCHITECTURE §2.1 asks for
"an `Engine/` namespace", which is a prefix and not a leaf, so the nesting satisfies it.

The one exception is `Engine/Compat/`, which holds shims the compiler requires by exact
name — `IsExternalInit` must be in `System.Runtime.CompilerServices` or it does nothing.
Compiler-mandated namespaces win; nothing else gets an exception.

**Two kinds of test, and they run separately** (ARCHITECTURE §8.1, §8.2). Every fixture
carries exactly one `[Category]`:

| | `TestCategories.Unit` | `TestCategories.Functional` | `TestCategories.Flaky` |
|---|---|---|---|
| Touches | nothing outside the code under test | filesystem, shipped balance files, later replay corpora | timing, performance, whatever else the machine was doing |
| A red test means | **the code is wrong** | usually the code is fine and **the world around it changed** | maybe nothing — it does not gate |
| Runs by default | yes | yes | **no**, `-Flaky` only |

The split is not about speed — both are milliseconds today. It is about what a failure
tells you, so a red build is not ambiguous. `tools	est -Unit` and `tools	est -Functional`
run them alone.

`Flaky` is **not a parking space for tests that fail.** A test whose *claim* is sound but
whose *mechanism* is unreliable should have its mechanism fixed — the determinism gate's
negative test drove divergence from the wall clock, was flaky, and the answer was static
mutable state, not a category. Use `Flaky` only where the unreliability is inherent to the
environment, and prefer `Assert.Warn` over `Assert.That` inside one so even the opt-in run
reports rather than fails.

**A fixture with no category runs in neither suite** — it does not fail, it disappears. A
convention test reflects over the assembly and fails if any fixture has no category, more
than one, or an unknown one. Do not delete it.

Use NUnit categories rather than separate projects, because Unity's test runner reads the
same attribute. Split into its own project when functional tests need infrastructure the
unit tests should not carry: replay corpora, long timeouts, saved sessions.

**Running them: `tools	est`** (double-click `tools	est.cmd`, or run it from a terminal).
With no switches it runs both kinds headlessly, because a complete fast loop is worth more
than a marginally faster one. `-Unit` or `-Functional` runs one kind alone. `tools	est -All`
adds the Unity suite; `-Unity -Launch` starts the editor first.

**CI runs the same script**, so it cannot drift from what you run locally: `.github/workflows/ci.yml`
calls `tools/Run-Tests.ps1` with no switches — on **Ubuntu for pull requests, and Ubuntu plus
Windows for pushes to `main`**. Keeping a Windows thread is deliberate: saves are a seed plus a
command log (§6.1), so cross-runtime determinism is a correctness property, and the `Rng`
golden vectors get asserted on Linux .NET, on Windows .NET and inside Unity's runtime. Keeping
it off pull requests is also deliberate: this repo is private, so minutes are metered and
Windows bills at twice the Linux rate for a job that takes about twice as long. The Unity EditMode suite is *not* in CI: it needs a licensed Unity
runner, which today would buy four tests. Revisit when the Unity side is load-bearing. It exits
non-zero if anything fails, and a Unity editor that is not running counts as a failure rather
than a silent skip — skipping would report green on an untested half.

**Two test suites, and they answer different questions.** `dotnet/Sim.Tests` runs outside
Unity in ~100ms and covers everything in `Sim` and `Content` — that is the loop to work in.
`Assets/Game/Tests/EditMode` exists only for what it structurally cannot reach: Unity APIs,
asset paths, anything where being inside the editor is the point. Do not duplicate a headless
test there; run them with `unity cmd run_tests --mode EditMode`.

Balance data is not code and does not live here: it goes in `Content/Balance/` as CSV
(ARCHITECTURE §5.2).

---

## 3. Adding a system

A system is not done until all five exist:

1. **Data** — its tunable numbers in `Content/Balance/`, never in code
2. **Code** — a small `ISystem` with one concern (C9)
3. **Registration** — a row in `Balance/pipeline.csv`, with a comment if its position is
   load-bearing (e.g. `Wages` must precede `Unrest` so unpaid wages feed grievance the
   same day)
4. **Tests** — see §5
5. **Validation** — any new data columns covered by the loader's schema checks

The loader hard-fails on a system present in code but absent from `pipeline.csv`, and
vice versa. A system that silently never runs is close to undebuggable, so this is
deliberate.

---

## 4. Branches and pull requests

**One branch per unit of work.** Naming:

| Prefix | Use |
|---|---|
| `phase0/`, `phase1/`, … | Build-order phases, e.g. `phase0/foundations` |
| `feat/` | New behaviour |
| `fix/` | Bug fixes |
| `docs/` | Documentation only |
| `chore/` | Plumbing, tooling, config |

**The PR body is the recap.** It is the project's running history, so write it for
someone reading in six months. Include:

- **What changed** — the substance, not a file list
- **Why now** — usually a phase or gate it serves
- **Which gate it moves**, or explicitly none
- **Deliberately not included** — the scope you declined, so it reads as a decision
  rather than an omission
- **Any constraint relaxed**, per §1

Merge with a merge commit, not a squash, so the phase structure stays legible in history.

---

## 5. Testing — the Definition of Done

**Tests ship with the feature.** Not a follow-up ticket. This is affordable precisely
because `Sim` has no Unity dependency: tests run in plain .NET in milliseconds, outside
the editor.

- **Unit tests** — per system, table-driven: given world state and config, running the
  system produces an expected state.
- **Functional tests** — a seed plus a command log, replayed, asserted on. Cheap, given
  commands-as-data and determinism.
- **Design assertions** — the interesting ones. Design rules are executable here: *"a
  single bad event never causes collapse"* (`GDD` §5.2.3) is a property to generate
  scenarios against, not just prose.
- **Balance validation** — the loader's schema and range checks run in CI over the
  shipped `Balance/` folder.
- **Replay determinism** — replay a stored log twice, assert identical end state. This
  guards saves, not just tests, so it runs on every build.

---

## 6. Commits

[Conventional Commits](https://www.conventionalcommits.org/): `type(scope): subject`.

Types in use: `feat`, `fix`, `docs`, `chore`, `test`, `refactor`, `perf`.

Subject in the imperative, ≤ 72 characters. Body only when the *why* isn't obvious from
the diff — and for anything touching §1, the why is never obvious, so include it.

---

## 7. Scope discipline

`GDD.md` Appendix A parks a long list of tempting features behind explicit gates. That
list exists because the 2021 version of this project died of scope, not of difficulty.

**Adding to Appendix A is free. Taking something out of it early is a decision that
needs an argument.** The MVP slice (`GDD` §8.1) and its kill test (§8.2) come first; if
the kill test fails, the answer is not more features.
