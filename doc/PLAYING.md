# Playing it

What is on screen, what the controls are, and how to make each mechanic actually happen.

**Status:** Phases 0–5 built. Deliberately ugly — lists, labels and coloured dots. Everything
here is scaffolding that will be replaced; none of it is a look.

---

## Running it

Open `Assets/Scenes/SampleScene.unity` and press Play. There is nothing to wire: the scene is one
object with one `GameBoot` component, and everything else loads from
`Assets/StreamingAssets/`.

If content fails to load the game throws on the first frame with the file and line in the
message. That is deliberate — content that half-loads is a port whose numbers are quietly wrong.

---

## The three views

**The map** is the world: five cities where `ports.csv` puts them. A lane appears between two
cities when a convoy is crossing, with the ship on it and its wake behind it. A city turns red
when there are people in its square.

**The port card** floats over the left of the screen: the clock, your readouts, and every order
you can give. It scrolls — there are more orders than fit.

**The port view** is one city close up: the longhouse in the middle, your buildings around it,
and whoever is standing in the square. You can only look inside your own city (§5.6 — what you
know about a neighbour is meant to be bought with a stance or a scout, and stances do not exist
yet).

---

## Controls

| | |
|---|---|
| **Space** | pause and unpause |
| **.** (full stop) | step one day |
| **1** / **2** / **4** | speed ×1, ×2, ×4 |
| **Enter** | look inside the selected city |
| **Esc** | back out to the map |
| **Click a city** | select it |
| **Click it again** | look inside it (your own city only) |
| **Click open water** | select your own city again |
| **Click the ground**, inside a city | back out to the map |

The card has the same clock controls as buttons: `Pause`, `>` (step a day), `x1 x2 x4`.

**A day takes twenty real minutes at ×1.** That is GDD §5.1 and it is a design number, not a
convenience. Use `.` to step days when you want to watch something happen — or drop
`seconds_per_day` in `Assets/StreamingAssets/Config/clock.csv` and relaunch. Put it back before
committing, and before judging how the pacing feels.

---

## The orders

Grouped on the card, under **orders**. What appears depends on which city is selected: your own
city offers everything, a neighbour offers only the routes to that one city.

| Group | What it is |
|---|---|
| **Unrest** | put down a riot, at each price §5.2.2 offers. Disabled until there is one |
| **Defence** | stand escorts up or down |
| **Buildings** | shut and reopen your buildings, post and recall specialists |
| **Trade** | buy from and sell to the other four cities |

Disabled orders stay on screen with the reason as a tooltip. A control you cannot discover is
worse than one you cannot use.

---

## Making things happen

### See a convoy

The one that is easy to miss, because **nothing dispatches a convoy on its own.**

1. Scroll the card down to **Trade**.
2. Click **Buy 5 iron from Ironhold** — 60 coin now, five days out.
3. Press `.` a few times.

A grey lane appears between Saltmarsh and Ironhold with a blue dot moving along it and a brighter
wake behind. It lands on day five and the feed says so.

Ironhold is five days away, Fairhaven two. That difference is the decision §5.1 wants: a near
partner is worth less per unit and more per week.

### See the workshop stop, and start again

Saltmarsh has a workshop that eats one iron a day and no mine. Step about ten days and the feed
starts saying *"the workshop ran short"* every morning. Buy iron from Ironhold and it stops.

That is the sentence Phase 4 was built to make true: a good you cannot make, a use for it, and no
way to get more without a route.

### See Heat and a raid

Heat is what your wealth is drawing. Bread and timber draw nothing; iron draws a little. Run iron
routes continuously and **Heat** on the card climbs to around 30%, and convoys start being
raided — the feed says what was taken.

Stand the escorts up under **Defence** to cut that, at 2 coin per convoy per day. It is
deliberately close to break-even: an escort takes coin every morning *before* wages, and a raid
takes cargo in one lump. One threatens payday, the other threatens the warehouse. That is the
dilemma, and there is no right answer to it.

### See a revolt

Hard on purpose — a mob is not the natural sequel to a late shipment. It takes sustained
mismanagement: unpaid crew, hungry commoners, day after day. The ladder climbs
Grumbling → Slowdown → Agitator → Riot → Uprising, holding each rung before the next, and every
rung has an exit.

At **Uprising** the commoners come out. Press Enter to stand in the square and watch them close
on the longhouse over about three days, with your named crew among them — each one having chosen
a side by loyalty, drawn larger and labelled.

The fastest way to see it is the headless harness rather than the editor:

```
dotnet run --project dotnet/Harness -- --days 60 --coin 90 --events
```

---

## The readouts

| | |
|---|---|
| **Coin**, **Unpaid** | the treasury, and wages owed. Unpaid only appears when there are some |
| **Upkeep** | what the port costs per day |
| **Crew**, **Town**, **Unemployed** | named specialists, commoners, and commoners with no work |
| **Morale**, **Condition** | how the crew feel, and the state of the buildings |
| **food / timber / iron** | what is in the warehouse |
| **Unrest** | which rung of the ladder the port stands on |
| **Heat** | what your wealth is drawing |
| **Escorts** | standing or stood down, and what they cost today |
| per-stratum rows | grievance for Commoners, NamedCrew and Merchants separately |

Under the orders is the **feed**: what happened, most recent first, indented under its cause.

---

## Where things are

| | |
|---|---|
| Balance and content | `Assets/StreamingAssets/Balance/*.csv` |
| Clock and logging | `Assets/StreamingAssets/Config/*.csv` |
| Recorded runs | `Assets/StreamingAssets/Scenarios/scenarios.csv` |
| Log files | `%USERPROFILE%/AppData/LocalLow/Laclaverie/RTS Port/Logs/` |
| Headless run | `dotnet run --project dotnet/Harness -- --help` |
| Tests | `dotnet test dotnet/RTS.Headless.slnx` |

Every CSV carries its own reasoning in comments at the top, including why each number is what it
is. They are the design documentation for the balance, and they are meant to be edited.

---

## Known rough edges

- **Trade is at the bottom of the card.** You have to scroll for it. The card is bounded and
  scrolls, but the ordering has not been thought about.
- **A route is access, not profit.** A city pays the same price the passing merchant does, so
  shipping grain somewhere earns what selling it at home would, several days later. What a route
  buys today is iron, which nothing else can get you.
- **The neighbours' traffic is thin.** They trade with each other now, but nearly all of it is
  iron: the passing merchant clears every other surplus the day it appears, so no city ever has
  enough spare food or timber to ship any. Expect one lane between Ironhold and Millrace more
  often than not.
- **Nobody trades with you.** Neighbours ship only to each other. An approach you could accept or
  refuse needs stances (§5.6), which do not exist yet.
- **Rum and spice can never be held**, so Heat only ever measures iron. That wants a warehouse
  with a real storage cap.
- **Nobody outside this project has played it.** Every judgement about how it *reads* is one
  person's, which is what Phase 6's kill test is for.
