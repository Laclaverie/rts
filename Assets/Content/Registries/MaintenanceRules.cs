using System;
using System.Collections.Generic;
using System.Globalization;
using RTS.Content.Loading;
using RTS.Content.Validation;

namespace RTS.Content.Registries
{
    /// <summary>
    /// What it costs to keep a building standing (GDD §5.5).
    /// </summary>
    /// <remarks>
    /// Upkeep was coin alone, and a building repaired itself out of nothing. A city with money
    /// therefore needed nothing else — which is why a port could sit on fourteen hundred coin
    /// with its timber stuck at six and never want a shipment. These numbers are what turned
    /// that into a demand.
    /// </remarks>
    public sealed class MaintenanceRules
    {
        public const string KeyColumn = "key";
        public const string ValueColumn = "value";

        public const string WearPerDayKey = "wear_per_day";
        public const string RepairPerDayKey = "repair_per_day";
        public const string MaterialsPerPointKey = "materials_per_point";

        private MaintenanceRules(float wearPerDay, float repairPerDay, float materialsPerPoint)
        {
            WearPerDay = wearPerDay;
            RepairPerDay = repairPerDay;
            MaterialsPerPoint = materialsPerPoint;
        }

        /// <summary>Condition every working building loses each day.</summary>
        /// <remarks>
        /// A building should drift, not crumble. A mothballed one does not wear at all, which is
        /// most of what shutting one is for.
        /// </remarks>
        public float WearPerDay { get; }

        /// <summary>Condition regained when the upkeep is paid and the materials are there.</summary>
        public float RepairPerDay { get; }

        /// <summary>
        /// Fraction of a building's build cost consumed per full point of condition repaired.
        /// </summary>
        /// <remarks>
        /// Derived from <c>build_timber</c> and <c>build_iron</c>, two more columns that had sat
        /// in <c>buildings.csv</c> since Phase 1 with nothing reading them. A repair is a
        /// fraction of a rebuild, which is why this is well under one.
        /// </remarks>
        public float MaterialsPerPoint { get; }

        public static MaintenanceRules Default { get; } = new MaintenanceRules(0.004f, 0.03f, 0.10f);

        public static MaintenanceRules Load(CsvTable table, ValidationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (table == null) return Default;

            float wear = Default.WearPerDay;
            float repair = Default.RepairPerDay;
            float materials = Default.MaterialsPerPoint;

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
                    case WearPerDayKey:
                        wear = Number(value, table, row, report, wear);
                        break;

                    case RepairPerDayKey:
                        repair = Number(value, table, row, report, repair);
                        break;

                    case MaterialsPerPointKey:
                        materials = Number(value, table, row, report, materials);
                        break;

                    default:
                        report.Add(table.SourceName, row.Line,
                            $"'{key}' is not a maintenance setting. Known keys: {WearPerDayKey}, " +
                            $"{RepairPerDayKey}, {MaterialsPerPointKey}.");
                        break;
                }
            }

            if (repair <= wear && wear > 0f)
            {
                report.Add(table.SourceName, 0,
                    $"'{RepairPerDayKey}' ({repair}) does not exceed '{WearPerDayKey}' ({wear}), " +
                    "so a building that is paid for and supplied still falls apart and nothing " +
                    "the player does can stop it.");
            }

            return new MaintenanceRules(wear, repair, materials);
        }

        private static float Number(string value, CsvTable table, CsvRow row,
            ValidationReport report, float fallback)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float parsed) || parsed < 0f || parsed > 1f)
            {
                report.Add(table.SourceName, row.Line,
                    $"'{value}' is not a fraction between zero and one.");
                return fallback;
            }

            return parsed;
        }
    }
}
