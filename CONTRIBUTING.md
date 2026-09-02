# Contributing

Audience: the maintainer, future collaborators, and AI agents working in this repo.
Solo today — written down anyway, because the rules below are the kind that get broken
silently and cost weeks to unpick.

Read [`doc/ARCHITECTURE.md`](doc/ARCHITECTURE.md) §1 first. The nine constraints there
are requirements, not style preferences.

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

## 2. Adding a system

A system is not done until all five exist:

1. **Data** — its tunable numbers in `Content/Balance/`, never in code
2. **Code** — a small `ISystem` with one concern (C9)
3. **Registration** — a row in `Balance/pipeline.csv`, with a comment if its position is
   load-bearing (e.g. `Wages` must precede `Unrest` so unpaid wages feed grievance the
   same day)
4. **Tests** — see §4
5. **Validation** — any new data columns covered by the loader's schema checks

The loader hard-fails on a system present in code but absent from `pipeline.csv`, and
vice versa. A system that silently never runs is close to undebuggable, so this is
deliberate.

---

## 3. Branches and pull requests

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

## 4. Testing — the Definition of Done

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

## 5. Commits

[Conventional Commits](https://www.conventionalcommits.org/): `type(scope): subject`.

Types in use: `feat`, `fix`, `docs`, `chore`, `test`, `refactor`, `perf`.

Subject in the imperative, ≤ 72 characters. Body only when the *why* isn't obvious from
the diff — and for anything touching §1, the why is never obvious, so include it.

---

## 6. Scope discipline

`GDD.md` Appendix A parks a long list of tempting features behind explicit gates. That
list exists because the 2021 version of this project died of scope, not of difficulty.

**Adding to Appendix A is free. Taking something out of it early is a decision that
needs an argument.** The MVP slice (`GDD` §8.1) and its kill test (§8.2) come first; if
the kill test fails, the answer is not more features.
