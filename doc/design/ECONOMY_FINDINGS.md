# Economy findings

What running the sim actually showed, and what each result implies. Not the design — that is
`GDD.md`. Not code mistakes — those are `doc/code/PITFALLS.md`. This is the record of numbers
that came out of the harness and what had to change because of them.

Add an entry when a run tells you something you did not already believe. Include the numbers;
a finding without them is an opinion.

Reproduce any of these with `dotnet run --project dotnet/Harness -- --days N [--coin N]`.

---

## Phase 1

### The port had no income at all

**Costs 29/day** — 17 wages across 7 crew, 12 upkeep across 6 buildings. Nothing produced
coin, because trade did not exist. From 200 starting coin: bankrupt on **day 7**, every
building derelict by **day 18**, on every run regardless of play. Timber and iron piled up with
nothing to spend them on.

This made the Phase 1 gate unrunnable rather than merely untuned: *"a single shock is always
survivable"* has no meaning when the baseline is guaranteed collapse.

→ `MarketSystem` added: sell surplus above a per-good `keep` at a fixed `sell_price`.

### The margin is deliberately thin

At full health the port earns **31/day against 29 of cost — +2/day**.

Generous prices and it can never fail; mean ones and it always does. Neither is a game. The
thin margin is what makes reserves the thing that decides, which is §5.2.3's claim.

Consequence worth remembering while tuning: growth raises fixed costs permanently and income
only variably, so *every* building added moves the port closer to the edge. That asymmetry is
the intended failure model, not a balance bug.

### Servicing arrears first made one missed payday permanent

The market originally paid down arrears before adding coin. Result: nothing left for today's
wages, which added to arrears, which absorbed tomorrow's income.

```
arrears-first:  day 1 coin 0,  arr 6   →  day 12 coin 0, arr 249, everything at zero
record-only:    day 1 coin 13, arr 19  →  morale recovers across days 4-6
```

It contradicted §5.2.3 directly — *one bad event is absorbed and recovered from, always*.

→ Arrears is now a record of what went unpaid, read by Phase 2 as grievance. Whether back pay
can be settled, and at what cost in loyalty, is a decision that belongs with unrest.

### The condition ratchet is the dominant failure mode

Buildings decay **0.10/day** unmaintained and recover **0.02/day** maintained: **five good days
to undo one bad one.** Once a port misses upkeep it is structurally behind, and this — not
hunger, not morale — is what eventually pulls a struggling port under.

This is the first number to move when tuning the Phase 1 gate. A 5:1 ratchet may be too
punishing for *"a single shock is always survivable"*, or it may be exactly the pressure that
makes mothballing a real decision. Unresolved.

### An unpaid but fed crew declines slowly

Unpaid costs **−0.10** morale; eating returns **+0.05**. A port that cannot pay but can still
feed drifts at about **−0.05/day** — roughly 20 days to zero.

Slow enough that wage failure alone is not a spiral. Whether that is right depends on what
Phase 2's unrest does with the same signal.

### Rum is a permanent small drag

Sailors and guards want rum. Rum is `ImportOnly` and nothing imports anything, so three of
seven crew sit at **0.98 morale** forever: +0.05 for eating, capped at 1.0, then −0.02 for
going dry. Average morale reads 0.99 rather than 1.00 even in a perfectly healthy port.

Correct behaviour, and legible, but it is a standing thumb on the scale to remember when
reading any morale number — and a reason not to treat 1.00 as the healthy baseline.

### The cascade had no ratchet, and shocks added instead of compounding

The first run of the Phase 1 gate failed, and raising the shock sizes to make three shocks
fatal made *one* shock fatal too. There was no band where one was survivable and three were
not.

Cause: every consequence in the economy was a **spring**. Morale returns when food does,
condition returns when upkeep is paid, so a port always climbed back unless a single blow
exceeded its reserves. Nothing was irreversible, so shocks added linearly rather than
compounding.

→ `DesertionSystem`. GDD §5.4 already said it — *"at the floor, crew desert"* — it simply had
not been built. Someone who leaves does not come back, so labour lost is production lost, which
is income lost, which is more unpaid wages. One person a day at most: a slope the player can
see coming and act against, not a cliff.

