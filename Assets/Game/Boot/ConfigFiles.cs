using RTS.Content.Loading;

namespace RTS.Game.Boot
{
    /// <summary>
    /// Developer settings: <c>StreamingAssets/Config/</c>, currently <c>logging.csv</c>.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="BalanceFiles"/> because the two are edited by different
    /// people for different reasons. Balance is game design and belongs in a save's content
    /// hash (§6.1); config is how the machinery is instrumented and must not affect the sim at
    /// all — a run with logging turned up has to reach the same state as one without.
    /// </remarks>
    public static class ConfigFiles
    {
        /// <summary>Folder name under StreamingAssets.</summary>
        public const string FolderName = "Config";

        public const string LoggingFile = "logging.csv";

        /// <summary>
        /// How fast the world runs. Config rather than balance because it is pacing: nothing in
        /// it reaches the simulation, and a session played at any speed reaches the same state.
        /// </summary>
        public const string ClockFile = "clock.csv";

        public static string Directory => StreamingFiles.DirectoryFor(FolderName);

        public static string PathTo(string fileName) => StreamingFiles.PathTo(FolderName, fileName);

        public static string ReadText(string fileName) => StreamingFiles.ReadText(FolderName, fileName);

        public static CsvTable ReadCsv(string fileName) => StreamingFiles.ReadCsv(FolderName, fileName);
    }
}
