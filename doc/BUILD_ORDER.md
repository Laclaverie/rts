# Build Order

**Status:** living document · created 2026-09-02 · companion to `GDD.md` and `ARCHITECTURE.md`
**Goal:** reach the kill test (`GDD` §8.2) in the fewest possible steps.

---

## 1. Ordering principles

1. **Everything is ordered by how much it shortens the path to the kill test.** A task
   that doesn't move that question closer to an answer is deferred, however appealing.
2. **The sim needs no renderer.** Most of this game can be built, tuned and validated in
   a console. That is the single biggest schedule lever available, and it exists only
   because `Sim` has no Unity dependency (`ARCHITECTURE` §2).
3. **Ugly but playable beats pretty but partial.** No art until the kill test passes.
   The Synty pack stays unopened; placeholder shapes throughout.
4. **Every phase ends runnable and green.** No phase leaves the tree broken or the tests
   red.
5. **Data, system and tests land together** (C7). A system without its tests is not done.
6. **Fail early on purpose.** Each phase has a gate that can *fail*. A gate that cannot
   fail is not a gate.

---

## 2. Phases

### Phase 0 — Foundations

Infrastructure only. No game concepts anywhere in this phase.

- Repo, Unity project, `.gitignore`
- `Sim.asmdef` / `Content.asmdef` with **no UnityEngine reference** — verify by adding
  one and confirming it fails to compile, then remove it
- Plain .NET test project outside Unity
- `EntityId`, `ComponentStore<T>`, `World` (`ARCHITECTURE` §3)
- `Pipeline`, phases, `pipeline.csv` loader (§4.2) — including the loud failure on a
  mismatch between file and code
- `Context`, `EventQueue` **with `CauseId` stamping from day one** (§6.2)
- `CommandDispatcher` + command log (§6)
- Seeded `Rng` (§7.1)
- CSV loader + validation harness (§5.3)
- CI running the test project

**Gate: replay determinism.** Run an empty world with a scripted command log twice;
assert byte-identical end state. *Implemented as `ReplayRun` plus
`ReplayDeterminismGateTests`: `Digest()` for "did it change", `Dump()` for "where", and a
deliberately wall-clock-reading system proving the gate can fail.* Everything downstream — saves, tests, the timeline —
rests on this, so it gets proven before anything is built on it.

**Not now:** anything visual, any game concept, any Unity scene beyond an empty one.

---

### Phase 1 — Economy skeleton, headless

One port, no map, no neighbours, no routes. Numbers in a console.

- `goods.csv`, `buildings.csv`, `crew_roles.csv`
- Systems: `Consumption`, `Production`, `Wages`, `Upkeep`
- Day-boundary phase wired and ordered (`Wages` before `Unrest` later — §4.2)
- Console harness: run N days, print a state table per day

**Gate: the cascade behaves as designed** (`GDD` §5.2.3). *Implemented as
`CascadeGateTests`, asserting on `PortCondition` rather than raw numbers so it survives
tuning. Passing; the reserve band it depends on is recorded in
`doc/design/ECONOMY_FINDINGS.md`.* Two executable assertions:

- a **single** shock is always survivable — inject one, assert recovery
- **correlated** shocks spiral — inject three, assert collapse
- reserves-as-slack visibly determines which happens

**This is the highest-value early gate in the project.** If that curve can't be tuned to
feel right, the failure model is wrong — and you find out in a console, weeks in, with
no art, no UI, and nothing sunk. Do not skip past a mushy result here.

**Not now:** trade, neighbours, Heat, map, UI.

---

### Phase 2 — Unrest and the revolution ladder, headless

Still no Unity.

- Strata and grievance (`GDD` §5.2.2)
- `Unrest` system, `RevolutionLadder` as an explicit state machine, rungs 1–5
- `SuppressRiot` command with its loyalty cost
- Console: ladder state and grievance per stratum, per day

**Gate: you can drive a port into revolt, and pull it back out, by playing the numbers.**
Both directions must work. A ladder that only ever climbs is a timer wearing a costume.

**Passed** — `Sim.Tests/RevoltGateTests.cs`, 11 tests against the shipped content through the
full pipeline. The climb worked immediately. Getting the second direction to work needed three
mechanics the gate exposed as missing, each written up in `doc/design/ECONOMY_FINDINGS.md`:

- `days_to_climb` per rung, because the ladder climbed faster than grievance could fall and no
  action could change the outcome once a port was pinned at 1.00
- `cowed_days` on repression, because a one-day relief was re-added by the same day's hunger and
  force ended a riot no faster than patience
- `relief_per_day` per stratum, because at 0.04/day fixing the economy was not a lever at all and
  repression was the only exit rather than one of two

**The one thing the gate could not make work has since been fixed.** Deposition was unreachable
from play and an emptied port read as Calm, because strata had no populations of their own: every
pressure was a count of crew, so when the last one deserted all three went quiet at once.
Commoners now exist — they work the buildings, they eat, and they leave only after sustained
starvation — and each stratum is angered by its own people rather than a shared tally. Named crew
became specialists who improve a building rather than manning it.

That rebalance broke the economy three ways before it settled, all measured in
`doc/design/ECONOMY_FINDINGS.md`: losing crew briefly became a windfall, a town that eats made
coin reserves meaningless until the market could sell food, and the food `keep` was below one
day's demand so the market had been selling the port into famine every morning.

