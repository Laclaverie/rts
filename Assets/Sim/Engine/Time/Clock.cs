using System;
using System.Collections.Generic;
using RTS.Content.Loading;
using RTS.Content.Validation;

namespace RTS.Sim.Engine.Time
{
    /// <summary>
    /// Turns real seconds into whole in-game days (GDD §3.2, §5.1).
    /// </summary>
    /// <remarks>
    /// The boundary between real time and the simulation, and the only place the two meet. The
    /// sim has no notion of a frame or a second: it advances a day at a time, and §7.1 forbids
    /// any system from reading frame time. This decides <em>when</em> a day happens; nothing
    /// about a day depends on how long the player took to watch it.
    /// <para>
    /// That separation is what keeps a played session and a headless replay identical. The
    /// accumulator below is a float and the frame rate is not deterministic, but neither ever
    /// reaches the world — only a whole number of days does.
    /// </para>
    /// <para>
    /// It lives in <c>Engine</c> with the other portable pieces (§2.1): no ports, no crew, no
    /// goods, nothing that would stop it being lifted into another project.
    /// </para>
    /// <para>
    /// <strong>Pause is not a convenience.</strong> §3.2 makes it the mechanism that separates
    /// decision complexity from reaction speed, which is the whole casual/complex
    /// reconciliation. It is here in the first Unity work rather than added later for exactly
    /// that reason.
    /// </para>
    /// </remarks>
    public sealed class Clock
    {
        public const string KeyColumn = "key";
        public const string ValueColumn = "value";

        public const string SecondsPerDayKey = "seconds_per_day";
        public const string SpeedsKey = "speeds";

        /// <summary>
        /// The most days a single <see cref="Advance"/> will hand back.
        /// </summary>
        /// <remarks>
        /// A breakpoint, a stalled frame or a laptop lid closing all produce one enormous delta.
        /// Without a ceiling the port would silently fast-forward a fortnight while nobody was
        /// looking, which is indistinguishable from a bug and destroys the run the player was
        /// watching. Time lost this way is dropped rather than banked: the clock is a pacing
        /// device, not an accounting one.
        /// </remarks>
        public const int MaximumDaysPerAdvance = 4;

        private float _accumulated;

        public Clock(float secondsPerDay, IReadOnlyList<int> speeds)
        {
            if (secondsPerDay <= 0f)
                throw new ArgumentOutOfRangeException(nameof(secondsPerDay), "A day must take some time.");
            if (speeds == null || speeds.Count == 0)
                throw new ArgumentException("A clock needs at least one speed.", nameof(speeds));

            SecondsPerDay = secondsPerDay;
            Speeds = speeds;
            Speed = speeds[0];
        }

        /// <summary>Real seconds in one in-game day at ×1. §5.1 says twenty minutes.</summary>
        public float SecondsPerDay { get; }

        /// <summary>The multipliers a player can choose, in the order they are offered.</summary>
        public IReadOnlyList<int> Speeds { get; }

        /// <summary>
        /// The current multiplier. Speed handles boredom; pause handles complexity (§5.1).
        /// </summary>
        public int Speed { get; set; }

        public bool Paused { get; private set; }

        /// <summary>How far into the current day the clock has run, 0..1. For a progress bar.</summary>
        public float DayProgress => _accumulated / SecondsPerDay;

        public void Pause() => Paused = true;

        public void Resume() => Paused = false;

        public void TogglePause() => Paused = !Paused;

        /// <summary>
        /// Feeds in the real time that has passed and returns how many whole days to run.
        /// </summary>
        /// <remarks>
        /// The remainder is kept, so the day boundary does not drift with the frame rate: sixty
        /// frames of a sixtieth of a second advance exactly as far as one frame of a second.
        /// </remarks>
        public int Advance(float realSeconds)
        {
            if (Paused || realSeconds <= 0f) return 0;

            _accumulated += realSeconds * Speed;

            int days = 0;
            while (_accumulated >= SecondsPerDay && days < MaximumDaysPerAdvance)
            {
                _accumulated -= SecondsPerDay;
                days++;
            }

            // Whatever a stall banked beyond the ceiling is discarded rather than paid out over
            // the following frames, which would leave the port racing for no visible reason.
            if (_accumulated >= SecondsPerDay) _accumulated = 0f;

            return days;
        }

        /// <summary>Reads <c>clock.csv</c>. Same reader and same loud failure as every table.</summary>
        public static Clock Load(CsvTable table, ValidationReport report)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (report == null) throw new ArgumentNullException(nameof(report));

            float secondsPerDay = 1200f;
            var speeds = new List<int> { 1 };

            if (!report.RequireColumns(table, KeyColumn, ValueColumn))
                return new Clock(secondsPerDay, speeds);

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
                    case SecondsPerDayKey:
                        secondsPerDay = ReadSeconds(value, table, row, report, secondsPerDay);
                        break;

                    case SpeedsKey:
                        ReadSpeeds(value, table, row, report, speeds);
                        break;

                    default:
                        // Loud, for the same reason a misspelled log channel is: a setting that
                        // silently keeps its default is worse than one that fails to load.
                        report.Add(table.SourceName, row.Line,
                            $"'{key}' is not a clock setting. Known keys: " +
                            $"{SecondsPerDayKey}, {SpeedsKey}.");
                        break;
                }
            }

            return new Clock(secondsPerDay, speeds);
        }

        private static float ReadSeconds(string value, CsvTable table, CsvRow row,
            ValidationReport report, float fallback)
        {
            if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float seconds) ||
                seconds <= 0f)
            {
                report.Add(table.SourceName, row.Line,
                    $"'{SecondsPerDayKey}' is '{value}', which is not a positive number of seconds.");
                return fallback;
            }

            return seconds;
        }

        private static void ReadSpeeds(string value, CsvTable table, CsvRow row,
            ValidationReport report, List<int> speeds)
        {
            string[] parts = value.Split(';');
            var parsed = new List<int>();

            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length == 0) continue;

                if (!int.TryParse(trimmed, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int speed) ||
                    speed <= 0)
                {
                    report.Add(table.SourceName, row.Line,
                        $"'{trimmed}' is not a speed. They are whole multipliers above zero.");
                    return;
                }

                parsed.Add(speed);
            }

            if (parsed.Count == 0)
            {
                report.Add(table.SourceName, row.Line,
                    $"'{SpeedsKey}' is empty. A clock with no speeds cannot run at all.");
                return;
            }

            // Ascending, because the buttons are drawn in this order and a row reading
            // "x4 x1 x2" would be a content mistake presented as a UI one.
            for (int i = 1; i < parsed.Count; i++)
            {
                if (parsed[i] > parsed[i - 1]) continue;

                report.Add(table.SourceName, row.Line,
                    $"'{SpeedsKey}' is not in ascending order: {parsed[i - 1]} then {parsed[i]}.");
                return;
            }

            speeds.Clear();
            speeds.AddRange(parsed);
        }
    }
}
