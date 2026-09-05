using System;
using System.Collections.Generic;
using System.Globalization;
using RTS.Content.Loading;
using RTS.Content.Validation;

namespace RTS.Content.Registries
{
    /// <summary>
    /// What a mob is made of and how it moves (GDD §5.2.2 rung 5, §6.4).
    /// </summary>
    /// <remarks>
    /// Key and value rather than a row per thing, like <c>clock.csv</c>, because these are
    /// settings for one mechanism rather than a list of entities. Same loud failure either way:
    /// an unknown key is a mistake in a file, and a setting that silently keeps its default is
    /// worse than one that refuses to load.
    /// <para>
    /// In content because §8.1 says to start at dozens and measure before optimising. That is
    /// only a real instruction if the number is a number somebody can change without a rebuild.
    /// </para>
    /// </remarks>
    public sealed class MobRules
    {
        public const string KeyColumn = "key";
        public const string ValueColumn = "value";

        public const string BodiesPerCommonerKey = "bodies_per_commoner";
        public const string MaximumBodiesKey = "max_bodies";
        public const string StepsPerDayKey = "steps_per_day";
        public const string SpeedKey = "speed";
        public const string MusterRadiusKey = "muster_radius";
        public const string PressRadiusKey = "press_radius";
        public const string LoyaltyToStandKey = "loyalty_to_stand";

        private MobRules(float bodiesPerCommoner, int maximumBodies, int stepsPerDay, float speed,
            float musterRadius, float pressRadius, float loyaltyToStand)
        {
            BodiesPerCommoner = bodiesPerCommoner;
            MaximumBodies = maximumBodies;
            StepsPerDay = stepsPerDay;
            Speed = speed;
            MusterRadius = musterRadius;
            PressRadius = pressRadius;
            LoyaltyToStand = loyaltyToStand;
        }

        /// <summary>How many bodies each commoner in the port puts on the street.</summary>
        public float BodiesPerCommoner { get; }

        /// <summary>
        /// The ceiling, whatever the population.
        /// </summary>
        /// <remarks>
        /// §8.1 says dozens first and hundreds only once the small version is proven. The cap is
        /// here so raising it is an edit to a file and a measurement, rather than a rewrite.
        /// </remarks>
        public int MaximumBodies { get; }

        /// <summary>
        /// How many movement steps happen in a day.
        /// </summary>
        /// <remarks>
        /// The mob moves in whole days like everything else in the sim, in this many equal
        /// sub-steps, and the renderer interpolates between yesterday's position and today's.
        /// Stepping on real frames instead would put frame time into the world and break replay
        /// (§7.1) — a revolt would come out differently depending on how long the player watched
        /// it, which is exactly what the determinism corpus exists to prevent.
        /// </remarks>
        public int StepsPerDay { get; }

        /// <summary>
        /// Distance covered in one step, in the units <c>ports.csv</c> is written in.
        /// </summary>
        /// <remarks>
        /// Slow enough that crossing from <see cref="MusterRadius"/> to
        /// <see cref="PressRadius"/> takes about three days. The first value tried covered it
        /// inside a single day, so the crowd was at the longhouse the morning it turned out and
        /// the approach — the part that reads as an event — never happened on screen.
        /// </remarks>
        public float Speed { get; }

        /// <summary>How far out the crowd first gathers.</summary>
        public float MusterRadius { get; }

        /// <summary>How close to the seat of power the crowd presses before it stops.</summary>
        public float PressRadius { get; }

        /// <summary>
        /// The loyalty at which a named crew member stands with you rather than against you.
        /// </summary>
        /// <remarks>
        /// §5.2.2 says named crew choose sides <em>individually</em>, and §5.4 keeps loyalty
        /// separate from morale so that they can. A hungry crew member who trusts you stands;
        /// a comfortable one who does not, does not.
        /// </remarks>
        public float LoyaltyToStand { get; }

        /// <summary>The defaults, for a world whose content says nothing about mobs.</summary>
        public static MobRules Default { get; } =
            new MobRules(1f, 60, 12, 0.07f, 2.6f, 0.55f, 0.5f);

        public static MobRules Load(CsvTable table, ValidationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (table == null) return Default;

            float bodiesPerCommoner = Default.BodiesPerCommoner;
            int maximumBodies = Default.MaximumBodies;
            int stepsPerDay = Default.StepsPerDay;
            float speed = Default.Speed;
            float musterRadius = Default.MusterRadius;
            float pressRadius = Default.PressRadius;
            float loyaltyToStand = Default.LoyaltyToStand;

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
                    case BodiesPerCommonerKey:
                        bodiesPerCommoner = Number(value, table, row, report, bodiesPerCommoner, 0f, 100f);
                        break;

                    case MaximumBodiesKey:
                        maximumBodies = Whole(value, table, row, report, maximumBodies);
                        break;

                    case StepsPerDayKey:
                        stepsPerDay = Whole(value, table, row, report, stepsPerDay);
                        break;

                    case SpeedKey:
                        speed = Number(value, table, row, report, speed, 0f, 1000f);
                        break;

                    case MusterRadiusKey:
                        musterRadius = Number(value, table, row, report, musterRadius, 0f, 1000f);
                        break;

                    case PressRadiusKey:
                        pressRadius = Number(value, table, row, report, pressRadius, 0f, 1000f);
                        break;

                    case LoyaltyToStandKey:
                        loyaltyToStand = Number(value, table, row, report, loyaltyToStand, 0f, 1f);
                        break;

                    default:
                        report.Add(table.SourceName, row.Line,
                            $"'{key}' is not a mob setting. Known keys: {BodiesPerCommonerKey}, " +
                            $"{MaximumBodiesKey}, {StepsPerDayKey}, {SpeedKey}, {MusterRadiusKey}, " +
                            $"{PressRadiusKey}, {LoyaltyToStandKey}.");
                        break;
                }
            }

            if (stepsPerDay < 1)
            {
                report.Add(table.SourceName, 0,
                    $"'{StepsPerDayKey}' is {stepsPerDay}. A mob that never steps is a number again.");
                stepsPerDay = Default.StepsPerDay;
            }

            if (pressRadius > musterRadius)
            {
                report.Add(table.SourceName, 0,
                    $"'{PressRadiusKey}' ({pressRadius}) is beyond '{MusterRadiusKey}' " +
                    $"({musterRadius}), so the crowd would gather inside the line it is meant " +
                    "to press against and walk outward.");
            }

            return new MobRules(bodiesPerCommoner, maximumBodies, stepsPerDay, speed,
                musterRadius, pressRadius, loyaltyToStand);
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
