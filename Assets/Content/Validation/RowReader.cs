using System;
using System.Collections.Generic;
using RTS.Content.Loading;

namespace RTS.Content.Validation
{
    /// <summary>
    /// Reads one row's typed values, recording problems instead of throwing
    /// (ARCHITECTURE §5.3).
    /// </summary>
    /// <remarks>
    /// <see cref="CsvRow"/> throws on a bad value, which is right for a caller that knows the
    /// data is good. A loader does not: it wants every problem in the file, so this catches
    /// and records, returns a harmless default, and lets the row finish being read. The
    /// registry then discards rows that reported anything, so no half-parsed entry reaches
    /// the sim.
    /// <para>
    /// Range checks live here rather than in the CSV reader because "no negative upkeep" is a
    /// content rule, not a parsing rule.
    /// </para>
    /// </remarks>
    public sealed class RowReader
    {
        private readonly CsvRow _row;
        private readonly ValidationReport _report;
        private readonly string _source;

        public RowReader(CsvRow row, ValidationReport report, string source)
        {
            _row = row ?? throw new ArgumentNullException(nameof(row));
            _report = report ?? throw new ArgumentNullException(nameof(report));
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public int Line => _row.Line;

        /// <summary>Whether this row has reported any problem. A dirty row is not kept.</summary>
        public bool HasProblems { get; private set; }

        /// <summary>Records a problem against this row, from a caller's own rule.</summary>
        public void Problem(string message)
        {
            _report.Add(_source, _row.Line, message);
            HasProblems = true;
        }

        /// <summary>A non-empty identifier.</summary>
        public string Id(string column = "id")
        {
            string value = Text(column);
            if (!HasProblems && string.IsNullOrWhiteSpace(value))
            {
                Problem($"column '{column}' is empty; every row needs an id.");
                return string.Empty;
            }

            return value;
        }

        /// <summary>Raw text. Empty is allowed unless <paramref name="required"/>.</summary>
        public string Text(string column, bool required = false)
        {
            try
            {
                string value = _row[column];
                if (required && string.IsNullOrWhiteSpace(value))
                    Problem($"column '{column}' is required and empty.");

                return value;
            }
            catch (CsvFormatException e)
            {
                Problem(e.Message);
                return string.Empty;
            }
        }

        /// <summary>An integer, optionally range-checked. Both bounds inclusive.</summary>
        public int Int(string column, int min = int.MinValue, int max = int.MaxValue)
        {
            try
            {
                int value = _row.GetInt(column);
                if (value < min || value > max)
                {
                    Problem($"column '{column}' is {value}, outside the allowed range {min}..{max}.");
                    return min;
                }

                return value;
            }
            catch (CsvFormatException e)
            {
                Problem(e.Message);
                return min == int.MinValue ? 0 : min;
            }
        }

        /// <summary>A float, optionally range-checked. Both bounds inclusive.</summary>
        public float Float(string column, float min = float.NegativeInfinity, float max = float.PositiveInfinity)
        {
            string raw = Text(column);
            if (HasProblems) return 0f;

            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float value))
            {
                Problem($"column '{column}' expected a number but read '{raw}'.");
                return 0f;
            }

            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                Problem($"column '{column}' is {raw}, which is not a finite number.");
                return 0f;
            }

            if (value < min || value > max)
            {
                Problem($"column '{column}' is {value}, outside the allowed range {min}..{max}.");
                return min;
            }

            return value;
        }

        public bool Bool(string column)
        {
            try
            {
                return _row.GetBool(column);
            }
            catch (CsvFormatException e)
            {
                Problem(e.Message);
                return false;
            }
        }

        /// <summary>
        /// One of a fixed set of names, compared exactly. Not <c>Enum.TryParse</c>, which
        /// accepts numeric strings and comma-separated lists and would let a typo resolve to
        /// something nobody chose.
        /// </summary>
        public T Enum<T>(string column) where T : struct
        {
            string raw = Text(column);
            if (HasProblems) return default;

            foreach (T candidate in (T[])System.Enum.GetValues(typeof(T)))
            {
                if (string.Equals(candidate.ToString(), raw, StringComparison.Ordinal))
                    return candidate;
            }

            Problem($"column '{column}' is '{raw}'. Expected one of " +
                    $"{string.Join(", ", System.Enum.GetNames(typeof(T)))}.");
            return default;
        }

        /// <summary>
        /// An identifier that must exist in another table. Recorded for a second pass, because
        /// the table it points at may not be loaded yet.
        /// </summary>
        public string Reference(string column, ICollection<PendingReference> pending, string targetTable)
        {
            string value = Text(column, required: true);
            if (HasProblems) return string.Empty;

            pending.Add(new PendingReference(_source, _row.Line, column, value, targetTable));
            return value;
        }
    }

    /// <summary>A cross-table reference to resolve once every table is loaded (§5.3).</summary>
    public readonly struct PendingReference
    {
        public PendingReference(string source, int line, string column, string value, string targetTable)
        {
            Source = source;
            Line = line;
            Column = column;
            Value = value;
            TargetTable = targetTable;
        }

        public readonly string Source;
        public readonly int Line;
        public readonly string Column;
        public readonly string Value;
        public readonly string TargetTable;
    }
}
