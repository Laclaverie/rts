using System.IO;
using RTS.Content.Loading;
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

            // Phase 0 implements no systems, so the shipped file must declare none. When the
            // first system lands this test starts failing until its row is added — which is
            // the loud failure of §4.2 arriving one step earlier, in CI rather than at launch.
            Pipeline pipeline = Pipeline.Build(table, new ISystem[0]);

            Assert.That(pipeline.Systems(Phase.Tick), Is.Empty);
            Assert.That(pipeline.Systems(Phase.DayBoundary), Is.Empty);
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
    }
}
