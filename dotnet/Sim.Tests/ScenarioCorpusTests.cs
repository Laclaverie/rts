using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Scenarios;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// Replays the recorded scenarios and compares digests (ARCHITECTURE §8.2).
    /// </summary>
    /// <remarks>
    /// The Phase 1 gate asserts the <em>shape</em> of the cascade — survived, collapsed. This
    /// asserts the exact arithmetic, so a tuning pass that preserves the shape but moves the
    /// numbers still shows up. Both are wanted: the first says the design still works, the
    /// second says the simulation still computes what it computed yesterday.
    /// </remarks>
    [Category(TestCategories.Functional)]
    public class ScenarioCorpusTests
    {
        private static string Balance(string file) =>
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Balance", file);

        private static string PipelineCsv() => File.ReadAllText(Balance("pipeline.csv"));

        private static BalanceTables Tables()
        {
            var report = new ValidationReport();
            BalanceTables tables = BalanceTables.Load(
                CsvTable.Parse(File.ReadAllText(Balance(BalanceTables.GoodsFile)), BalanceTables.GoodsFile),
                CsvTable.Parse(File.ReadAllText(Balance(BalanceTables.BuildingsFile)), BalanceTables.BuildingsFile),
                CsvTable.Parse(File.ReadAllText(Balance(BalanceTables.CrewRolesFile)), BalanceTables.CrewRolesFile),
                report);

            report.ThrowIfInvalid();
            return tables;
        }

        private static IReadOnlyList<Scenario> Corpus()
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Scenarios",
                ScenarioFile.FileName);

            Assert.That(File.Exists(path), Is.True, path + " was not copied to the test output.");

            var report = new ValidationReport();
            IReadOnlyList<Scenario> scenarios =
                ScenarioFile.Load(CsvTable.Parse(File.ReadAllText(path), ScenarioFile.FileName), report);

            report.ThrowIfInvalid();
            return scenarios;
        }

        [Test]
        public void The_corpus_loads_and_is_not_empty()
        {
            // A corpus that silently loaded nothing would make every other test here vacuous.
            Assert.That(Corpus(), Is.Not.Empty);
        }

        [Test]
        public void Every_scenario_replays_to_its_recorded_digest()
        {
            BalanceTables balance = Tables();
            string pipeline = PipelineCsv();

            var mismatches = new List<string>();
            var unpinned = new List<string>();

            foreach (Scenario scenario in Corpus())
            {
                ScenarioResult result = ScenarioRunner.Run(scenario, balance, pipeline);

                if (!scenario.IsPinned)
                {
                    unpinned.Add($"  {scenario.Id,-32} {result.Digest}   ({result.Condition})");
                    continue;
                }

                if (!result.Matches)
                {
                    mismatches.Add(
                        $"  {scenario.Id,-32} expected {scenario.ExpectedDigest}, got {result.Digest}" +
                        Environment.NewLine + $"      {result.Report.ToRow().Trim()}");
                }
            }

            var message = new StringBuilder();

            if (mismatches.Count > 0)
            {
                message.AppendLine("The simulation changed. That may be intended — if it is, paste these")
                    .AppendLine("digests into scenarios.csv with a note in the commit about why:")
                    .AppendLine()
                    .AppendLine(string.Join(Environment.NewLine, mismatches));
            }

            if (unpinned.Count > 0)
            {
                message.AppendLine()
                    .AppendLine("Scenarios with no recorded digest yet. Paste these into scenarios.csv:")
                    .AppendLine()
                    .AppendLine(string.Join(Environment.NewLine, unpinned));
            }

            Assert.That(mismatches, Is.Empty, message.ToString());
        }

        [Test]
        public void Every_scenario_is_pinned()
        {
            // Separate from the comparison above so that adding a scenario fails loudly here
            // rather than passing quietly there. An unpinned row runs and proves nothing.
            IReadOnlyList<Scenario> unpinned = Corpus().Where(s => !s.IsPinned).ToArray();

            Assert.That(unpinned.Select(s => s.Id), Is.Empty,
                "run `dotnet run --project dotnet/Harness -- --corpus` and paste the digests in");
        }

        [Test]
        public void Replaying_the_same_scenario_twice_gives_the_same_digest()
        {
            // The corpus is only meaningful because replay is deterministic. If this fails,
            // every recorded digest is noise.
            BalanceTables balance = Tables();
            string pipeline = PipelineCsv();
            Scenario scenario = Corpus()[0];

            ScenarioResult first = ScenarioRunner.Run(scenario, balance, pipeline);
            ScenarioResult second = ScenarioRunner.Run(scenario, balance, pipeline);

            Assert.That(second.Digest, Is.EqualTo(first.Digest));
        }

        [Test]
        public void A_different_seed_produces_a_different_run()
        {
            // Otherwise the seed column is decoration and the corpus covers one path.
            BalanceTables balance = Tables();
            string pipeline = PipelineCsv();

            var quiet = new Scenario("probe-a", seed: 1, startingCoin: 150, days: 30,
                Array.Empty<ScheduledShock>(), string.Empty);
            var other = new Scenario("probe-b", seed: 99, startingCoin: 150, days: 30,
                Array.Empty<ScheduledShock>(), string.Empty);

            ScenarioResult a = ScenarioRunner.Run(quiet, balance, pipeline);
            ScenarioResult b = ScenarioRunner.Run(other, balance, pipeline);

            Assert.That(b.Digest, Is.Not.EqualTo(a.Digest));
        }
    }
}
