using System;
using System.IO;
using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Events;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Engine.Randomness;
using RTS.Sim.Engine.Time;
using RTS.Sim.Session;
using RTS.Sim.Systems;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// Heat, raids, and the dilemma they are for (GDD §5.2, §5.2.1; P1).
    /// </summary>
    /// <remarks>
    /// P1 says wealth is cargo, cargo moves on the map, and anything on the map can be
    /// intercepted — "prosperity is therefore <em>exposed</em> by construction". Convoys have
    /// been sailing since Phase 4 and nothing has ever taken one, so the exposure was a claim.
    /// These are the tests that make it a fact, and the last of them is Phase 4's own gate: that
    /// the two pressures §5.2 puts against each other actually fight.
    /// </remarks>
    [Category(TestCategories.Functional)]
    public class HeatAndRaidTests
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
                Heat = Table(BalanceTables.HeatFile),
            }, report);

            report.ThrowIfInvalid();
            return tables;
        }

        private sealed class Run
        {
            public readonly BalanceTables Tables = Balance();
            public readonly GameSession Session;

            public Run()
            {
                Session = GameSession.Start(
                    Tables,
                    new Clock(10f, new[] { 1 }),
                    File.ReadAllText(BalancePath("pipeline.csv")));
            }

            public World World => Session.World;

            public EntityId Player => Port.Player(World);

            public EntityId City(string id)
            {
                ComponentStore<PortState> ports = World.Store<PortState>();
                for (int i = 0; i < ports.Count; i++)
                    if (Tables.Ports[ports.Values[i].DefinitionIndex].Id == id) return ports.Ids[i];

                return EntityId.None;
            }

            public int Good(string id) => ConsumptionSystem.IndexOf(Tables, id);

            public float Heat => HeatSystem.Of(World, Player);

            public int Coin => Port.Treasury(World, Player).Coin;

            public int Convoys => World.Store<Convoy>().Count;

            public void Days(int n) { for (int i = 0; i < n; i++) Session.Step(); }

            /// <summary>Puts a valuable cargo in the player's warehouse.</summary>
            public void Stock(string good, float units) =>
                Port.Add(World, Player, Good(good), units);

            public string Feed() =>
                string.Join(" | ", Session.Feed.Entries.Select(e => e.Text));
        }

        // ---------------------------------------------------------------------- heat

        [Test]
        public void A_port_that_keeps_its_head_down_is_barely_noticed()
        {
            // §5.2.1 replaced "penalties for being strong" with a pressure that is *earned*,
            // and offers "keep a lower profile and grow slower" as a counter. A port that runs
            // no routes is not invisible - it still has iron in a shed - but it is a long way
            // from one that ships every week.
            var quiet = new Run();
            var busy = new Run();

            for (int day = 0; day < 30; day++)
            {
                if (busy.Convoys == 0)
                    busy.Session.Submit(new BuyFrom(busy.City("ironhold"), busy.Good("iron"), 5f));

                quiet.Days(1);
                busy.Days(1);
            }

            Assert.That(busy.Heat, Is.GreaterThan(quiet.Heat * 2f),
                $"quiet {quiet.Heat:0.000}, busy {busy.Heat:0.000}");
        }

        [Test]
        public void Wealth_on_show_draws_attention()
        {
            var run = new Run();
            var quiet = new Run();

            run.Stock("iron", 40f);

            run.Days(10);
            quiet.Days(10);

            Assert.That(run.Heat, Is.GreaterThan(quiet.Heat));
        }

        [Test]
        public void What_you_trade_is_the_lever_rather_than_how_much()
        {
            // The counter §5.2.1 actually offers: "keep a lower profile and grow slower". A port
            // can be rich in grain and invisible, or thin in spice and hunted.
            var grain = new Run();
            var metal = new Run();

            grain.Stock("food", 200f);
            metal.Stock("iron", 20f);

            grain.Days(10);
            metal.Days(10);

            Assert.That(metal.Heat, Is.GreaterThan(grain.Heat),
                $"grain {grain.Heat:0.000} on 200 units, iron {metal.Heat:0.000} on 20");
        }

        [Test]
        public void Attention_fades_when_there_is_less_to_see()
        {
            var run = new Run();
            run.Stock("iron", 60f);
            run.Days(15);

            float watched = run.Heat;
            Assert.That(watched, Is.GreaterThan(0.05f), "nothing to fade from");

            Port.Take(run.World, run.Player, run.Good("iron"),
                Port.UnitsOf(run.World, run.Player, run.Good("iron")));
            run.Days(20);

            Assert.That(run.Heat, Is.LessThan(watched));
        }

        [Test]
        public void A_cargo_on_the_water_shows_more_than_the_same_cargo_at_home()
        {
            // §5.2.1 lists the fat convoy first among the things that are visible, and it is what
            // makes "split cargo across routes" a real counter rather than flavour.
            Assert.That(Balance().Heat.AtSeaWeight,
                Is.GreaterThan(Balance().Heat.HeldWeight));
        }

        // ---------------------------------------------------------------------- raids

        [Test]
        public void A_port_nobody_is_watching_is_never_raided()
        {
            var run = new Run();

            run.Session.Submit(new BuyFrom(run.City("ironhold"), run.Good("iron"), 4f));
            run.Days(30);

            Assert.That(run.Feed(), Does.Not.Contain("raid"), run.Feed());
        }

        [Test]
        public void A_watched_port_loses_cargo_on_the_water()
        {
            // P1's promise made a fact. Prosperity is exposed because the exposure now costs.
            var run = new Run();
            bool raided = false;

            for (int day = 0; day < 200 && !raided; day++)
            {
                if (run.Convoys == 0)
                    run.Session.Submit(new BuyFrom(run.City("ironhold"), run.Good("iron"), 5f));

                run.Days(1);
                raided = run.Feed().Contains("raid");
            }

            Assert.That(raided, Is.True,
                $"two hundred days of iron convoys at heat {run.Heat:0.000} and nobody took one");
        }

        [Test]
        public void A_raid_takes_a_share_rather_than_the_hold()
        {
            // §5.2.3: a single shock is always survivable. A raid that emptied the hold would
            // make one bad roll the end of a route rather than the cost of running one.
            Assert.That(Balance().Heat.RaidTakes, Is.LessThan(1f));
        }

        [Test]
        public void What_is_taken_is_not_paid_for()
        {
            // A sale is paid on arrival for what arrives. Anything else has the buyer paying for
            // barrels somebody else is drinking.
            //
            // Run against a world built for it, with a certainty of being raided, rather than
            // sailing real convoys until the dice cooperate. What is under test is the
            // arithmetic of a raid; how often one happens is
            // A_watched_port_loses_cargo_on_the_water's job, and mixing the two would give a
            // test that passes on one seed and fails on the next.
            var world = new World();
            var events = new EventQueue();
            BalanceTables tables = Certain();

            EntityId port = world.CreateEntity();
            world.Add(port, new PortState { DefinitionIndex = 0, IsPlayer = true });

            EntityId watched = world.CreateEntity();
            world.Add(watched, new Heat { Value = 1f });
            world.Add(watched, new Owner { Port = port });

            EntityId convoy = world.CreateEntity();
            world.Add(convoy, new Convoy
            {
                Origin = port,
                Destination = port,
                GoodIndex = 0,
                Units = 8f,
                CoinOnArrival = 40,
                DaysRemaining = 4,
                TotalDays = 4,
            });
            world.Add(convoy, new Owner { Port = port });

            events.BeginCause(CauseId.Root, 1);
            var ctx = new Context(1, 0f, events, new Rng(1), tables);
            new RaidSystem().Run(world, in ctx);
            events.EndCause();

            Convoy after = world.Store<Convoy>().GetRef(convoy);

            Assert.That(after.Units, Is.EqualTo(4f).Within(0.01f), "half the cargo");
            Assert.That(after.CoinOnArrival, Is.EqualTo(20), "and half the payment");
            Assert.That(events.Pending.Any(e => e.Is<ConvoyRaided>()), Is.True);
        }

        [Test]
        public void A_cargo_taken_to_the_last_barrel_takes_the_convoy_with_it()
        {
            // Otherwise an empty ship sails on to deliver nothing, and the feed reports an
            // arrival of zero units at the end of it.
            var world = new World();
            var events = new EventQueue();

            EntityId port = world.CreateEntity();
            world.Add(port, new PortState { DefinitionIndex = 0, IsPlayer = true });

            EntityId watched = world.CreateEntity();
            world.Add(watched, new Heat { Value = 1f });
            world.Add(watched, new Owner { Port = port });

            EntityId convoy = world.CreateEntity();
            world.Add(convoy, new Convoy
            {
                Origin = port, Destination = port, GoodIndex = 0,
                Units = 0.005f, DaysRemaining = 3, TotalDays = 3,
            });
            world.Add(convoy, new Owner { Port = port });

            events.BeginCause(CauseId.Root, 1);
            var ctx = new Context(1, 0f, events, new Rng(1), Certain());
            new RaidSystem().Run(world, in ctx);
            events.EndCause();

            Assert.That(world.IsAlive(convoy), Is.False);
        }

        /// <summary>Tables whose raiders never miss, for testing what a raid does.</summary>
        private static BalanceTables Certain()
        {
            var report = new ValidationReport();
            BalanceTables tables = BalanceTables.Load(new BalanceSources
            {
                Goods = Table(BalanceTables.GoodsFile),
                Buildings = Table(BalanceTables.BuildingsFile),
                CrewRoles = Table(BalanceTables.CrewRolesFile),
                Heat = CsvTable.Parse(
                    "key,value\nraid_chance_at_full,1.0\nraid_takes,0.5\n", "heat.csv"),
            }, report);

            report.ThrowIfInvalid();
            return tables;
        }

        // -------------------------------------------------------------------- escorts

        [Test]
        public void Standing_the_guard_up_is_an_order_the_player_can_give()
        {
            var run = new Run();

            Assert.That(run.Session.Validate(new SetEscort(true)),
                Is.EqualTo(CommandRejection.None));

            run.Session.Submit(new SetEscort(true));
            run.Days(1);

            Assert.That(EscortSystem.IsEscorting(run.World, run.Player), Is.True);
            Assert.That(run.Session.Validate(new SetEscort(true)),
                Is.EqualTo(CommandRejection.AlreadyInState));
        }

        [Test]
        public void An_escort_with_nothing_at_sea_costs_nothing()
        {
            // The bill is the size of what is being protected. A port paying for guards over an
            // empty harbour would make the posture a tax rather than a choice.
            var run = new Run();
            var unguarded = new Run();

            run.Session.Submit(new SetEscort(true));
            run.Days(6);
            unguarded.Days(6);

            Assert.That(run.Coin, Is.EqualTo(unguarded.Coin));
        }

        [Test]
        public void An_escort_is_paid_for_every_convoy_it_is_watching()
        {
            var run = new Run();
            var unguarded = new Run();

            run.Session.Submit(new SetEscort(true));
            run.Session.Submit(new BuyFrom(run.City("ironhold"), run.Good("iron"), 4f));
            unguarded.Session.Submit(new BuyFrom(unguarded.City("ironhold"), unguarded.Good("iron"), 4f));

            run.Days(4);
            unguarded.Days(4);

            Assert.That(run.Coin, Is.LessThan(unguarded.Coin), "the guard was free");
            Assert.That(run.Feed(), Does.Contain("escort"), run.Feed());
        }

        [Test]
        public void An_escort_that_bought_nothing_would_be_refused_by_the_loader()
        {
            // §5.2.1 requires Heat to be counterable. A multiplier of one takes coin and returns
            // nothing, which is a content mistake worth failing loudly on.
            var report = new ValidationReport();
            HeatRules.Load(
                CsvTable.Parse("key,value\nescort_raid_multiplier,1.0\n", "heat.csv"), report);

            Assert.That(report.Problems.Any(p => p.Contains("counterable")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void Shipping_being_safer_than_hoarding_would_be_refused_too()
        {
            // It would remove the reason to escort anything, which quietly deletes the mechanic
            // rather than breaking it — the expensive kind of content bug.
            var report = new ValidationReport();
            HeatRules.Load(
                CsvTable.Parse("key,value\nheld_weight,0.5\nat_sea_weight,0.1\n", "heat.csv"),
                report);

            Assert.That(report.Problems.Any(p => p.Contains("safer than sitting")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void An_unknown_setting_is_a_mistake_in_a_file_rather_than_a_default()
        {
            var report = new ValidationReport();
            HeatRules.Load(CsvTable.Parse("key,value\nraid_chance,0.5\n", "heat.csv"), report);

            Assert.That(report.Problems.Any(p => p.Contains("not a heat setting")), Is.True,
                string.Join("; ", report.Problems));
        }

        // -------------------------------------------------------------- on the screen

        [Test]
        public void Both_pressures_are_on_the_readout_at_once()
        {
            // §5.2 puts Heat and Unrest against each other, and a player who cannot see both at
            // the same time cannot feel them pull.
            var run = new Run();
            string[] labels = run.Session.Readouts().Select(r => r.Label).ToArray();

            Assert.That(labels, Does.Contain("Heat"));
            Assert.That(labels, Does.Contain("Unrest"));
            Assert.That(labels, Does.Contain("Escorts"));
        }

        [Test]
        public void The_guard_is_offered_with_its_price_on_it()
        {
            // §5.2.2 wants repression to be a decision rather than a reflex and puts its cost on
            // the button; the same is true of the other pressure's counter.
            var run = new Run();

            PlayerAction escort = run.Session.Actions()
                .Single(a => a.Group == "Defence");

            Assert.That(escort.Label, Does.Contain("Stand the escorts up"));
            Assert.That(escort.Detail, Does.Contain("coin"));
            Assert.That(escort.Enabled, Is.True);
        }

        [Test]
        public void The_bill_is_the_size_of_what_it_is_guarding()
        {
            var run = new Run();
            Assert.That(run.Session.EscortBill(), Is.Zero, "nothing is at sea");

            run.Session.Submit(new BuyFrom(run.City("ironhold"), run.Good("iron"), 5f));
            run.Days(1);

            Assert.That(run.Session.EscortBill(),
                Is.EqualTo(run.Tables.Heat.EscortCoinPerDay));
        }

        // ----------------------------------------------------------------- the gate

        [Test]
        public void The_guard_is_paid_before_the_crew_are()
        {
            // BUILD_ORDER's Phase 4 gate: "Heat and Unrest demonstrably fight each other."
            // \u00a75.2's table says reducing Heat raises Unrest, and this is the mechanism with
            // nothing invented for it - no tax system, because coin was always the thing both
            // pressures were competing for.
            //
            // Asserted at the join rather than through a season of economy: a treasury too thin
            // for its payroll, one convoy at sea, and the question of who gets paid first. The
            // ordering in pipeline.csv is the whole dilemma, and this is it.
            int withGuard = Arrears(escorting: true);
            int without = Arrears(escorting: false);

            Assert.That(withGuard, Is.GreaterThan(without),
                $"guarding cost the crew nothing ({withGuard} against {without}), so the two " +
                "pressures do not touch");
        }

        /// <summary>
        /// Runs one morning for a port too poor to pay everybody, and reports what went unpaid.
        /// </summary>
        private static int Arrears(bool escorting)
        {
            var run = new Run();
            EntityId port = run.Player;

            run.Session.Submit(new BuyFrom(run.City("fairhaven"), run.Good("food"), 3f));
            if (escorting) run.Session.Submit(new SetEscort(escorting));

            run.Days(1);
            Assume.That(run.Convoys, Is.EqualTo(1), "nothing at sea to guard");

            // Thin enough that the escort is the difference between paying the crew and not.
            Port.Treasury(run.World, port).Coin = 10;
            Port.Treasury(run.World, port).Arrears = 0;

            run.Days(1);

            return Port.Treasury(run.World, port).Arrears;
        }

        [Test]
        public void Guarding_costs_something_over_a_long_run()
        {
            // The same claim at the scale a player feels it, and deliberately a close-run thing:
            // an escort that paid for itself would be a single right answer with the sign
            // flipped, which is no more a dilemma than one that never paid.
            //
            // Both ports are funded every morning. Without it they simply go broke buying iron
            // by about day twenty, stop shipping, and the test ends up comparing two ports with
            // nothing on the water - which measures the price of iron rather than the price of a
            // guard.
            var guarded = new Run();
            var exposed = new Run();

            guarded.Session.Submit(new SetEscort(true));

            for (int day = 0; day < 60; day++)
            {
                Fund(guarded, 40);
                Fund(exposed, 40);

                if (guarded.Convoys == 0)
                    guarded.Session.Submit(new BuyFrom(guarded.City("ironhold"), guarded.Good("iron"), 5f));
                if (exposed.Convoys == 0)
                    exposed.Session.Submit(new BuyFrom(exposed.City("ironhold"), exposed.Good("iron"), 5f));

                guarded.Days(1);
                exposed.Days(1);
            }

            TestContext.Out.WriteLine(
                $"guarded {guarded.Coin} coin at heat {guarded.Heat:0.000}, " +
                $"exposed {exposed.Coin} coin at heat {exposed.Heat:0.000}");

            Assume.That(guarded.Heat, Is.GreaterThan(0.1f), "neither port was ever worth watching");

            Assert.That(exposed.Feed(), Does.Contain("raid"),
                "the unguarded port was never troubled, so the guard buys nothing");
            Assert.That(guarded.Feed(), Does.Contain("escort"),
                "the guard was never paid for");
        }

        private static void Fund(Run run, int coin) =>
            Port.Treasury(run.World, run.Player).Coin += coin;

    }
}
