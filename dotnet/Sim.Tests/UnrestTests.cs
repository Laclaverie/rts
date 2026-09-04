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
    /// Grievance per stratum (GDD §5.2.2): different groups, angered by different things.
    /// </summary>
    [Category(TestCategories.Unit)]
    public class UnrestTests
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

        private World _world = null!;
        private BalanceTables _balance = null!;
        private EventQueue _events = null!;

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
            }, report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));

            _world = new World();
            _events = new EventQueue();

            for (int i = 0; i < _balance.Strata.Count; i++)
            {
                EntityId entity = _world.CreateEntity();
                _world.Add(entity, new Grievance { StratumIndex = i, Value = 0f, Baseline = 0f });
            }
        }

        private int IndexOf(Stratum stratum) =>
            Enumerable.Range(0, _balance.Strata.Count).First(i => _balance.Strata[i].Stratum == stratum);

        private float GrievanceOf(Stratum stratum)
        {
            ComponentStore<Grievance> store = _world.Store<Grievance>();
            int index = IndexOf(stratum);

            for (int i = 0; i < store.Count; i++)
                if (store.Values[i].StratumIndex == index)
                    return store.Values[i].Value;

            return 0f;
        }

        private void SetGrievance(Stratum stratum, float value, float baseline = 0f)
        {
            ComponentStore<Grievance> store = _world.Store<Grievance>();
            int index = IndexOf(stratum);

            for (int i = 0; i < store.Count; i++)
            {
                if (store.Values[i].StratumIndex != index) continue;

                ref Grievance grievance = ref store.GetRef(store.Ids[i]);
                grievance.Value = value;
                grievance.Baseline = baseline;
                return;
            }
        }

        /// <summary>Emits the day's events, then runs Unrest as the pipeline would.</summary>
        private void RunDay(params object[] emitted)
        {
            _events.BeginCause(CauseId.Root, 1);
            try
            {
                foreach (object payload in emitted)
                {
                    switch (payload)
                    {
                        case FoodShortfall hunger: _events.Emit(hunger); break;
                        case CommonersWentHungry town: _events.Emit(town); break;
                        case WagesUnpaid unpaid: _events.Emit(unpaid); break;
                        case CrewDeserted gone: _events.Emit(gone); break;
                    }
                }

                var ctx = new Context(1, 0f, _events, rng: null, balance: _balance);
                new UnrestSystem().Run(_world, in ctx);
            }
            finally
            {
                _events.EndCause();
            }

            _events.Drain();
        }

        // ------------------------------------------------------------------- drivers

        [Test]
        public void Each_stratum_is_angered_by_its_own_hunger_and_not_the_others()
        {
            // "Crew signed up for hard weather; a commoner who cannot feed a family did not."
            // The two counts are separate events because the two populations are separate: a
            // starving town does not calm down because the crew ate.
            RunDay(new CommonersWentHungry { Commoners = 3, Wanted = 3f, Eaten = 0f });

            Assert.That(GrievanceOf(Stratum.Commoners), Is.EqualTo(0.30f).Within(1e-4f));
            Assert.That(GrievanceOf(Stratum.NamedCrew), Is.EqualTo(0f).Within(1e-4f),
                "the crew were fed");
        }

        [Test]
        public void Crew_hunger_angers_the_crew_and_not_the_town()
        {
            RunDay(new FoodShortfall { Crew = 3, Wanted = 3f, Eaten = 0f });

            Assert.That(GrievanceOf(Stratum.NamedCrew), Is.EqualTo(0.09f).Within(1e-4f));
            Assert.That(GrievanceOf(Stratum.Commoners), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void The_same_hunger_angers_commoners_more_than_crew()
        {
            // Per head, and this is the asymmetry §5.2.2 asks for.
            RunDay(new CommonersWentHungry { Commoners = 3 });
            float commoners = GrievanceOf(Stratum.Commoners);

            SetGrievance(Stratum.Commoners, 0f);
            RunDay(new FoodShortfall { Crew = 3 });

            Assert.That(commoners, Is.GreaterThan(GrievanceOf(Stratum.NamedCrew)));
        }

        [Test]
        public void Unpaid_wages_anger_only_the_people_who_draw_one()
        {
            // Commoners are not on the payroll. What will anger them about money is taxes
            // (§5.2.2), and there is no tax yet — so a missed payday is the crew's grievance
            // alone rather than a small share of everyone's.
            RunDay(new WagesUnpaid { Crew = 2, Owed = 4, Paid = 0 });

            Assert.That(GrievanceOf(Stratum.NamedCrew), Is.EqualTo(0.24f).Within(1e-4f));
            Assert.That(GrievanceOf(Stratum.Commoners), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Desertion_angers_both()
        {
            RunDay(new CrewDeserted { RoleIndex = 0, Remaining = 3 });

            Assert.That(GrievanceOf(Stratum.NamedCrew), Is.EqualTo(0.08f).Within(1e-4f));
            Assert.That(GrievanceOf(Stratum.Commoners), Is.EqualTo(0.03f).Within(1e-4f));
        }

        /// <summary>
        /// A town with nowhere to work. Unemployment is commoners' grievance, not the crew's:
        /// named crew have an idle_weight of zero because a specialist nobody has posted yet is
        /// the port's problem, not theirs.
        /// </summary>
        private void AddUnemployedCommoners(int count)
        {
            EntityId town = _world.CreateEntity();
            _world.Add(town, new Population { Commoners = count });
        }

        [Test]
        public void One_idle_worker_is_a_grumble_that_never_grows()
        {
            // Unemployment is a standing condition, not an event, so it renews daily — but at
            // 0.02 a day against 0.04 of decay it settles rather than escalating. A single
            // person with nothing to do should be a background annoyance, not a countdown.
            AddUnemployedCommoners(1);

            RunDay();
            float afterOneDay = GrievanceOf(Stratum.Commoners);

            for (int day = 0; day < 10; day++) RunDay();

            Assert.That(afterOneDay, Is.EqualTo(0.02f).Within(1e-4f));
            Assert.That(GrievanceOf(Stratum.Commoners), Is.EqualTo(afterOneDay).Within(1e-4f),
                "one idle worker plateaus: the daily contribution is below the daily decay");
        }

        [Test]
        public void Enough_idle_workers_outrun_the_decay_and_climb()
        {
            // Three at 0.02 is 0.06 a day against 0.04 of decay. Unemployment becomes a problem
            // when it is widespread, which is the shape §5.2.2 wants.
            AddUnemployedCommoners(3);

            RunDay();
            float afterOneDay = GrievanceOf(Stratum.Commoners);

            for (int day = 0; day < 5; day++) RunDay();

            Assert.That(GrievanceOf(Stratum.Commoners), Is.GreaterThan(afterOneDay));
        }

        [Test]
        public void A_commoner_with_work_does_not_anger_anybody()
        {
            EntityId building = _world.CreateEntity();
            _world.Add(building, new BuildingState { DefinitionIndex = 0, Condition = 1f, Workers = 1 });

            AddUnemployedCommoners(1);   // the one commoner, and the one job

            RunDay();

            Assert.That(GrievanceOf(Stratum.Commoners), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Merchants_are_unmoved_by_everything_the_port_can_currently_do_to_them()
        {
            // Honest rather than an oversight: tariffs, blockades and seizures need trade,
            // routes and neighbours. The stratum exists so the ladder reads all three and
            // adding it later would not mean re-tuning the other two.
            RunDay(new FoodShortfall { Crew = 5 }, new WagesUnpaid { Crew = 5 },
                new CrewDeserted { Remaining = 0 });

            Assert.That(GrievanceOf(Stratum.Merchants), Is.EqualTo(0f).Within(1e-4f));
        }

        // --------------------------------------------------------------------- decay

        [Test]
        public void A_day_with_nothing_to_resent_earns_the_faster_rate()
        {
            // Nothing happened to the named crew today: fed, paid, nobody gone. They cool at
            // relief_per_day rather than decay_per_day, which is what makes fixing the cause a
            // lever rather than a slower way of waiting.
            SetGrievance(Stratum.NamedCrew, 0.50f);

            RunDay();

            Assert.That(GrievanceOf(Stratum.NamedCrew), Is.EqualTo(0.35f).Within(1e-4f));
        }

        [Test]
        public void A_day_that_was_merely_not_worse_earns_the_slow_rate()
        {
            // One unpaid wage adds 0.12 and denies the faster rate, so the day nets out above
            // where it started. A port that has stopped bleeding is not a port that is working,
            // and the two must not cool at the same speed.
            SetGrievance(Stratum.NamedCrew, 0.50f);

            RunDay(new WagesUnpaid { Crew = 1 });

            Assert.That(GrievanceOf(Stratum.NamedCrew),
                Is.EqualTo(0.50f - 0.05f + 0.12f).Within(1e-4f));
        }

        [Test]
        public void One_stratums_complaint_does_not_deny_another_its_relief()
        {
            // A commoner with no work is the commoners' grievance and nobody else's, so the same
            // day is a slow one for them and a clean one for the crew. Asking the question
            // port-wide would let either group hold the other's recovery hostage — and the
            // shipped port always has somebody unemployed, so a port-wide clean day would never
            // happen at all.
            SetGrievance(Stratum.NamedCrew, 0.50f);
            SetGrievance(Stratum.Commoners, 0.50f);
            AddUnemployedCommoners(1);

            RunDay();

            Assert.That(GrievanceOf(Stratum.NamedCrew), Is.EqualTo(0.35f).Within(1e-4f),
                "the crew had nothing to complain about");
            Assert.That(GrievanceOf(Stratum.Commoners), Is.EqualTo(0.50f - 0.04f + 0.02f).Within(1e-4f),
                "the commoners did");
        }

        [Test]
        public void Grievance_rises_faster_than_it_fades()
        {
            // Or nothing ever compounds, and the ladder can only be climbed by one bad day.
            StratumRules crew = _balance.Strata[IndexOf(Stratum.NamedCrew)];

            Assert.That(crew.UnpaidWeight, Is.GreaterThan(crew.DecayPerDay));
        }

        [Test]
        public void Grievance_decays_to_the_baseline_and_no_further()
        {
            // Repression raises the floor permanently (§5.2.2). A port put down by force never
            // returns to calm.
            SetGrievance(Stratum.Commoners, 0.40f, baseline: 0.30f);

            RunDay();
            RunDay();
            RunDay();
            RunDay();

            Assert.That(GrievanceOf(Stratum.Commoners), Is.EqualTo(0.30f).Within(1e-4f));
        }

        [Test]
        public void Grievance_cannot_exceed_one()
        {
            SetGrievance(Stratum.Commoners, 0.95f);

            RunDay(new CommonersWentHungry { Commoners = 10 });

            Assert.That(GrievanceOf(Stratum.Commoners), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void A_world_with_no_strata_is_left_alone()
        {
            // Not wrapped in Assert.DoesNotThrow: Context is a ref struct, so it cannot be
            // captured in a lambda — which is §7.2's guarantee working. If this throws, the
            // test fails just as loudly.
            var empty = new World();
            var ctx = new Context(1, 0f, _events, rng: null, balance: _balance);

            new UnrestSystem().Run(empty, in ctx);

            Assert.That(empty.Store<Grievance>().Count, Is.EqualTo(0));
        }
    }
}
