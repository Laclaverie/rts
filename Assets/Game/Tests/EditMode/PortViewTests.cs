using System.Linq;
using NUnit.Framework;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Game.Boot;
using RTS.Game.Presentation;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Time;
using RTS.Sim.Presentation;
using RTS.Sim.Session;
using RTS.Sim.Systems;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = RTS.Sim.Engine.Entities.EntityId;

namespace RTS.Game.Tests
{
    /// <summary>
    /// The half of the close-up view the headless suite cannot reach.
    /// </summary>
    /// <remarks>
    /// Where the buildings stand and who is in the square are settled in <c>PortModelTests</c>
    /// without an engine. What is left for Unity is narrow: that the view opens and closes, that
    /// what is in the model reaches the screen, and that the square stays square.
    /// </remarks>
    [Category("Functional")]
    public class PortViewTests
    {
        private static GameSession Session()
        {
            var report = new ValidationReport();

            BalanceTables balance = BalanceTables.Load(new BalanceSources
            {
                Goods = BalanceFiles.ReadCsv(BalanceTables.GoodsFile),
                Buildings = BalanceFiles.ReadCsv(BalanceTables.BuildingsFile),
                CrewRoles = BalanceFiles.ReadCsv(BalanceTables.CrewRolesFile),
                Strata = BalanceFiles.ReadCsv(BalanceTables.StrataFile),
                Ladder = BalanceFiles.ReadCsv(BalanceTables.LadderFile),
                Repression = BalanceFiles.ReadCsv(BalanceTables.RepressionFile),
                Ports = BalanceFiles.ReadCsv(BalanceTables.PortsFile),
                Mob = BalanceFiles.ReadCsv(BalanceTables.MobFile),
            }, report);

            Clock clock = Clock.Load(ConfigFiles.ReadCsv(ConfigFiles.ClockFile), report);
            report.ThrowIfInvalid();

            return GameSession.Start(balance, clock, BalanceFiles.ReadText("pipeline.csv"));
        }

        private static Label[] Labels(PortView view) =>
            view.Root.Query<Label>().ToList().Where(l => !string.IsNullOrEmpty(l.text)).ToArray();

        [Test]
        public void It_is_shut_until_somebody_opens_it()
        {
            GameSession session = Session();
            var view = new PortView(session);
            view.Build();

            Assert.That(view.IsOpen, Is.False);
            Assert.That(view.Root.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void Opening_a_city_draws_its_town()
        {
            GameSession session = Session();
            var view = new PortView(session);
            view.Build();

            bool told = false;
            view.Changed = () => told = true;

            view.Open(session.PlayerPort);

            Assert.That(view.IsOpen, Is.True);
            Assert.That(told, Is.True, "the card is redrawn when the view changes");
            Assert.That(view.Port.Buildings, Is.Not.Empty);

            // Every building in the model is on screen with its own name on it.
            string[] drawn = Labels(view).Select(l => l.text).ToArray();
            foreach (PortBuilding building in view.Port.Buildings)
                Assert.That(drawn.Any(t => t == building.Name), Is.True, building.Name);
        }

        [Test]
        public void The_heading_says_where_you_are_and_how_it_is()
        {
            GameSession session = Session();
            var view = new PortView(session);
            view.Build();
            view.Open(session.PlayerPort);

            string heading = Labels(view)[0].text;

            Assert.That(heading, Does.Contain("Saltmarsh"));
            Assert.That(heading, Does.Contain("Calm"));
        }

        [Test]
        public void Closing_it_puts_the_map_back()
        {
            GameSession session = Session();
            var view = new PortView(session);
            view.Build();

            view.Open(session.PlayerPort);
            view.Close();

            Assert.That(view.IsOpen, Is.False);
            Assert.That(view.Root.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(view.Port.Buildings, Is.Empty);
        }

        [Test]
        public void Ticking_a_shut_view_does_nothing_rather_than_throwing()
        {
            // Boot ticks it every frame whether or not anybody is inside a city.
            GameSession session = Session();
            var view = new PortView(session);
            view.Build();

            Assert.DoesNotThrow(() => view.Tick());
            Assert.That(view.IsOpen, Is.False);
        }

        [Test]
        public void A_revolt_fills_the_square_with_people_who_have_names()
        {
            // The gate. At map scale a named face is a slightly larger dot; here the carpenter
            // is a person with a label saying which way they went.
            GameSession session = Session();
            var view = new PortView(session);
            view.Build();
            view.Open(session.PlayerPort);

            Assert.That(view.Port.Crowd, Is.Empty, "a calm port has an empty square");

            for (int day = 0; day < 120; day++)
            {
                session.Submit(new Shock(ShockKind.Theft, 100000f));
                session.Step();

                if (MobSystem.Bodies(session.World, session.PlayerPort) > 0) break;
            }

            view.Refresh();

            Assert.That(view.Port.Crowd, Is.Not.Empty, "the port rose and the square is empty");
            Assert.That(Labels(view)[0].text, Does.Contain("in the square"));
        }

        [Test]
        public void The_square_stays_square_in_a_window_of_any_shape()
        {
            // A crowd that is round on one monitor and flat on another is a crowd nobody trusts.
            // Placing everything as a percentage of the view would have been a percentage of the
            // width one way and of the height the other, which is an oval in any window that is
            // not itself square — which is all of them.
            foreach (Rect window in new[]
                     {
                         new Rect(0, 0, 1600, 500),
                         new Rect(0, 0, 500, 1600),
                         new Rect(0, 0, 900, 900),
                     })
            {
                Rect box = PortView.SquareIn(window);

                Assert.That(box.width, Is.EqualTo(box.height).Within(1e-3f), window.ToString());

                // And clear of the two things that float over it: the port card down the left,
                // whose orders stay reachable from inside a city, and the heading.
                Assert.That(box.xMin, Is.GreaterThan(0f), "behind the card: " + window);
                Assert.That(box.yMin, Is.GreaterThan(0f), "under the heading: " + window);
                Assert.That(box.xMax, Is.LessThanOrEqualTo(window.width + 1e-3f), window.ToString());
                Assert.That(box.yMax, Is.LessThanOrEqualTo(window.height + 1e-3f), window.ToString());
            }
        }

        [Test]
        public void Looking_somewhere_you_are_not_allowed_is_ignored()
        {
            // The door is the session's to open (§5.6), not the panel's. A neighbour's rung and
            // the state of their buildings are intelligence you have not bought.
            GameSession session = Session();
            var view = new PortView(session);
            view.Build();

            EntityId ironhold = EntityId.None;
            ComponentStore<PortState> ports = session.World.Store<PortState>();
            for (int i = 0; i < ports.Count; i++)
                if (session.Balance.Ports[ports.Values[i].DefinitionIndex].Id == "ironhold")
                    ironhold = ports.Ids[i];

            view.Open(EntityId.None);
            Assert.That(view.IsOpen, Is.False);

            view.Open(ironhold);
            Assert.That(view.IsOpen, Is.False, "a neighbour's city is not yours to walk into");

            view.Open(session.PlayerPort);
            Assert.That(view.IsOpen, Is.True);
        }
    }
}
