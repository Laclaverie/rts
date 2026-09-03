# Architecture Specification

**Status:** living document · created 2026-09-02 · companion to `GDD.md`
**Scope:** the technical contract. Design rationale lives in `GDD.md`.

---

## 1. Constraints

These are requirements, not preferences. Every decision below traces back to one, and
any future decision that violates one is a scope change to be argued explicitly.

| ID | Constraint |
|---|---|
| **C1** | Balancing lives in **one central place**. Never hunt through per-object assets to change a number. |
| **C2** | **Composition over inheritance** wherever possible. |
| **C3** | Clear **separation of concerns**. |
| **C4** | **Reusable modules** — except the glue, which is allowed to be project-specific. |
| **C5** | **Minimal Unity-specific types.** Defining our own equivalents is preferred. |
| **C6** | **Explicit control over which system runs in which order.** |
| **C7** | **Tests ship with the feature.** Unit tests now; functional tests once the core loop works. |
| **C8** | **Data-driven. Nothing hardcoded** that could be data. |
| **C9** | **Small dedicated functions.** No spaghetti, no god objects. |

### 1.1 Correction to `GDD.md` §6.4

That document specified ScriptableObjects for content. **Superseded by C1 + C5.**
ScriptableObjects are Unity types, scatter balance across one asset per entity, and
carry the asset-vs-instance ambiguity that C1 exists to prevent. Content and balance are
plain data files parsed into POCOs inside the sim. ScriptableObjects are permitted
**only** in the presentation layer, for binding art and prefabs to content IDs.

---

## 2. Layers

```
┌──────────────────────────────────────────────────────────┐
│ Composition Root  (glue — exempt from C4, not from C9)   │
└──────────────────────────────────────────────────────────┘
      │ wires everything below; nothing depends back on it
      ▼
┌───────────────┐  ┌───────────────┐  ┌────────────────────┐
│ Input         │  │ Presentation  │  │ Modes              │
│ → Commands    │  │ reads state   │  │ rules + scripts    │
└───────────────┘  └───────────────┘  └────────────────────┘
      │                   │ read-only          │
      ▼                   ▼                    ▼
┌──────────────────────────────────────────────────────────┐
│ Sim  — plain C#, zero UnityEngine reference               │
│   World · Systems · Pipeline · Commands · Events · RNG    │
└──────────────────────────────────────────────────────────┘
      ▲
┌──────────────────────────────────────────────────────────┐
│ Content — loaders, schema validation, typed registries    │
└──────────────────────────────────────────────────────────┘
```

**Assembly definitions enforce this, not discipline (C3, C5).** `Sim.asmdef` and
`Content.asmdef` have **no UnityEngine reference**; adding one is a compile error, which
is the point. The sim can be run headless from a console test runner with no Unity
process at all — that is what makes C7 cheap.

**Dependency rule:** arrows point one way. Presentation reads sim state and never writes
it. Input never touches world state; it produces commands (§6).

### 2.1 What is genuinely reusable (C4)

Portable to any future project, no game concepts inside: `ComponentStore<T>`,
`Pipeline`, `EventQueue`, `CommandDispatcher`, `ConfigRegistry` + loaders, `Rng`.
Keep these in an `Engine/` namespace with no reference to ports, crew, or goods.

Explicitly **not** reusable and fine that way: the Composition Root, the Unity scene
bootstrap, the presentation binders.

---

## 3. World and composition (C2)

Entities are IDs. Data lives in typed component stores. **No entity base class, no
inheritance hierarchy, no `Building : Entity`.**

```csharp
public readonly struct EntityId : IEquatable<EntityId>   // see note below
{
    public readonly int Value;
}

// Dense storage, insertion-ordered iteration (determinism — §7).
public sealed class ComponentStore<T> where T : struct
{
    public bool TryGet(EntityId id, out T value);
    public void  Add(EntityId id, in T value);   // throws if already present
    public ref T GetRef(EntityId id);            // update in place
    public bool  Remove(EntityId id);
    public ReadOnlySpan<T>        Values { get; }
    public ReadOnlySpan<EntityId> Ids    { get; }
}

public sealed class World
{
    public ComponentStore<Position>  Positions  { get; }
    public ComponentStore<Morale>    Morale     { get; }
    public ComponentStore<Loyalty>   Loyalty    { get; }
    public ComponentStore<Inventory> Inventory  { get; }
    public ComponentStore<Upkeep>    Upkeep     { get; }
    // …
}
```

