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
        public World Build(BalanceTables balance)
        {
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            var world = new World();

            EntityId treasury = world.CreateEntity();
            world.Add(treasury, new Treasury { Coin = StartingCoin });

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
            }

            foreach (KeyValuePair<string, float> pile in Stock)
            {
                int goodIndex = IndexOf(balance.Goods, pile.Key, "good");
                Port.Add(world, goodIndex, pile.Value);
            }

            Assign(world, balance);

            // One per stratum, in file order, so a port always has the same strata in the same
            // order regardless of what happens to it later.
            for (int i = 0; i < balance.Strata.Count; i++)
            {
                EntityId stratum = world.CreateEntity();
                world.Add(stratum, new Grievance { StratumIndex = i, Value = 0f, Baseline = 0f });
            }

            if (balance.Ladder.Count > 0)
            {
                EntityId ladder = world.CreateEntity();
                world.Add(ladder, new RevolutionLadder
                {
                    Rung = LadderRung.Calm,
                    DaysAtRung = 0,
                    LeadingStratumIndex = 0,
                });
            }

            return world;
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
        private static void Assign(World world, BalanceTables balance)
        {
            ComponentStore<CrewMember> crew = world.Store<CrewMember>();
            ComponentStore<BuildingState> buildings = world.Store<BuildingState>();

            int nextWorker = 0;

            for (int b = 0; b < buildings.Count; b++)
            {
                Building definition = balance.Buildings[buildings.Values[b].DefinitionIndex];
                if (!definition.IsProducer || definition.Staff <= 0) continue;

                for (int slot = 0; slot < definition.Staff && nextWorker < crew.Count; slot++)
                {
                    world.Add(crew.Ids[nextWorker], new Assignment { Building = buildings.Ids[b] });
                    nextWorker++;
                }
            }

            // Everyone else is idle, recorded explicitly rather than by absence so the digest
            // shows the whole crew and a later reassignment has something to overwrite.
            for (int i = nextWorker; i < crew.Count; i++)
                world.Add(crew.Ids[i], new Assignment { Building = EntityId.None });
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
