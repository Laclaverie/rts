using System;
using System.IO;
using System.Linq;
using System.Reflection;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// That every shipped table actually reaches the game.
    /// </summary>
    /// <remarks>
    /// <strong>This bug happened twice before anybody noticed.</strong> Both composition roots —
    /// the headless harness and the Unity boot — built their list of content files by hand, and
    /// both stopped at <c>ports.csv</c>. Four files that had been added since were never read, so
    /// <see cref="BalanceTables.Load"/> quietly used the built-in defaults for all of them.
    /// <para>
    /// The consequences were not small. The harness is the tuning instrument: editing
    /// <c>maintenance.csv</c> and re-running the corpus measured nothing at all, and the same
    /// numbers coming back looked like evidence that the change had no effect — twice, in a row,
    /// while the actual value was whatever the code said. And in Unity, the game being played
    /// was not the game the content described.
    /// </para>
    /// <para>
    /// Nothing failed loudly, because a missing table is legitimately optional: a fixture that
    /// only needs goods and buildings passes null for the rest, and the defaults are exactly
    /// right for it. The defaults are right for a fixture and wrong for a shipped game, and only
    /// the caller knows which it is — so the guard cannot live inside the loader. It lives here.
    /// </para>
    /// </remarks>
    [Category(TestCategories.Functional)]
    public class BalanceSourcesTests
    {
        private static string BalanceDirectory =>
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Balance");

        private static CsvTable Read(string file) =>
            CsvTable.Parse(File.ReadAllText(Path.Combine(BalanceDirectory, file)), file);

        [Test]
        public void Every_table_the_game_has_is_a_table_the_game_loads()
        {
            // The one that would have caught it. Adding a file and a property without adding it
            // to From() leaves that property null, and this fails by name.
            BalanceSources sources = BalanceSources.From(Read);

            Assert.That(sources.Missing(), Is.Empty,
                "BalanceSources.From does not read every table it can hold");
        }

        [Test]
        public void Every_table_it_loads_is_a_file_that_ships()
        {
            // The other direction: a property wired to a file nobody shipped would throw here
            // rather than at somebody's first launch.
            foreach (string file in Files())
                Assert.That(File.Exists(Path.Combine(BalanceDirectory, file)), Is.True, file);
        }

        [Test]
        public void The_shipped_files_load_without_a_single_complaint()
        {
            var report = new ValidationReport();
            BalanceTables.Load(BalanceSources.From(Read), report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));
        }

        [Test]
        public void A_forgotten_table_is_reported_by_name()
        {
            // Proving the guard works, rather than trusting that an empty list means anything.
            BalanceSources sources = BalanceSources.From(Read);
            sources.Heat = null;

            Assert.That(sources.Missing(), Is.EqualTo(new[] { nameof(BalanceSources.Heat) }));
        }

        [Test]
        public void A_table_left_out_actually_changes_the_game()
        {
            // Why the test above matters. Without heat.csv the world runs on built-in defaults,
            // and defaults are not what the content says — which is exactly how tuning a file and
            // measuring no change came to look like evidence.
            var report = new ValidationReport();

            BalanceSources full = BalanceSources.From(Read);
            BalanceSources without = BalanceSources.From(Read);
            without.Heat = null;

            BalanceTables shipped = BalanceTables.Load(full, report);
            BalanceTables fallback = BalanceTables.Load(without, report);

            report.ThrowIfInvalid();

            Assume.That(shipped.Heat.RaidChanceAtFull,
                Is.Not.EqualTo(HeatRules.Default.RaidChanceAtFull).Within(1e-6f),
                "heat.csv currently matches the default, so this proves nothing today");

            Assert.That(fallback.Heat.RaidChanceAtFull,
                Is.Not.EqualTo(shipped.Heat.RaidChanceAtFull).Within(1e-6f));
        }

        /// <summary>Every <c>*File</c> constant on <see cref="BalanceTables"/>.</summary>
        /// <remarks>
        /// By reflection, so the list cannot fall behind the constants the way the hand-written
        /// ones in the two composition roots did.
        /// </remarks>
        private static string[] Files() =>
            typeof(BalanceTables)
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Where(f => f.Name.EndsWith("File", StringComparison.Ordinal))
                .Select(f => f.GetRawConstantValue() as string)
                .Where(value => value != null)
                .Select(value => value!)
                .ToArray();

        [Test]
        public void There_is_a_property_for_every_file_constant()
        {
            // Catches the first half of the mistake: a table added to BalanceTables but with
            // nowhere to put it, which would make From() impossible to write correctly.
            int properties = typeof(BalanceSources).GetProperties()
                .Count(p => p.PropertyType == typeof(CsvTable));

            Assert.That(properties, Is.EqualTo(Files().Length),
                "files: " + string.Join(", ", Files()));
        }
    }
}