> **Language version: C# 9.** Unity 6.3 compiles with `-langversion:9.0` — its own
> generated `Sim.csproj` says so. `record struct` is C# 10 and fails with `CS8773`, so
> value types here spell out `IEquatable<T>` and the equality operators by hand. Record
> *classes* are C# 9 and remain available where an allocation is acceptable. The shadow
> projects under `dotnet/` pin the same language version, so the constraint is enforced
> by the compiler in both places rather than remembered.

> **Attaching and updating are separate operations, and `Add` is strict.** Attaching a
> component twice almost always means two systems each believe they own it, so it throws
> rather than overwriting; updating an existing component is `GetRef`. An upsert — *ensure
> this component equals this value* — is the natural shape for idempotent command handlers
> and for recomputed derived components, and a `Set` can be added next to the first caller
> that genuinely needs one. Deliberately in that order: loosening this later breaks no
> existing code, whereas tightening it later would.

A crew member is whatever components it has. A building is whatever components it has.
Behaviour lives in systems, never on the data.

> **Do not build an ECS framework.** No archetypes, no query compiler, no DOTS. Unit
> counts are dozens of named agents plus a few hundred simple mob agents (`GDD` §6.4).
> `ComponentStore<T>` should be roughly 150 lines. Building a framework is the most
> likely way this project dies of engineering instead of shipping.

---

## 4. Systems and the pipeline (C6)

A system is a function over the world. Nothing more.

```csharp
public interface ISystem
{
    string Id { get; }
    void Run(World world, in Context ctx);
}

public readonly ref struct Context
{
    public readonly ConfigRegistry Config;   // all tuned numbers (§5)
    public readonly EventQueue     Events;   // emit-only
    public readonly Rng            Rng;      // seeded, deterministic
    public readonly int            Day;
    public readonly float          Dt;       // Tick phase only
}
```

### 4.1 Phases

Two phases, from the 20-minute day in `GDD` §5.1:

| Phase | Cadence | Contains |
|---|---|---|
| **Tick** | fixed step, real time | movement, steering, combat resolution, mob flow-fields |
| **DayBoundary** | once per in-game day | consumption, wages, upkeep, production, market, Heat, Unrest, revolution ladder, event rolls |

### 4.2 Order is data, not code

The pipeline is declared in a config file and loaded at startup. Reordering a system, or
disabling one to isolate a bug, is a **data edit and a relaunch — never a recompile.**

```csv
# Balance/pipeline.csv
phase,order,system,enabled
Tick,10,Movement,true
Tick,20,Combat,true
Tick,30,MobFlow,true
DayBoundary,10,Consumption,true
DayBoundary,20,Wages,true
DayBoundary,30,Upkeep,true
DayBoundary,40,Production,true
DayBoundary,50,Market,true
DayBoundary,60,Heat,true
DayBoundary,70,Unrest,true
DayBoundary,80,RevolutionLadder,true
DayBoundary,90,EventRoll,true
```

**Phases are a closed set, and deliberately not extensible by data.** A mod (§5.5) is data,
so it can insert a system into an existing phase — one row, pick an order — but it cannot
add a phase. Nothing is iterating a list of phases: `Tick` and `DayBoundary` are called from
two different places on two different cadences, so a third phase would need code deciding
*when* to call it. Making the column free-form would let a file name a phase nothing ever
runs, which is the silent-omission failure this section exists to prevent. A new cadence is
a scheduler feature, not a config edit.

**Order is load-bearing design, not an implementation detail.** `Wages` must run before
`Unrest` so that an unpaid wage feeds grievance on the same day rather than the next
(`GDD` §5.2.3). Ordering decisions of that kind belong in this file with a comment,
where a designer can see and change them.

The loader fails loudly on a system ID that no code implements, and on any implemented
system missing from the file. **Silent omission is the failure mode to prevent** — a
system that quietly never runs is close to undebuggable.

---

## 5. Data and balance (C1, C8)

### 5.1 The rule

> **One place. Numbers live in `Balance/`. Code contains no tuned constants.**

If a value could plausibly be tweaked during balancing, it is data. Anything that
survives in code as a literal number needs a comment justifying why it is structural
rather than tuned.

### 5.2 Layout

