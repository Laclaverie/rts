using System;
using System.Collections.Generic;
using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;

namespace RTS.Sim.Tests
{
    [Category(TestCategories.Unit)]
    public class ContentValidationTests
    {
        // A toy table. Phase 0 admits no game concepts, so these stand in for goods.csv and
        // buildings.csv without importing any of their meaning.
        private enum Kind { Solid, Liquid }

        private sealed class Thing : IHasId
        {
            public string Id { get; set; } = string.Empty;
            public int Weight { get; set; }
            public float Volatility { get; set; }
            public bool Tradable { get; set; }
            public Kind Kind { get; set; }
        }

        private sealed class Holder : IHasId
        {
            public string Id { get; set; } = string.Empty;
            public string Holds { get; set; } = string.Empty;
        }

        private const string ThingColumns = "id,weight,volatility,tradable,kind\n";

        private static CsvTable Things(string rows) =>
            CsvTable.Parse(ThingColumns + rows, "things.csv");

        private static ConfigRegistry<Thing> LoadThings(CsvTable table, ValidationReport report) =>
            ConfigRegistry<Thing>.Load(table, report, row => new Thing
            {
                Id = row.Id(),
                Weight = row.Int("weight", min: 0),
                Volatility = row.Float("volatility", 0f, 1f),
                Tradable = row.Bool("tradable"),
                Kind = row.Enum<Kind>("kind"),
            }, "id", "weight", "volatility", "tradable", "kind");

        // ------------------------------------------------------------------- happy path

        [Test]
        public void A_valid_table_loads_in_file_order()
        {
            var report = new ValidationReport();
            ConfigRegistry<Thing> things = LoadThings(
                Things("iron,10,0.5,true,Solid\nale,3,0.9,true,Liquid\n"), report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));
            Assert.That(things.Count, Is.EqualTo(2));
            Assert.That(things.Select(t => t.Id), Is.EqualTo(new[] { "iron", "ale" }),
                "iteration is file order, never dictionary order (§7.1)");
            Assert.That(things["ale"].Volatility, Is.EqualTo(0.9f).Within(1e-6f));
            Assert.That(things["iron"].Kind, Is.EqualTo(Kind.Solid));
        }

        [Test]
        public void Lookup_by_id_and_by_index_agree()
        {
            var report = new ValidationReport();
            ConfigRegistry<Thing> things = LoadThings(Things("iron,10,0.5,true,Solid\n"), report);

            Assert.That(things[0].Id, Is.EqualTo("iron"));
            Assert.That(things.Contains("iron"), Is.True);
            Assert.That(things.TryGet("iron", out Thing found), Is.True);
            Assert.That(found.Weight, Is.EqualTo(10));
            Assert.That(things.LineOf("iron"), Is.EqualTo(2));
        }

        [Test]
        public void An_unknown_id_is_a_miss_not_a_default()
        {
            var report = new ValidationReport();
            ConfigRegistry<Thing> things = LoadThings(Things("iron,10,0.5,true,Solid\n"), report);

            Assert.That(things.TryGet("nope", out Thing _), Is.False);
            Assert.Throws<KeyNotFoundException>(() => _ = things["nope"]);
        }

        // -------------------------------------------------------------- collecting

        [Test]
        public void Every_problem_is_reported_not_just_the_first()
        {
            var report = new ValidationReport();
            LoadThings(Things(
                "iron,-5,0.5,true,Solid\n" +      // weight below range
                "ale,3,9.9,true,Liquid\n" +       // volatility above range
                "silk,2,0.1,maybe,Solid\n" +      // not a bool
                "oil,1,0.2,true,Gas\n"), report); // not a Kind

            Assert.That(report.Count, Is.EqualTo(4), string.Join(Environment.NewLine, report.Problems));
        }

