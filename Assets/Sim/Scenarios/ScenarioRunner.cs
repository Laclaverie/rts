using System;
using System.Collections.Generic;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Engine.State;
using RTS.Sim.Systems;

namespace RTS.Sim.Scenarios
{
    /// <summary>What a scenario produced.</summary>
    public readonly struct ScenarioResult
    {
        public ScenarioResult(Scenario scenario, string digest, PortCondition condition, PortReport report)
        {
            Scenario = scenario;
            Digest = digest;
            Condition = condition;
            Report = report;
        }

        public readonly Scenario Scenario;

        /// <summary>The full replay digest: world, generator position, command log and events.</summary>
        public readonly string Digest;

        public readonly PortCondition Condition;
        public readonly PortReport Report;

        public bool Matches => !Scenario.IsPinned ||
                               string.Equals(Digest, Scenario.ExpectedDigest, StringComparison.Ordinal);

        public override string ToString() =>
            $"{Scenario.Id}: {Digest} {Condition} — {Report.ToRow().Trim()}";
    }

    /// <summary>
    /// Runs a <see cref="Scenario"/> against the shipped content and pipeline.
    /// </summary>
    /// <remarks>
    /// It builds nothing of its own: the pipeline comes from <c>pipeline.csv</c> and the port
    /// from <see cref="PortScenario"/>, so a scenario exercises the game rather than a
    /// re-implementation of it. That is the whole value of the corpus — a run that passed
    /// against a private copy of the rules would prove nothing about the game.
    /// </remarks>
    public static class ScenarioRunner
    {
        public static ScenarioResult Run(Scenario scenario, BalanceTables balance, string pipelineCsv)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            if (pipelineCsv == null) throw new ArgumentNullException(nameof(pipelineCsv));

            PortScenario port = PortScenario.Default();
            port.StartingCoin = scenario.StartingCoin;

            ReplayRun run = ReplayRun.Start(
                scenario.Seed,
                new ICommandHandler[] { new ShockHandler() },
                dispatcher => BuildPipeline(pipelineCsv, dispatcher),
                port.Build(balance),
                balance);

            for (int day = 1; day <= scenario.Days; day++)
            {
                for (int i = 0; i < scenario.Shocks.Count; i++)
                {
                    ScheduledShock shock = scenario.Shocks[i];
                    if (shock.Day == day) run.Submit(new Shock(shock.Kind, shock.Magnitude));
                }

                run.AdvanceDay();

                // Drained each day, as a game loop would. Left pending, they would accumulate
                // into the digest and the scenario would measure the queue rather than the run.
                run.Events.Drain();
            }

            return new ScenarioResult(scenario, run.Digest(),
                PortHealth.Of(run.World, balance),
                PortReport.Of(run.World, balance, scenario.Days));
        }

        /// <summary>
        /// The shipped pipeline plus the command drain, which is not in the file yet because
        /// nothing in the game constructs a dispatcher.
        /// </summary>
        public static Pipeline BuildPipeline(string shippedCsv, CommandDispatcher dispatcher)
        {
            var systems = new List<ISystem>
            {
                new ConsumptionSystem(), new WagesSystem(), new UpkeepSystem(),
                new DesertionSystem(), new ProductionSystem(), new MarketSystem(),
                new CommandDrainSystem(dispatcher),
            };

            // Order 5: input takes effect before anything else that day, so a shock scheduled
            // for day N is felt on day N rather than the morning after. When the game gains a
            // dispatcher of its own, this row moves into pipeline.csv and its position becomes
            // a design decision like every other (§4.2).
            string csv = shippedCsv.TrimEnd() + "\n" +
                         $"DayBoundary,5,{CommandDrainSystem.SystemId},true\n";

            return Pipeline.Build(CsvTable.Parse(csv, "pipeline.csv"), systems);
        }
    }
}