```
Content/
  Balance/                  # ← the one place a designer opens
    buildings.csv           # id, upkeep_coin, build_timber, build_iron, capacity, …
    goods.csv               # id, base_price, volatility, heat_per_unit, …
    crew_roles.csv          # id, wage, work_rate, …
    traits.csv
    unrest.csv              # rung thresholds, decay rates, repression costs
    heat.csv
    events.csv              # id, weight_formula, cooldown, …
    pipeline.csv            # system order (§4.2)
    tuning.csv              # loose global scalars: day length, speed multipliers, …
  Definitions/              # structural, rarely touched: what exists and how it links
    ports.json
    event_scripts/
```

> **Where `Balance/` physically lives: `Assets/StreamingAssets/Balance/`.** `Content` has no
> `UnityEngine` reference (C5, §2), so it cannot read a `TextAsset` and cannot ask Unity for
> a path. StreamingAssets is the one Unity location that survives into a build as ordinary
> files on disk, readable with `System.IO`. The composition root — which is Unity-side and
> may use `Application.streamingAssetsPath` — passes the directory in; loaders take a path
> or a stream and stay ignorant of Unity. Tests read the same shipped files directly.

**Tables are CSV because balancing is spreadsheet work.** They open in Excel/LibreOffice,
sort and chart naturally, and diff cleanly in git — three things JSON does badly and
which matter more than nesting. The handful of genuinely nested structures (event
definitions, port graphs) stay JSON. Two formats, each where it is strongest.

### 5.3 Loading and validation

CSV → typed POCO registries at startup. **Validation runs at load and fails loudly**,
because a designer typo must not become a mysterious runtime behaviour:

- every referenced ID resolves
- required columns present, types parse
- range assertions (no negative upkeep, weights ≥ 0, thresholds monotonic)
- every good has at least one producer and one consumer
- every system in `pipeline.csv` exists, and vice versa

These validations are the cheapest tests in the project and catch the most common class
of error. Treat them as part of the loader, not as an optional lint.

### 5.4 Hot reload

In-editor, re-reading `Balance/` and rebuilding registries **without restarting the
session** is a large multiplier on balancing throughput at a 20-minute day. Registries
are therefore immutable-and-swapped rather than mutated in place. Worth building early.

### 5.5 Mods and modes

A mode or mod is a folder of the same shape whose files override the base by ID. Same
loader, same validation. Mod support is a consequence of C1 + C8, not extra work.

---

## 6. Commands — the only way in

Player input, AI, and campaign scripts all enter the sim identically.

```csharp
// Commands are DATA, so they serialise → save, replay, and functional tests are free.
// Record *classes* (C# 9) rather than record structs (C# 10) — see the language
// note in section 3. Commands are input-rate, so the allocation is irrelevant and
// the free value equality and ToString() are worth having in the command log.
public sealed record SetStance(EntityId Port, Stance Stance);
public sealed record AssignCrew(EntityId Crew, JobId Job);
public sealed record SuppressRiot(EntityId District, Harshness Harshness);
```

Rules:

- Commands are **queued**, drained at a defined pipeline position, never applied mid-system
- A command is validated then applied by its handler; handlers are small (C9)
- **Nothing else mutates the world.** Presentation and input never write

This is what makes the campaign layer safe: a script issuing `SuppressRiot` is
indistinguishable from a player doing it, so scripted and emergent content interleave
without a second code path (`GDD` §6.3).

### 6.1 Replay and saves — one mechanism

**Decided.** A save is not a serialised world. It is:

```
save = { seed, content_hash, game_version, command_log[] }
```

Loading means replaying. Saving means flushing the log. There is no second
serialisation path to write, keep in sync, or debug — and because the same machinery
backs functional tests (§8.2), the save system is exercised by every test run rather
than only when a player loads a game.

#### Snapshots are a cache, never the truth

Replaying 40 in-game days from scratch is slow even headless. So: snapshot the world
every N days and replay from the nearest one.

**The distinction matters.** Snapshots are a derived performance cache and may be
discarded at any time; the log remains the single source of truth. If a snapshot ever
becomes authoritative, both problems this design avoids come straight back.

#### Two real costs, stated plainly

1. **Determinism stops being a testing concern and becomes save corruption.** Every rule
   in §7.1 is now load-bearing for players, not just for CI. One stray `DateTime.Now` in
   `Sim` and saves silently load into a divergent world. Mitigation: a replay-determinism
   test in CI — replay a stored log twice, assert identical end state — so a violation
   fails the build rather than a player's save.