        [Test]
        public void A_row_that_reported_a_problem_is_not_kept()
        {
            var report = new ValidationReport();
            ConfigRegistry<Thing> things = LoadThings(
                Things("iron,-5,0.5,true,Solid\nale,3,0.9,true,Liquid\n"), report);

            Assert.That(things.Count, Is.EqualTo(1), "a half-parsed entry must not reach the sim");
            Assert.That(things.Contains("ale"), Is.True);
            Assert.That(things.Contains("iron"), Is.False);
        }

        [Test]
        public void Problems_name_the_file_and_the_line()
        {
            var report = new ValidationReport();
            LoadThings(Things("iron,10,0.5,true,Solid\nale,-1,0.9,true,Liquid\n"), report);

            Assert.That(report.Problems.Single(), Does.StartWith("things.csv(3):"));
            Assert.That(report.Problems.Single(), Does.Contain("outside the allowed range"));
        }

        [Test]
        public void A_missing_column_is_reported_once_not_once_per_row()
        {
            var report = new ValidationReport();
            CsvTable table = CsvTable.Parse(
                "id,weight,volatility,tradable\niron,10,0.5,true\nale,3,0.9,true\nsilk,1,0.2,false\n",
                "things.csv");

            ConfigRegistry<Thing> things = ConfigRegistry<Thing>.Load(table, report,
                row => new Thing { Id = row.Id(), Kind = row.Enum<Kind>("kind") },
                "id", "weight", "volatility", "tradable", "kind");

            Assert.That(report.Count, Is.EqualTo(1), "a header problem must not repeat per row");
            Assert.That(report.Problems.Single(), Does.Contain("missing column 'kind'"));
            Assert.That(things.Count, Is.EqualTo(0), "no rows are read once the header is wrong");
        }

        [Test]
        public void A_duplicate_id_is_rejected_and_points_at_the_first()
        {
            var report = new ValidationReport();
            ConfigRegistry<Thing> things = LoadThings(
                Things("iron,10,0.5,true,Solid\niron,20,0.5,true,Solid\n"), report);

            Assert.That(things.Count, Is.EqualTo(1));
            Assert.That(things["iron"].Weight, Is.EqualTo(10), "the first definition wins");
            Assert.That(report.Problems.Single(), Does.Contain("already defined on line 2"));
        }

        [Test]
        public void An_empty_id_is_rejected()
        {
            var report = new ValidationReport();
            LoadThings(Things(",10,0.5,true,Solid\n"), report);

            Assert.That(report.Problems.Single(), Does.Contain("empty"));
        }

        [Test]
        public void An_empty_table_is_valid_and_empty()
        {
            var report = new ValidationReport();
            ConfigRegistry<Thing> things = LoadThings(Things(string.Empty), report);

            Assert.That(report.IsValid, Is.True);
            Assert.That(things.Count, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------- numbers

        [Test]
        public void Floats_parse_invariantly_regardless_of_machine_locale()
        {
            // A decimal comma would split the CSV field; a locale-sensitive parse would read
            // "0.5" as 5 on a French machine. Both are silent balance corruption.
            var report = new ValidationReport();
            ConfigRegistry<Thing> things = LoadThings(Things("iron,10,0.5,true,Solid\n"), report);

            Assert.That(report.IsValid, Is.True);
            Assert.That(things["iron"].Volatility, Is.EqualTo(0.5f).Within(1e-6f));
        }

        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("-Infinity")]
        public void Non_finite_numbers_are_rejected(string raw)
        {
            var report = new ValidationReport();
            LoadThings(Things($"iron,10,{raw},true,Solid\n"), report);

            Assert.That(report.Count, Is.EqualTo(1));
            Assert.That(report.Problems.Single(), Does.Contain("volatility"));
        }

        [Test]
        public void An_enum_column_will_not_accept_a_number_or_a_list()
        {
            // Same trap as pipeline.csv's phase column: Enum.TryParse would take both.
            var report = new ValidationReport();
            LoadThings(Things("a,1,0.1,true,0\nb,1,0.1,true,\"Solid,Liquid\"\nc,1,0.1,true,solid\n"), report);

            Assert.That(report.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(string.Join(" ", report.Problems), Does.Contain("Expected one of"));
        }

        // ---------------------------------------------------------------- references

        private static ConfigRegistry<Holder> LoadHolders(
            string rows, ValidationReport report, ICollection<PendingReference> pending) =>
            ConfigRegistry<Holder>.Load(
                CsvTable.Parse("id,holds\n" + rows, "holders.csv"), report,
                row => new Holder
                {
                    Id = row.Id(),
                    Holds = row.Reference("holds", pending, "things.csv"),
                }, "id", "holds");

        [Test]
        public void A_reference_that_resolves_passes()
        {
            var report = new ValidationReport();
            var pending = new List<PendingReference>();

            ConfigRegistry<Thing> things = LoadThings(Things("iron,10,0.5,true,Solid\n"), report);
            LoadHolders("crate,iron\n", report, pending);

            int checkedCount = ReferenceResolver.Resolve(report, pending, "things.csv", things);

            Assert.That(checkedCount, Is.EqualTo(1));
            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));
        }

