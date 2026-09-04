# Game Design Document — *(working title: RTS Port)*

**Status:** living document · rewritten 2026-09-02 · supersedes `GDD_RTS.odt` (2021, team-era)
**Team:** solo
**Engine:** Unity (see §7)

---

## 1. One-line pitch

You run a single port town in an age-of-sail world where every coin of wealth
physically travels a route that someone can steal — and your crew are individuals
with their own needs, opinions, and limits.

---

## 2. Design pillars

Three. Everything in this document must serve one of them, or it gets cut.

### P1 — Routes are real
No abstract income tick. Wealth is cargo, cargo moves along a route on the map,
and anything on the map can be intercepted. Prosperity is therefore *exposed* by
construction. This is the source of most tension in the game.

### P2 — Stances, not alliances
Nobody is permanently friend or enemy. You hold a posture toward each neighbour;
they hold one toward you; postures drift based on what you actually do, not on a
diplomacy menu. Covert action is possible and can be discovered.

### P3 — Your crew has opinions
Units are people, not counters. They have needs, morale, loyalty and traits. They
*interpret* orders rather than executing them perfectly. They gossip, they slack,
they desert. This is where most stories come from.

---

## 3. The emergence model (read this before anything else)

The 2021 draft wanted "casual audience" and "complex mechanics" and used
StarCraft II as its UI reference. Those don't combine. The resolution:

> **Emergence comes from the simulation, not from the player's hands.**

### 3.1 Three sources of emergence

Emergence in an RTS can come from three different places. They are not
interchangeable, and they cost wildly different amounts to build.

| Source | Exemplar | Casual-compatible? | Solo-dev cost |
|---|---|---|---|
| **Player skill under time pressure** — APM, control groups, micro | StarCraft II | No — this *is* the hardcore axis | Low |
| **Spatial / physical simulation** — crowds, chokepoints, walls, siege, terrain | *Diplomacy is Not an Option* | Yes, with pause | **Tech-expensive**: crowd pathfinding, mass combat, heavy optimisation |
| **Agent / economy simulation** — needs, morale, prices, loyalty, reputation | RimWorld, Mount & Blade | Yes, with pause | **Design-expensive**: systems design and legibility UI |