### Reserves at which the design holds: roughly 100–250

Sweeping starting coin against the standard shock set (storm 0.30 condition, harvest 8 food,
theft 100 coin — one on day 10, three on days 10/12/14, 40-day runs):

```
coin | undisturbed | single storm | three correlated
  80 | Healthy     | Collapsed    | Collapsed          too thin: one shock is already fatal
 120 | Healthy     | Healthy      | Collapsed          the design holds
 160 | Healthy     | Healthy      | Collapsed          the design holds
 200 | Healthy     | Healthy      | Struggling         edge
 300 | Healthy     | Healthy      | Healthy            too fat: three are absorbed
```

The band is wide — about four to five and a half days of reserves — so this is a curve, not a
knife edge. That is what makes it a design that can be tuned rather than a coincidence.

→ The starting port holds **150 coin**, inside the band, because outside it reserves are not a
decision. It was 200, which sat just above.

**Re-run this sweep after any tuning pass.** The numbers move together, and the band is the
thing to preserve — not any individual value in it.

### The port is over-staffed, so losing crew currently makes it richer

The scenario corpus put a number on something the gate had hidden. Against a 200-coin
baseline, losing two crew on day 10 ends the run at **448 coin** — desertion is *profitable*.

Staffing is `crew effort / producing buildings`, clamped to 1. The default port has 7 crew and
4 producers, so effort is about 6.8 against 4: staffing is already **capped**. Crew beyond the
cap draw wages and add no output, so removing them is pure saving until the cap finally binds.

This inverts the cascade's labour link. §5.2.3 wants desertion to hurt — fewer crew, less
production, less income, more unpaid wages — and right now it helps until roughly half the
crew are gone.

Not fixed, because there is more than one right answer and they are different games:

- **Fewer starting crew**, so the cap binds from day one and every hire is a real decision.
- **Crew are assigned to buildings**, so a producer without staff simply stops rather than
  drawing from a pool. This is the direction `ProductionSystem` already flags as provisional.
- **Surplus crew do something else** — construction, defence, rowing a boat — so they are not
  idle payroll.

The second is the one the design implies. **Done** — see below.

### Assigning crew to buildings fixed the sign

Crew now work a named building rather than a port-wide pool. A producer at half its staff makes
half its output, and someone who leaves takes their building's output with them.

The same scenario, before and after:

```
one-desertion (lose 2 crew on day 10, 40 days)
  pooled labour:      Healthy      448 coin      losing crew made the port richer
  assigned to work:   Struggling    18 coin      losing crew costs what they produced
```

Every other scenario kept its shape — three correlated shocks still collapse, deep reserves
still absorb them, single shocks are still survived — so this corrected the labour link without
disturbing the band.

Two consequences worth knowing:

- **Staff requirements are tuned against the starting crew.** Two farms, a sawmill and a mine
  want 2+2+1+1 = six; the port hires seven. Everything that produces is worked at full rate and
  the guard is left idle.
- **The idle guard costs 3 coin a day.** Deliberate, and deliberately *not* framed as a
  mistake: over-hiring is a position, not an error. Taking the best hand off a rival port,
  holding a specialist for a building that is not finished, buying loyalty before you need it —
  all legitimate, all priced in wages and food. What the model gets right is that the cost is
  visible; what it does not have yet is anywhere the *benefit* could come from, since there are
  no rival ports to hire away from. See the parked note below.

Baseline is back to **+2 coin a day** after the change: dropping the sawmill's requirement from
2 to 1 put a full-rate worker on every producer, where previously the mine fell to the guard at
0.8.

## Phase 2

### The economy already drives the ladder, without anything connecting them on purpose

The first run of the corpus after the ladder landed:

```
three-correlated                 Collapsed   Deposition      0
one-harvest-failure              Healthy     Calm          120     (was 176)
three-correlated-deep-reserves   Healthy     Calm          251     (was 298)
everything else                  Healthy     Calm          unchanged
```

