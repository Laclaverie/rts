using System.Linq;
using NUnit.Framework;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Game.Boot;
using RTS.Game.Presentation;
using RTS.Sim.Components;
using RTS.Sim.Engine.Time;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Presentation;
using RTS.Sim.Session;
using RTS.Sim.Systems;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = RTS.Sim.Engine.Entities.EntityId;

namespace RTS.Game.Tests
{
    /// <summary>
    /// The half of the map the headless suite cannot reach.
    /// </summary>
    /// <remarks>
    /// Where a city is, where a ship has got to and what a click means are all proved in
    /// <c>MapModelTests</c> without an engine. What is left for Unity is narrow: does a marker
    /// exist for every city, does clicking one actually select it, and do landed convoys stop
    /// being drawn. If this file grows past that, something has leaked across the line.
    /// </remarks>
    [Category("Functional")]
    public class MapPanelTests
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
            }, report);

            Clock clock = Clock.Load(ConfigFiles.ReadCsv(ConfigFiles.ClockFile), report);
            report.ThrowIfInvalid();

            return GameSession.Start(balance, clock, BalanceFiles.ReadText("pipeline.csv"));
        }

        private static EntityId City(GameSession session, string id)
        {
            ComponentStore<PortState> ports = session.World.Store<PortState>();
            for (int i = 0; i < ports.Count; i++)
                if (session.Balance.Ports[ports.Values[i].DefinitionIndex].Id == id)
                    return ports.Ids[i];

            return EntityId.None;
        }

        /// <summary>Every marker the panel drew, at any depth.</summary>
        private static VisualElement[] Markers(MapPanel map) =>
            map.Root.Query<VisualElement>().ToList()
                .Where(e => !string.IsNullOrEmpty(e.tooltip)).ToArray();

        [Test]
        public void Every_city_gets_a_marker_on_screen()
        {
            GameSession session = Session();
            var map = new MapPanel(session);
            map.Build();

            foreach (MapPort port in map.Map.Ports)
                Assert.That(Markers(map).Any(m => m.tooltip == port.Name), Is.True, port.Name);
        }

        [Test]
        public void Clicking_a_city_selects_it_and_says_so()
        {
            // The one thing only Unity can answer, and the reason this file exists: that the
            // handler on the element the panel built is actually reached. A panel that draws
            // perfectly and responds to nothing has happened here before.
            //
            // The elements have to hang off a real panel for that: UI Toolkit dispatches
            // through the panel, and SendEvent on a detached tree returns quietly having done
            // nothing — which would have made this test pass while proving the opposite.
            GameSession session = Session();
            var map = new MapPanel(session);

            var host = new GameObject("map test host");
            try
            {
                UIDocument document = host.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>(GameBoot.PanelSettingsResource);
                Assert.That(document.panelSettings, Is.Not.Null, "no PanelSettings to draw with");

                document.rootVisualElement.Add(map.Build());

                bool told = false;
                map.SelectionChanged = () => told = true;

                EntityId ironhold = City(session, "ironhold");
                VisualElement marker = Markers(map).First(m => m.tooltip == "Ironhold");

                using (var click = ClickEvent.GetPooled())
                {
                    click.target = marker;
                    marker.SendEvent(click);
                }

                Assert.That(session.Selected, Is.EqualTo(ironhold));
                Assert.That(told, Is.True, "the orders card is redrawn on a real change");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void The_panel_and_the_map_agree_about_what_is_selected()
        {
            // Two surfaces reading one piece of session state. If selection lived in the panel
            // instead, this is the test that could not be written.
            GameSession session = Session();
            var map = new MapPanel(session);
            map.Build();

            var panel = new PortPanel(session);
            panel.Build();

            session.Select(City(session, "ironhold"));
            panel.Refresh();

            Assert.That(panel.Root.Query<Label>().ToList().Any(l => l.text.Contains("Ironhold")),
                Is.True, "the card names the city the map is highlighting");
        }

        [Test]
        public void A_convoy_is_drawn_while_it_sails_and_gone_once_it_lands()
        {
            GameSession session = Session();
            var map = new MapPanel(session);
            map.Build();

            int before = Markers(map).Length;

            session.Submit(new BuyFrom(
                City(session, "fairhaven"),
                ConsumptionSystem.IndexOf(session.Balance, "food"),
                3f));
            session.Step();
            map.Tick();

            Assert.That(Markers(map).Length, Is.GreaterThan(before), "a ship is on the water");

            for (int day = 0; day < 4; day++) session.Step();
            map.Tick();

            Assert.That(Markers(map).Length, Is.EqualTo(before), "and its marker went with it");
        }

        [Test]
        public void The_highlight_follows_a_selection_made_anywhere()
        {
            // Not only the map's own clicks. The panel, a shortcut, or anything added later can
            // change what is selected, and a ring left on the wrong city says nothing about
            // being wrong.
            GameSession session = Session();
            var map = new MapPanel(session);
            map.Build();

            session.Select(City(session, "ironhold"));
            map.Tick();

            VisualElement ironhold = Markers(map).First(m => m.tooltip == "Ironhold");
            VisualElement home = Markers(map).First(m => m.tooltip == "Saltmarsh");

            Assert.That(ironhold.resolvedStyle.borderTopColor.a, Is.GreaterThan(0f));
            Assert.That(home.resolvedStyle.borderTopColor.a, Is.EqualTo(0f));
        }

        [Test]
        public void A_risen_port_puts_bodies_on_the_map()
        {
            // BUILD_ORDER's gate for this phase is that the revolt reads as an event rather than
            // a number. Whether it reads is for a person watching; what can be asserted here is
            // that there is something to watch at all.
            GameSession session = Session();
            var map = new MapPanel(session);
            map.Build();

            Assert.That(map.Map.Crowd, Is.Empty, "a calm port has an empty square");

            EntityId home = session.PlayerPort;
            for (int day = 0; day < 90; day++)
            {
                session.Submit(new Shock(ShockKind.Theft, 100000f));
                session.Step();

                if (MobSystem.Bodies(session.World, home) > 0) break;
            }

            map.Refresh();

            Assert.That(map.Map.Crowd, Is.Not.Empty, "the port rose and nobody was drawn");
            Assert.That(map.Map.Crowd.All(b => b.Port == home), Is.True);

            // Drawn under the cities, so a marker is never lost inside its own mob.
            Assert.That(map.Root.Query<VisualElement>().ToList().Count,
                Is.GreaterThan(map.Map.Crowd.Count));
        }

        [Test]
        public void The_map_can_be_built_before_a_day_has_run()
        {
            // Boot builds the panel inside Awake, before anything has advanced.
            GameSession session = Session();
            var map = new MapPanel(session);

            Assert.That(map.Build(), Is.Not.Null);
            Assert.That(map.Map.Ports, Is.Not.Empty);
        }
    }
}