        [Test]
        public void A_dangling_reference_is_reported_against_the_row_that_made_it()
        {
            var report = new ValidationReport();
            var pending = new List<PendingReference>();

            ConfigRegistry<Thing> things = LoadThings(Things("iron,10,0.5,true,Solid\n"), report);
            LoadHolders("crate,gold\n", report, pending);

            ReferenceResolver.Resolve(report, pending, "things.csv", things);

            Assert.That(report.Problems.Single(), Does.StartWith("holders.csv(2):"));
            Assert.That(report.Problems.Single(), Does.Contain("references 'gold'"));
            Assert.That(report.Problems.Single(), Does.Contain("things.csv"));
        }

        [Test]
        public void References_are_resolved_after_loading_so_forward_order_is_fine()
        {
            // holders.csv is read before things.csv exists as a registry.
            var report = new ValidationReport();
            var pending = new List<PendingReference>();

            LoadHolders("crate,iron\n", report, pending);
            ConfigRegistry<Thing> things = LoadThings(Things("iron,10,0.5,true,Solid\n"), report);

            ReferenceResolver.Resolve(report, pending, "things.csv", things);

            Assert.That(report.IsValid, Is.True);
        }

        [Test]
        public void A_reference_to_a_table_nobody_resolved_is_reported()
        {
            // Otherwise it reads as validated while having been checked against nothing.
            var report = new ValidationReport();
            var pending = new List<PendingReference>();

            LoadHolders("crate,iron\n", report, pending);

            ReferenceResolver.ReportUnresolvedTables(report, pending, new[] { "somewhere-else.csv" });

            Assert.That(report.Problems.Single(), Does.Contain("was never resolved"));
        }

        [Test]
        public void An_unresolved_table_is_reported_once_however_many_rows_point_at_it()
        {
            var report = new ValidationReport();
            var pending = new List<PendingReference>();

            LoadHolders("a,iron\nb,ale\nc,silk\n", report, pending);
            ReferenceResolver.ReportUnresolvedTables(report, pending, Array.Empty<string>());

            Assert.That(report.Count, Is.EqualTo(1));
        }

        // -------------------------------------------------------------------- failing

        [Test]
        public void ThrowIfInvalid_is_quiet_when_valid_and_lists_everything_when_not()
        {
            var clean = new ValidationReport();
            Assert.DoesNotThrow(() => clean.ThrowIfInvalid());

            var dirty = new ValidationReport();
            LoadThings(Things("iron,-5,0.5,true,Solid\nale,3,9.9,true,Liquid\n"), dirty);

            var e = Assert.Throws<ContentValidationException>(() => dirty.ThrowIfInvalid());

            Assert.That(e.Problems.Count, Is.EqualTo(2));
            Assert.That(e.Message, Does.Contain("2 problem(s)"));
            Assert.That(e.Message, Does.Contain("things.csv(2)"));
            Assert.That(e.Message, Does.Contain("things.csv(3)"));
        }
    }
}
