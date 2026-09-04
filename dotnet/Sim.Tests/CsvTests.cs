using RTS.Content.Loading;

namespace RTS.Sim.Tests
{
    [Category(TestCategories.Unit)]
    public class CsvTests
    {
        private static CsvTable Parse(string text) => CsvTable.Parse(text, "test.csv");

        [Test]
        public void Reads_header_and_rows()
        {
            CsvTable table = Parse("a,b\n1,2\n3,4\n");

            Assert.That(table.Columns, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(table.Rows.Count, Is.EqualTo(2));
            Assert.That(table.Rows[0]["a"], Is.EqualTo("1"));
            Assert.That(table.Rows[1]["b"], Is.EqualTo("4"));
        }

        [Test]
        public void Ignores_comments_and_blank_lines_but_still_counts_them()
        {
            CsvTable table = Parse("# a comment\n\na,b\n   # indented comment\n1,2\n");

            Assert.That(table.Rows.Count, Is.EqualTo(1));
            Assert.That(table.Rows[0].Line, Is.EqualTo(5),
                "line numbers must count skipped lines, or errors point at the wrong row");
        }

        [Test]
        public void Trims_surrounding_whitespace()
        {
            CsvTable table = Parse("a , b\n 1 ,  2\n");

            Assert.That(table.Columns, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(table.Rows[0]["b"], Is.EqualTo("2"));
        }

        [Test]
        public void Handles_crlf()
        {
            CsvTable table = Parse("a,b\r\n1,2\r\n");

            Assert.That(table.Rows[0]["b"], Is.EqualTo("2"));
        }

        [Test]
        public void Quoted_fields_may_contain_commas_and_hashes()
        {
            CsvTable table = Parse("id,note\nWages,\"before Unrest, same day # always\"\n");

            Assert.That(table.Rows[0]["note"], Is.EqualTo("before Unrest, same day # always"));
        }

        [Test]
        public void Doubled_quote_inside_a_quoted_field_is_a_literal_quote()
        {
            CsvTable table = Parse("id,note\nx,\"say \"\"hello\"\"\"\n");

            Assert.That(table.Rows[0]["note"], Is.EqualTo("say \"hello\""));
        }

        [Test]
        public void Unterminated_quote_throws_naming_the_line()
        {
            var e = Assert.Throws<CsvFormatException>(() => Parse("a\n\"oops\n"));

            Assert.That(e.Line, Is.EqualTo(2));
            Assert.That(e.Message, Does.Contain("test.csv(2)"));
        }

        [Test]
        public void Wrong_field_count_throws_naming_the_line()
        {
            var e = Assert.Throws<CsvFormatException>(() => Parse("a,b\n1,2\n3\n"));

            Assert.That(e.Line, Is.EqualTo(3));
            Assert.That(e.Message, Does.Contain("expected 2 fields but read 1"));
        }

        [Test]
        public void Duplicate_column_throws()
        {
            var e = Assert.Throws<CsvFormatException>(() => Parse("a,a\n1,2\n"));

            Assert.That(e.Message, Does.Contain("duplicate column 'a'"));
        }

        [Test]
        public void A_file_with_only_comments_has_no_header_and_throws()
        {
            Assert.Throws<CsvFormatException>(() => Parse("# nothing here\n\n"));
        }

        [Test]
        public void Header_only_file_is_valid_and_empty()
        {
            CsvTable table = Parse("a,b\n");

            Assert.That(table.Rows, Is.Empty);
            Assert.That(table.Columns.Count, Is.EqualTo(2));
        }

        [Test]
        public void Unknown_column_throws_naming_it()
        {
            CsvTable table = Parse("a\n1\n");

            var e = Assert.Throws<CsvFormatException>(() => _ = table.Rows[0]["nope"]);
            Assert.That(e.Message, Does.Contain("no column 'nope'"));
        }

        [Test]
        public void GetInt_rejects_a_non_integer()
        {
            CsvTable table = Parse("order\nten\n");

            var e = Assert.Throws<CsvFormatException>(() => table.Rows[0].GetInt("order"));
            Assert.That(e.Message, Does.Contain("expected an integer but read 'ten'"));
        }

        [Test]
        public void GetBool_accepts_only_true_or_false()
        {
            Assert.That(Parse("enabled\nTRUE\n").Rows[0].GetBool("enabled"), Is.True);
            Assert.That(Parse("enabled\nFalse\n").Rows[0].GetBool("enabled"), Is.False);

            CsvTable yes = Parse("enabled\nyes\n");
            var e = Assert.Throws<CsvFormatException>(() => yes.Rows[0].GetBool("enabled"));
            Assert.That(e.Message, Does.Contain("expected true or false but read 'yes'"));
        }
    }
}
