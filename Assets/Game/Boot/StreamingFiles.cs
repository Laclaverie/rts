using System.IO;
using RTS.Content.Loading;
using UnityEngine;

namespace RTS.Game.Boot
{
    /// <summary>
    /// Reads files from a folder under StreamingAssets and hands them to the loaders as text.
    /// </summary>
    /// <remarks>
    /// The one place allowed to know both Unity and the on-disk layout. <c>Content</c> may not
    /// (C5, §2): with no <c>UnityEngine</c> reference it cannot read a <c>TextAsset</c> or ask
    /// for a path. StreamingAssets is the only Unity location that survives into a build as
    /// ordinary files, so <c>System.IO</c> behaves the same in the editor, a player and the
    /// headless test runner — one code path, no <c>#if UNITY_EDITOR</c> (ARCHITECTURE §5.2).
    /// <para>
    /// Desktop only. On Android StreamingAssets lives inside the APK and on WebGL it is a URL;
    /// neither answers to <c>File.ReadAllText</c>. Known limit, not an oversight.
    /// </para>
    /// </remarks>
    public static class StreamingFiles
    {
        public static string DirectoryFor(string folder) =>
            Path.Combine(Application.streamingAssetsPath, folder);

        public static string PathTo(string folder, string fileName) =>
            Path.Combine(DirectoryFor(folder), fileName);

        /// <summary>
        /// Reads a file. A missing one is a startup failure naming the path it tried — the
        /// alternative is a default-valued sim that looks like it works (§5.3).
        /// </summary>
        public static string ReadText(string folder, string fileName)
        {
            string path = PathTo(folder, fileName);

            if (!File.Exists(path))
                throw new FileNotFoundException($"File '{fileName}' not found at '{path}'.", path);

            return File.ReadAllText(path);
        }

        /// <summary>Reads and parses a table, naming the file in any parse error.</summary>
        public static CsvTable ReadCsv(string folder, string fileName) =>
            CsvTable.Parse(ReadText(folder, fileName), fileName);
    }
}
