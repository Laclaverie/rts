using System;
using System.Collections.Generic;
using System.IO;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Engine.State;
using RTS.Sim.Systems;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// The Phase 1 gate (BUILD_ORDER §2): the cascade behaves as designed.
    /// </summary>
    /// <remarks>
    /// Three assertions, from §5.2.3:
    /// <list type="bullet">
    /// <item>a single shock is always survivable</item>
    /// <item>correlated shocks spiral</item>
    /// <item>reserves-as-slack visibly determines which happens</item>
    /// </list>
    /// <para>
    /// Written against <see cref="PortCondition"/> rather than raw numbers. A gate asserting
    /// "coin above 143 on day 20" would need re-editing on every tuning pass until someone
    /// deleted it; these survive tuning, and if the words stop meaning what they say then that
    /// is the thing to fix.
    /// </para>
    /// <para>
    /// Functional rather than unit: these load the shipped balance files and run whole
    /// scenarios, which is §8.2's shape — a seed plus a command log, replayed.
    /// </para>
    /// </remarks>
    [Category(TestCategories.Functional)]
    public class CascadeGateTests
    {
        private static BalanceTables ShippedBalance()
        {
            string directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "Balance");
            var report = new ValidationReport();

            BalanceTables tables = BalanceTables.Load(
                CsvTable.Parse(File.ReadAllText(Path.Combine(directory, BalanceTables.GoodsFile)),
                    BalanceTables.GoodsFile),
                CsvTable.Parse(File.ReadAllText(Path.Combine(directory, BalanceTables.BuildingsFile)),
                    BalanceTables.BuildingsFile),
                CsvTable.Parse(File.ReadAllText(Path.Combine(directory, BalanceTables.CrewRolesFile)),
                    BalanceTables.CrewRolesFile),
                report);

            report.ThrowIfInvalid();
            return tables;
        }

        private static Pipeline DayBoundary(CommandDispatcher dispatcher)
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Balance", "pipeline.csv");

            var systems = new List<ISystem>
            {
                new ConsumptionSystem(), new WagesSystem(), new UpkeepSystem(),
                new DesertionSystem(), new ProductionSystem(), new MarketSystem(),
            };

            // The drain is not in the shipped pipeline.csv yet, so the scenario declares it
            // alongside the shipped rows rather than the file being edited for a test.
            string csv = File.ReadAllText(path).TrimEnd() + "\n" +
                         $"DayBoundary,5,{CommandDrainSystem.SystemId},true\n";

            systems.Add(new CommandDrainSystem(dispatcher));
            return Pipeline.Build(CsvTable.Parse(csv, "pipeline.csv"), systems);
        }

        /// <summary>
        /// Runs a port for <paramref name="days"/>, submitting whatever the schedule says on
        /// each day, and returns how it ended up.
        /// </summary>
        /// <summary>The reserves the shipped starting port actually has.</summary>
        private static int DefaultReserves => PortScenario.Default().StartingCoin;

        private static Run Simulate(int startingCoin, int days,
            IReadOnlyList<KeyValuePair<int, Shock>>? schedule = null)
        {
            BalanceTables balance = ShippedBalance();

            PortScenario scenario = PortScenario.Default();
            scenario.StartingCoin = startingCoin;

            ReplayRun run = ReplayRun.Start(
                seed: 1,
                new ICommandHandler[] { new ShockHandler() },
                DayBoundary,
                scenario.Build(balance),
                balance);

            var worst = PortCondition.Healthy;

            for (int day = 1; day <= days; day++)
            {
                if (schedule != null)
                {
                    foreach (KeyValuePair<int, Shock> entry in schedule)
                        if (entry.Key == day)
                            run.Submit(entry.Value);
                }

                run.AdvanceDay();
                run.Events.Drain();

                PortCondition today = PortHealth.Of(run.World, balance);
                if (today > worst) worst = today;
            }

            return new Run(PortHealth.Of(run.World, balance), worst,
                PortReport.Of(run.World, balance, days));
        }

        private readonly struct Run
        {
            public Run(PortCondition ended, PortCondition worst, PortReport report)
            {
                Ended = ended;
                Worst = worst;
                Report = report;
            }

            public readonly PortCondition Ended;

            /// <summary>The worst it got. A port that dipped and came back is the interesting case.</summary>
            public readonly PortCondition Worst;

            public readonly PortReport Report;

            public override string ToString() =>
                $"ended {Ended} (worst {Worst}) — {Report.ToRow().Trim()}";
        }


        // ------------------------------------------------------------------- the gate

        [Test]
        public void An_undisturbed_port_stays_healthy()
        {
            // The control. Without it, "the shocked port collapsed" says nothing.
            Run run = Simulate(DefaultReserves, days: 40);

            Assert.That(run.Ended, Is.EqualTo(PortCondition.Healthy), run.ToString());
        }

        [Test]
        public void A_single_shock_is_survived()
        {
            // §5.2.3: "A single shock is paid for out of reserves and recovered from — always.
            // Collapse is never the result of one roll, which is what keeps it fair."
            foreach (ShockKind kind in new[]
                     {
                         ShockKind.HarvestFailure, ShockKind.Storm, ShockKind.Theft, ShockKind.Desertion,
                     })
            {
                Run run = Simulate(DefaultReserves, days: 40, Schedule((10, Shock(kind))));

                Assert.That(run.Ended, Is.Not.EqualTo(PortCondition.Collapsed),
                    $"{kind}: {run}");
            }
        }

        [Test]
        public void Three_correlated_shocks_spiral()
        {
            // "But shocks compound, and the compounding is mechanical rather than authored."
            Run run = Simulate(DefaultReserves, days: 40, Schedule(
                (10, Shock(ShockKind.Storm)),
                (12, Shock(ShockKind.HarvestFailure)),
                (14, Shock(ShockKind.Theft))));

            Assert.That(run.Ended, Is.EqualTo(PortCondition.Collapsed), run.ToString());
        }

        [Test]
        public void Reserves_decide_which_happens()
        {
            // "Reserves are therefore the real resource, and maintaining slack is the actual
            // skill the game rewards."
            IReadOnlyList<KeyValuePair<int, Shock>> shocks = Schedule(
                (10, Shock(ShockKind.Storm)),
                (12, Shock(ShockKind.HarvestFailure)),
                (14, Shock(ShockKind.Theft)));

            Run withSlack = Simulate(DefaultReserves * 3, days: 40, shocks);
            Run withoutSlack = Simulate(DefaultReserves / 2, days: 40, shocks);

            Assert.That(withoutSlack.Ended, Is.EqualTo(PortCondition.Collapsed),
                $"thin reserves: {withoutSlack}");
            Assert.That(withSlack.Ended, Is.Not.EqualTo(PortCondition.Collapsed),
                $"deep reserves: {withSlack}");
        }

        [Test]
        public void The_same_scenario_replays_identically()
        {
            // The gate rests on the determinism gate. If these diverged, every result above
            // would be noise.
            Run first = Simulate(DefaultReserves, 25, Schedule((10, Shock(ShockKind.Storm))));
            Run second = Simulate(DefaultReserves, 25, Schedule((10, Shock(ShockKind.Storm))));

            Assert.That(second.Report.ToRow(), Is.EqualTo(first.Report.ToRow()));
        }

        // ------------------------------------------------------------------- helpers

        /// <summary>
        /// Shock magnitudes sized to hurt without being instantly fatal: about a third of the
        /// stored food, a third of every building's condition, or half the starting reserves.
        /// </summary>
        private static Shock Shock(ShockKind kind) => kind switch
        {
            ShockKind.HarvestFailure => new Shock(kind, 8f),
            ShockKind.Storm => new Shock(kind, 0.30f),
            ShockKind.Theft => new Shock(kind, 100f),
            ShockKind.Desertion => new Shock(kind, 2f),
            _ => new Shock(kind, 1f),
        };

        private static IReadOnlyList<KeyValuePair<int, Shock>> Schedule(
            params (int Day, Shock Shock)[] entries)
        {
            var schedule = new List<KeyValuePair<int, Shock>>();
            foreach ((int day, Shock shock) in entries)
                schedule.Add(new KeyValuePair<int, Shock>(day, shock));

            return schedule;
        }
    }
}
