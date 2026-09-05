using System.IO;
using RTS.Sim.Scenarios;
using RTS.Sim.Systems;
using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Engine.Diagnostics;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// Parses the files that actually ship, not fixtures. A balance file is edited by hand
    /// and a typo in one is a launch failure, so it is worth catching in CI instead
    /// (ARCHITECTURE §5.3, §8.3).
    /// </summary>
    [Category(TestCategories.Functional)]
    public class ShippedBalanceFilesTests
    {
        private static string PathTo(string file) =>
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Balance", file);

        private static CsvTable Table(string file) =>
            CsvTable.Parse(File.ReadAllText(PathTo(file)), file);

        private static string ConfigPathTo(string file) =>
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Config", file);

        [Test]
        public void Pipeline_csv_is_present_and_parses()
        {
            string path = PathTo("pipeline.csv");
            Assert.That(File.Exists(path), Is.True, path + " was not copied to the test output.");

            CsvTable table = CsvTable.Parse(File.ReadAllText(path), "pipeline.csv");

            Assert.That(table.Columns, Is.EqualTo(new[]
            {
                Pipeline.PhaseColumn, Pipeline.OrderColumn, Pipeline.SystemColumn, Pipeline.EnabledColumn,
            }), "pipeline.csv header must match the columns the loader reads.");
        }

        [Test]
        public void Pipeline_csv_binds_against_the_systems_that_exist()
        {
            CsvTable table = CsvTable.Parse(File.ReadAllText(PathTo("pipeline.csv")), "pipeline.csv");

            // Every system that exists must be declared, and everything declared must exist.
            // Build throws listing both kinds of mismatch, so this test is the §4.2 loud failure
            // arriving in CI rather than at launch — it caught the Phase 1 systems the moment
            // they were written and before their rows were added.
            Pipeline pipeline = Pipeline.Build(table, ScenarioRunner.AllSystems());

            Assert.That(pipeline.Systems(Phase.Tick), Is.Empty, "nothing runs per-tick yet");

            // The order is design, not incidental: convoys land before anything eats, so bread
            // that arrives this morning is edible this morning; crew eat yesterday's stock
            // before today's output lands; wages are paid before buildings are maintained
            // (§5.2.3).
            Assert.That(pipeline.Systems(Phase.DayBoundary).Select(s => s.Id),
                Is.EqualTo(new[]
                {
                    ConvoySystem.SystemId,
                    ConsumptionSystem.SystemId,
                    WagesSystem.SystemId,
                    UpkeepSystem.SystemId,
                    DesertionSystem.SystemId,
                    LabourSystem.SystemId,
                    ProductionSystem.SystemId,
                    MarketSystem.SystemId,
                    UnrestSystem.SystemId,
                    RevolutionLadderSystem.SystemId,

                    // The crowd is a reading of the rung, so it is told after the ladder has
                    // decided. Running it first would put yesterday's rung on today's street.
                    MobSystem.SystemId,
                }));
        }

        [Test]
        public void Logging_csv_is_present_and_parses()
        {
            string path = ConfigPathTo("logging.csv");
            Assert.That(File.Exists(path), Is.True, path + " was not copied to the test output.");

            var report = new ValidationReport();
            LogSettings settings = LogSettings.Load(
                CsvTable.Parse(File.ReadAllText(path), "logging.csv"), report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));
            Assert.That(settings.Levels.Count, Is.GreaterThan(0),
                "the shipped file should configure at least one channel");
        }

        [Test]
        public void The_shipped_balance_tables_load_and_cross_check_clean()
        {
            // The rules of §5.3 applied to the real files: every Local good produced by
            // something, everything consumed by something, every `produces` resolving.
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

            Assert.That(report.IsValid, Is.True,
                "shipped balance content is invalid:" + System.Environment.NewLine +
                string.Join(System.Environment.NewLine, report.Problems));

            Assert.That(tables.Goods.Count, Is.GreaterThan(0));
            Assert.That(tables.Buildings.Count, Is.GreaterThan(0));
            Assert.That(tables.CrewRoles.Count, Is.GreaterThan(0));
        }
    }
}
