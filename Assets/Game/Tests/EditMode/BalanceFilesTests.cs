using System.IO;
using NUnit.Framework;
using RTS.Content.Loading;
using RTS.Game.Boot;
using RTS.Sim.Engine.Pipeline;

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

            // Phase 0 implements no systems, so the shipped file must declare none. The day the
            // first system lands, this fails until its row is added.
            Pipeline pipeline = Pipeline.Build(table, new ISystem[0]);

            Assert.That(pipeline.Systems(Phase.Tick), Is.Empty);
            Assert.That(pipeline.Systems(Phase.DayBoundary), Is.Empty);
        }
    }
}
