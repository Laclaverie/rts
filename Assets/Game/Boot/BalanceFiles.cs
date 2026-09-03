using System.IO;
using RTS.Content.Loading;
using UnityEngine;

namespace RTS.Game.Boot
{
    /// <summary>
    /// Resolves the balance directory and hands its files to the loaders as plain text.
    /// </summary>
    /// <remarks>
    /// This is the one place allowed to know both Unity and the balance layout, and it exists
    /// because <c>Content</c> may not (C5, §2): with no <c>UnityEngine</c> reference it cannot
    /// read a <c>TextAsset</c> or ask for a path. StreamingAssets is the only Unity location
    /// that survives into a build as ordinary files on disk, so <c>System.IO</c> behaves the
    /// same in the editor, in a player, and in the headless test runner — one code path, no
    /// <c>#if UNITY_EDITOR</c> (ARCHITECTURE §5.2).
    /// <para>
    /// Desktop only. On Android StreamingAssets lives inside the APK and on WebGL it is a URL;
    /// neither answers to <c>File.ReadAllText</c>. That is a known limit, not an oversight —
    /// the GDD targets desktop and Windows standalone is the only build target installed.
    /// </para>
    /// </remarks>
    public static class BalanceFiles
    {
        /// <summary>Folder name under StreamingAssets. Matches `Balance/` in ARCHITECTURE §5.2.</summary>
        public const string FolderName = "Balance";

        public static string Directory => Path.Combine(Application.streamingAssetsPath, FolderName);

        public static string PathTo(string fileName) => Path.Combine(Directory, fileName);

        /// <summary>
        /// Reads a balance file. A missing file is a startup failure and says where it looked —
        /// the alternative is a default-valued sim that looks like it works (§5.3).
        /// </summary>
        public static string ReadText(string fileName)
        {
            string path = PathTo(fileName);

            if (!File.Exists(path))
                throw new FileNotFoundException($"Balance file '{fileName}' not found at '{path}'.", path);

            return File.ReadAllText(path);
        }

        /// <summary>Reads and parses a balance table, naming the file in any parse error.</summary>
        public static CsvTable ReadCsv(string fileName) =>
            CsvTable.Parse(ReadText(fileName), fileName);
    }
}
