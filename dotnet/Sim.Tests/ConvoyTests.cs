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
using RTS.Sim.Engine.State;
using RTS.Sim.Engine.Time;
using RTS.Sim.Scenarios;
using RTS.Sim.Session;
using RTS.Sim.Systems;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// Cargo between cities (GDD P1, §5.1, §5.3).
    /// </summary>
    /// <remarks>
    /// P1: "wealth is cargo, cargo moves along a route on the map, and anything on the map can
    /// be intercepted. Prosperity is therefore exposed by construction." These tests are about
    /// the exposure as much as the delivery — a convoy exists for days, and what it holds is in
    /// nobody's warehouse while it does.
    /// </remarks>
    [Category(TestCategories.Functional)]
    public class ConvoyTests
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

            public float Units(EntityId port, string good) =>
                Port.UnitsOf(World, port, Good(good));

            public int Coin(EntityId port) => Port.Treasury(World, port).Coin;

            public int Convoys => World.Store<Convoy>().Count;

            public void Days(int n) { for (int i = 0; i < n; i++) Session.Step(); }
        }

        // ------------------------------------------------------------------ sailing

        [Test]
        public void Buying_pays_now_and_delivers_later()
        {
            // Measured against a city doing nothing, because the port does not hold still: the
            // workshop eats iron every day and the market sells the rum it makes. An absolute
            // number here would be testing the economy, not the convoy.
            var run = new Run();
            var control = new Run();
            EntityId ironhold = run.City("ironhold");

            run.Session.Submit(new BuyFrom(ironhold, run.Good("iron"), 4f));
            run.Days(1);
            control.Days(1);

            Assert.That(run.Coin(run.Player), Is.LessThan(control.Coin(control.Player)),
                "paid on dispatch");
            Assert.That(run.Units(run.Player, "iron"),
                Is.EqualTo(control.Units(control.Player, "iron")).Within(0.01f),
                "and the iron is still at sea");
            Assert.That(run.Convoys, Is.EqualTo(1));
        }

        [Test]
        public void The_cargo_lands_after_the_crossing()
        {
            var run = new Run();
            var control = new Run();
            EntityId ironhold = run.City("ironhold");

            run.Session.Submit(new BuyFrom(ironhold, run.Good("iron"), 4f));

            // Ironhold is five days out, and nothing arrives early.
            run.Days(4);
            control.Days(4);
            Assert.That(run.Units(run.Player, "iron"),
                Is.EqualTo(control.Units(control.Player, "iron")).Within(0.01f),
                "still at sea on day four");

            run.Days(2);
            control.Days(2);

            Assert.That(run.Units(run.Player, "iron"),
                Is.GreaterThan(control.Units(control.Player, "iron")));
            Assert.That(run.Convoys, Is.Zero, "and the convoy is gone once it has landed");
        }

        [Test]
        public void While_it_is_at_sea_it_belongs_to_nobody_ashore()
        {
            // The exposure P1 is built on. Goods in transit are in neither city's store, which
            // is what gives a raid something to take.
            var run = new Run();
            var control = new Run();
            EntityId ironhold = run.City("ironhold");

            run.Session.Submit(new BuyFrom(ironhold, run.Good("iron"), 4f));
            run.Days(1);
            control.Days(1);

            Assert.That(run.Units(ironhold, "iron"),
                Is.LessThan(control.Units(control.City("ironhold"), "iron")), "it has left");
            Assert.That(run.Units(run.Player, "iron"),
                Is.EqualTo(control.Units(control.Player, "iron")).Within(0.01f),
                "and has not arrived");
        }

        [Test]
        public void Selling_sends_the_goods_now_and_is_paid_on_arrival()
        {
            var run = new Run();
            var control = new Run();
            EntityId ironhold = run.City("ironhold");

            run.Session.Submit(new SellTo(ironhold, run.Good("food"), 5f));
            run.Days(1);
            control.Days(1);

            Assert.That(run.Convoys, Is.EqualTo(1), "the grain has gone");
            Assert.That(run.Coin(run.Player), Is.LessThan(control.Coin(control.Player)),
                "and has not been paid for: the merchant would have bought it this morning");

            run.Days(6);
            control.Days(6);

            // Level again once it lands, not ahead: a city pays the same sell_price the passing
            // merchant does, so shipping grain to Ironhold earns exactly what selling it at home
            // would have earned, seven days later. That is the honest state of §5.3 today — a
            // route buys access to goods you cannot make, not a better price. Prices that differ
            // by port, which is where the profit is meant to come from, need local supply to move
            // them and is its own piece of work.
            Assert.That(run.Coin(run.Player), Is.EqualTo(control.Coin(control.Player)),
                "and is paid on arrival");
            Assert.That(run.Convoys, Is.Zero, "the crossing is over");
        }

        [Test]
        public void A_nearer_city_is_a_shorter_wait()
        {
            // Distance is a decision, not decoration. Fairhaven is two days out and Ironhold
            // five, and the difference is what makes a near partner worth less per unit and
            // more per week.
            var run = new Run();

            Assert.That(
                ConvoySystem.DaysBetween(run.World, run.Tables, run.Player, run.City("fairhaven")),
                Is.LessThan(
                    ConvoySystem.DaysBetween(run.World, run.Tables, run.Player, run.City("ironhold"))));
        }

        // --------------------------------------------------------------- refusals

        [Test]
        public void A_city_will_not_sell_the_grain_it_means_to_eat()
        {
            // Only what it can spare, above its own reserve. A city that starved to fill an
            // order is not a trading partner, it is a bug.
            var run = new Run();

            Assert.That(run.Session.Validate(new BuyFrom(run.City("ironhold"), run.Good("food"), 500f)),
                Is.EqualTo(CommandRejection.NotYet));
        }

        [Test]
        public void You_cannot_buy_what_you_cannot_pay_for()
        {
            var run = new Run();

            Assert.That(run.Session.Validate(new BuyFrom(run.City("ironhold"), run.Good("iron"), 500f)),
                Is.Not.EqualTo(CommandRejection.None));
        }

        [Test]
        public void You_cannot_sell_what_you_do_not_have()
        {
            var run = new Run();

            Assert.That(run.Session.Validate(new SellTo(run.City("ironhold"), run.Good("iron"), 900f)),
                Is.EqualTo(CommandRejection.NotYet));
        }

        [Test]
        public void A_city_cannot_trade_with_itself()
        {
            var run = new Run();

            Assert.That(run.Session.Validate(new BuyFrom(run.Player, run.Good("iron"), 1f)),
                Is.EqualTo(CommandRejection.InvalidTarget));
        }

        [Test]
        public void Something_that_is_not_a_city_is_refused()
        {
            var run = new Run();
            EntityId crew = run.World.Store<CrewMember>().Ids[0];

            Assert.That(run.Session.Validate(new BuyFrom(crew, run.Good("iron"), 1f)),
                Is.EqualTo(CommandRejection.InvalidTarget));
        }

        // ------------------------------------------------------------ the whole point

        [Test]
        public void Iron_from_Ironhold_starts_the_workshop_again()
        {
            // The sentence the whole phase was built to make true. Saltmarsh has no mine and a
            // workshop that wants iron every day; it runs out within a week and the feed says so
            // every morning after. A route is the answer, and this is it working.
            var run = new Run();
            EntityId ironhold = run.City("ironhold");

            run.Days(10);
            Assert.That(run.Units(run.Player, "iron"), Is.LessThan(1f), "the iron has run out");
            Assert.That(Feed(run), Does.Contain("ran short"), "and the workshop has gone quiet");

            run.Session.Submit(new BuyFrom(ironhold, run.Good("iron"), 6f));
            run.Days(5);

            // Rum is sold the day it is made — its keep is zero — so a stock reading would show
            // nothing whatever happened. What proves the workshop is working is that it stops
            // complaining.
            run.Session.Feed.Clear();
            run.Days(2);

            Assert.That(Feed(run), Does.Not.Contain("ran short"),
                "the workshop is distilling again: " + Feed(run));
        }

        [Test]
        public void A_landing_is_reported()
        {
            var run = new Run();

            run.Session.Submit(new BuyFrom(run.City("fairhaven"), run.Good("food"), 3f));
            run.Days(4);

            Assert.That(Feed(run), Does.Contain("sent"), Feed(run));
            Assert.That(Feed(run), Does.Contain("arrived"), Feed(run));
        }

        [Test]
        public void Nothing_is_lost_or_made_on_the_crossing()
        {
            // What leaves one city arrives at the other, and the journey changes no total. A
            // route that quietly created iron would be an income tick with extra steps, which is
            // exactly what P1 forbids.
            //
            // Measured on a bare world rather than a running port, because a city with a market
            // is selling and eating all week: a world total there would be measuring the economy,
            // and it would not balance however correct the convoy was.
            var world = new World();
            var events = new EventQueue();
            BalanceTables balance = Balance();
            var ctx = new Context(1, 0f, events, rng: null, balance: balance);

            EntityId from = City(world, balance, "ironhold");
            EntityId to = City(world, balance, "saltmarsh");
            int iron = ConsumptionSystem.IndexOf(balance, "iron");

            Port.Add(world, from, iron, 10f);
            float total = Port.UnitsOf(world, from, iron) + Port.UnitsOf(world, to, iron);

            Port.Take(world, from, iron, 4f);
            events.BeginCause(CauseId.Root, 1);
            ConvoySystem.Dispatch(world, balance, from, to, iron, 4f,
                coinOnArrival: 0, owner: to, in ctx);
            events.EndCause();

            var convoy = new ConvoySystem();
            for (int day = 0; day < 8; day++)
            {
                Assert.That(Carried(world, from, to, iron), Is.EqualTo(total).Within(0.01f),
                    "day " + day);

                events.BeginCause(CauseId.Root, day + 2);
                convoy.Run(world, in ctx);
                events.EndCause();
            }

            Assert.That(Port.UnitsOf(world, to, iron), Is.EqualTo(4f).Within(0.01f),
                "and all of it came ashore");
            Assert.That(world.Store<Convoy>().Count, Is.Zero);
        }

        /// <summary>A port with real coordinates, so the crossing takes its real number of days.</summary>
        private static EntityId City(World world, BalanceTables balance, string id)
        {
            EntityId port = world.CreateEntity();
            world.Add(port, new PortState
            {
                DefinitionIndex = Enumerable.Range(0, balance.Ports.Count)
                    .First(i => balance.Ports[i].Id == id),
            });

            return port;
        }

        private static float Carried(World world, EntityId a, EntityId b, int good)
        {
            float total = Port.UnitsOf(world, a, good) + Port.UnitsOf(world, b, good);

            ComponentStore<Convoy> convoys = world.Store<Convoy>();
            for (int i = 0; i < convoys.Count; i++)
                if (convoys.Values[i].GoodIndex == good) total += convoys.Values[i].Units;

            return total;
        }

        // ------------------------------------------------------------------ offered

        [Test]
        public void The_player_is_offered_a_route_to_every_neighbour()
        {
            // A mechanic the player cannot reach is not a mechanic. Every city that has
            // something to spare should appear as a button, with its price and its days on it.
            var run = new Run();

            string[] trade = run.Session.Actions()
                .Where(a => a.Group == "Trade").Select(a => a.Label).ToArray();

            Assert.That(trade, Is.Not.Empty);
            Assert.That(trade.Any(l => l.Contains("Ironhold")), Is.True, string.Join(" | ", trade));
            Assert.That(trade.All(l => l.StartsWith("Buy") || l.StartsWith("Sell")), Is.True);
        }

        [Test]
        public void Only_deals_that_would_be_accepted_are_offered()
        {
            // Listing a purchase that will be refused teaches the player nothing except to
            // distrust the list.
            var run = new Run();

            foreach (PlayerAction action in run.Session.Actions().Where(a => a.Group == "Trade"))
                Assert.That(run.Session.Validate(action.Command), Is.EqualTo(CommandRejection.None),
                    action.Label);
        }

        [Test]
        public void A_route_offered_can_be_taken()
        {
            // The end to end sentence, through the same surface a player uses.
            var run = new Run();

            PlayerAction buyIron = run.Session.Actions().First(
                a => a.Group == "Trade" && a.Label.StartsWith("Buy") && a.Label.Contains("iron"));

            run.Session.Submit(buyIron.Command);
            run.Days(1);

            Assert.That(run.Convoys, Is.EqualTo(1));
        }

        private static string Feed(Run run) =>
            string.Join(" | ", run.Session.Feed.Entries.Select(e => e.Text));

    }
}
