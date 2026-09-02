# RTS *(working title)*

A single-player, age-of-sail strategy game about running one port town — where every
coin of wealth physically travels a route someone can steal, and your crew are
individuals with their own needs, opinions and limits.

**Status: pre-MVP.** No Unity project yet. The design and architecture are specified; the
next step is Phase 0. Nothing here has passed its kill test, and the project is
explicitly allowed to fail it.

---

## The idea in three lines

- **Routes are real.** No abstract income tick. Wealth is cargo, cargo moves, cargo can be taken.
- **Stances, not alliances.** Nobody is permanently friend or enemy. Postures drift from what you actually do.
- **Your crew has opinions.** Units are people with morale, loyalty and traits. They interpret orders. They desert.

It is **casual** — active pause means depth never demands reaction speed — and
**emergent**: stories come from systems interacting, not from scripted content. The
flagship example is revolution, which is a state machine fed by your own economy rather
than a wave or a timer.

---

## Documentation

| Document | Answers |
|---|---|
| [`doc/GDD.md`](doc/GDD.md) | What the game is. Pillars, the emergence model, Heat/Unrest/Upkeep, the revolution ladder, modes, MVP and kill criteria. |
| [`doc/ARCHITECTURE.md`](doc/ARCHITECTURE.md) | How it is built. Deterministic Unity-independent sim, ordered system pipeline, commands as data, event provenance, centralised balance. |
| [`doc/BUILD_ORDER.md`](doc/BUILD_ORDER.md) | What to build, in what order, and the gate that ends each phase. |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Workflow, conventions, and the rules that are easy to break by accident. |

Start with the GDD. `ARCHITECTURE.md` §1 lists the nine engineering constraints
everything else follows from.

---

## Technical shape

- **Unity** (current LTS) + URP, low-poly.
- **The simulation is plain C# with no UnityEngine dependency**, enforced by assembly
  definitions. It runs headless in a console test runner. This is the single most
  important structural decision in the project — it makes most of the game buildable and
  tunable without a renderer.
- **Deterministic and tick-based.** A save is a seed plus a command log; loading is
  replaying. Functional tests are the same mechanism.
- **Data-driven.** All tuned numbers live in `Content/Balance/` as CSV. Code contains no
  balance constants.

---

## Layout

```
doc/            design, architecture and build-order specs
Assets/         Unity project                          (Phase 0)
  Sim/          the game — plain C#, no UnityEngine    (Phase 0)
  Content/      loaders, validation, Balance/ tables   (Phase 1)
  Presentation/ rendering, reads sim state only        (Phase 3)
Tests/          headless sim tests                     (Phase 0)
```

---

## Building and running

Nothing to build yet. Phase 0 delivers the sim skeleton and the test project; this
section gets filled in then.

---

## History

This supersedes a 2021 team-era design document written for a four-person student
project that did not ship. The original export is kept out of version control; its
contents were rewritten, corrected and cut down in `doc/GDD.md`.

## License

None yet. Private repository, all rights reserved.
