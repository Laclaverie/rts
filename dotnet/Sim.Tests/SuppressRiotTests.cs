using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Events;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Systems;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// Repression (GDD §5.2.2): quiet now, a worse floor forever, loyalty from everyone.
    /// </summary>
    [Category(TestCategories.Unit)]
    public class SuppressRiotTests
    {
        private const string Goods = "id,base_price,volatility,heat_per_unit,supply,keep,sell_price\n" +
                                     "food,4,0.25,0.00,Local,0,1\n";

        private const string Buildings =
            "id,upkeep_coin,build_timber,build_iron,capacity,produces,output_per_day,staff\n" +
            "farm,1,0,0,0,food,6,1\n";

        private const string Crew = "id,wage_coin,work_rate,food_per_day,rum_per_day\n" +
                                    "laborer,2,1.00,1.0,0.00\n";

        private const string Strata =
            "id,decay_per_day,relief_per_day,hunger_weight,unpaid_weight,desertion_weight,idle_weight\n" +
            "Commoners,0.04,0.12,0.10,0.02,0.03,0.02\n" +
            "NamedCrew,0.05,0.15,0.03,0.12,0.08,0.00\n" +
            "Merchants,0.06,0.18,0.00,0.00,0.00,0.00\n";

        private const string Ladder =
            "rung,climb_at,fall_below,days_to_climb,output_multiplier,condition_damage\n" +
            "Calm,0.00,0.00,1,1.00,0.00\nGrumbling,0.35,0.25,1,1.00,0.00\n" +
            "Slowdown,0.50,0.40,1,0.75,0.00\nAgitator,0.65,0.55,1,0.60,0.00\n" +
            "Riot,0.80,0.70,1,0.35,0.05\nUprising,0.92,0.85,1,0.10,0.10\n" +
            "Deposition,0.99,0.00,1,0.00,0.00\n";

        private const string Repression =
            "id,grievance_relief,cowed_days,baseline_increase,loyalty_cost\n" +
            "Restrained,0.15,2,0.03,0.05\nFirm,0.30,4,0.08,0.12\nBrutal,0.50,7,0.18,0.25\n";

        private World _world = null!;
        private BalanceTables _balance = null!;
        private EventQueue _events = null!;
        private EntityId _crew;

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
                Ladder = CsvTable.Parse(Ladder, "ladder.csv"),
                Repression = CsvTable.Parse(Repression, "repression.csv"),
            }, report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));

            _world = new World();
            _events = new EventQueue();

            for (int i = 0; i < _balance.Strata.Count; i++)
            {
                EntityId entity = _world.CreateEntity();
                _world.Add(entity, new Grievance { StratumIndex = i, Value = 0.85f, Baseline = 0f });
            }

            EntityId ladder = _world.CreateEntity();
            _world.Add(ladder, new RevolutionLadder { Rung = LadderRung.Riot });

            _crew = _world.CreateEntity();
            _world.Add(_crew, new CrewMember { RoleIndex = 0, Morale = 1f, Loyalty = 1f });
        }

        private Context Ctx() => new Context(1, 0f, _events, rng: null, balance: _balance);

        private CommandRejection Validate(Harshness harshness)
        {
            Context ctx = Ctx();
            return new SuppressRiotHandler().Validate(new SuppressRiot(harshness), _world, in ctx);
        }

        private void Suppress(Harshness harshness)
        {
            _events.BeginCause(CauseId.Root, 1);
            try
            {
                Context ctx = Ctx();
                new SuppressRiotHandler().Apply(new SuppressRiot(harshness), _world, in ctx);
            }
            finally
            {
                _events.EndCause();
            }
        }

        private void SetRung(LadderRung rung) =>
            _world.Store<RevolutionLadder>().GetRef(_world.Store<RevolutionLadder>().Ids[0]).Rung = rung;

        private Grievance First => _world.Store<Grievance>().Values[0];

        private void RunUnrestDays(int days, params WagesUnpaid[] pressure)
        {
            for (int i = 0; i < days; i++)
            {
                _events.BeginCause(CauseId.Root, 1);
                foreach (WagesUnpaid unpaid in pressure) _events.Emit(unpaid);
                Context ctx = Ctx();
                new UnrestSystem().Run(_world, in ctx);
                _events.EndCause();
                _events.Drain();
            }
        }

        // ------------------------------------------------------------------- the trade

        [Test]
        public void Suppression_buys_quiet_immediately()
        {
            Suppress(Harshness.Firm);

            Assert.That(First.Value, Is.EqualTo(0.85f - 0.30f).Within(1e-4f));
            Assert.That(_events.Pending.Any(e => e.Is<RiotSuppressed>()), Is.True);
        }

        [Test]
        public void Suppression_raises_the_floor_permanently()
        {
            // The part that never goes away. Grievance decays towards the baseline, so a port
            // put down by force never returns to calm.
            Suppress(Harshness.Firm);

            Assert.That(First.Baseline, Is.EqualTo(0.08f).Within(1e-4f));

            RunUnrestDays(40);

            Assert.That(First.Value, Is.EqualTo(0.08f).Within(1e-4f),
                "forty quiet days and it still sits above zero");
        }

        [Test]
        public void Suppression_costs_loyalty_from_everybody()
        {
            // "costs loyalty with every crew member who disagreed" — nobody was asked.
            Suppress(Harshness.Firm);

            Assert.That(_world.Store<CrewMember>().GetRef(_crew).Loyalty,
                Is.EqualTo(1f - 0.12f).Within(1e-4f));
        }

        [Test]
        public void Harsher_buys_more_and_costs_more()
        {
            // There is no option here that is simply better than the others.
            RepressionRules restrained = _balance.Repression["Restrained"];
            RepressionRules brutal = _balance.Repression["Brutal"];

            Assert.That(brutal.GrievanceRelief, Is.GreaterThan(restrained.GrievanceRelief));
            Assert.That(brutal.BaselineIncrease, Is.GreaterThan(restrained.BaselineIncrease));
            Assert.That(brutal.LoyaltyCost, Is.GreaterThan(restrained.LoyaltyCost));
        }

        [Test]
        public void Relief_never_takes_grievance_below_the_new_floor()
        {
            // A port put down by force does not get to be calmer than one never put down at all.
            _world.Store<Grievance>().GetRef(_world.Store<Grievance>().Ids[0]).Value = 0.10f;

            Suppress(Harshness.Brutal);

            Assert.That(First.Value, Is.EqualTo(0.18f).Within(1e-4f));
            Assert.That(First.Value, Is.EqualTo(First.Baseline).Within(1e-4f));
        }

        [Test]
        public void Suppression_buys_a_window_not_just_a_subtraction()
        {
            // The relief alone was worthless. Grievance is capped at 1.00 and a rioting port is
            // already there, so the next day's hunger put back everything the crackdown took —
            // measured at twelve days to leave a riot whether or not force was used, which made
            // the permanent floor a pure loss. What force actually buys is silence, and what
            // silence buys is time to fix the cause.
            Suppress(Harshness.Firm);
            _events.Drain();

            float afterCrackdown = First.Value;

            // Four cowed days: the same pressure that drove the riot, landing on nobody.
            for (int day = 0; day < 4; day++) RunUnrestDays(1, new WagesUnpaid { Crew = 5 });

            Assert.That(First.Value, Is.LessThan(afterCrackdown),
                "a cowed port keeps cooling even while the cause is untouched");

            // The window closes and the grievance is still there, waiting.
            RunUnrestDays(1, new WagesUnpaid { Crew = 5 });

            Assert.That(First.Value, Is.GreaterThan(First.Baseline),
                "silence was not forgiveness");
        }

        [Test]
        public void A_cowed_port_cools_slowly_because_silence_is_not_contentment()
        {
            // It gets decay_per_day, not relief_per_day. People with their heads down are not
            // a port that is visibly working, and paying the second rate for the first would
            // make force strictly better than fixing anything.
            Suppress(Harshness.Firm);
            _events.Drain();

            float before = First.Value;
            RunUnrestDays(1);

            StratumRules rules = _balance.Strata[0];
            Assert.That(before - First.Value, Is.EqualTo(rules.DecayPerDay).Within(1e-4f));
            Assert.That(rules.ReliefPerDay, Is.GreaterThan(rules.DecayPerDay));
        }

        [Test]
        public void The_longer_window_wins_when_a_port_is_crushed_twice()
        {
            // A second, milder crackdown does not make people bolder than the first left them.
            Suppress(Harshness.Brutal);
            SetRung(LadderRung.Riot);
            Suppress(Harshness.Restrained);

            Assert.That(_world.Store<Grievance>().Values[0].CowedDays,
                Is.EqualTo(_balance.Repression["Brutal"].CowedDays));
        }

        [Test]
        public void Repeated_repression_stacks_the_floor()
        {
            // A port ruled by force gets harder to rule, which is the whole argument against
            // reaching for it twice.
            Suppress(Harshness.Firm);
            SetRung(LadderRung.Riot);
            Suppress(Harshness.Firm);

            Assert.That(First.Baseline, Is.EqualTo(0.16f).Within(1e-4f));
        }

        // ------------------------------------------------------------------ rejections

        [Test]
        public void There_has_to_be_a_riot_to_put_down()
        {
            // Otherwise a player could buy the permanent penalty for nothing.
            SetRung(LadderRung.Slowdown);

            Assert.That(Validate(Harshness.Firm), Is.EqualTo(CommandRejection.NotYet));
        }

        [Test]
        public void An_uprising_can_still_be_suppressed()
        {
            SetRung(LadderRung.Uprising);

            Assert.That(Validate(Harshness.Brutal), Is.EqualTo(CommandRejection.None));
        }

        [Test]
        public void After_deposition_there_is_nobody_to_give_the_order()
        {
            SetRung(LadderRung.Deposition);

            Assert.That(Validate(Harshness.Brutal), Is.EqualTo(CommandRejection.TargetGone));
        }

        [Test]
        public void A_port_with_no_ladder_cannot_be_suppressed()
        {
            var bare = new World();
            Context ctx = Ctx();

            Assert.That(new SuppressRiotHandler().Validate(new SuppressRiot(Harshness.Firm), bare, in ctx),
                Is.EqualTo(CommandRejection.InvalidTarget));
        }

        // ----------------------------------------------------------------- validation

        [Test]
        public void Repression_that_would_make_things_worse_is_rejected()
        {
            // A trap rather than a decision: the player punished for taking the option the game
            // offered, with no way to see it coming.
            var report = new ValidationReport();
            BalanceTables.Load(new BalanceSources
            {
                Goods = CsvTable.Parse(Goods, "goods.csv"),
                Buildings = CsvTable.Parse(Buildings, "buildings.csv"),
                CrewRoles = CsvTable.Parse(Crew, "crew_roles.csv"),
                Strata = CsvTable.Parse(Strata, "strata.csv"),
                Ladder = CsvTable.Parse(Ladder, "ladder.csv"),
                Repression = CsvTable.Parse(
                    Repression.Replace("Firm,0.30,4,0.08", "Firm,0.30,4,0.40"), "repression.csv"),
            }, report);

            Assert.That(report.Problems.Any(p => p.Contains("worse the same day")), Is.True,
                string.Join("; ", report.Problems));
        }
    }
}