2. **Changing a system or a balance number invalidates existing logs.** A v0.3 log
   replayed under v0.4 rules diverges, by definition. The honest positions:
   - **Pre-1.0: accept save-breaking.** Stamp `content_hash` + `game_version`, refuse to
     load a mismatch with a clear message, and move on. Solo project in flux — a save
     migration system now would be effort spent on a game that may not exist.
   - **Post-1.0:** the snapshot cache becomes the compatibility path — old saves load
     from their snapshot even when replay would diverge.

   This is also why hot-reloading `Balance/` (§5.4) must mark the session
   **replay-dirty**: the numbers changed mid-log, so the log no longer reproduces. Warn,
   don't silently produce a broken save.

#### What it unlocks beyond saving

- **Bug reports become tiny and exact.** A playtester sends a seed, a hash, and a log —
  a few KB — and you reproduce their session exactly. For a solo dev with a handful of
  testers, this is worth more than most tooling you could build.
- **The regression corpus writes itself.** Record real playtest sessions, keep the
  interesting ones, replay them in CI (§8.2). Test coverage grows as a by-product of
  people playing.
- **Counterfactuals.** Replay to snapshot N, change one command, run forward. *"What if
  I had suppressed the riot instead of paying them?"* — a genuine balancing instrument
  for a game built on cascading consequences (`GDD` §5.2.3).
- **Chronicle (parked).** The game's product is emergent stories; a replayable, shareable
  account of a port's rise and fall falls out of this almost for free. Not MVP — but
  worth not designing it out.

### 6.2 Provenance — build the hook now, ship the feature later

The Decision Timeline (`GDD` Appendix A) wants to show *"you raised tariffs → trust fell
→ the merchants agitated → rung 3"* and let the player branch from that decision. The
feature is post-MVP. **One piece of it is not deferrable.**

Branching is nearly free (§6.1). Causal explanation is not: it requires knowing *why*
each change happened, and that knowledge only exists at the moment a system acts.
**Reconstructing it afterwards is impossible; retrofitting it means editing every
system.** So the hook goes in now, even though nothing consumes it for months.

#### The hook

Every event carries the cause that produced it:

```csharp
public readonly record struct CauseId(int Value);   // a command, or a prior event

public readonly record struct Envelope<T>(EventId Id, CauseId Cause, int Day, T Payload);
```

`Context` (§4) carries a `CurrentCause`, set by the command dispatcher when it applies a
command and by the event drain when a subscriber reacts. **`ctx.Events.Emit(payload)`
stamps it automatically** — systems cannot forget, because there is nothing to remember.
Make the correct thing the only thing (C9).

The result is an append-only causal DAG alongside the command log. It can be disabled
or discarded without affecting simulation behaviour.

#### Cost

One field on the envelope, one slot on `Context`, and the emit signature. Effectively
nothing today. Without it, the timeline feature later is a rewrite rather than a
feature — which is the entire reason this section exists at pre-MVP stage.

#### Naturally reused

The same DAG feeds the player-facing event feed (`GDD` §5.1 — *"read the world"*), the
Chronicle (§6.1), and debugging: *"why did this port starve?"* becomes a query rather
than a print-statement session.

### 6.3 The two graphs

The Decision Timeline is graph-shaped, and it is **two graphs at two scales**. Keeping
them distinct is what makes the feature buildable.

| | **Branch tree** | **Causal DAG** |
|---|---|---|
| Root | the seed | a command |
| Node | a decision point | an event |
| Edge | *"the player chose X here"* | *"this caused that"* |
| Size | tens per session | **thousands** per session |
| Shape | a tree — branches diverge, never rejoin | a DAG |
| Shown | **directly, always** | **never raw — aggregated first** |

The player navigates the **branch tree**; selecting a node expands the *aggregated*
causal explanation behind it. One is the map, the other is what happened at a location.

> **Do not render the raw causal DAG.** Thousands of nodes is an unreadable hairball —
> the classic failure mode of graph visualisation. The value is in the summary, not the
> completeness.

#### Aggregation is the real work

Rendering a graph is easy; turning thousands of causal edges into *"tariffs → trust −20
→ merchants agitate → rung 3"* is the actual feature. It is a summarisation pass over
the DAG, and its rules belong in `Balance/` (C1, C8):

- drop edges whose state delta is below a significance threshold
- collapse a chain running through one system into a single edge
- group by day
- surface the top-N contributing paths into an outcome, not all of them

