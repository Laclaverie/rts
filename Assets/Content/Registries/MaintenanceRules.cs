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
        public const string CrowdedBeyondKey = "crowded_beyond";
        public const string CrowdingStepKey = "crowding_step";

        private MaintenanceRules(float wearPerDay, float repairPerDay, float materialsPerPoint,
            int crowdedBeyond, float crowdingStep)
        {
            WearPerDay = wearPerDay;
            RepairPerDay = repairPerDay;
            MaterialsPerPoint = materialsPerPoint;
            CrowdedBeyond = crowdedBeyond;
            CrowdingStep = crowdingStep;
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

        /// <summary>How many buildings a port keeps before upkeep starts getting harder.</summary>
        /// <remarks>
        /// Warcraft III's upkeep, and for the same reason: a cost you notice rather than a cost
        /// you fight. Under this many buildings a port pays the plain material cost; past it,
        /// every repair costs more, so sprawl is self-limiting without any rule forbidding it.
        /// <para>
        /// Nothing can be built yet, so today this is a lever with nothing pulling it — the
        /// shipped ports are all under the threshold and pay exactly the base cost. It is here
        /// now because the shape of the cost is a design decision and the number is where a
        /// playtest will want it, not because it does anything this week.
        /// </para>
        /// </remarks>
        public int CrowdedBeyond { get; }

        /// <summary>How much each building past the threshold adds to every repair.</summary>
        /// <remarks>
        /// The answer to "what stops me building a thousand sheds to soak an army". Nothing
        /// forbids it; it simply costs more timber than a thousand sheds are worth.
        /// </remarks>
        public float CrowdingStep { get; }

        /// <summary>
        /// What a port's repairs are multiplied by, given how much it has standing.
        /// </summary>
        public float Crowding(int buildings)
        {
            int over = buildings - CrowdedBeyond;
            return over <= 0 ? 1f : 1f + (over * CrowdingStep);
        }

        public static MaintenanceRules Default { get; } =
            new MaintenanceRules(0.0004f, 0.03f, 0.10f, 10, 0.15f);

        public static MaintenanceRules Load(CsvTable table, ValidationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (table == null) return Default;

            float wear = Default.WearPerDay;
            float repair = Default.RepairPerDay;
            float materials = Default.MaterialsPerPoint;
            int crowdedBeyond = Default.CrowdedBeyond;
            float crowdingStep = Default.CrowdingStep;

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

                    case CrowdedBeyondKey:
                        crowdedBeyond = Whole(value, table, row, report, crowdedBeyond);
                        break;

                    case CrowdingStepKey:
                        crowdingStep = Number(value, table, row, report, crowdingStep);
                        break;

                    default:
                        report.Add(table.SourceName, row.Line,
                            $"'{key}' is not a maintenance setting. Known keys: {WearPerDayKey}, " +
                            $"{RepairPerDayKey}, {MaterialsPerPointKey}, {CrowdedBeyondKey}, " +
                            $"{CrowdingStepKey}.");
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

            return new MaintenanceRules(wear, repair, materials, crowdedBeyond, crowdingStep);
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
