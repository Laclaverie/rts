using System.IO;
using NUnit.Framework;
using RTS.Content.Loading;
using RTS.Game.Boot;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Scenarios;

namespace RTS.Game.Tests
{
    /// <summary>
    /// The half the headless suite cannot reach. `dotnet test` proves the parser and the
    /// binder given a string; only Unity can answer whether the StreamingAssets path resolves
    /// and whether the file is actually where the loader looks.
    /// </summary>
    /// <remarks>
    /// These run in the editor, where streamingAssetsPath is Assets/StreamingAssets. They do
    /// not prove the build copy — that needs a real player build, and there is no game to
    /// build yet. Known gap, deliberately left open.
    /// </remarks>
    [Category("Functional")]
    public class BalanceFilesTests
    {
        private const string PipelineCsv = "pipeline.csv";

        [Test]
        public void Balance_directory_resolves_under_streaming_assets()
        {
            string directory = BalanceFiles.Directory;

            Assert.That(Directory.Exists(directory), Is.True, directory + " does not exist.");
            Assert.That(directory.Replace('\\', '/'),
                Does.EndWith("StreamingAssets/" + BalanceFiles.FolderName));
        }

        [Test]
        public void Pipeline_csv_is_readable_through_the_wrapper()
        {
            string text = BalanceFiles.ReadText(PipelineCsv);

            Assert.That(text, Is.Not.Empty);
        }

        [Test]
        public void A_missing_balance_file_fails_loudly_and_says_where_it_looked()
        {
            var e = Assert.Throws<FileNotFoundException>(
                () => BalanceFiles.ReadText("no-such-file.csv"));

            Assert.That(e.Message, Does.Contain("no-such-file.csv"));
            Assert.That(e.Message, Does.Contain(BalanceFiles.Directory));
        }

        [Test]
        public void Pipeline_csv_parses_and_binds_inside_unity()
        {
            CsvTable table = BalanceFiles.ReadCsv(PipelineCsv);

            Assert.That(table.Columns, Is.EqualTo(new[]
            {
                Pipeline.PhaseColumn, Pipeline.OrderColumn, Pipeline.SystemColumn, Pipeline.EnabledColumn,
            }));

            // Bound against the real system list, which is the only thing this can usefully
            // assert from inside Unity: that the file the editor resolves is the file the game
            // runs. What the order should be is asserted headlessly, where it belongs.
            //
            // This spent three phases asserting that the pipeline declared no systems at all —
            // true in Phase 0, false from Phase 1 — and stayed red without anyone noticing,
            // because EditMode tests only run when the editor happens to be open. Worth
            // remembering before putting anything load-bearing on this side of the line.
            Pipeline pipeline = Pipeline.Build(table, ScenarioRunner.AllSystems());

            Assert.That(pipeline.Systems(Phase.Tick), Is.Empty, "nothing runs per-tick yet");
            Assert.That(pipeline.Systems(Phase.DayBoundary), Is.Not.Empty);
        }
    }
}
