using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;
using RTS.Sim.Engine.Events;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Engine.Randomness;
using RTS.Sim.Scenarios;
using RTS.Sim.Session;
using RTS.Sim.Systems;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// Rung 5 as a crowd (GDD §5.2.2, §6.4; BUILD_ORDER Phase 5).
    /// </summary>
    /// <remarks>
    /// The phase's gate is that the revolt reads as an event rather than a number. What can be
    /// asserted headlessly is everything underneath that: who comes out, who stands where, that
    /// they move and stop, and that they go home when the port calms down. Whether it *reads*
    /// is a question for a person watching it, which is the other half of the gate.
    /// </remarks>
    [Category(TestCategories.Functional)]
    public class MobTests
    {
        private static string BalancePath(string file) =>
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Balance", file);

        private static CsvTable Table(string file) =>
            CsvTable.Parse(File.ReadAllText(BalancePath(file)), file);

        private static BalanceTables Balance()
        {
            var report = new ValidationReport();
            BalanceTables tables = BalanceTables.Load(new BalanceSources
            {
                Goods = Table(BalanceTables.GoodsFile),
                Buildings = Table(BalanceTables.BuildingsFile),
                CrewRoles = Table(BalanceTables.CrewRolesFile),
                Strata = Table(BalanceTables.StrataFile),
                Ladder = Table(BalanceTables.LadderFile),
                Repression = Table(BalanceTables.RepressionFile),
                Ports = Table(BalanceTables.PortsFile),
                Mob = Table(BalanceTables.MobFile),
            }, report);

            report.ThrowIfInvalid();
            return tables;
        }

        /// <summary>
        /// A port sitting on a chosen rung, with a chosen crew and population.
        /// </summary>
        /// <remarks>
        /// Built by hand rather than driven up the ladder with sixty days of theft. The gate
        /// tests already prove the climb; what is under test here is what happens once the port
        /// is there, and reaching Uprising the long way would make every one of these depend on
        /// the balance of a file it is not about.
        /// </remarks>
        private sealed class Square
        {
            public readonly BalanceTables Tables = Balance();
            public readonly World World = new World();
            public readonly EventQueue Events = new EventQueue();
            public readonly EntityId Port;

            private readonly MobSystem _system = new MobSystem();
            private readonly Rng _rng = new Rng(1);

            public Square(int commoners = 12, LadderRung rung = LadderRung.Uprising,
                params float[] loyalties)
            {
                Port = World.CreateEntity();
                World.Add(Port, new PortState { DefinitionIndex = 0, IsPlayer = true });

                // Hung off the port rather than set on it, which is where the rest of the sim
                // puts a population and a ladder. A fixture that put them on the port entity
                // would be testing a world the game never builds.
                Own(World.CreateEntity(), new Population { Commoners = commoners });
                Ladder = Own(World.CreateEntity(), new RevolutionLadder { Rung = rung });

                foreach (float loyalty in loyalties)
                {
                    EntityId member = World.CreateEntity();
                    World.Add(member, new CrewMember { RoleIndex = 0, Morale = 1f, Loyalty = loyalty });
                    World.Add(member, new Owner { Port = Port });
                }
            }

            public EntityId Ladder { get; }

            private EntityId Own<T>(EntityId entity, T component) where T : struct, IComponentData
            {
                World.Add(entity, component);
                World.Add(entity, new Owner { Port = Port });
                return entity;
            }

            public void SetRung(LadderRung rung) =>
                World.Store<RevolutionLadder>().GetRef(Ladder).Rung = rung;

            public void Day(int count = 1)
            {
                for (int i = 0; i < count; i++)
                {
                    Events.BeginCause(CauseId.Root, 1);
                    try
                    {
                        var ctx = new Context(1, 0f, Events, _rng, Tables);
                        _system.Run(World, in ctx);
                    }
                    finally
                    {
                        Events.EndCause();
                    }
                }
            }

            public int Bodies => MobSystem.Bodies(World, Port);

            public int Loyalists => MobSystem.Loyalists(World, Port);

            public IEnumerable<MobAgent> Agents()
            {
                ComponentStore<MobAgent> agents = World.Store<MobAgent>();
                for (int i = 0; i < agents.Count; i++) yield return agents.Values[i];
            }

            public bool Emitted<T>() where T : struct =>
                Events.Pending.Any(e => e.Is<T>());

            public IEnumerable<T> All<T>() where T : struct =>
                Events.Pending.Where(e => e.Is<T>()).Select(e => e.Get<T>());
        }

        private static float Distance(in MobAgent agent) =>
            (float)Math.Sqrt((agent.X * agent.X) + (agent.Y * agent.Y));

        // ------------------------------------------------------------------ mustering

        [Test]
        public void Nobody_is_in_the_square_until_the_port_rises()
        {
            // Every rung below has its own character (§5.2.2) and none of them is a crowd. A
            // mob that turned out for a slowdown would make the ladder's top rung mean nothing.
            foreach (LadderRung rung in new[]
                     {
                         LadderRung.Calm, LadderRung.Grumbling, LadderRung.Slowdown,
                         LadderRung.Agitator, LadderRung.Riot,
                     })
            {
                var square = new Square(rung: rung);
                square.Day();

                Assert.That(square.Bodies, Is.Zero, rung.ToString());
            }
        }

        [Test]
        public void An_uprising_puts_the_commoners_on_the_street()
        {
            var square = new Square(commoners: 12);
            square.Day();

            Assert.That(square.Bodies, Is.EqualTo(12));
            Assert.That(square.Emitted<MobMustered>(), Is.True);
        }

        [Test]
        public void A_port_with_nobody_left_in_it_has_nobody_to_riot_with()
        {
            // The Phase 2 gate found this from the other end: a ruin with no population cannot
            // be angry. It should not be able to produce a crowd out of nothing either.
            var square = new Square(commoners: 0);
            square.Day();

            Assert.That(square.Bodies, Is.Zero);
        }

        [Test]
        public void The_crowd_is_capped_however_large_the_port()
        {
            // §8.1: dozens first, hundreds only once the small version is proven. The cap is
            // the instruction made mechanical.
            var square = new Square(commoners: 100000);
            square.Day();

            Assert.That(square.Bodies, Is.EqualTo(square.Tables.Mob.MaximumBodies));
        }

        [Test]
        public void They_only_turn_out_once()
        {
            var square = new Square(commoners: 12);
            square.Day(5);

            Assert.That(square.Bodies, Is.EqualTo(12), "the same crowd, not five of them");
            Assert.That(square.All<MobMustered>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void They_go_home_when_the_port_comes_down_the_ladder()
        {
            // §5.2.2: every rung has an exit, and the Phase 2 gate is that a revolt can be
            // pulled back out of. A crowd that stayed on the street after the port calmed down
            // would make the exit a lie on screen while the numbers said otherwise.
            var square = new Square(commoners: 12);
            square.Day();
            Assert.That(square.Bodies, Is.EqualTo(12));

            square.SetRung(LadderRung.Riot);
            square.Day();

            Assert.That(square.Bodies, Is.Zero);
            Assert.That(square.Emitted<MobDispersed>(), Is.True);
        }

        [Test]
        public void Deposition_keeps_them_where_they_are()
        {
            // The failure state is the crowd's, not an empty square. Deposition is above
            // Uprising on the ladder and the mob is what put it there.
            var square = new Square(commoners: 12);
            square.Day();

            square.SetRung(LadderRung.Deposition);
            square.Day();

            Assert.That(square.Bodies, Is.EqualTo(12));
        }

        // ------------------------------------------------------------------ named faces

        [Test]
        public void Named_crew_choose_sides_one_at_a_time()
        {
            // §5.2.2's sentence, and the reason §5.4 keeps loyalty separate from morale. All
            // four of these are equally well fed; two of them still walk out.
            var square = new Square(12, LadderRung.Uprising, 0.9f, 0.8f, 0.2f, 0.1f);
            square.Day();

            CrewChoseSide[] chose = square.All<CrewChoseSide>().ToArray();

            Assert.That(chose.Length, Is.EqualTo(4));
            Assert.That(chose.Count(c => c.Side == MobSide.Loyalist), Is.EqualTo(2));
            Assert.That(chose.Count(c => c.Side == MobSide.Rioter), Is.EqualTo(2));
        }

        [Test]
        public void A_face_in_the_crowd_is_the_crew_member_who_stood_there()
        {
            // Not a separate drawing beside the crowd: the same component, so whatever happens
            // to a body can happen to a named one.
            var square = new Square(0, LadderRung.Uprising, 0.1f);
            square.Day();

            MobAgent[] agents = square.Agents().ToArray();

            Assert.That(agents.Length, Is.EqualTo(1));
            Assert.That(agents[0].Crew.IsNone, Is.False);
            Assert.That(square.World.Has<CrewMember>(agents[0].Crew), Is.True);
        }

        [Test]
        public void Anonymous_bodies_are_anonymous()
        {
            var square = new Square(commoners: 6);
            square.Day();

            Assert.That(square.Agents().All(a => a.Crew.IsNone), Is.True);
        }

        [Test]
        public void The_loyal_stand_between_the_crowd_and_the_longhouse()
        {
            var square = new Square(6, LadderRung.Uprising, 0.9f);
            square.Day();

            Assert.That(square.Loyalists, Is.EqualTo(1));

            MobAgent loyalist = square.Agents().First(a => a.Side == MobSide.Loyalist);
            Assert.That(Distance(in loyalist),
                Is.LessThanOrEqualTo(square.Tables.Mob.PressRadius + 0.01f));
        }

        // --------------------------------------------------------------------- moving

        [Test]
        public void The_crowd_closes_on_the_longhouse()
        {
            var square = new Square(commoners: 12);
            square.Day();

            float before = square.Agents().Average(a => Distance(in a));
            square.Day();
            float after = square.Agents().Average(a => Distance(in a));

            Assert.That(after, Is.LessThan(before));
        }

        [Test]
        public void It_stops_at_the_line_rather_than_piling_into_a_point()
        {
            // A crowd that converged on one coordinate would draw as a single dot, which is the
            // number again with extra steps.
            var square = new Square(commoners: 20);
            square.Day(12);

            Assert.That(square.Agents().Select(a => Distance(in a)).Max(),
                Is.LessThan(square.Tables.Mob.MusterRadius));
            Assert.That(square.Agents().Select(a => Distance(in a)).Min(),
                Is.GreaterThan(0.1f), "nobody is standing inside the longhouse");
        }

        [Test]
        public void Nobody_stands_inside_anybody_else()
        {
            var square = new Square(commoners: 24);
            square.Day(12);

            MobAgent[] agents = square.Agents().ToArray();

            for (int i = 0; i < agents.Length; i++)
            {
                for (int j = i + 1; j < agents.Length; j++)
                {
                    float dx = agents[i].X - agents[j].X;
                    float dy = agents[i].Y - agents[j].Y;

                    Assert.That(Math.Sqrt((dx * dx) + (dy * dy)), Is.GreaterThan(0.05f),
                        $"{i} and {j} are in the same spot");
                }
            }
        }

        [Test]
        public void Yesterdays_position_is_kept_so_a_renderer_can_interpolate()
        {
            // The sim moves in whole days. Without this the crowd teleports once a day, and a
            // revolt that teleports is a report rather than an event.
            var square = new Square(commoners: 12);
            square.Day(2);

            Assert.That(square.Agents().Any(a => Math.Abs(a.X - a.PreviousX) > 1e-4f ||
                                                 Math.Abs(a.Y - a.PreviousY) > 1e-4f), Is.True);
        }

        [Test]
        public void Two_cities_rioting_at_once_do_not_shove_each_other()
        {
            // Every body carries an Owner for exactly this. Positions are offsets from their own
            // port, so without the check the two crowds would occupy the same coordinates and
            // elbow each other across the sea.
            var square = new Square(commoners: 10);

            EntityId other = square.World.CreateEntity();
            square.World.Add(other, new PortState { DefinitionIndex = 1 });

            EntityId people = square.World.CreateEntity();
            square.World.Add(people, new Population { Commoners = 10 });
            square.World.Add(people, new Owner { Port = other });

            EntityId ladder = square.World.CreateEntity();
            square.World.Add(ladder, new RevolutionLadder { Rung = LadderRung.Uprising });
            square.World.Add(ladder, new Owner { Port = other });

            square.Day(4);

            Assert.That(MobSystem.Bodies(square.World, square.Port), Is.EqualTo(10));
            Assert.That(MobSystem.Bodies(square.World, other), Is.EqualTo(10));
        }

        [Test]
        public void The_same_seed_produces_the_same_crowd()
        {
            // Positions are world state and go into the digest, so a crowd that differed between
            // runs would break replay. This is the reason the mob steps in days rather than on
            // frames.
            var a = new Square(commoners: 20);
            var b = new Square(commoners: 20);

            a.Day(6);
            b.Day(6);

            float[] left = a.Agents().Select(x => x.X + x.Y).ToArray();
            float[] right = b.Agents().Select(x => x.X + x.Y).ToArray();

            Assert.That(left, Is.EqualTo(right));
        }

        // ------------------------------------------------------------- the whole way

        [Test]
        public void A_port_robbed_every_day_ends_up_with_its_people_in_the_square()
        {
            // Through the real pipeline, from a working port to a crowd, with nothing set by
            // hand. The fixtures above start the port on the rung; this is the one that proves
            // the row in pipeline.csv, the ladder and the mob are actually joined up — and it
            // is the shape of the failure that hid a broken build for three phases.
            BalanceTables tables = Balance();
            PortScenario scenario = PortScenario.Default();
            scenario.StartingCoin = 150;

            ReplayRun run = ReplayRun.Start(
                seed: 1,
                GameSession.PlayerCommands(),
                dispatcher => ScenarioRunner.BuildPipeline(
                    File.ReadAllText(BalancePath("pipeline.csv")), dispatcher),
                scenario.Build(tables),
                tables);

            EntityId port = Port.Player(run.World);

            for (int day = 0; day < 90; day++)
            {
                run.Submit(new Shock(ShockKind.Theft, 100000f));
                run.AdvanceDay();
                run.Events.Drain();

                if (MobSystem.Bodies(run.World, port) > 0) break;
            }

            Assert.That(RevolutionLadderSystem.RungOf(run.World, port),
                Is.GreaterThanOrEqualTo(MobSystem.MustersAt));
            Assert.That(MobSystem.Bodies(run.World, port), Is.GreaterThan(0),
                "the port rose and nobody came out");
        }

        // ---------------------------------------------------------------- the budget

        [Test]
        [Category(TestCategories.Flaky)]
        public void A_full_crowd_costs_a_measurable_and_small_amount_of_a_day()
        {
            // §8.1: measure before optimising anything, and dozens may simply be enough. This is
            // the measurement. It is not a threshold anybody should tune towards — it is here so
            // that the day a flow field is proposed, the number it has to beat is written down.
            var square = new Square(commoners: 60);
            square.Day();

            var watch = Stopwatch.StartNew();
            square.Day(100);
            watch.Stop();

            double perDay = watch.Elapsed.TotalMilliseconds / 100.0;
            TestContext.Out.WriteLine(
                $"{square.Bodies} bodies, {square.Tables.Mob.StepsPerDay} steps: " +
                $"{perDay:0.000} ms/day");

            Assert.That(perDay, Is.LessThan(20.0),
                "dozens of agents should not be anywhere near a frame's worth of a day");
        }
    }
}
