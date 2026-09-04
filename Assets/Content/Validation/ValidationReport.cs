using System;
using System.Collections.Generic;
using System.Linq;
using RTS.Content.Loading;

namespace RTS.Content.Validation
{
    /// <summary>
    /// Collects everything wrong with the shipped content, then fails once
    /// (ARCHITECTURE §5.3).
    /// </summary>
    /// <remarks>
    /// Accumulating rather than throwing on the first problem is the whole point: a designer
    /// who mistyped four columns should learn that in one run, not in four. Same discipline as
    /// the pipeline loader (§4.2).
    /// <para>
    /// Every problem names a source and a line, because "invalid value" without a location is
    /// barely better than silence.
    /// </para>
    /// </remarks>
    public sealed class ValidationReport
    {
        private readonly List<string> _problems = new List<string>();

        public IReadOnlyList<string> Problems => _problems;

        public bool IsValid => _problems.Count == 0;

        public int Count => _problems.Count;

        /// <summary>Records a problem at a known location.</summary>
        public void Add(string source, int line, string message) =>
            _problems.Add($"{source}({line}): {message}");

        /// <summary>Records a problem that belongs to a file rather than to one row.</summary>
        public void Add(string source, string message) =>
            _problems.Add($"{source}: {message}");

        /// <summary>
        /// Checks the header before any row is read. A missing column would otherwise produce
        /// one identical problem per row, burying everything else.
        /// </summary>
        public bool RequireColumns(CsvTable table, params string[] columns)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            string[] missing = columns
                .Where(c => !table.Columns.Contains(c, StringComparer.Ordinal))
                .ToArray();

            if (missing.Length == 0) return true;

            Add(table.SourceName, 1,
                $"missing column{(missing.Length > 1 ? "s" : "")} {string.Join(", ", missing.Select(m => "'" + m + "'"))}. " +
                $"Found: {string.Join(", ", table.Columns)}.");

            return false;
        }

        /// <summary>Throws with every problem found, or returns quietly.</summary>
        public void ThrowIfInvalid()
        {
            if (IsValid) return;

            throw new ContentValidationException(_problems.ToArray());
        }

        public override string ToString() =>
            IsValid ? "content valid" : $"{_problems.Count} content problem(s)";
    }
}
