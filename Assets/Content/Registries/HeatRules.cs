using System;
using System.Collections.Generic;
using System.Globalization;
using RTS.Content.Loading;
using RTS.Content.Validation;

namespace RTS.Content.Registries
{
    /// <summary>
    /// What draws attention, and what attention costs (GDD §5.2.1).
    /// </summary>
    /// <remarks>
    /// Key and value, like <c>clock.csv</c> and <c>mob.csv</c>: settings for one mechanism
    /// rather than a list of things. Every number here is one side of the dilemma §5.2 calls the
    /// game, so every one of them is in a file where a playtest can move it.
    /// </remarks>
    public sealed class HeatRules
    {
        public const string KeyColumn = "key";
        public const string ValueColumn = "value";

        public const string HeldWeightKey = "held_weight";
        public const string AtSeaWeightKey = "at_sea_weight";
        public const string DecayPerDayKey = "decay_per_day";
        public const string RaidChanceAtFullKey = "raid_chance_at_full";
        public const string RaidTakesKey = "raid_takes";
        public const string EscortCoinPerDayKey = "escort_coin_per_day";
        public const string EscortRaidMultiplierKey = "escort_raid_multiplier";

        private HeatRules(float heldWeight, float atSeaWeight, float decayPerDay,
            float raidChanceAtFull, float raidTakes, int escortCoinPerDay,
            float escortRaidMultiplier)
        {
            HeldWeight = heldWeight;
            AtSeaWeight = atSeaWeight;
            DecayPerDay = decayPerDay;
            RaidChanceAtFull = raidChanceAtFull;
            RaidTakes = raidTakes;
            EscortCoinPerDay = escortCoinPerDay;
            EscortRaidMultiplier = escortRaidMultiplier;
        }

        /// <summary>Attention per unit of <c>heat_per_unit</c> sitting in a warehouse.</summary>
        public float HeldWeight { get; }

        /// <summary>
        /// Attention per unit of <c>heat_per_unit</c> on the water.
        /// </summary>
        /// <remarks>
        /// Higher than what is held, because §5.2.1 lists the fat convoy first among the things
        /// that are visible. It is also what makes "split cargo across routes" a real counter
        /// rather than flavour text: the same goods drawn out over several smaller shipments
        /// still draw the same total, but no single one of them is worth the trip.
        /// </remarks>
        public float AtSeaWeight { get; }

        /// <summary>How much attention fades in a day with nothing on show.</summary>
        public float DecayPerDay { get; }

        /// <summary>The chance a given convoy is raided on a given day at Heat 1.0.</summary>
        /// <remarks>
        /// Per convoy per day, so a long crossing is more dangerous than a short one without
        /// anything having to say so. Ironhold being five days out is already a decision about
        /// price against time; this makes it a decision about risk as well.
        /// </remarks>
        public float RaidChanceAtFull { get; }

        /// <summary>The share of a convoy's cargo a raid takes.</summary>
        /// <remarks>
        /// Less than all of it. A raid that emptied the hold would make a single bad roll the end
        /// of a route rather than a cost of running one, and §5.2.3's rule is that a single shock
        /// is always survivable.
        /// </remarks>
        public float RaidTakes { get; }

        /// <summary>
        /// Coin per day, per convoy at sea, while escorts are standing.
        /// </summary>
        /// <remarks>
        /// This is the whole dilemma in one number. Coin spent on escorts is coin that is not in
        /// the treasury on payday, an unpaid wage feeds grievance the same day (§5.2.3), and
        /// grievance is the ladder. §5.2's table says reducing Heat raises Unrest; this is the
        /// mechanism, and it needs no tax system to work because coin was already the thing both
        /// pressures compete for.
        /// </remarks>
        public int EscortCoinPerDay { get; }

        /// <summary>What escorting multiplies a convoy's raid chance by.</summary>
        /// <remarks>
        /// This and <see cref="EscortCoinPerDay"/> have to be priced against each other or the
        /// posture has one right answer and stops being a decision. The first pair tried cost
        /// about fifteen coin to guard a crossing that was expected to lose about four, so a
        /// player who did the arithmetic would never have escorted anything.
        /// </remarks>
        public float EscortRaidMultiplier { get; }

        public static HeatRules Default { get; } =
            new HeatRules(0.5f, 1.5f, 0.15f, 0.30f, 0.5f, 2, 0.25f);

        public static HeatRules Load(CsvTable table, ValidationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (table == null) return Default;

            float held = Default.HeldWeight;
            float atSea = Default.AtSeaWeight;
            float decay = Default.DecayPerDay;
            float chance = Default.RaidChanceAtFull;
            float takes = Default.RaidTakes;
            int escortCoin = Default.EscortCoinPerDay;
            float escortMultiplier = Default.EscortRaidMultiplier;

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
                    case HeldWeightKey:
                        held = Number(value, table, row, report, held, 0f, 100f);
                        break;

                    case AtSeaWeightKey:
                        atSea = Number(value, table, row, report, atSea, 0f, 100f);
                        break;

                    case DecayPerDayKey:
                        decay = Number(value, table, row, report, decay, 0f, 1f);
                        break;

                    case RaidChanceAtFullKey:
                        chance = Number(value, table, row, report, chance, 0f, 1f);
                        break;

                    case RaidTakesKey:
                        takes = Number(value, table, row, report, takes, 0f, 1f);
                        break;

                    case EscortCoinPerDayKey:
                        escortCoin = Whole(value, table, row, report, escortCoin);
                        break;

                    case EscortRaidMultiplierKey:
                        escortMultiplier = Number(value, table, row, report, escortMultiplier, 0f, 1f);
                        break;

                    default:
                        report.Add(table.SourceName, row.Line,
                            $"'{key}' is not a heat setting. Known keys: {HeldWeightKey}, " +
                            $"{AtSeaWeightKey}, {DecayPerDayKey}, {RaidChanceAtFullKey}, " +
                            $"{RaidTakesKey}, {EscortCoinPerDayKey}, {EscortRaidMultiplierKey}.");
                        break;
                }
            }

            if (escortMultiplier >= 1f)
            {
                report.Add(table.SourceName, 0,
                    $"'{EscortRaidMultiplierKey}' is {escortMultiplier}, so an escort costs coin " +
                    "and buys nothing. §5.2.1 requires Heat to be counterable.");
            }

            if (atSea < held)
            {
                report.Add(table.SourceName, 0,
                    $"'{AtSeaWeightKey}' ({atSea}) is below '{HeldWeightKey}' ({held}), which " +
                    "makes shipping goods safer than sitting on them and removes the reason to " +
                    "escort anything.");
            }

            return new HeatRules(held, atSea, decay, chance, takes, escortCoin, escortMultiplier);
        }

        private static float Number(string value, CsvTable table, CsvRow row,
            ValidationReport report, float fallback, float min, float max)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float parsed) || parsed < min || parsed > max)
            {
                report.Add(table.SourceName, row.Line,
                    $"'{value}' is not a number between {min} and {max}.");
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