Budget for this as the bulk of the work. The graph rendering is the easy half.

#### Multi-cause refinement

`CauseId` (§6.2) is a single parent, which yields a tree. Real cascades are often
multi-cause — Unrest rises from unpaid wages *and* hunger *and* a recent repression.
Aggregator systems (`Unrest`, `Heat`, `Market`) are exactly where this matters:

```csharp
public readonly record struct Envelope<T>(
    EventId Id, CauseId Cause, CauseId[]? Contributors, int Day, T Payload);
```

Primary cause is always present and cheap; `Contributors` is optional and attached only
by aggregators. Accurate where accuracy matters, free everywhere else.

#### Fork points and snapshot points should coincide

§6.1 proposed snapshotting every N days. Better: **snapshot at every decision node.**
Those are precisely the points a player can rewind to, so branching becomes instant
instead of a replay, and the snapshot cadence follows meaningful play rather than an
arbitrary interval. Keep an N-day fallback for long stretches without decisions.

A branch stores only `(parent_branch, fork_index, divergent_command_suffix)` — the
shared prefix is never duplicated.

#### No graph database

The mental model is Neo4j; **the dependency must not be.** In-memory adjacency lists
serialised beside the command log. A few thousand nodes is trivial for plain C#
structures, and a database dependency in a solo Unity project is the
framework-instead-of-game trap in its purest form (§3).

---

## 7. Events vs systems — the distinction that keeps ordering honest

Both exist and they are **not** interchangeable:

| | Systems (§4) | Events |
|---|---|---|
| Purpose | **decide and mutate** | **report what was decided** |
| Order | explicit, from data | irrelevant by construction |
| May mutate world? | yes | **no** |

> **Subscribers may not mutate the world. They may only enqueue commands.**

This single rule is what prevents the event bus from becoming implicit, unordered
control flow — the exact failure C6 exists to avoid. Events are drained at defined phase
boundaries, never delivered re-entrantly mid-system.

Consumers: presentation (animation, notifications), the event log / player feed, and
campaign scripts (`GDD` §6.3).

### 7.1 Determinism rules

Non-negotiable, because replay-based functional testing (§8.2) depends on them:

- **No `UnityEngine.Random`.** One seeded `Rng` per world, seed saved with the game
- **No `DateTime.Now`**, no wall-clock, anywhere in `Sim`
- **No static mutable state.** It breaks determinism and parallel test runs at once
- Iteration that affects state must be over ordered collections — never raw `Dictionary`
- Fixed `Dt` in the Tick phase; never frame-time
- **No coroutines and no `async`/`await` inside `Sim`** — see §7.2

### 7.2 No coroutines in the sim

> **All sim state lives in `World`.**

C# coroutines (`IEnumerator` + `yield`) and `async`/`await` both park state in a
compiler-generated state machine on the heap — *outside* `World`. Three consequences,
the last being decisive:

1. Unity coroutines require a `MonoBehaviour` (violates C5)
2. `WaitForSeconds` is frame-driven and `async` brings thread-pool scheduling, breaking
   determinism (§7.1) and explicit ordering (C6)
3. **A suspended state machine cannot be snapshotted.** Replay from the seed would
   reconstruct it, so the command log alone survives — but snapshots (§6.1) do not, and
   snapshots are what make loads fast and fork points instant (§6.3). Hidden state is
   only acceptable in a design that never snapshots. This one does.

**Model processes as data instead.** The pattern is needed constantly — voyages,
construction, the revolution ladder — so express it explicitly:

```csharp
public readonly record struct Voyage(EntityId Route, VoyagePhase Phase, int DaysRemaining);
```

…advanced by a system at the day boundary. Serializable, inspectable, orderable in
`pipeline.csv`, and visible to the causal DAG. The implicit state machine becomes an
explicit one, which is the whole point.

The same rule governs campaign scripting: *"wait 3 days, then act"* is a data-driven
sequence with explicit state, never a coroutine.

**Permitted freely in `Presentation` and I/O** — animation, camera moves, UI transitions,
async loading of `Balance/`, save writes. None of it is authoritative or snapshotted.

---

## 8. Testing (C7)

**Definition of Done for a feature: the tests ship with it.** Not a follow-up ticket.

Because `Sim` has no Unity dependency, tests run in plain .NET in milliseconds, outside
the editor. This is the practical payoff of §2 and the reason C7 is affordable.

