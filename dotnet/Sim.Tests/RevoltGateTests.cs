using System.Collections.Generic;
using System.IO;
using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Engine.State;
using RTS.Sim.Scenarios;
using RTS.Sim.Systems;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// The Phase 2 gate (BUILD_ORDER §2): a port can be driven into revolt, and pulled back
    /// out, by playing the numbers. Both directions must work.
    /// </summary>
    /// <remarks>
    /// "A ladder that only ever climbs is a timer wearing a costume." The climb is the easy
    /// half — anything that only ever goes up would pass it. The tests below that matter are
    /// the ones going back down, and there are two routes: fix the economy so grievance decays
    /// on its own, or pay for repression and carry the floor afterwards. The gate exercises
    /// both, and asserts they are not the same thing.
    /// <para>
    /// Unlike the unit tests for <see cref="RevolutionLadderSystem"/>, nothing here writes a
    /// grievance value directly. Every number in these tests is produced by the full pipeline
    /// out of player-visible causes — theft, unpaid wages, hunger — because a ladder that can
    /// only be moved by the test harness is not a mechanic.
    /// </para>
    /// </remarks>
    [Category(TestCategories.Functional)]
    public class RevoltGateTests
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
            }, report);

            report.ThrowIfInvalid();
            return tables;
        }

        /// <summary>A run of the shipped content the test can steer a day at a time.</summary>
        private sealed class Run
        {
            public readonly BalanceTables Tables = Balance();
            public readonly ReplayRun Replay;

            public Run(int startingCoin = 150)
            {
                PortScenario scenario = PortScenario.Default();
                scenario.StartingCoin = startingCoin;

                Replay = ReplayRun.Start(
                    seed: 1,
                    new ICommandHandler[]
                    {
                        new ShockHandler(), new SuppressRiotHandler(),
                        new AssignCrewHandler(), new MothballBuildingHandler(),
                    },
                    dispatcher => ScenarioRunner.BuildPipeline(
                        File.ReadAllText(BalancePath("pipeline.csv")), dispatcher),
                    scenario.Build(Tables),
                    Tables);
            }

            public World World => Replay.World;

            public LadderRung Rung => World.Store<RevolutionLadder>().Values[0].Rung;

            public float WorstGrievance => Worst(g => g.Value);

            /// <summary>The permanent floor grievance decays towards. Repression raises it.</summary>
            public float Floor => Worst(g => g.Baseline);

            private float Worst(System.Func<Grievance, float> pick)
            {
                ComponentStore<Grievance> store = World.Store<Grievance>();
                float worst = 0f;
                for (int i = 0; i < store.Count; i++)
                {
                    float value = pick(store.Values[i]);
                    if (value > worst) worst = value;
                }

                return worst;
            }

            public int Coin => World.Store<Treasury>().Values[0].Coin;

            public void Advance(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    Replay.AdvanceDay();
                    Replay.Events.Drain();
                }
            }

            /// <summary>Takes every coin in the treasury, so the next payday goes unmet.</summary>
            public void Rob() => Replay.Submit(new Shock(ShockKind.Theft, 100000f));

            /// <summary>
            /// Puts coin back, standing in for a windfall the player earned elsewhere. This is
            /// the "fix the economy" lever in its simplest form: the port can pay its people.
            /// </summary>
            public void Fund(int coin) => Port.Treasury(World, Port.Player(World)).Coin += coin;

            /// <summary>
            /// Robs the port daily until it riots, and stops there. Deposition is terminal and
            /// would make the recovery half of the gate untestable.
            /// </summary>
            /// <remarks>
            /// This counts days rather than asserting a fixed number of them, because how long
            /// the climb takes is a balance decision in ladder.csv. The gate is that the climb
            /// happens and can be undone, not that it takes any particular number of days.
            /// </remarks>
            public Run DriveIntoRevolt()
            {
                for (Days = 1; Days <= 60; Days++)
                {
                    Rob();
                    Advance(1);
                    if (Rung == LadderRung.Riot) return this;
                    Assert.That(Rung, Is.Not.EqualTo(LadderRung.Deposition),
                        $"deposed on day {Days} without ever stopping at a riot");
                }

                Assert.Fail("sixty days of total theft and the port never rioted: " + this);
                return this;
            }

            /// <summary>Days spent driving the port up. Set by <see cref="DriveIntoRevolt"/>.</summary>
            public int Days { get; private set; }

            public override string ToString() =>
                $"{Rung} (grievance {WorstGrievance:0.00}, floor {Floor:0.00}, coin {Coin})";
        }

        // ------------------------------------------------------------------------ up

        [Test]
        public void Mismanagement_drives_a_port_into_revolt()
        {
            // Nothing here scripts a riot. Coin is stolen, wages go unpaid, and the grievance
            // that follows is what the ladder reads.
            var run = new Run();

            Assert.That(run.Rung, Is.EqualTo(LadderRung.Calm), "a fresh port starts calm");

            run.DriveIntoRevolt();

            Assert.That(run.WorstGrievance, Is.GreaterThan(0.7f), run.ToString());
        }

        [Test]
        public void The_climb_shows_every_rung_on_the_way_up()
        {
            // §5.2.2 wants each rung visible so the player can act before the next one. Jumping
            // Calm straight to Riot would be an event, not a ladder.
            var run = new Run();
            var seen = new List<LadderRung>();

            for (int day = 0; day < 60 && run.Rung != LadderRung.Riot; day++)
            {
                run.Rob();
                run.Advance(1);
                if (seen.Count == 0 || seen[seen.Count - 1] != run.Rung) seen.Add(run.Rung);
            }

            Assert.That(seen, Is.EqualTo(new[]
            {
                LadderRung.Grumbling, LadderRung.Slowdown, LadderRung.Agitator, LadderRung.Riot,
            }));
        }

        [Test]
        public void Every_rung_lasts_long_enough_to_be_acted_on()
        {
            // A rung the player sees for one frame of a day is decoration. Each one has to be
            // held long enough that noticing it is worth something — which is what days_to_climb
            // in ladder.csv buys, and the reason the climb is not one rung a day.
            var run = new Run().DriveIntoRevolt();

            Assert.That(run.Days, Is.GreaterThanOrEqualTo(8),
                $"Calm to Riot in {run.Days} days under total theft is not time to react");
        }

        // ---------------------------------------------------------------------- down

        [Test]
        public void Fixing_what_caused_it_pulls_the_port_back_out()
        {
            // The half of the gate that matters, and the one a ladder-shaped timer would fail.
            // Stop robbing it, let it pay its people, and it comes back down unaided — no
            // repression, no permanent mark, and the crew still there at the end.
            //
            // That last assertion is not decoration. Without it this test passed while the port
            // emptied completely: a ruin with nobody left in it also reads as calm. Recovery has
            // to mean the port survived, not that the aggrieved left.
            var run = new Run();

            RobUntil(run, LadderRung.Slowdown);
            int crewBefore = run.World.Store<CrewMember>().Count;

            run.Fund(3000);
            run.Advance(20);

            Assert.That(run.Rung, Is.EqualTo(LadderRung.Calm), run.ToString());
            Assert.That(run.Floor, Is.EqualTo(0f).Within(1e-4f),
                "fixing the cause leaves nothing behind");
            Assert.That(run.World.Store<CrewMember>().Count, Is.EqualTo(crewBefore),
                "calm because the port recovered, not because everyone left");
        }

        [Test]
        public void Past_a_riot_coin_alone_cannot_save_the_port()
        {
            // Where the economic exit runs out, and the reason repression is a decision rather
            // than a formality.
            //
            // A rioting port produces 35% of its output (ladder.csv). Two farms at six a day
            // become four, and seven crew eat seven — so the port starves however much coin it
            // has, because coin does not buy food. Money fixes the symptom the theft caused; it
            // does not fix a port that has stopped working.
            var run = new Run().DriveIntoRevolt();

            run.Fund(100000);
            run.Advance(40);

            Assert.That(run.World.Store<CrewMember>().Count, Is.Zero,
                "a hundred thousand coin and the crew still left: " + run);
        }

        [Test]
        public void Repression_is_the_other_way_down_and_it_is_faster()
        {
            // Otherwise there is no reason to ever pay its price.
            var repressed = new Run().DriveIntoRevolt();
            var patient = new Run().DriveIntoRevolt();

            repressed.Fund(3000);
            patient.Fund(3000);
            repressed.Replay.Submit(new SuppressRiot(Harshness.Brutal));

            int withForce = DaysUntilBelowRiot(repressed);
            int withoutForce = DaysUntilBelowRiot(patient);

            Assert.That(withForce, Is.LessThan(withoutForce),
                $"force took {withForce} days, patience {withoutForce}");
        }

        [Test]
        public void Repression_leaves_a_mark_that_feeding_them_does_not()
        {
            // Both routes end the riot. Only one of them is free, and the price is a floor the
            // port carries for the rest of the game (§5.2.2).
            var repressed = new Run().DriveIntoRevolt();
            var fed = new Run().DriveIntoRevolt();

            repressed.Replay.Submit(new SuppressRiot(Harshness.Brutal));
            repressed.Fund(3000);
            fed.Fund(3000);

            // Long enough for both to settle. A rioting port produces a third of its food, so
            // the one left to recover on its own has to buy its way back to a full store before
            // it can start cooling — which is slower than a crackdown and is meant to be.
            repressed.Advance(100);
            fed.Advance(100);

            Assert.That(repressed.Rung, Is.EqualTo(LadderRung.Calm), "repressed: " + repressed);
            Assert.That(fed.Rung, Is.EqualTo(LadderRung.Calm), "fed: " + fed);

            Assert.That(repressed.Floor, Is.GreaterThan(fed.Floor),
                "the repressed port carries a floor the fed one does not");
            Assert.That(repressed.WorstGrievance, Is.GreaterThan(fed.WorstGrievance),
                "and never becomes as quiet again");
        }

        [Test]
        public void A_port_pulled_back_out_can_be_driven_in_again()
        {
            // The ladder is a state machine, not a one-shot. A player who fixes a port and then
            // ruins it again gets the same consequences.
            //
            // This stops at Slowdown both times rather than at Riot, because a riot costs the
            // port its crew and there is no way to hire anyone back yet — the second climb would
            // be measuring the missing population instead of the ladder.
            var run = new Run();

            RobUntil(run, LadderRung.Slowdown);
            run.Fund(3000);
            run.Advance(20);
            Assert.That(run.Rung, Is.EqualTo(LadderRung.Calm), "not recovered: " + run);

            RobUntil(run, LadderRung.Slowdown);

            Assert.That(run.Rung, Is.EqualTo(LadderRung.Slowdown), run.ToString());
        }

        // ------------------------------------------------------------------ terminal

        [Test]
        public void Total_mismanagement_ends_in_deposition()
        {
            // This test and the next replace a deliberately-wrong one. Before the strata had
            // populations of their own, every grievance pressure was a count of crew: rob the
            // port and the crew deserted by day twelve, grievance lost its source, and the
            // ladder walked back down to Calm on a ruin with nobody left in it. Deposition was
            // unreachable from play and the flagship system reported that all was well.
            //
            // A port with a town in it still has somebody to be angry.
            var run = new Run();

            for (int day = 0; day < 60 && run.Rung != LadderRung.Deposition; day++)
            {
                run.Rob();
                run.Advance(1);
            }

            Assert.That(run.Rung, Is.EqualTo(LadderRung.Deposition), run.ToString());

            run.Fund(100000);
            run.Advance(60);

            Assert.That(run.Rung, Is.EqualTo(LadderRung.Deposition),
                "a hundred thousand coin changes nothing: " + run);
        }

        [Test]
        public void A_port_that_loses_its_crew_is_not_calm()
        {
            // The crew are the port's named professionals, not its population. Losing every one
            // of them is a catastrophe, and it used to read as peace.
            var run = new Run();

            for (int day = 0; day < 40 && run.World.Store<CrewMember>().Count > 0; day++)
            {
                run.Rob();
                run.Advance(1);
            }

            Assert.That(run.World.Store<CrewMember>().Count, Is.Zero, "the crew all deserted");
            Assert.That(run.World.Store<Population>().Values[0].Commoners, Is.GreaterThan(0),
                "the town is still there: commoners do not leave over a missed payday");
            Assert.That(run.Rung, Is.GreaterThanOrEqualTo(LadderRung.Riot), run.ToString());
        }

        // ----------------------------------------------------------- the third lever

        [Test]
        public void Mothballing_stops_the_bleeding()
        {
            // §5.2.3: "Deliberate downsizing must be a viable, respected strategy." A shut
            // building draws no upkeep, so a port that cannot pay its bills has something to do
            // about it besides waiting.
            //
            // The building shut here is one nobody is working. That is the honest measurement:
            // mothballing something staffed also stops its output, and the trade between upkeep
            // saved and goods lost is the player's judgement, not a property of the command.
            // An idle building still billing upkeep is pure waste, and closing it must pay.
            var running = new Run();
            var downsized = new Run();

            EntityId idle = Unstaffed(downsized);
            downsized.Replay.Submit(new MothballBuilding(idle, mothballed: true));

            running.Advance(10);
            downsized.Advance(10);

            Assert.That(downsized.Coin, Is.GreaterThan(running.Coin),
                $"downsized {downsized}, running {running}");
        }

        /// <summary>A building nobody is assigned to, which therefore produces nothing.</summary>
        private static EntityId Unstaffed(Run run)
        {
            ComponentStore<Assignment> assignments = run.World.Store<Assignment>();
            var worked = new HashSet<EntityId>();
            for (int i = 0; i < assignments.Count; i++) worked.Add(assignments.Values[i].Building);

            foreach (EntityId building in run.World.Store<BuildingState>().Ids.ToArray())
                if (!worked.Contains(building)) return building;

            Assert.Fail("the default port has no idle building to mothball");
            return EntityId.None;
        }

        [Test]
        public void Mothballing_releases_whoever_worked_there()
        {
            // Leaving them assigned to a place that produces nothing would be a silent waste —
            // still eating, still drawing wages, and the port believing they were employed.
            var run = new Run();
            EntityId building = run.World.Store<BuildingState>().Ids[0];

            run.Replay.Submit(new MothballBuilding(building, mothballed: true));
            run.Advance(1);

            Assert.That(run.World.Store<BuildingState>().GetRef(building).Mothballed, Is.True);

            ComponentStore<Assignment> assignments = run.World.Store<Assignment>();
            bool stillThere = false;
            for (int i = 0; i < assignments.Count; i++)
                if (assignments.Values[i].Building == building) stillThere = true;

            Assert.That(stillThere, Is.False, "shutting a building frees its crew");
        }

        private static void RobUntil(Run run, LadderRung rung)
        {
            for (int day = 1; day <= 30; day++)
            {
                run.Rob();
                run.Advance(1);
                if (run.Rung == rung) return;
            }

            Assert.Fail($"thirty days of theft and the port never reached {rung}: {run}");
        }

        private static int DaysUntilBelowRiot(Run run)
        {
            for (int day = 1; day <= 200; day++)
            {
                run.Advance(1);
                if (run.Rung < LadderRung.Riot) return day;
            }

            return int.MaxValue;
        }
    }
}
