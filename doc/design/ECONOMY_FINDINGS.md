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

The second is the one the design implies. Until then, read any desertion result knowing the
sign is currently wrong.

---

## Parked mechanics that came out of tuning

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