`three-correlated` no longer merely bankrupts the port — it **deposes** you. Nothing was
written to make that happen: the cascade produces hunger and unpaid wages, those are what
grievance is made of, and grievance is what the ladder climbs. §5.2.2 calls unrest "a state
machine fed by the economy", and it is now literally that.

`one-harvest-failure` is the more interesting line. It lost 56 coin and **ends Calm**. Only
rungs at Slowdown and above reduce output, so it must have climbed at least to Slowdown and
come back down on its own — the Phase 2 gate's property showing up before the gate was written.

### One idle worker is visible in every reading

Commoner grievance sits at **0.02** in a perfectly healthy port, because the spare crew member
has nothing to do and commoners resent unemployment. It plateaus there and never climbs.

Harmless, and worth knowing when reading any grievance number: 0.00 is not the resting state of
a working port, 0.02 is.

---

## Parked mechanics that came out of tuning

### A labour market gives over-hiring a reason

Idle crew are currently pure cost, which is correct arithmetic and half a mechanic. Over-hiring
is a real strategy — denying a rival a skilled hand, banking labour before construction
finishes, holding a specialist who cannot be replaced — but every one of those needs somewhere
else the person could have gone.

The design already leans this way: §5.4 gives crew skill that improves with use, traits, and
deserters who leave *"to a neighbour, with what they know"*. Once neighbours exist, hiring is
two-sided and paying someone to be idle can be the cheapest way to stop a rival having them.

Until then the cost is honest and the benefit is absent, so the default port keeps one spare
hand rather than being trimmed to exactly six. Trimming would encode "over-hiring is a mistake"
into the starting position, which is the opposite of what is intended.

Gate: after neighbouring ports exist.

### Subsidised buyers — the withdrawal is the mechanic

A rival power funds a buyer to overpay, absorbing the loss from tax revenue, to capture a
market. Full write-up in `GDD.md` Appendix A.0.

The part worth keeping in mind is not the good price. A player restructures around a buyer
paying above base, grows to match it, and raises fixed costs permanently. When the subsidy
stops, income falls to the real rate while upkeep stays where the good years put it.

That is a **correlated shock arriving through the economy** rather than through an event roll,
and unlike a random event it is legible in advance to a player paying attention — which is the
casual-but-deep target of §3.2 rather than a difficulty spike.

Until a price has an actor behind it, the loader rejects a sell price above base price. That
rule is a property of the current single-merchant model, not an economic law.

---

## Phase 2: what the revolution gate measured

The gate is "drive a port into revolt and pull it back out, both directions". The first
direction worked on the first run. The second did not, three times over, and each failure was
a different missing mechanic rather than a number needing a nudge.

### The ladder climbed faster than grievance could fall

Grievance saturates in a day and decays in fortieths. With the ladder climbing one rung per
day, a port pinned at 1.00 went Calm to Deposition in **six days**, and there was no window in
which any player action could change the outcome. Every exit §5.2.2 promises for the upper
rungs was unreachable.

`days_to_climb` per rung fixed it: 1, 2, 3, 3, 4, 5 from Grumbling upward, so total theft now
takes **nine days** to reach Riot instead of four. Falling is deliberately not paced — the
hysteresis already prevents flicker, and slowing the way down would undo the point of it.

The loader rejects a ladder that speeds up as it gets worse, which would leave least time to
act where it mattered most.

### Repression bought a permanent penalty and nothing else

Grievance is capped at 1.00 and a rioting port is already there, so Brutal's −0.50 was re-added
by the same day's hunger: 1.00 → 0.50 → 0.91 → 1.00. Measured end to end, force and patience
both took **12 days** to leave a riot. The permanent floor was a pure loss and repression was a
trap, not a decision.

`cowed_days` (Restrained 2, Firm 4, Brutal 7) is the fix: the day's pressures land on nobody
while the window is open. A cowed stratum still cools at the *slow* rate, because silence is
not contentment — paying the fast rate for it would make force strictly better than fixing
anything. After Brutal: out of Riot on day 1, Calm by day 7, floor 0.18 forever.

### Fixing the economy was not actually a lever

At 0.04/day, unwinding a saturated grievance takes 25 clear days, which the ladder outruns. So
even with the pacing above, the only working exit was repression — turning "a viable strategy,
not a free one" into the only strategy.