### 8.1 Unit tests — now

Per system, table-driven: given a world state and a config, running the system produces
an expected state. Systems being pure functions over state (C9) makes these small.

Priority targets: `Upkeep` cascade arithmetic, `Unrest` rung transitions, `Market`
pricing, `Heat` accumulation.

### 8.2 Functional tests — once the loop runs

Nearly free given §6 + §7.1: a test is **a seed plus a command log**. Replay it, assert
on the end state. Uses:

- **Regression:** a saved log of a real session must still reach the same state
- **Invariants over long runs:** no negative stock, no orphaned entity, no stuck rung
- **Design assertions**, which is the interesting part: *"a single bad event never
  causes collapse"* (`GDD` §5.2.3) is a testable property. Generate single-shock
  scenarios, assert recovery. The design rule becomes an executable check.

### 8.3 Balance validation

§5.3 runs in CI as a test over the shipped `Balance/` folder.

---

### 8.4 World inspector — the component graph (parked)

A dynamically built graph of stores, components and the entities that carry them, for
runtime debugging and for documentation. Parked; no gate, because nothing depends on it.
Recorded here because **one half of it is free and the other half gets more expensive with
every system written.**

**The instance graph — free, build whenever.** What exists right now: which entities carry
which components. `World` already holds its stores in registration order and each store
exposes `Ids`/`Values` in insertion order, so a snapshot needs no new bookkeeping, no hook,
and no change to `Sim`. Most useful grouped by *component set* rather than per entity —
*"three entities have `{Position, Upkeep}` and no `Inventory`"* finds a missing-component
bug far faster than a thousand-line entity dump.

**The schema graph — needs a decision, not code.** Which systems *read* and which *write*
each component. This is the half with teeth: it documents the sim, and it can check
`pipeline.csv`, because two systems writing the same component in the same phase is exactly
the ordering hazard §4.2 exists to prevent, and a schema graph finds it mechanically rather
than by playtest.

It needs systems to declare their reads and writes. That is the §6.2 shape — cheap per
system, tedious to retrofit across forty — but unlike `CauseId` it is **not undeferrable**:
declarations can be added system by system. The decision wants making before the system
count gets high, not before the first one.

The failure mode to avoid: **declarations that drift from the code, which lie**, and a lie
in documentation is worse than a blank page. Replay determinism offers a cheap check —
run the corpus, observe which stores each system actually touched, diff against what it
declared, fail CI on a mismatch (§8.2).

Two constraints on whatever gets built:

- **It lives outside `Sim`.** A debug tool is presentation; `Sim` gains nothing and C5 says
  so.
- **No instrumentation in the hot path.** Do not have `ComponentStore` record accesses to
  feed a graph. Observation belongs in the replay harness, where it costs nothing at
  runtime and cannot perturb determinism.

> This is a tool, not sim infrastructure. `BUILD_ORDER` §3 lists framework-building as a
> failure mode with a target of zero, and an entity inspector is precisely the kind of
> thing that grows into an ECS framework if allowed to.

---

## 9. Code conventions (C9)

- Systems are functions over state. **No god objects, no manager singletons.**
- Prefer pure functions; isolate mutation at the edges of a system
- **No static mutable state**, ever (§7.1)
- One system, one concern. A system that needs a section header inside it is two systems
- Struct components, readonly where possible
- Fail loudly on data errors; never silently default a missing value
- Naming matches the GDD: a system implementing §5.2.2 is `RevolutionLadderSystem`, so
  design and code stay searchable against each other

---

## 10. Open decisions

| # | Decision | Recommendation |
|---|---|---|
| ~~1~~ | ~~Save format~~ | **Resolved: command log + seed. See §6.1.** |
| 2 | CSV parsing | Hand-rolled reader (~100 lines, no dependency) over a library; the schema is ours and validation is custom anyway. |
| 3 | Test runner | Plain NUnit/xUnit project outside Unity, so tests run on a keystroke rather than an editor round-trip. |
| 4 | Mob agent budget | Start at dozens (`GDD` §8.1). Measure before flow-field optimisation. |
| 5 | Presentation binding | Content ID → prefab lookup table. The one place ScriptableObjects are acceptable (§1.1). |
| 6 | System read/write declarations | Defer, but decide before the system count is high. Enables the schema graph and mechanical `pipeline.csv` checking (§8.4); the cost is per-system and the risk is drift. |
