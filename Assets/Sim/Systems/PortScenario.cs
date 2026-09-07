using System;
using System.Collections.Generic;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// A starting port, described as data and built into a world.
    /// </summary>
    /// <remarks>
    /// It lives in <c>Sim</c> rather than in the harness so that the console you tune against
    /// and the tests that assert on the cascade build the <em>same</em> port. Two setups that
    /// drift apart would mean tuning one thing and testing another, which is a slow and
    /// confusing way to be wrong.
    /// <para>
    /// Everything is an ordered list, never a dictionary: entity creation order determines every
    /// id in the world, and §7.1 forbids iteration that affects state over a collection whose
    /// order is not deterministic.
    /// </para>
    /// </remarks>
    public sealed class PortScenario
    {
        public int StartingCoin { get; set; } = 150;

        /// <summary>
        /// Civilians living in the port. They work the buildings and they eat (§5.2.2).
        /// </summary>
        public int StartingCommoners { get; set; } = 12;

        /// <summary>Role id and how many, in the order they are hired.</summary>
        public List<KeyValuePair<string, int>> Crew { get; } = new List<KeyValuePair<string, int>>();

        /// <summary>Building ids, in the order they are built.</summary>
        public List<string> Buildings { get; } = new List<string>();

        /// <summary>Good id and starting units, in file order.</summary>
        public List<KeyValuePair<string, float>> Stock { get; } = new List<KeyValuePair<string, float>>();

        /// <summary>
        /// A small port that works: enough crew to run its producers, enough food to survive
        /// the first day before anything is produced, and reserves to pay for a while.
        /// </summary>
        /// <remarks>
        /// 150 coin is about five days of wages and upkeep, and it is chosen rather than
        /// rounded to. Sweeping reserve levels against the Phase 1 shock set gives a clear
        /// band: below about 100 a single shock is already fatal, above about 250 three
        /// correlated ones are absorbed, and between them the design holds — one is survivable,
        /// three are not, and the difference is the slack you kept (§5.2.3). A starting port
        /// belongs inside that band, because outside it reserves are not a decision.
        /// <para>
        /// The table is in doc/design/ECONOMY_FINDINGS.md. Re-run it after any tuning pass:
        /// these numbers move together, and the band is the thing to preserve.
        /// </para>
        /// </remarks>
        public static PortScenario Default()
        {
            var scenario = new PortScenario { StartingCoin = 150 };

            scenario.Crew.Add(new KeyValuePair<string, int>("laborer", 4));
            scenario.Crew.Add(new KeyValuePair<string, int>("sailor", 2));
            scenario.Crew.Add(new KeyValuePair<string, int>("guard", 1));

            scenario.Buildings.Add("longhouse");
            scenario.Buildings.Add("farm");
            scenario.Buildings.Add("farm");
            scenario.Buildings.Add("sawmill");
            scenario.Buildings.Add("mine");
            scenario.Buildings.Add("warehouse");

            scenario.Stock.Add(new KeyValuePair<string, float>("food", 20f));
            scenario.Stock.Add(new KeyValuePair<string, float>("timber", 10f));
            scenario.Stock.Add(new KeyValuePair<string, float>("iron", 5f));

            return scenario;
        }

        /// <summary>
        /// Builds the world. Unknown ids throw: a scenario naming a building that does not
        /// exist is a mistake in the scenario, not a port with one fewer building.
        /// </summary>
        /// <summary>
        /// Builds a world holding this one port, which the player runs.
        /// </summary>
        /// <remarks>
        /// Kept for tests and for scenarios that describe a single city inline. A world of
        /// several cities is <see cref="WorldScenario"/>, which reads ports.csv and calls
        /// <see cref="BuildInto"/> once per row.
        /// </remarks>
        public World Build(BalanceTables balance)
        {
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            var world = new World();
            BuildInto(world, balance, definitionIndex: 0, isPlayer: true);
            return world;
        }

        /// <summary>
        /// Adds this port to a world that may already hold others, and returns its entity.
        /// </summary>
        /// <remarks>
        /// The port entity is created first so everything it owns can point at it. Creation
        /// order decides every id in the world (§7.1), so ports are built one whole city at a
        /// time rather than interleaved — a city's ids stay contiguous, and adding a sixth does
        /// not renumber the first five.
        /// </remarks>
        public EntityId BuildInto(World world, BalanceTables balance, int definitionIndex,
            bool isPlayer)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            EntityId port = world.CreateEntity();
            world.Add(port, new PortState
            {
                DefinitionIndex = definitionIndex,
                IsPlayer = isPlayer,
            });

            EntityId treasury = world.CreateEntity();
            world.Add(treasury, new Treasury { Coin = StartingCoin });
            world.Add(treasury, new Owner { Port = port });

            // The town. Created before the crew so that a port always has a population even if
            // every named individual in it leaves — which is the whole point of it existing.
            EntityId town = world.CreateEntity();
            world.Add(town, new Population { Commoners = StartingCommoners, HungryDays = 0 });
            world.Add(town, new Owner { Port = port });

            foreach (KeyValuePair<string, int> hire in Crew)
            {
                int roleIndex = IndexOf(balance.CrewRoles, hire.Key, "crew role");

                for (int i = 0; i < hire.Value; i++)
                {
                    EntityId member = world.CreateEntity();
                    world.Add(member, new CrewMember
                    {
                        RoleIndex = roleIndex,
                        Morale = 1f,
                        Loyalty = 1f,
                    });
                    world.Add(member, new Owner { Port = port });
                }
            }

            foreach (string id in Buildings)
            {
                int definition = IndexOf(balance.Buildings, id, "building");

                EntityId built = world.CreateEntity();
                world.Add(built, new BuildingState
                {
                    DefinitionIndex = definition,
                    Condition = 1f,
                    Mothballed = false,
                });
                world.Add(built, new Owner { Port = port });
            }

            // A pile for every good, reserving what goods.csv says to. Created up front rather
            // than on demand because a pile that appeared later would start with a reserve of
            // nothing and quietly sell the port's own bread — and because the reserve is now
            // state the player edits, so it has to exist before they can edit it.
            for (int i = 0; i < balance.Goods.Count; i++)
                Port.SetReserve(world, port, i, balance.Goods[i].Keep);

            if (!isPlayer) HoldTradingStock(world, port, balance);

            foreach (KeyValuePair<string, float> pile in Stock)
            {
                int goodIndex = IndexOf(balance.Goods, pile.Key, "good");
                Port.Add(world, port, goodIndex, pile.Value);
            }

            Assign(world, port, balance);

            // One per stratum, in file order, so a port always has the same strata in the same
            // order regardless of what happens to it later.
            for (int i = 0; i < balance.Strata.Count; i++)
            {
                EntityId stratum = world.CreateEntity();
                world.Add(stratum, new Grievance { StratumIndex = i, Value = 0f, Baseline = 0f });
                world.Add(stratum, new Owner { Port = port });
            }

            // Every port draws attention, including one whose content mentions no strata: Heat
            // is read off wealth rather than off anger, and the two pressures are independent by
            // design (§5.2).
            EntityId attention = world.CreateEntity();
            world.Add(attention, new Heat { Value = 0f, Drawn = 0f, DaysSinceRaid = 0 });
            world.Add(attention, new Owner { Port = port });

            if (balance.Ladder.Count > 0)
            {
                EntityId ladder = world.CreateEntity();
                world.Add(ladder, new RevolutionLadder
                {
                    Rung = LadderRung.Calm,
                    DaysAtRung = 0,
                    LeadingStratumIndex = 0,
                });
                world.Add(ladder, new Owner { Port = port });
            }

            return port;
        }

        /// <summary>
        /// A neighbour keeps back a shippable stock of its main export (GDD §5.3).
        /// </summary>
        /// <remarks>
        /// Demand alone was not enough. Once repairs started costing materials every city wanted
        /// timber, and none of them could get any — because the passing merchant buys everything
        /// above a port's reserve, so every city sat at exactly its keep with nothing spare to
        /// ship. Ironhold, which has two mines and no sawmill, decayed to nothing over a hundred
        /// and twenty days with fourteen hundred coin in the treasury and nobody able to sell it
        /// a plank.
        /// <para>
        /// One export, not everything it makes. Holding back three goods at once cost Coldwater —
        /// the poorest city, with the least margin — enough income while it filled its sheds to
        /// miss a payday, lose its crew and starve with two idle farms. One export is also what
        /// §5.3 describes: a port leans towards something rather than hoarding a bit of
        /// everything.
        /// </para>
        /// <para>
        /// It costs a producer nothing in the long run. The merchant still takes everything above
        /// the reserve, so raising it moves a few units into the warehouse once and leaves the
        /// daily income where it was.
        /// </para>
        /// <para>
        /// Only neighbours: the player's reserves are the player's decision (§5.5), and a
        /// scenario that quietly set them would be making it for them.
        /// </para>
        /// </remarks>
        private void HoldTradingStock(World world, EntityId port, BalanceTables balance)
        {
            // What it has left over, not what it makes most of. Millrace runs two sawmills and
            // two farms, so its largest output is food — but it eats nearly all of that and uses
            // none of the timber, and timber is what Ironhold is desperate for. Picking by raw
            // output had it hoarding grain while the city three days away decayed for want of
            // planks.
            var surplus = new Dictionary<int, float>();

            ComponentStore<BuildingState> buildings = world.Store<BuildingState>();
            for (int i = 0; i < buildings.Count; i++)
            {
                if (!Port.BelongsTo(world, buildings.Ids[i], port)) continue;

                Building definition = balance.Buildings[buildings.Values[i].DefinitionIndex];

                if (!string.IsNullOrEmpty(definition.Produces))
                {
                    int made = ConsumptionSystem.IndexOf(balance, definition.Produces);
                    if (made >= 0) Add(surplus, made, definition.OutputPerDay);
                }

                foreach (KeyValuePair<string, float> input in definition.Consumes)
                {
                    int eaten = ConsumptionSystem.IndexOf(balance, input.Key);
                    if (eaten >= 0) Add(surplus, eaten, -input.Value);
                }
            }

            // The people eat too, and they eat the thing most cities produce most of.
            int food = ConsumptionSystem.IndexOf(balance, MarketSystem.BuyableGood);
            if (food >= 0)
            {
                float eaten = 0f;

                StratumRules townsfolk = ConsumptionSystem.RulesFor(balance, Stratum.Commoners);
                if (townsfolk != null) eaten += StartingCommoners * townsfolk.FoodPerDay;

                foreach (KeyValuePair<string, int> hired in Crew)
                {
                    int role = IndexOf(balance.CrewRoles, hired.Key, "crew role");
                    eaten += balance.CrewRoles[role].FoodPerDay * hired.Value;
                }

                Add(surplus, food, -eaten);
            }

            int best = -1;
            float most = 0f;

            for (int good = 0; good < balance.Goods.Count; good++)
            {
                if (!surplus.TryGetValue(good, out float spare)) continue;

                // Ties by good order, so the same content always picks the same export.
                if (spare <= most) continue;

                most = spare;
                best = good;
            }

            if (best < 0) return;

            Port.SetReserve(world, port, best,
                balance.Goods[best].Keep + balance.TradeAi.Spare + balance.TradeAi.Parcel);
        }

        private static void Add(Dictionary<int, float> into, int good, float amount)
        {
            into.TryGetValue(good, out float running);
            into[good] = running + amount;
        }

        /// <summary>
        /// Puts the crew to work, filling each producer to its staff requirement in build order
        /// before moving to the next.
        /// </summary>
        /// <remarks>
        /// Deterministic and dull on purpose: creation order in, creation order out, so a
        /// scenario assigns the same people to the same buildings in every replay. Anyone left
        /// over is idle — still fed, still paid, producing nothing — which is the honest cost of
        /// hiring more crew than there is work for.
        /// <para>
        /// This is a starting arrangement, not a policy. Changing it during play is the
        /// <c>AssignCrew</c> command of §6, which does not exist yet.
        /// </para>
        /// </remarks>
        private static void Assign(World world, EntityId port, BalanceTables balance)
        {
            ComponentStore<CrewMember> crew = world.Store<CrewMember>();
            ComponentStore<BuildingState> buildings = world.Store<BuildingState>();

            // This port's own people and its own buildings. A world of several cities has one
            // crew store and one building store, so without the filter Ironhold's miners would
            // be posted to Saltmarsh's farms.
            var mine = new System.Collections.Generic.List<EntityId>();
            for (int i = 0; i < crew.Count; i++)
                if (Port.BelongsTo(world, crew.Ids[i], port)) mine.Add(crew.Ids[i]);

            int nextWorker = 0;

            for (int b = 0; b < buildings.Count; b++)
            {
                if (!Port.BelongsTo(world, buildings.Ids[b], port)) continue;

                Building definition = balance.Buildings[buildings.Values[b].DefinitionIndex];
                if (!definition.IsProducer || definition.Staff <= 0) continue;

                for (int slot = 0; slot < definition.Staff && nextWorker < mine.Count; slot++)
                {
                    world.Add(mine[nextWorker], new Assignment { Building = buildings.Ids[b] });
                    nextWorker++;
                }
            }

            // Everyone else is idle, recorded explicitly rather than by absence so the digest
            // shows the whole crew and a later reassignment has something to overwrite.
            for (int i = nextWorker; i < mine.Count; i++)
                world.Add(mine[i], new Assignment { Building = EntityId.None });
        }

        private static int IndexOf<T>(ConfigRegistry<T> registry, string id, string what)
            where T : IHasId
        {
            for (int i = 0; i < registry.Count; i++)
                if (registry[i].Id == id) return i;

            throw new ArgumentException($"No {what} named '{id}' in {registry.SourceName}.", nameof(id));
        }
    }
}