`relief_per_day` (0.12 / 0.15 / 0.18 against decay 0.04 / 0.05 / 0.06) applies on a day the
stratum had **nothing** to resent, as opposed to a day that merely was not worse. Asked per
stratum, not port-wide: named crew do not care that a labourer is idle, and — decisively — the
default port has 7 crew for 6 work slots, so a port-wide clean day would never have happened at
all.

Measured: from Slowdown, funding the port reaches Calm in **6 days with all 7 crew intact**.

### Where the economic exit stops working, and why that is right

A rioting port produces 35% of output. Two farms at 6/day become 4.2; seven crew eat 7. Coin
does not buy food, so **past Riot, money cannot save a port that has stopped working** — the
crew starve out however deep the treasury. Verified: 100,000 coin at Riot still ends with zero
crew.

This is a good shape rather than a hole. The lower rungs are fixed by management; the upper ones
are paid for in loyalty and a permanent floor. It is what makes repression a decision.

### The recovery test was passing for the wrong reason

Worth recording as a method note. The first version of "fixing it pulls the port back out"
passed — while the port emptied completely. A ruin with nobody left in it also reads as Calm.

Any test asserting a system has calmed down must also assert that the thing being measured is
still there. The gate now checks crew count on both sides of every recovery.

### Corpus impact

All ten scenario digests moved, which is expected: three changes to grievance arithmetic that
apply on ordinary days. Shape change worth noting — `three-correlated` used to end at
Deposition and now ends Collapsed but Calm, which is the population gap in Appendix A.-1
showing through the corpus.

---

## Strata populations: what changed and what it cost to rebalance

Commoners exist. They work the buildings, they eat, and after sustained starvation they leave.
Named crew stopped being the labour and became specialists who improve a building rather than
manning it. That closed the gap the Phase 2 gate found — Deposition is reachable, an emptied
port is no longer Calm — and it broke the economy in three separate ways on the way there.

### Losing crew became a windfall

The first corpus run after the change had `one-desertion` ending **richer than undisturbed**:
448 coin against 404. Exactly the inversion the pooled-labour model produced two phases ago,
arrived at from the opposite direction — crew no longer produce anything directly, so their
wages are pure cost and desertion is a saving.

The arithmetic is worth keeping, because it is a constraint on any future crew mechanic. A
labourer costs 2 coin of wage plus a food. On a two-hand farm, one labourer is half the
specialist bonus, so at a 25% cap they add `0.5 × 0.25 × 6 = 0.75` food a day, worth 0.75 coin
against a cost of 3. **A specialist only pays for themselves on high-value output**: the same
crew member on the mine adds `0.25 × 4 = 1` iron a day, worth 4 coin, and clears their cost
comfortably.

`PortScenario.Assign` fills producers in build order, which puts four of seven crew on farms
where they lose money. The starting port is therefore deliberately over-crewed, which is
consistent with what `Assignment` already says about hiring being a decision rather than a
mistake — but a desertion shock still reads as a small windfall in the corpus. Left as it
stands, recorded here, and the fix is either posting crew by the value of what a building makes
or giving crew something to do that is not production.

### A town that eats made reserves meaningless

With commoners eating, the binding constraint stopped being coin and became food — and coin
could not buy food. `three-correlated-deep-reserves` at 450 coin died exactly as fast as the
150-coin run. That is not a tuning problem, it is the Phase 1 gate losing its premise: "one
shock is survivable, three are not, and the difference is the slack you kept" means nothing if
the slack cannot be spent.

`MarketSystem` now buys food up to `keep` when the store is short, at `base_price` rather than
`sell_price` — four coin against one. The spread is the point: importing what you should be
growing is an emergency, not a business model.

This also revises a Phase 2 finding. "Past a riot, money cannot save the port" was recorded as
a deliberate shape; it is now softer. A rioting port can buy its way through a famine if it is
rich enough, which makes deep reserves a genuine alternative to repression rather than a
consolation. Repression is still far faster.

### The market was selling the port into famine

