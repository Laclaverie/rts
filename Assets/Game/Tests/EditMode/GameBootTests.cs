using System.Linq;
using NUnit.Framework;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Game.Boot;
using RTS.Sim.Engine.Time;
using RTS.Sim.Session;
using RTS.Sim.Systems;
using UnityEngine;
using UnityEngine.UIElements;

namespace RTS.Game.Tests
{
    /// <summary>
    /// The half of the composition root the headless suite cannot reach.
    /// </summary>
    /// <remarks>
    /// <c>dotnet test</c> already proves the clock, the session and every readout, because none
    /// of them knows what an engine is. What is left for Unity to answer is narrow and worth
    /// asking exactly once: do the shipped files resolve through StreamingAssets, does the
    /// panel have something to draw with, and is the scene actually wired.
    /// <para>
    /// If this file ever grows past that, something has leaked across the line.
    /// </para>
    /// </remarks>
    [Category("Functional")]
    public class GameBootTests
    {
        private static GameSession StartTheWayBootDoes(out ValidationReport report)
        {
            report = new ValidationReport();

            BalanceTables balance = BalanceTables.Load(new BalanceSources
            {
                Goods = BalanceFiles.ReadCsv(BalanceTables.GoodsFile),
                Buildings = BalanceFiles.ReadCsv(BalanceTables.BuildingsFile),
                CrewRoles = BalanceFiles.ReadCsv(BalanceTables.CrewRolesFile),
                Strata = BalanceFiles.ReadCsv(BalanceTables.StrataFile),
                Ladder = BalanceFiles.ReadCsv(BalanceTables.LadderFile),
                Repression = BalanceFiles.ReadCsv(BalanceTables.RepressionFile),
            }, report);

            Clock clock = Clock.Load(ConfigFiles.ReadCsv(ConfigFiles.ClockFile), report);

            return GameSession.Start(
                balance, clock, BalanceFiles.ReadText("pipeline.csv"), PortScenario.Default());
        }

        [Test]
        public void The_shipped_files_start_a_session()
        {
            GameSession session = StartTheWayBootDoes(out ValidationReport report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));
            Assert.That(session.Day, Is.EqualTo(1));
            Assert.That(session.Readouts().Count, Is.GreaterThan(0));
        }

        [Test]
        public void Clock_csv_resolves_and_says_what_the_design_says()
        {
            StartTheWayBootDoes(out ValidationReport report);
            Clock clock = Clock.Load(ConfigFiles.ReadCsv(ConfigFiles.ClockFile), report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));
            Assert.That(clock.SecondsPerDay, Is.EqualTo(1200f).Within(1e-4f));
        }

        [Test]
        public void The_panel_settings_asset_is_where_boot_looks_for_it()
        {
            // Loaded from Resources rather than an inspector reference so the scene needs no
            // wiring by hand — which means nothing but this test notices if it goes missing.
            var settings = Resources.Load<PanelSettings>(GameBoot.PanelSettingsResource);

            Assert.That(settings, Is.Not.Null,
                "Resources/" + GameBoot.PanelSettingsResource + " is missing");
            Assert.That(settings.themeStyleSheet, Is.Not.Null,
                "a PanelSettings with no theme draws nothing at all");
        }

        [Test]
        public void The_panel_builds_a_row_for_every_readout()
        {
            GameSession session = StartTheWayBootDoes(out _);
            var panel = new Presentation.PortPanel(session);

            VisualElement root = panel.Build();

            Assert.That(root.childCount, Is.EqualTo(2), "a control bar and the readouts");

            VisualElement readouts = root[1];
            Assert.That(readouts.childCount, Is.EqualTo(session.Readouts().Count));
        }

        [Test]
        public void The_panel_reflects_a_paused_clock()
        {
            GameSession session = StartTheWayBootDoes(out _);
            var panel = new Presentation.PortPanel(session);
            panel.Build();

            Button pause = panel.Root.Query<Button>().ToList().First();
            Assert.That(pause.text, Is.EqualTo("Pause"));

            session.Clock.Pause();
            panel.Refresh();

            Assert.That(pause.text, Is.EqualTo("Play"));
        }
    }
}
