using System;
using System.Collections.Generic;
using System.Globalization;
using RTS.Content.Loading;
using RTS.Content.Validation;

namespace RTS.Content.Registries
{
    /// <summary>
    /// How the other cities trade among themselves (GDD §5.3, §5.2.2).
    /// </summary>
    /// <remarks>
    /// Key and value, like the other single-mechanism files. Small on purpose: this is a city
    /// deciding what it can spare and who wants it, not a merchant intelligence.
    /// </remarks>
    public sealed class TradeAiRules
    {
        public const string KeyColumn = "key";
        public const string ValueColumn = "value";

        public const string ParcelKey = "parcel";
        public const string SpareKey = "spare";
        public const string ConvoysPerCityKey = "convoys_per_city";

        private TradeAiRules(float parcel, float spare, int convoysPerCity)
        {
            Parcel = parcel;
            Spare = spare;
            ConvoysPerCity = convoysPerCity;
        }

        /// <summary>Units in one shipment.</summary>
        public float Parcel { get; }

        /// <summary>
        /// How far above its own reserve a city must be before it will part with anything.
        /// </summary>
        /// <remarks>
        /// Above the keep, not at it. A city that shipped the moment it had one unit spare would
        /// be trading its own buffer away, and the reserve exists precisely so that a bad harvest
        /// is survivable.
        /// </remarks>
        public float Spare { get; }

        /// <summary>How many convoys one city will have at sea at once.</summary>
        /// <remarks>
        /// The rate limiter, and deliberately the only one. A cooldown in days would be a second
        /// clock to reason about; "finish what you started" is a rule a player can read off the
        /// map by counting ships.
        /// </remarks>
        public int ConvoysPerCity { get; }

        public static TradeAiRules Default { get; } = new TradeAiRules(5f, 6f, 1);

        public static TradeAiRules Load(CsvTable table, ValidationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (table == null) return Default;

            float parcel = Default.Parcel;
            float spare = Default.Spare;
            int convoys = Default.ConvoysPerCity;

            if (!report.RequireColumns(table, KeyColumn, ValueColumn)) return Default;

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (CsvRow row in table.Rows)
            {
                var reader = new RowReader(row, report, table.SourceName);

                string key = reader.Text(KeyColumn, required: true);
                string value = reader.Text(ValueColumn, required: true);
                if (reader.HasProblems) continue;

                if (!seen.Add(key))
                {
                    report.Add(table.SourceName, row.Line,
                        $"'{key}' is set twice. Which one wins would be a coin toss.");
                    continue;
                }

                switch (key)
                {
                    case ParcelKey:
                        parcel = Number(value, table, row, report, parcel);
                        break;

                    case SpareKey:
                        spare = Number(value, table, row, report, spare);
                        break;

                    case ConvoysPerCityKey:
                        convoys = Whole(value, table, row, report, convoys);
                        break;

                    default:
                        report.Add(table.SourceName, row.Line,
                            $"'{key}' is not a trade setting. Known keys: {ParcelKey}, " +
                            $"{SpareKey}, {ConvoysPerCityKey}.");
                        break;
                }
            }

            if (parcel <= 0f)
            {
                report.Add(table.SourceName, 0,
                    $"'{ParcelKey}' is {parcel}. A shipment of nothing is a convoy carrying air.");
                parcel = Default.Parcel;
            }

            if (spare < parcel)
            {
                report.Add(table.SourceName, 0,
                    $"'{SpareKey}' ({spare}) is below '{ParcelKey}' ({parcel}), so a city would " +
                    "ship more than it had decided it could do without.");
            }

            return new TradeAiRules(parcel, spare, convoys);
        }

        private static float Number(string value, CsvTable table, CsvRow row,
            ValidationReport report, float fallback)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float parsed) || parsed < 0f || parsed > 100000f)
            {
                report.Add(table.SourceName, row.Line, $"'{value}' is not a quantity.");
                return fallback;
            }

            return parsed;
        }

        private static int Whole(string value, CsvTable table, CsvRow row,
            ValidationReport report, int fallback)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int parsed) || parsed < 0)
            {
                report.Add(table.SourceName, row.Line,
                    $"'{value}' is not a whole number of zero or more.");
                return fallback;
            }

            return parsed;
        }
    }
}