Food `keep` was 10 while daily demand was 11, so the market sold the store down below what the
port would eat the next morning, every single day. Chronic hunger by construction, and invisible
until commoners existed to starve.

→ **A good the port consumes must have `keep` above one day's demand.** Food is now 20 against a
demand of 13. Not validated in the loader, because demand depends on the scenario's population
rather than on content alone.

### The re-measured band

Three correlated shocks (storm, harvest failure, theft) against starting reserves:

| Coin | Outcome |
|---|---|
| 80–150 | Collapsed, Deposition |
| 200 | Collapsed, Uprising |
| **250** | **Healthy, Calm** |
| 400 | Healthy, Calm |

A single storm:

| Coin | Outcome |
|---|---|
| 20–60 | Collapsed, Deposition |
| 80 | Struggling, survives |
| 100+ | Healthy |

The band is sharper than the old one and the shape is the same: below ~80 a single shock is
fatal, above ~250 three correlated ones are absorbed, and the starting port at **150** sits
inside — one is survivable, three are not. The starting coin did not need to move, which is a
reassuring sign that the shape is a property of the design rather than of the numbers.

### Settings that came out of this

- Town of **12 commoners**, eating **0.5** a day. At 8 the port was too rich (+19/day against a
  target of ~+2); at 16 the undisturbed run drifted to Grumbling and a single storm killed it.
- **`MaximumSpecialistBonus` 0.25.** At 0.5 the port earned +19/day; at 0.10 a specialist was
  not worth noticing.
- **`idle_weight` 0.005** for commoners, down from 0.02. A town always has more people than
  jobs — 12 commoners against 6 places — so at the old weight unemployment alone outran decay
  and every port drifted into permanent unrest.
- **`leave_after_days` 12.** Crew desert in two or three; if commoners left as readily, a
  collapsing port would empty before the ladder could climb, which was the original bug.

---

## Five cities: what the player lost, and why that is the point

The world is now Saltmarsh plus four neighbours, all running the same systems. Saltmarsh is the
player's, and **it has no mine**.

### The cost of specialising the player

Iron was the single largest earner — four coin a unit against food's one — so losing the mine
costs about sixteen coin a day. Saltmarsh now runs at a **small loss**, roughly one coin a day,
where it used to make two.

That is deliberate and it is the whole argument of §5.3: a port that gets rich alone has no
reason to run a route. What Saltmarsh has is food it cannot eat and timber it does not need;
what it lacks is metal it cannot make at all. Ironhold has the opposite problem and sits five
days away, which is the longest commitment on the map.

**Until routes exist, that loss has no remedy.** The port survives a forty-day corpus run and
bleeds slowly over a hundred. Trade is what turns it around, and it is the next piece of work
rather than an oversight here.

### The port had to be rebuilt to survive at all

The first attempt kept Saltmarsh's old composition minus the mine, and it was deposed by day
forty: two farms feed twelve commoners and seven crew for exactly one day less than the month
has. It now has **three farms and five crew** instead of two and seven — more bread, a smaller
payroll, and a surplus worth selling.

### The band, re-measured

Three correlated shocks (storm, harvest failure, theft):

| Coin | Outcome |
|---|---|
| 80–200 | Collapsed, Deposition |
| **250** | **Healthy, Calm** |
| 400 | Healthy, Calm |

A single storm:

| Coin | Outcome |
|---|---|
| ≤110 | Collapsed |
| **120** | Struggling, survives |
| 130+ | Healthy |

**The shape holds and the margin narrowed.** One shock is survivable and three are not, which is
§5.2.3's requirement. But the starting port at 150 now sits thirty coin above the single-shock
edge where it used to sit seventy, because a port losing a coin a day has less slack the longer
a run goes.

→ **Watch this after routes land.** If trade does not restore the margin, the starting reserves
have to rise rather than the shocks softening — the band is the design, and a port that dies to
one storm has lost the property §5.2.3 is built on.

### Neighbours are not scenery

Every city runs consumption, wages, upkeep, labour, production, market, unrest and the ladder.
Ten days in, the five hold visibly different amounts of coin, which is the cheap proof that the
systems are really running per city rather than once over a heap of entities.