Source 1 is rejected outright. **Sources 2 and 3 both work** — *DINO* is proof that
casual, emergent and RTS reconcile at high unit counts, and it does so with anonymous
units and no crew psychology at all. Its stories are tactical ("the west wall held for
nine seconds"); source 3's are personal ("Renaud deserted and took the route map").

**This project bets on source 3**, for one reason: solo budget. Source 2 spends its
money on engine performance; source 3 spends it on thinking. A secondary reason —
wave-escalation structures tend toward run-to-run similarity, and non-repetition is an
explicit goal here.

**Hybrid (open, and likely correct):** small named crew under player command, large
anonymous masses as the *threat* — raider hordes, mobs, hostile colonists. Personal
stories on your side, spectacle on theirs, and crowd tech is paid for only on the enemy
side, at hundreds rather than thousands. This is the Mount & Blade split. See
Appendix B.

### 3.2 Pause is the casual mechanism

**Active pause, always available, one key, orders issuable while paused.** Not a
convenience feature — the load-bearing one.

Pause decouples *decision complexity* from *reaction speed*, which is the whole
casual/complex reconciliation. A game can be arbitrarily deep and still be casual if
the player is never required to think fast. This is how *DINO* stays approachable while
thousands of units fight, and how real-time-with-pause CRPGs work.

Corollary: **low unit count is not what makes this game casual — pause is.** Unit count
is low to serve P3 (crew must be individually legible), which is a different argument
and must be defended on its own terms.

### 3.3 Practical consequences

All load-bearing:

| Consequence | Why |
|---|---|
| Active pause, prominent and instant | Decision complexity without reaction-speed demands (§3.2) |
| Low count of *named* crew (~10–30) | Legibility for P3; 200 units can't have personalities. Anonymous masses may be far larger |
| Orders are *standing*, not *continuous* | Player sets intent and stance, then watches; no sustained input required |
| Units have autonomy and can fail | An order that always executes perfectly produces no stories |
| Systems must feed each other | An event is only interesting if it propagates: raid → shortage → morale → desertion |

**Reference points:** Mount & Blade (campaign layer), RimWorld, Kenshi's factions,
Port Royale, *Diplomacy is Not an Option* (for pause and for crowd-as-threat).
**Not** StarCraft, not Age of Empires.

### The "so what" test
Every system added must answer: *what other system does this push on?* A system that
only feeds a number the player reads is decoration. Cut it.

---

## 4. World & setting

Age of sail, low-poly, lightly fictional. Human-dominated, grounded — no magic in the
base game (see Appendix A for where the 2021 fantasy/sci-fi material went).

**The setting is deliberately underwritten for now.** Backstory, named nations and
history come *after* the loop is proven fun. What matters at this stage is that the
setting is an emergence engine: routes, cargo, ports, raiders, reputation, weather and
seasons are all natural interaction surfaces between systems. A generic land-based
medieval setting offers far fewer, which is why this framing is kept.

Art direction: low-poly, Synty POLYGON Pirate as the base pack. Royalty-free audio.

---

## 5. Core gameplay

### 5.1 The loop

**Moment to moment (the casual verb set):**
1. Read the world — an event feed reports what happened and what's brewing
2. Decide — assign crew, adjust a route, change a stance, commit resources
3. Commit and watch — consequences arrive over minutes, not seconds
4. Absorb the fallout — which is usually the next decision

**Session arc (persistent — no match reset):**
- **Early** — subsistence. One route. Every crew member matters individually.
- **Mid** — 3–5 routes. First raid lands on you. First real stance decision toward a neighbour.
- **Late** — you're the richest port on the coast, and that is now your main problem.

**Time.** One in-game day ≈ **20 minutes** of real time. The day is the decision unit,
and several things follow from that:

- **The day boundary is the economic pulse.** Wages paid, food eaten, upkeep charged,
  prices moved, event rolls made — all resolve at dawn, not continuously. Legible for
  the player, and a clean anchor for the sim (§6.4).
- **Route round trips are measured in days** (1–3), so committing a route is a real
  commitment rather than a toggle.
- **Revolution rungs advance on day boundaries** (§5.2.2), never on seconds. The player
  always has a day to respond to a rung — that is what makes the ladder fair.
- **Speed controls (×1 / ×2 / ×4) are a requirement, not a nicety.** At 20 minutes a
  day, a quiet stretch must be skippable or the pacing reads as dead air. Pause (§3.2)
  handles complexity; speed handles boredom. Both are needed.

### 5.2 Pressure: Heat and Unrest

Two pressure systems, deliberately opposed. **Heat** comes from outside and is driven by
your wealth. **Unrest** comes from inside and is driven by how you got it. Neither is a
wave and neither is scripted — both are read off world state.

The pair is the design's central dilemma, because **their counters fight each other**:

| To reduce… | You… | Which raises… |
|---|---|---|
| Heat | fortify, hire guards, escort convoys, raise taxes to pay for it | Unrest |
| Unrest | feed, pay, distribute, ease up on repression | Heat (visible prosperity, thinner defence) |

There is no stable equilibrium, only management. That's the game.

#### 5.2.1 Heat — external pressure

The 2021 draft proposed bonuses for weak cities and penalties for strong ones. That
punishes playing well and reads as arbitrary. **Replaced by Heat**, which is the same
pressure made diegetic:

- Wealth is visible: fat convoys, full warehouses, a busy dock
- Visible wealth raises **Heat**
- Heat raises raider interest, rival attention, and price-gouging from suppliers

Crucially, Heat is **counterable and readable**: escort convoys, split cargo across
routes, run decoys, pay protection, bribe a rival, or simply keep a lower profile and
grow slower. The player is never punished for succeeding — they are given a
*consequence to manage*. Same anti-snowball function, no feel-bad.

#### 5.2.2 Unrest and revolution — internal pressure

**Revolution is the flagship emergent event and the showcase of the whole design.**
It is explicitly *not* a wave, not a timer, and not scripted. It is a state machine fed
by the economy and by the player's own choices, which is why it can't be memorised or
farmed the way escalating waves can.

**Strata.** Population is not one number. Three groups, each with its own grievance, each
angered by what happens to *its own people* rather than by a shared tally:

| Stratum | Wants | Grievance driven by |
|---|---|---|
| **Commoners** | food, work, safety | hunger, unemployment, taxes, casualties, repression |
| **Named crew** | pay, rest, respect | wages, morale, losses, orders they disagree with |
| **Merchants** | open routes, low tax | tariffs, blockades, lost convoys, seizures |

**Commoners are the port's labour and the port's mouths.** They are a count rather than
entities — the mob of rung 5 is "hundreds of anonymous bodies with a handful of named faces
inside it", and the named faces are the crew. They fill the `staff` a building asks for,
nobody places them by hand, and whoever the port has no work for is unemployed and resents
it. They eat from the same store the crew do, and after sustained starvation they leave.

**Named crew are specialists, not labour.** A crew member assigned to a building improves
what the hands there achieve; a building nobody works produces nothing however skilled the
person standing in it. That keeps `AssignCrew` a decision about where expertise is worth
most rather than a way of manning things, and it is why losing crew and losing commoners are
two different kinds of disaster.

*This was learned the hard way.* Until the Phase 2 gate, only crew were modelled: every
pressure was a count of crew, so Commoners and Merchants were three weightings of the same
events. Rob a port and its crew deserted by day twelve, grievance lost its source, and the
ladder walked back down to Calm on a ruin with nobody in it — the flagship system reporting
that all was well, and Deposition unreachable from play. Populations of their own are what
make the three groups three groups.

**The ladder.** Escalation is a ladder, not a spawn table. Every rung is visible, and
every rung has an exit:

1. **Grumbling** — rumours surface in the Tavern (§5.5 — the information building earns
   its place here)
2. **Slowdown** — work is done late, badly, or not at all
3. **Agitator** — a named NPC emerges with a specific, stated demand
4. **Riot** — localised, property damage, warehouses are a target
5. **Uprising** — a mob. Named crew choose sides *individually*, by loyalty
6. **Deposition** — failure state

**Every rung is held before the next is earned.** `days_to_climb` in `ladder.csv` says how
long the rung below must stand before the port moves up, and the higher rungs ask for
longer. Without it the ladder failed its own gate: grievance saturates in a day and decays
in fortieths, so a port pinned at 1.00 climbed every single day and one that reached Riot
was deposed three days later whatever the player did. The exits below only exist if there
is time to reach them. Falling is not paced — a port whose cause is fixed comes down as
soon as the numbers say so.

**Repression is available and honest about its cost.** Crushing a riot lowers Unrest
immediately, raises baseline grievance permanently, and costs loyalty with every crew
member who disagreed. A viable strategy, not a free one.

What it actually buys is a *window*, not a subtraction. Grievance is capped, and a rioting
port is already at the cap, so the relief alone was undone by the next day's hunger —
measured at twelve days to leave a riot whether or not force was used, which made the
permanent floor a pure loss. `cowed_days` is the number of days the day's pressures land on
nobody: people are still hungry and still unpaid, and they say nothing. That window is time
to fix the cause. A player who spends it on nothing has bought a worse port for no reason,
which is the honest version of the trade.

**Anger fades faster when the port is visibly working.** Two rates, not one: `decay_per_day`
for a day that merely was not worse, and `relief_per_day` — roughly triple — for a day this
stratum had nothing at all to resent. Asked per stratum, so one group's complaint cannot
deny another its recovery. Without the second rate, fixing the economy was not a lever: it
took twenty-five clear days to unwind a saturated grievance, which the ladder outran, and
repression became the only exit rather than one option among them.

**Where the economic exit runs out.** A rioting port produces 35% of its output, so it cannot
feed itself out of a riot: two farms at six a day become four against a demand of thirteen.
Coin buys food from a passing merchant at four times what the port sells it for, which means a
rich port *can* import its way through, slowly and expensively, while a thin one cannot. That
is the point at which repression stops being one option among several and becomes the obvious
one — the lower rungs are fixed by good management, the upper ones are paid for, in coin or in
loyalty.

**Rung 5 is where the bounded crowd tech is spent** (§6.4) — and it is the *only* place
it is spent. A mob is hundreds of anonymous bodies with a handful of named faces inside
it. That bound is what keeps the tech budget honest.

**Neighbour ports revolt too.** The same system runs for AI ports, which turns their
internal crises into your opportunities: buy cheap, poach their crew, or back the
rebels covertly under a *Shadow* stance (§5.6). One system, applied uniformly, generates
both the player's crisis and the world's opportunities. This is the single highest
payoff-per-line system in the document.

#### 5.2.3 Upkeep — the ratchet that turns shocks into cascades

Heat and Unrest are the pressures. **Upkeep is what makes them dangerous.**

Every building carries maintenance and every crew member carries wages, charged at each
day boundary. Growth therefore raises your *fixed* costs permanently while income stays
variable. That asymmetry is the whole failure model.

**The governing rule:**

> **One bad event is absorbed. Correlated bad events cascade.**

A single shock is paid for out of reserves and recovered from — always. Collapse is
never the result of one roll, which is what keeps it fair. But shocks compound, and the
compounding is mechanical rather than authored:

```
reserves exhausted → wages unpaid → morale + loyalty fall
  → desertion and Unrest rise (§5.2.2)
    → production falls → income falls
      → upkeep unpayable → buildings decay
        → capacity falls → income falls further ⟳
```

**Reserves are therefore the real resource,** and maintaining slack is the actual skill
the game rewards. That is a strategic skill, not a reaction-speed one — exactly the
casual/deep target (§3.2).

**A spiral without exits is just punishment, so the exits are explicit:**

| Exit | Cost |
|---|---|
| Demolish or mothball buildings | Permanent capacity loss; sunk investment gone |
| Abandon routes | Income loss now, market position lost to a rival |
| Default on wages | Immediate Unrest and loyalty damage (§5.2.2) |
| Dump cargo at a bad price | Terrible rates; merchants remember |
| Borrow | A creditor is a new pressure with its own demands |
| Appeal to a neighbour | Trust spent, Fear lost, obligations incurred |

**Deliberate downsizing must be a viable, respected strategy** — cutting your port down
to survive a bad season should feel like competent play, not like losing slowly. If
playtesting shows players never voluntarily shrink, the exit costs are tuned wrong.

### 5.3 Resources & goods

Deliberately small. Four commodities, one currency, one morale good.

| Resource | Source | Sink |
|---|---|---|
| **Food** | farms, fishing, import | crew upkeep, morale floor |
| **Timber** | forest, import | construction, ship repair |
| **Iron** | mine, import | tools, weapons, fortification |
| **Spice** | import only (distant ports) | pure trade good — high value, high Heat |
| **Rum** | workshop, import | morale, recruiting, bribes |
| **Coin** | trade | wages, contracts, bribes, buyouts |

**Trade only works because ports differ.** Each port produces some goods and demands
others; prices move with local supply. Finding and protecting a profitable differential
*is* the economic game — a decision, not an execution challenge. Exactly the casual/
emergent shape wanted.

### 5.4 Crew

Every unit is a named individual with:

- **Role** — Laborer, Sailor, Guard, Agent (MVP set)
- **Skill** — improves with use
- **Morale** — driven by food, rum, rest, recent events, and losses
- **Loyalty** — to you specifically; separate from morale
- **Traits** — 1–2 each: Greedy, Brave, Superstitious, Drunkard, Loyal, Grudge-holder…

Low morale or loyalty means orders are executed slowly, badly, or not at all;
at the floor, crew desert — sometimes to a neighbour, with what they know.
This is the primary story generator, and it is why named-unit count stays low.

**In a revolt, named crew choose sides individually** (§5.2.2, rung 5), scored on
loyalty rather than on a faction flag. This is the payoff moment for the entire crew
system: a mob is anonymous, but the guard captain standing in it is not.

### 5.5 Buildings

MVP set, five (Workshop is the first post-MVP addition):

| Building | Function |
|---|---|
| **Longhouse** | population cap, rest/morale recovery |
| **Dock** | creates and services trade routes; route capacity |
| **Warehouse** | storage cap; raid target; concentrates Heat |
| **Tavern** | recruiting, morale, rumours (information system) |
| **Watchtower** | detection radius, raid warning, basic defence |
| *Workshop* | *(post-MVP)* Timber+Iron → tools; Food → Rum |

### 5.6 Stances & reputation

Toward each neighbour, one stance: **Trade · Neutral · Shadow · War**.

*Shadow* is covert raiding while publicly neutral — the pirate fantasy proper.
It's profitable and it can be **discovered**, which is what makes it a decision
rather than a free lunch.

Reputation is two axes, deliberately in tension:

- **Trust** — will they trade with you, and on what terms
- **Fear** — will they dare to raid you

High Fear deters raids but poisons Trust and can trigger a coalition. High Trust
gets rich fast but reads as soft. There is no correct answer, which is the point.

### 5.7 Events

Table-driven, weighted by world state — **never uniform random**. An event fires
because the world made it likely: a storm in the wet season, a plague in a crowded
town, a defection after a bad month, a raider fleet after a run of high Heat.

**Revolution is not in this table.** Events are discrete shocks the world throws at you;
revolution is a state machine you walked into (§5.2.2). Keeping the two apart matters —
if revolt ever fires from an event roll, it stops being a consequence and becomes bad
luck, which is precisely the failure mode the 2021 rubber-banding had.

The same event pipe carries both unscripted (sandbox) and scripted (campaign) events.
See §6.3 — this is an architecture decision as much as a design one.

### 5.8 Winning and losing

**Sandbox has no victory condition.** It has failure states and it has ambition.
Victory conditions belong to modes that opt into them.

Three ways to lose, one per pressure:

| Failure | Source | Pressure |
|---|---|---|
| **Sacked** | raiders or a rival coalition take the port | Heat |
| **Deposed** | your own people, rung 6 | Unrest |
| **Collapse** | upkeep outruns income until nothing functions | Upkeep |

The symmetry is worth preserving: you can lose outward, inward, or economically, and
the defences against each make the others worse. **No single event causes any of them**
(§5.2.3) — each is the end of a cascade the player could see coming and had days to act
against.

---

## 6. Modes & architecture

### 6.1 Modes as content, not as code

A mode is a **rules module + a content folder**, not a branch in the codebase.
The simulation never knows which mode is running.

| Mode | Status | Definition |
|---|---|---|
| **Sandbox** | MVP | The sim with a null rules module. No goals, no ending. |
| **Campaign** | next | Same sim + scripted spine layered on the live world |
| Others | parked | See Appendix A |

### 6.2 Why this ordering

Sandbox first is not just scope discipline — it's the **test of the core premise**.
If the simulation can't produce a story on its own, campaign content would be papering
over a dead engine. Build the engine, prove it, then write for it.

### 6.3 Campaign: scripted and unscripted together

Story mode needs both authored beats and a living world. If the scripted layer gets
its own bespoke pipe, the two never interleave and the seams show.

**Therefore, from day one:** the sim emits facts onto a single event bus
(*"convoy raided"*, *"port starving"*, *"crew member deserted"*). Sandbox merely
surfaces them. Campaign subscribes to the same bus **and injects scripted events onto
it**, so authored content reacts to emergent state and vice versa. This costs almost
nothing if built now and is expensive to retrofit.

### 6.4 The four technical decisions that matter

1. **Sim core is plain C# with zero UnityEngine dependency.**
   Enforced by assembly definitions, not discipline. Deterministic and tick-based.
   *This is the only decision here that cannot be affordably retrofitted.*
   Buys: unit tests, replays, saves, console-speed iteration, and multiplayer as a
   future option rather than a rewrite.

2. **Everything enters the sim as a command.**
   Player input, AI decisions and campaign scripts all speak one command API.
   Nothing bypasses it. Save/load, replay, AI and scripting stop being four systems.

3. **One event bus for all events.** Per §6.3.

4. **Content is data, not code.**
   Units, buildings, goods, event tables and victory rules live in plain data files
   (CSV for balance tables, JSON for nested definitions) parsed into POCOs. A mode is a
   folder plus a rules module. **A mod is the same folder shape** — modding support is a
   consequence of doing this correctly, not extra work.

   *Not ScriptableObjects* — they are Unity-specific and scatter balance across one
   asset per entity. See `ARCHITECTURE.md` §1.1 and §5.

**Anti-decisions**, stated so they don't get relitigated:
- **No DOTS/ECS.** Named crew are in the dozens. Mobs are hundreds of *simple* agents —
  flow-field steering toward a target, not per-agent A* — which plain C# handles fine.
  DOTS is a solo-project time sink and buys nothing at these counts.
- **Crowd tech is bounded to one use case: the revolt mob** (§5.2.2), enemy/mob side
  only, hundreds not thousands. If a second use case appears, it is a scope change and
  gets argued as one.
- **No networking scaffolding.** Determinism keeps the option open for free.
- **No custom engine, no custom editor tooling** until the MVP passes its kill test.

### 6.5 Layout sketch

```
Assets/
  Sim/            # plain C#, asmdef, no UnityEngine reference — the game
  Presentation/   # reads sim state, never writes to it
  Input/          # UI + input -> Commands
  Modes/
    Sandbox/      # rules module (null) + content
    Campaign/     # rules module + scripts + content
  Content/        # shared data: goods, buildings, crew traits, event tables
Tests/            # sim tests, headless, no Unity runtime needed
```

---

## 7. Tools

- **Unity**, current LTS. The 2021 doc pinned 2020.3 and left an unresolved
  "maybe Unreal after all" note — **resolved: Unity**, because the sim core is
  engine-agnostic C# anyway, which makes the engine a rendering choice rather than
  an architectural one.
- **URP**, low-poly, no bespoke rendering work.
- **Git**, local + one private remote. No GitLab/Trello/Discord sprint apparatus —
  that was for a four-person team that no longer exists.
- **Assets:** Synty POLYGON Pirate. Royalty-free audio.

---

## 8. MVP — and the conditions for killing the project

### 8.1 The slice

One map · one faction · **4 crew roles** · **5 buildings** · one
resource→trade→raid loop · **one AI neighbour** · Heat · Unrest · Upkeep · stances ·
**the revolution ladder, rungs 1–5** · **zero scripted content**.

Full game targets **6–8 neighbours** (§Appendix B); the MVP ships one, because one is
enough to test whether a stance is an interesting decision. If it isn't interesting
against one, more won't fix it.

Revolution is *in* the MVP, because it is the thing being tested (§8.2) — a slice
without it doesn't answer the question the slice exists to answer. But it enters
**small**: the rung-5 mob is dozens at first, scaled toward hundreds only once the
ladder is proven fun at a size that needs no new tech. Crowd work is earned, not
front-loaded.

### 8.2 The kill test

The MVP exists to falsify one claim: *that this simulation generates stories on its own.*

> **Test:** put it in front of someone for 15 minutes.
> **Pass:** at least once, an unscripted event makes them react out loud.
> **Fail:** it doesn't.

On failure, the emergence premise is wrong and no amount of added content repairs it —
that's the moment to kill or fundamentally re-pitch, not to add features. This test is
cheap and should be run as early as it can possibly be run.

### 8.3 Explicitly not in the MVP

Campaign, story, dialogue, cinematics, quests, achievements, multiple maps, multiple
factions, ships as controllable units, sieges, magic, tech tree, main menu polish.

---

## Appendix A — Parked ideas

Written down so they stop leaking into scope. Not rejected — deferred, with a gate.

| Idea | Gate |
|---|---|
| Campaign / story mode | After sandbox passes the kill test |
| Magic: mages, golems, surhommes | After a second mode ships |
| Sci-fi setting / second volume | Not before 1.0 |
| Hardcore mode (for RTS veterans) | Post-1.0; as a *mode*, not the default |
| "All for one" boss/siege mode | Post-1.0 |
| Flag/king-of-the-hill mode | Post-1.0 |
| Dungeon mode | Undefined; needs a pitch before it gets a gate |
| Multiplayer | Never planned; determinism keeps it possible |
| Ships as directly controlled units | Evaluate after routes prove fun |
| Labour market: hiring away from rival ports | After neighbouring ports exist |
| Subsidised buyers (below) | After trade and routes exist |
| **Decision Timeline** (below) | After the game is playable and fun |

### A.0 Subsidised buyers — a price that is not a market

A merchant who pays *above* the going rate is not necessarily a mistake. A rival power can
fund a buyer to overpay, absorbing the loss out of tax revenue, to capture a market and
squeeze out competitors — which is what mercantile states actually did.

The mechanic is not the good price. It is the **withdrawal**. A player restructures around
a buyer paying well above base, grows to match it, and raises their fixed costs
permanently (§5.2.3). When the subsidy stops, income falls to the real rate while upkeep
stays where the good years put it. That is a correlated shock arriving through the economy
rather than through an event roll, and it is legible in advance to a player paying
attention.

It also gives Stances (§5.6) an economic instrument rather than only a diplomatic one, and
it is a reason for Heat to matter at a port that is merely *profitable*.

Numbers behind this, and the other findings from tuning the Phase 1 economy, are in
`doc/design/ECONOMY_FINDINGS.md`.

**Until then**, the loader rejects a sell price above base price. With one static merchant
and nobody funding the difference, an above-market price is simply money from nowhere, and
the bug would read as generous tuning. That rule is a property of the current model, not an
economic law, and it is the first thing to change when a buyer has an actor behind it.

### A.1 Decision Timeline — replacing the save-slot list

**The idea.** Instead of a list of save files, the player sees the *decisions* they made
— not raw commands, but the meaningful ones: raising a tariff, repressing a riot,
signing a treaty. Each shows what it caused: *"you raised tariffs on Portsmouth → trust
fell → your merchants agitated → rung 3."* The player can return to any decision and
**branch** — *"this time, instead of tariffs, I offer one of them an extraordinary trade
agreement, and only them."*

**Why it fits this game specifically.** The product here is cascading consequences
(§5.2.3). A save list hides them; a causal timeline is the game's own subject matter
turned into UI. It doubles as the Chronicle and as onboarding — the clearest possible
answer to *"what did I do wrong?"*

**Presentation — visual, not a list.** A graph: the seed is the root, the run runs
outward, and each revisited decision sprouts a new branch. Two zoom levels, because they
are two different graphs (`ARCHITECTURE.md` §6.3):

- **Zoomed out — the branch tree.** Tens of nodes, always legible. Your run, and every
  alternative you tried, at a glance. This replaces the save-slot list.
- **Zoomed in — one decision.** Selecting a node opens the *summarised* cascade behind
  it: *tariffs → trust −20 → merchants agitate → rung 3*. Roughly five steps, never the
  raw causal graph, which is thousands of nodes and unreadable.

**Cost.** Branching is nearly free given the save model. Causal tracing is not — but its
one non-deferrable piece is already specified in `ARCHITECTURE.md` §6.2, so this stays
buildable later rather than becoming a rewrite. Note the real expense is not drawing the
graph, it is **summarising** it down to five readable steps.

**The risk, which is real.** This game's core tension is that Heat and Unrest have no
stable equilibrium — you live with consequences (§5.2). Unrestricted rewind dissolves
exactly that. Consequences you can undo are not consequences, and the central dilemma
stops mattering.

**Proposed resolution: retrospective by default.**

| Mode | Timeline behaviour |
|---|---|
| **Default** | Browsable *during* a run — read-only. Branching unlocks when the run **ends** (collapse, deposition, or the player stops). A post-mortem you can replay from, not an undo button. |
| **Explorer** (opt-in) | Branch freely at any time. For players who want the story space rather than the pressure. |

The retrospective framing keeps the tension intact while still delivering the feature's
real value — understanding the cascade — and Explorer serves the other audience without
compromising the default. Decide before building the UI, not after.

---

## Appendix B — Open questions

**Open:**

1. **Crew ceiling.** Where exactly between 10 and 30 does legibility break?
   A playtest question, not a design decision — do not settle it on paper.

**Resolved (kept here for the reasoning):**

| Question | Answer | Where |
|---|---|---|
| Pause-with-orders? | Yes — it is *the* casual mechanism | §3.2 |
| Day length | ≈20 real minutes; day boundary is the economic pulse; speed controls required | §5.1 |
| Crowd-as-threat? | Yes, bounded — revolt mobs only, hundreds not thousands | §5.2.2, §6.4 |
| Failure severity | Recoverable from any single shock; collapse only from cascades | §5.2.3, §5.8 |
| Neighbour count | 5–9 (target 6–8). Under 5 the stance system has nothing to say; 10+ is unreadable. MVP ships 1 | §8.1 |
| Naming and setting | Deferred — will be driven by story mode, so it waits for the campaign | §4, §6.1 |
