using System;
using System.Collections.Generic;
using System.Text;

namespace RTS.Content.Loading
{
    /// <summary>Raised for any malformed CSV. Always names the source and line.</summary>
    public sealed class CsvFormatException : Exception
    {
        public CsvFormatException(string source, int line, string message)
            : base($"{source}({line}): {message}")
        {
            SourceName = source;
            Line = line;
        }

        public string SourceName { get; }
        public int Line { get; }
    }

    /// <summary>One data row, carrying the line it came from so errors can point at it.</summary>
    public sealed class CsvRow
    {
        private readonly CsvTable _table;
        private readonly string[] _fields;

        internal CsvRow(CsvTable table, string[] fields, int line)
        {
            _table = table;
            _fields = fields;
            Line = line;
        }

        public int Line { get; }

        /// <summary>Whether the table this row came from declares that column.</summary>
        public bool HasColumn(string column) => _table.IndexOf(column) >= 0;

        public string this[string column]
        {
            get
            {
                int index = _table.IndexOf(column);
                if (index < 0)
                    throw new CsvFormatException(_table.SourceName, Line, $"no column '{column}'.");

                return _fields[index];
            }
        }

        public int GetInt(string column)
        {
            string raw = this[column];
            if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int value))
            {
                throw new CsvFormatException(_table.SourceName, Line,
                    $"column '{column}' expected an integer but read '{raw}'.");
            }

            return value;
        }

        /// <summary>Strictly "true" or "false", case-insensitively. Nothing else, so a typo is loud.</summary>
        public bool GetBool(string column)
        {
            string raw = this[column];
            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)) return false;

            throw new CsvFormatException(_table.SourceName, Line,
                $"column '{column}' expected true or false but read '{raw}'.");
        }
    }

    /// <summary>
    /// A hand-rolled CSV reader (ARCHITECTURE §10, decision 2). Blank lines and lines whose
    /// first non-space character is '#' are ignored, so balance files can carry the comments
    /// §4.2 asks designers to leave. Quoted fields are supported, with "" for a literal quote.
    /// </summary>
    public sealed class CsvTable
    {
        private readonly Dictionary<string, int> _columnIndex;

        private CsvTable(string sourceName, string[] columns, List<CsvRow> rows)
        {
            SourceName = sourceName;
            Columns = columns;
            Rows = rows;

            _columnIndex = new Dictionary<string, int>(columns.Length, StringComparer.Ordinal);
            for (int i = 0; i < columns.Length; i++) _columnIndex[columns[i]] = i;
        }

        public string SourceName { get; }
        public IReadOnlyList<string> Columns { get; }
        public IReadOnlyList<CsvRow> Rows { get; }

        internal int IndexOf(string column) =>
            _columnIndex.TryGetValue(column, out int index) ? index : -1;

        public static CsvTable Parse(string text, string sourceName)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (string.IsNullOrEmpty(sourceName)) throw new ArgumentException("Source name required.", nameof(sourceName));

            string[] columns = null;
            var rows = new List<CsvRow>();
            var table = (CsvTable)null;

            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNumber = i + 1;
                string line = lines[i].TrimEnd('\r');

                if (IsIgnorable(line)) continue;

                string[] fields = SplitLine(line, sourceName, lineNumber);

                if (columns == null)
                {
                    columns = fields;
                    var duplicate = FirstDuplicate(columns);
                    if (duplicate != null)
                        throw new CsvFormatException(sourceName, lineNumber, $"duplicate column '{duplicate}'.");

                    table = new CsvTable(sourceName, columns, rows);
                    continue;
                }

                if (fields.Length != columns.Length)
                {
                    throw new CsvFormatException(sourceName, lineNumber,
                        $"expected {columns.Length} fields but read {fields.Length}.");
                }

                rows.Add(new CsvRow(table, fields, lineNumber));
            }

            if (table == null)
                throw new CsvFormatException(sourceName, 1, "no header row.");

            return table;
        }

        private static bool IsIgnorable(string line)
        {
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (char.IsWhiteSpace(c)) continue;
                return c == '#';
            }

            return true;
        }

        private static string FirstDuplicate(string[] values)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
                if (!seen.Add(value)) return value;

            return null;
        }

        private static string[] SplitLine(string line, string sourceName, int lineNumber)
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (quoted)
                {
                    if (c != '"') { field.Append(c); continue; }

                    bool escapedQuote = i + 1 < line.Length && line[i + 1] == '"';
                    if (escapedQuote) { field.Append('"'); i++; }
                    else quoted = false;

                    continue;
                }

                if (c == '"')
                {
                    if (field.ToString().Trim().Length > 0)
                        throw new CsvFormatException(sourceName, lineNumber, "unexpected quote mid-field.");

                    field.Clear();
                    quoted = true;
                    continue;
                }

                if (c == ',') { fields.Add(field.ToString().Trim()); field.Clear(); continue; }

                field.Append(c);
            }

            if (quoted) throw new CsvFormatException(sourceName, lineNumber, "unterminated quote.");

            fields.Add(field.ToString().Trim());
            return fields.ToArray();
        }
    }
}