**Not now:** the mob as a visual thing — rung 5 is a state, not a scene, until Phase 5.

---

### Phase 3 — Minimum playable UI

First Unity work. Deliberately ugly — lists, labels, buttons.

- **Pause and speed controls first** (`GDD` §3.2, §5.1). They are the casual mechanism,
  not a convenience, so they are not a late addition
- Event feed, driven by the provenance DAG (`ARCHITECTURE` §6.2)
- Issuable commands: assign crew, set tax, build, demolish, repress
- State readouts: reserves, upkeep, stocks, unrest by stratum

**Gate: a person who is not you can play a session and describe what happened.**

**Pause, speed, the readouts and the event feed are in.** Still to do before the gate can be
tried on someone: buttons for the commands that already exist (`AssignCrew`,
`MothballBuilding`, `SuppressRiot`).

The feed is the first thing to consume the causal DAG, which `ARCHITECTURE` §6.2 built months
early on the grounds that it could not be reconstructed afterwards. That turned out to be
right: a consequence is drawn indented under its cause, so "you shut a building" and "2 crew
released" read as one thought rather than two adjacent lines.

The shape this took is worth stating, because it is now a rule (`ARCHITECTURE` §2.2): the game
is a `GameSession` in `Sim` — advance time, read state, issue commands, see what happened — and
Unity is a renderer holding a `GameBoot` and a `PortPanel`. Everything Phase 3 added that could
be tested headlessly was, including the readouts' own wording; the editor answers only whether
StreamingAssets resolves and whether the panel has something to draw with.

From here on, **hand it to someone at every phase.** The formal kill test is Phase 6,
but informal ones start now and are cheap. Waiting until Phase 6 to learn how it reads
would waste the entire point of building headless-first.

**Not now:** 3D, art, animation, camera work, menus, audio.

---

### Phase 4 — Trade, one neighbour, Heat

The economic game arrives.

- Routes as entities with multi-day round trips (`GDD` §5.1)
- `Market` system: local supply, price differentials
- One AI port running the same sim, including its own Unrest
- Raids and the `Heat` system
- Stances, including `Shadow` (§5.6)

**Gate: Heat and Unrest demonstrably fight each other in play** — a tester raises taxes
to fund escorts and feels the Unrest bill arrive. If the dilemma isn't felt, the design's
core tension isn't real yet, and more content won't add it.

**Not now:** more than one neighbour. Six to eight is the target, but the second one adds
nothing the first hasn't already proven.

---

### Phase 5 — Map and mob

- Positions, movement, a real map
- Rung 5 as a scene: **dozens** of mob agents, flow-field steering (`ARCHITECTURE` §6.4)
- Named crew choose sides individually by loyalty (`GDD` §5.4)

**Gate: the revolt reads as an event, not a number.** Measure performance before
optimising anything; dozens may simply be enough.

**Not now:** hundreds of agents. Scale only after the small version is proven fun.

---

### Phase 6 — Kill test

Minimum legibility work only — clarity, not beauty.

Run `GDD` §8.2: fifteen minutes, a real person, an unscripted event that makes them
react out loud. **Pass or kill.**

If it fails, the answer is not more features. Re-pitch or stop.

---

### Phase 7 — Only if Phase 6 passes

Art pass, neighbours 2–8, Workshop and remaining buildings, save/load UI, audio,
the vertical slice. Everything in `GDD` Appendix A stays parked.

---

## 3. Risk register

| Risk | Phase | Mitigation |
|---|---|---|
| **Cascade tuning doesn't feel right** | 1 | Front-loaded deliberately, headless, cheap to abandon |
| **Legibility failure** — good sim, player can't perceive it | 3–6 | See below. The sleeper risk |
| Mob performance | 5 | Start at dozens; measure before optimising |
| Framework-building instead of game-building | 0 | `ComponentStore` ≈150 lines; no ECS, no graph DB |
| Scope leak from Appendix A | all | Parked with gates, in writing |

### The legibility risk deserves its own paragraph

This is a systems game. **Emergence the player cannot perceive does not exist.** A
beautifully cascading simulation that reads as random noise fails the kill test exactly
as hard as a simulation that does nothing — and it fails in a way that looks like a
content problem, which tempts exactly the wrong response.

The event feed is therefore **not UI polish, it is the interface to the product**, and it
is why the provenance hook lands in Phase 0 rather than being deferred with the timeline
feature it was designed for. Budget real thought for "why did this happen?" long before
it looks like it needs any.

---

## 4. First session — concrete

1. `git init`, Unity project, empty scene, `.gitignore`
2. Create `Sim.asmdef` with no UnityEngine reference. Add a `using UnityEngine;`,
   confirm the compile error, delete it. **That error is the architecture working.**
3. Plain .NET test project referencing `Sim`
4. `EntityId`, `ComponentStore<T>`, and its tests
5. `Rng` with a fixed seed, and its test
6. Commit

That is a complete, useful, testable first sitting — and it ends with the guarantee
everything else depends on already under test.

---

## 5. What is deliberately absent

No calendar. Phase sizes are relative, not scheduled, because solo availability varies
and a missed date on a self-imposed schedule demoralises without informing. **The gates
are the schedule.** A phase is done when its gate passes, and not before.
