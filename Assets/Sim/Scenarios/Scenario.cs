using System;
using System.Collections.Generic;
using System.Globalization;
using RTS.Content.Loading;
using RTS.Content.Validation;
using RTS.Sim.Systems;

namespace RTS.Sim.Scenarios
{
    /// <summary>One recorded run: a seed, a starting port, a command log, and what it produced.</summary>
    /// <remarks>
    /// §6.1 makes a save a seed plus a command log, and §8.2 makes a functional test the same
    /// thing. A scenario is therefore not test scaffolding — it is a save with an expected
    /// answer attached, and the corpus is the regression suite that "writes itself" as real
    /// sessions are kept.
    /// </remarks>
    public sealed class Scenario
    {
        public Scenario(string id, ulong seed, int startingCoin, int days,
            IReadOnlyList<ScheduledShock> shocks, string expectedDigest)
        {
            Id = id;
            Seed = seed;
            StartingCoin = startingCoin;
            Days = days;
            Shocks = shocks;
            ExpectedDigest = expectedDigest;
        }

        public string Id { get; }
        public ulong Seed { get; }
        public int StartingCoin { get; }
        public int Days { get; }
        public IReadOnlyList<ScheduledShock> Shocks { get; }

        /// <summary>
        /// The digest this run must produce, or empty while a new scenario is being added.
        /// </summary>
        /// <remarks>
        /// Empty is deliberate rather than an error: you add the row, run the corpus, and paste
        /// back what it printed. Requiring the answer before the question could be asked would
        /// mean computing digests by hand.
        /// </remarks>
        public string ExpectedDigest { get; }

        public bool IsPinned => !string.IsNullOrWhiteSpace(ExpectedDigest);

        public override string ToString() => Id;
    }

    /// <summary>A shock and the day it lands.</summary>
    public readonly struct ScheduledShock
    {
        public ScheduledShock(int day, ShockKind kind, float magnitude)
        {
            Day = day;
            Kind = kind;
            Magnitude = magnitude;
        }

        public readonly int Day;
        public readonly ShockKind Kind;
        public readonly float Magnitude;

        public override string ToString() =>
            $"{Day}:{Kind}:{Magnitude.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Reads <c>scenarios.csv</c>.</summary>
    /// <remarks>
    /// One row per scenario, with the shocks packed into a single quoted column as
    /// <c>day:kind:magnitude</c> separated by semicolons. Flat, so the CSV reader we already
    /// have is enough and no second config format appears; and one row per scenario means the
    /// corpus reads as a list of runs rather than a nested document.
    /// </remarks>
    public static class ScenarioFile
    {
        public const string FileName = "scenarios.csv";

        public const string IdColumn = "id";
        public const string SeedColumn = "seed";
        public const string CoinColumn = "coin";
        public const string DaysColumn = "days";
        public const string ShocksColumn = "shocks";
        public const string DigestColumn = "expected_digest";

        public static IReadOnlyList<Scenario> Load(CsvTable table, ValidationReport report)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (report == null) throw new ArgumentNullException(nameof(report));

            var scenarios = new List<Scenario>();

            if (!report.RequireColumns(table, IdColumn, SeedColumn, CoinColumn, DaysColumn,
                    ShocksColumn, DigestColumn))
            {
                return scenarios;
            }

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (CsvRow row in table.Rows)
            {
                var reader = new RowReader(row, report, table.SourceName);

                string id = reader.Id(IdColumn);
                int seed = reader.Int(SeedColumn, min: 0);
                int coin = reader.Int(CoinColumn, min: 0);
                int days = reader.Int(DaysColumn, min: 1, max: 10000);
                string shockText = reader.Text(ShocksColumn);
                string digest = reader.Text(DigestColumn);

                if (reader.HasProblems) continue;

                if (seen.TryGetValue(id, out int firstLine))
                {
                    report.Add(table.SourceName, row.Line,
                        $"scenario '{id}' is already defined on line {firstLine}.");
                    continue;
                }

                if (!TryParseShocks(shockText, out List<ScheduledShock> shocks, out string error))
                {
                    report.Add(table.SourceName, row.Line, error);
                    continue;
                }

                foreach (ScheduledShock shock in shocks)
                {
                    if (shock.Day > days)
                    {
                        report.Add(table.SourceName, row.Line,
                            $"a shock lands on day {shock.Day} but the run is only {days} days, " +
                            "so it would never happen.");
                    }
                }

                seen.Add(id, row.Line);
                scenarios.Add(new Scenario(
                    id: id,
                    seed: (ulong)seed,
                    startingCoin: coin,
                    days: days,
                    shocks: shocks,
                    expectedDigest: digest));
            }

            return scenarios;
        }

        /// <summary>Parses <c>10:Storm:0.30;12:Theft:100</c>. Empty means an undisturbed run.</summary>
        public static bool TryParseShocks(string text, out List<ScheduledShock> shocks, out string error)
        {
            shocks = new List<ScheduledShock>();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(text)) return true;

            foreach (string entry in text.Split(';'))
            {
                string trimmed = entry.Trim();
                if (trimmed.Length == 0) continue;

                string[] parts = trimmed.Split(':');
                if (parts.Length != 3)
                {
                    error = $"'{trimmed}' is not day:kind:magnitude.";
                    return false;
                }

                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out int day) || day < 1)
                {
                    error = $"'{parts[0]}' is not a day.";
                    return false;
                }

                // Exact name, never Enum.TryParse: it would accept "2" and silently mean Storm.
                ShockKind kind = ShockKind.None;
                bool known = false;
                foreach (ShockKind candidate in (ShockKind[])Enum.GetValues(typeof(ShockKind)))
                {
                    if (!string.Equals(candidate.ToString(), parts[1], StringComparison.Ordinal)) continue;

                    kind = candidate;
                    known = true;
                    break;
                }

                if (!known || kind == ShockKind.None)
                {
                    error = $"'{parts[1]}' is not a shock. Expected one of " +
                            $"{string.Join(", ", Enum.GetNames(typeof(ShockKind)))}.";
                    return false;
                }

                if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture,
                        out float magnitude) || magnitude <= 0f)
                {
                    error = $"'{parts[2]}' is not a positive magnitude.";
                    return false;
                }

                shocks.Add(new ScheduledShock(day, kind, magnitude));
            }

            return true;
        }
    }
}
