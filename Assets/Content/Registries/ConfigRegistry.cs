using System;
using System.Collections;
using System.Collections.Generic;
using RTS.Content.Loading;
using RTS.Content.Validation;

namespace RTS.Content.Registries
{
    /// <summary>Anything a <see cref="ConfigRegistry{T}"/> can hold: it has a stable id.</summary>
    public interface IHasId
    {
        /// <summary>The `id` column. Referenced from other tables and from save files.</summary>
        string Id { get; }
    }

    /// <summary>
    /// An immutable, insertion-ordered table of typed content, keyed by id
    /// (ARCHITECTURE §5.3, and one of §2.1's genuinely reusable pieces).
    /// </summary>
    /// <remarks>
    /// Insertion order is file order, and iteration is over that order rather than over the
    /// dictionary — §7.1 forbids state-affecting iteration over a dictionary because its order
    /// is not deterministic. The dictionary is a lookup index only.
    /// <para>
    /// Immutable because §5.4 swaps whole registries on hot reload rather than mutating them
    /// in place: a half-updated registry mid-frame is a class of bug worth designing out.
    /// </para>
    /// </remarks>
    public sealed class ConfigRegistry<T> : IReadOnlyList<T> where T : IHasId
    {
        private readonly T[] _items;
        private readonly Dictionary<string, int> _indexById;
        private readonly Dictionary<string, int> _lineById;

        private ConfigRegistry(string sourceName, T[] items, Dictionary<string, int> indexById,
            Dictionary<string, int> lineById)
        {
            SourceName = sourceName;
            _items = items;
            _indexById = indexById;
            _lineById = lineById;
        }

        /// <summary>The file this came from, for error messages.</summary>
        public string SourceName { get; }

        public int Count => _items.Length;

        public T this[int index] => _items[index];

        public T this[string id] =>
            TryGet(id, out T value)
                ? value
                : throw new KeyNotFoundException($"{SourceName} has no entry '{id}'.");

        public bool Contains(string id) => _indexById.ContainsKey(id);

        public bool TryGet(string id, out T value)
        {
            if (id != null && _indexById.TryGetValue(id, out int index))
            {
                value = _items[index];
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>The line an entry came from, or 0. For pointing at the offending row.</summary>
        public int LineOf(string id) => _lineById.TryGetValue(id, out int line) ? line : 0;

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        /// <summary>
        /// Builds a registry from a table, recording every problem rather than throwing on the
        /// first. Rows that reported a problem are dropped, so nothing half-parsed reaches the
        /// sim; the report is what fails the load.
        /// </summary>
        public static ConfigRegistry<T> Load(
            CsvTable table, ValidationReport report, Func<RowReader, T> factory,
            params string[] requiredColumns)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            var items = new List<T>();
            var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
            var lineById = new Dictionary<string, int>(StringComparer.Ordinal);

            // Header first: a missing column would otherwise report once per row and bury
            // everything else.
            if (requiredColumns != null && requiredColumns.Length > 0 &&
                !report.RequireColumns(table, requiredColumns))
            {
                return new ConfigRegistry<T>(table.SourceName, Array.Empty<T>(), indexById, lineById);
            }

            foreach (CsvRow row in table.Rows)
            {
                var reader = new RowReader(row, report, table.SourceName);

                T item;
                try
                {
                    item = factory(reader);
                }
                catch (CsvFormatException e)
                {
                    report.Add(table.SourceName, row.Line, e.Message);
                    continue;
                }

                if (reader.HasProblems) continue;

                string id = item?.Id;
                if (string.IsNullOrWhiteSpace(id))
                {
                    report.Add(table.SourceName, row.Line, "entry has no id.");
                    continue;
                }

                if (indexById.TryGetValue(id, out int firstIndex))
                {
                    report.Add(table.SourceName, row.Line,
                        $"duplicate id '{id}', already defined on line {lineById[id]}.");
                    continue;
                }

                indexById.Add(id, items.Count);
                lineById.Add(id, row.Line);
                items.Add(item);
            }

            return new ConfigRegistry<T>(table.SourceName, items.ToArray(), indexById, lineById);
        }

        public override string ToString() => $"{SourceName} ({Count} entries)";
    }
}
