using System.Globalization;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Events;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Engine.State;
using RTS.Sim.Systems;

namespace RTS.Harness;

/// <summary>
/// Runs the port headlessly and prints a table per day (BUILD_ORDER Phase 1).
/// </summary>
/// <remarks>
/// The point of this is to make the Phase 1 gate something you can look at. Assertions can
/// tell you a cascade happened; only a table tells you whether the curve feels right, and
/// §5.2.3 says a mushy result here is not to be skipped past.
/// <para>
/// It contains no rules. Everything it prints comes from <see cref="PortReport"/> and
/// everything it runs comes from <c>pipeline.csv</c>, so tuning here is tuning the game and
/// not tuning a second implementation of it.
/// </para>
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(Options.Usage);
                return 0;
            }

            return Run(options);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Options.Usage);
            return 1;
        }
    }

    private static int Run(Options options)
    {
        BalanceTables balance = LoadBalance(options.BalanceDirectory);

        Pipeline pipeline = Pipeline.Build(
            CsvTable.Parse(File.ReadAllText(Path.Combine(options.BalanceDirectory, "pipeline.csv")),
                "pipeline.csv"),
            EconomySystems());

        PortScenario scenario = PortScenario.Default();
        if (options.StartingCoin.HasValue) scenario.StartingCoin = options.StartingCoin.Value;

        var events = new EventQueue();
        var rng = new RTS.Sim.Engine.Randomness.Rng(options.Seed);
        World world = scenario.Build(balance);

        Console.WriteLine($"seed {options.Seed}, {options.Days} days, starting coin {scenario.StartingCoin}");
        Console.WriteLine(PortReport.Header(balance));
        Console.WriteLine(PortReport.Separator(balance));
        Console.WriteLine(PortReport.Of(world, balance, day: 0).ToRow());

        for (int day = 1; day <= options.Days; day++)
        {
            events.BeginCause(CauseId.Root, day);
            try
            {
                var ctx = new Context(day, 0f, events, rng, balance);
                pipeline.Run(Phase.DayBoundary, world, ctx);
            }
            finally
            {
                events.EndCause();
            }

            Console.WriteLine(PortReport.Of(world, balance, day).ToRow());

            if (options.ShowEvents) PrintEvents(events, day);
            events.Drain();
        }

        Console.WriteLine();
        Console.WriteLine($"digest {DigestOf(world)}");
        return 0;
    }

    private static void PrintEvents(EventQueue events, int day)
    {
        for (int i = 0; i < events.PendingCount; i++)
        {
            Envelope envelope = events.Pending[i];
            Console.WriteLine($"      · {envelope.PayloadType?.Name}");
        }
    }

    private static ISystem[] EconomySystems() => new ISystem[]
    {
        new ConsumptionSystem(), new WagesSystem(), new UpkeepSystem(),
        new DesertionSystem(), new ProductionSystem(), new MarketSystem(),
    };

    /// <summary>
    /// The world's state as a digest, so two runs can be compared without reading the table —
    /// the same value the replay gate compares (§6.1).
    /// </summary>
    private static string DigestOf(World world)
    {
        var writer = new HashStateWriter();
        world.WriteTo(writer);
        return writer.Digest;
    }

    private static BalanceTables LoadBalance(string directory)
    {
        var report = new ValidationReport();

        BalanceTables tables = BalanceTables.Load(
            Read(directory, BalanceTables.GoodsFile),
            Read(directory, BalanceTables.BuildingsFile),
            Read(directory, BalanceTables.CrewRolesFile),
            report);

        // Loud, and before anything runs. A sim started on invalid content produces numbers
        // that look plausible and mean nothing (§5.3).
        report.ThrowIfInvalid();
        return tables;
    }

    private static CsvTable Read(string directory, string file) =>
        CsvTable.Parse(File.ReadAllText(Path.Combine(directory, file)), file);

    private sealed class Options
    {
        public const string Usage = """
            rts — run the port headlessly and print a day-by-day table.

              --days N        how many days to run            (default 30)
              --seed N        the world's seed                (default 1)
              --coin N        starting coin, overriding the scenario
              --balance PATH  where the CSVs live             (default: found by walking up)
              --events        list the events emitted each day
              --help

            Everything it runs comes from pipeline.csv and everything it prints comes from the
            sim, so tuning here tunes the game rather than a second copy of it.
            """;

        public int Days { get; private set; } = 30;
        public ulong Seed { get; private set; } = 1;
        public int? StartingCoin { get; private set; }
        public string BalanceDirectory { get; private set; } = string.Empty;
        public bool ShowEvents { get; private set; }
        public bool ShowHelp { get; private set; }

        public static Options Parse(string[] args)
        {
            var options = new Options();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--days": options.Days = Int(args, ++i, "--days"); break;
                    case "--seed": options.Seed = (ulong)Int(args, ++i, "--seed"); break;
                    case "--coin": options.StartingCoin = Int(args, ++i, "--coin"); break;
                    case "--balance": options.BalanceDirectory = Next(args, ++i, "--balance"); break;
                    case "--events": options.ShowEvents = true; break;
                    case "--help":
                    case "-h": options.ShowHelp = true; break;
                    default: throw new ArgumentException($"Unknown argument '{args[i]}'.");
                }
            }

            if (options.Days < 0) throw new ArgumentException("--days cannot be negative.");

            if (options.BalanceDirectory.Length == 0)
                options.BalanceDirectory = FindBalanceDirectory();

            return options;
        }

        private static string Next(string[] args, int index, string name) =>
            index < args.Length ? args[index] : throw new ArgumentException($"{name} needs a value.");

        private static int Int(string[] args, int index, string name) =>
            int.TryParse(Next(args, index, name), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int value)
                ? value
                : throw new ArgumentException($"{name} needs a number.");

        /// <summary>
        /// Walks up from the executable looking for the repository's balance folder, so the
        /// harness runs from anywhere without arguments.
        /// </summary>
        private static string FindBalanceDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "Assets", "StreamingAssets", "Balance");
                if (Directory.Exists(candidate)) return candidate;

                directory = directory.Parent;
            }

            throw new ArgumentException(
                "Could not find Assets/StreamingAssets/Balance. Pass --balance PATH.");
        }
    }
}
