using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Events;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Systems;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// The ladder as a state machine (GDD §5.2.2): every rung visible, every rung with an exit.
    /// </summary>
    [Category(TestCategories.Unit)]
    public class RevolutionLadderTests
    {
        private const string Goods = "id,base_price,volatility,heat_per_unit,supply,keep,sell_price\n" +
                                     "food,4,0.25,0.00,Local,0,1\n";

        private const string Buildings =
            "id,upkeep_coin,build_timber,build_iron,capacity,produces,output_per_day,staff\n" +
            "farm,1,0,0,0,food,6,1\n";

        private const string Crew = "id,wage_coin,work_rate,food_per_day,rum_per_day\n" +
                                    "laborer,2,1.00,1.0,0.00\n";

        private const string Strata =
            "id,decay_per_day,relief_per_day,food_per_day,leave_after_days,hunger_weight,unpaid_weight,desertion_weight,idle_weight\n" +
            "Commoners,0.04,0.12,0.00,0,0.10,0.02,0.03,0.02\n" +
            "NamedCrew,0.05,0.15,0.00,0,0.03,0.12,0.08,0.00\n" +
            "Merchants,0.06,0.18,0.00,0,0.00,0.00,0.00,0.00\n";

        private const string Ladder =
            "rung,climb_at,fall_below,days_to_climb,output_multiplier,condition_damage\n" +
            "Calm,0.00,0.00,1,1.00,0.00\n" +
            "Grumbling,0.35,0.25,1,1.00,0.00\n" +
            "Slowdown,0.50,0.40,1,0.75,0.00\n" +
            "Agitator,0.65,0.55,1,0.60,0.00\n" +
            "Riot,0.80,0.70,1,0.35,0.05\n" +
            "Uprising,0.92,0.85,1,0.10,0.10\n" +
            "Deposition,0.99,0.00,1,0.00,0.00\n";

        private World _world = null!;
        private BalanceTables _balance = null!;
        private EventQueue _events = null!;
        private EntityId _ladder;
        private EntityId _port;

        [SetUp]
        public void SetUp()
        {
            var report = new ValidationReport();
            _balance = BalanceTables.Load(new BalanceSources
            {
                Goods = CsvTable.Parse(Goods, "goods.csv"),
                Buildings = CsvTable.Parse(Buildings, "buildings.csv"),
                CrewRoles = CsvTable.Parse(Crew, "crew_roles.csv"),
                Strata = CsvTable.Parse(Strata, "strata.csv"),
                Ladder =
                CsvTable.Parse(Ladder, "ladder.csv"),
            }, report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));

            _world = new World();
            _events = new EventQueue();
            _port = TestPort.Create(_world);

            for (int i = 0; i < _balance.Strata.Count; i++)
            {
                EntityId entity = _world.CreateEntity();
                _world.Add(entity, new Grievance { StratumIndex = i, Value = 0f, Baseline = 0f });
                TestPort.Own(_world, entity, _port);
            }

            _ladder = _world.CreateEntity();
            _world.Add(_ladder, new RevolutionLadder { Rung = LadderRung.Calm });
            TestPort.Own(_world, _ladder, _port);
        }

        private void SetGrievance(Stratum stratum, float value)
        {
            int index = Enumerable.Range(0, _balance.Strata.Count)
                .First(i => _balance.Strata[i].Stratum == stratum);

            ComponentStore<Grievance> store = _world.Store<Grievance>();
            for (int i = 0; i < store.Count; i++)
            {
                if (store.Values[i].StratumIndex != index) continue;
                store.GetRef(store.Ids[i]).Value = value;
                return;
            }
        }

        private LadderRung Rung => _world.Store<RevolutionLadder>().Values[0].Rung;

        private int DaysAtRung => _world.Store<RevolutionLadder>().Values[0].DaysAtRung;

        private void RunDay()
        {
            _events.BeginCause(CauseId.Root, 1);
            try
            {
                var ctx = new Context(1, 0f, _events, rng: null, balance: _balance);
                new RevolutionLadderSystem().Run(_world, in ctx);
            }
            finally
            {
                _events.EndCause();
            }
        }

        private void RunDays(int days)
        {
            for (int i = 0; i < days; i++)
            {
                RunDay();
                _events.Drain();
            }
        }

        // -------------------------------------------------------------------- climbing

        [Test]
        public void A_calm_port_stays_calm()
        {
            RunDays(5);

            Assert.That(Rung, Is.EqualTo(LadderRung.Calm));
            Assert.That(DaysAtRung, Is.EqualTo(5));
        }

        [Test]
        public void The_ladder_never_skips_a_rung_however_bad_it_gets()
        {
            // Skipping rungs would be a spawn table. Every rung being visible is what gives a
            // player something to act on. This fixture sets every days_to_climb to 1, so the
            // climb is as fast as the ladder allows and still takes one rung at a time.
            SetGrievance(Stratum.NamedCrew, 0.95f);

            RunDay();
            Assert.That(Rung, Is.EqualTo(LadderRung.Grumbling));

            RunDay();
            Assert.That(Rung, Is.EqualTo(LadderRung.Slowdown));

            RunDay();
            Assert.That(Rung, Is.EqualTo(LadderRung.Agitator));
        }

        [Test]
        public void A_rung_must_be_held_before_the_next_one_is_earned()
        {
            // days_to_climb, and the reason the top of the ladder has exits at all. Grievance
            // saturates in a day and decays in fortieths, so with no dwell time a port that
            // reached Riot was deposed three days later whatever the player did.
            var report = new ValidationReport();
            BalanceTables slow = BalanceTables.Load(new BalanceSources
            {
                Goods = CsvTable.Parse(Goods, "goods.csv"),
                Buildings = CsvTable.Parse(Buildings, "buildings.csv"),
                CrewRoles = CsvTable.Parse(Crew, "crew_roles.csv"),
                Strata = CsvTable.Parse(Strata, "strata.csv"),
                Ladder =
                // Every rung paced at three days, so the table still satisfies the rule that a
                // ladder must not speed up as it gets worse.
                CsvTable.Parse(Ladder.Replace(",1,", ",3,"), "ladder.csv"),
            }, report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));

            Assert.That(RevolutionLadderSystem.Next(slow, LadderRung.Grumbling, 0.95f, daysAtRung: 2),
                Is.EqualTo(LadderRung.Grumbling), "two days is not the three the rung asks for");
            Assert.That(RevolutionLadderSystem.Next(slow, LadderRung.Grumbling, 0.95f, daysAtRung: 3),
                Is.EqualTo(LadderRung.Slowdown));
        }

        [Test]
        public void Falling_is_never_delayed()
        {
            // Only climbing is paced. A port whose cause has been fixed comes down as soon as
            // the numbers say so — the hysteresis already stops it flickering, and making the
            // way down as slow as the way up would undo the point of having a way down.
            Assert.That(RevolutionLadderSystem.Next(_balance, LadderRung.Riot, 0f, daysAtRung: 0),
                Is.EqualTo(LadderRung.Agitator));
        }

        [Test]
        public void The_angriest_stratum_drives_it_not_the_average()
        {
            // A port whose crew are furious is in trouble even if its commoners are content.
            // Averaging would let one contented group hide another's fury.
            SetGrievance(Stratum.NamedCrew, 0.90f);
            SetGrievance(Stratum.Commoners, 0f);
            SetGrievance(Stratum.Merchants, 0f);

            RunDay();

            Assert.That(Rung, Is.EqualTo(LadderRung.Grumbling));
        }

        [Test]
        public void Who_is_driving_it_is_recorded()
        {
            // Rung 3 is an agitator with a specific demand; they have to come from somewhere.
            SetGrievance(Stratum.Commoners, 0.90f);

            RunDay();

            int leading = _world.Store<RevolutionLadder>().Values[0].LeadingStratumIndex;
            Assert.That(_balance.Strata[leading].Stratum, Is.EqualTo(Stratum.Commoners));
        }

        [Test]
        public void A_move_is_reported_in_both_directions()
        {
            SetGrievance(Stratum.NamedCrew, 0.60f);
            RunDay();

            Assert.That(_events.Pending.Any(e => e.Is<LadderMoved>()), Is.True);
            LadderMoved up = _events.Pending.First(e => e.Is<LadderMoved>()).Get<LadderMoved>();
            Assert.That(up.From, Is.EqualTo(LadderRung.Calm));
            Assert.That(up.To, Is.EqualTo(LadderRung.Grumbling));

            _events.Drain();
            SetGrievance(Stratum.NamedCrew, 0f);
            RunDay();

            LadderMoved down = _events.Pending.First(e => e.Is<LadderMoved>()).Get<LadderMoved>();
            Assert.That(down.To, Is.EqualTo(LadderRung.Calm));
        }

        // --------------------------------------------------------------------- falling

        [Test]
        public void A_port_can_be_pulled_back_down_the_way_it_came()
        {
            // The Phase 2 gate in miniature. A ladder that only climbs is a timer in a costume.
            SetGrievance(Stratum.NamedCrew, 0.95f);
            RunDays(4);
            Assert.That(Rung, Is.EqualTo(LadderRung.Riot));

            SetGrievance(Stratum.NamedCrew, 0f);
            RunDays(4);

            Assert.That(Rung, Is.EqualTo(LadderRung.Calm));
        }

        [Test]
        public void Hysteresis_stops_a_port_on_the_boundary_flickering()
        {
            // Slowdown climbs at 0.50 and falls below 0.40. At 0.45 a port that has climbed
            // stays climbed, and a calm one stays calm — the same grievance, two answers,
            // which is the point of hysteresis.
            SetGrievance(Stratum.NamedCrew, 0.55f);
            RunDays(2);
            Assert.That(Rung, Is.EqualTo(LadderRung.Slowdown));

            SetGrievance(Stratum.NamedCrew, 0.45f);
            RunDays(5);

            Assert.That(Rung, Is.EqualTo(LadderRung.Slowdown),
                "0.45 is below the climb point but not below the fall point");
        }

        // -------------------------------------------------------------------- effects

        [Test]
        public void A_slowdown_is_worth_less_work()
        {
            SetGrievance(Stratum.NamedCrew, 0.55f);
            RunDays(2);

            Assert.That(Rung, Is.EqualTo(LadderRung.Slowdown));
            Assert.That(ProductionSystem.UnrestMultiplier(_world, _balance, _port),
                Is.EqualTo(0.75f).Within(1e-4f));
        }

        [Test]
        public void A_riot_damages_property()
        {
            EntityId farm = _world.CreateEntity();
            _world.Add(farm, new BuildingState { DefinitionIndex = 0, Condition = 1f });
            TestPort.Own(_world, farm, _port);

            SetGrievance(Stratum.NamedCrew, 0.85f);
            RunDays(4);

            Assert.That(Rung, Is.EqualTo(LadderRung.Riot));
            Assert.That(_world.Store<BuildingState>().GetRef(farm).Condition, Is.LessThan(1f));
        }

        [Test]
        public void A_mothballed_building_is_not_worth_rioting_over()
        {
            EntityId shut = _world.CreateEntity();
            _world.Add(shut, new BuildingState { DefinitionIndex = 0, Condition = 0.6f, Mothballed = true });
            TestPort.Own(_world, shut, _port);

            SetGrievance(Stratum.NamedCrew, 0.85f);
            RunDays(4);

            Assert.That(_world.Store<BuildingState>().GetRef(shut).Condition,
                Is.EqualTo(0.6f).Within(1e-4f));
        }

        // ------------------------------------------------------------------- terminal

        [Test]
        public void Deposition_is_terminal()
        {
            // The failure state, not a bad mood. A port does not climb out of it by feeding
            // people afterwards.
            SetGrievance(Stratum.NamedCrew, 1f);
            RunDays(8);
            Assert.That(Rung, Is.EqualTo(LadderRung.Deposition));

            SetGrievance(Stratum.NamedCrew, 0f);
            RunDays(20);

            Assert.That(Rung, Is.EqualTo(LadderRung.Deposition));
        }

        // ----------------------------------------------------------------- validation

        [Test]
        public void A_rung_with_no_hysteresis_is_rejected()
        {
            var report = new ValidationReport();
            BalanceTables.Load(new BalanceSources
            {
                Goods = CsvTable.Parse(Goods, "goods.csv"),
                Buildings = CsvTable.Parse(Buildings, "buildings.csv"),
                CrewRoles = CsvTable.Parse(Crew, "crew_roles.csv"),
                Strata = CsvTable.Parse(Strata, "strata.csv"),
                Ladder =
                CsvTable.Parse(Ladder.Replace("Slowdown,0.50,0.40", "Slowdown,0.50,0.50"), "ladder.csv"),
            }, report);

            Assert.That(report.Problems.Any(p => p.Contains("no hysteresis")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void A_ladder_that_speeds_up_as_it_gets_worse_is_rejected()
        {
            // days_to_climb is what gives the player time to act, and the time needed grows with
            // the stakes. A ladder that escalated fastest at the top would take longest where it
            // mattered least.
            var report = new ValidationReport();
            BalanceTables.Load(new BalanceSources
            {
                Goods = CsvTable.Parse(Goods, "goods.csv"),
                Buildings = CsvTable.Parse(Buildings, "buildings.csv"),
                CrewRoles = CsvTable.Parse(Crew, "crew_roles.csv"),
                Strata = CsvTable.Parse(Strata, "strata.csv"),
                Ladder =
                CsvTable.Parse(
                    Ladder.Replace("Slowdown,0.50,0.40,1", "Slowdown,0.50,0.40,4"), "ladder.csv"),
            }, report);

            Assert.That(report.Problems.Any(p => p.Contains("speed up as it")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void A_missing_rung_is_rejected()
        {
            // It would be skipped without ever being seen, and §5.2.2 wants every rung visible.
            var report = new ValidationReport();
            BalanceTables.Load(new BalanceSources
            {
                Goods = CsvTable.Parse(Goods, "goods.csv"),
                Buildings = CsvTable.Parse(Buildings, "buildings.csv"),
                CrewRoles = CsvTable.Parse(Crew, "crew_roles.csv"),
                Strata = CsvTable.Parse(Strata, "strata.csv"),
                Ladder =
                CsvTable.Parse(Ladder.Replace("Agitator,0.65,0.55,1,0.60,0.00\n", ""), "ladder.csv"),
            }, report);

            Assert.That(report.Problems.Any(p => p.Contains("'Agitator' is missing")), Is.True,
                string.Join("; ", report.Problems));
        }
    }
}
