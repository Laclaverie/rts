using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using RTS.Sim.Engine.Commands;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// Keeps enums honest.
    /// </summary>
    /// <remarks>
    /// C# already refuses an implicit conversion from <c>int</c> to an enum, with one exception:
    /// the literal <c>0</c>. What it always permits is the <em>explicit</em> cast, and
    /// <c>(LogChannel)99</c> is legal, unchecked, and produces a value no <c>switch</c> handles.
    /// No compiler setting forbids it, so it is forbidden here.
    /// <para>
    /// A Roslyn analyser package would be the other option. It was not taken: Unity does not run
    /// NuGet analysers, so the rule would hold in the shadow build and quietly not in the
    /// editor — and a rule that applies in one of two compilers is worse than one test that
    /// applies to the source itself.
    /// </para>
    /// </remarks>
    public static class EnumConventions
    {
        /// <summary>The assemblies whose enums are covered.</summary>
        public static IEnumerable<Assembly> Assemblies => new[]
        {
            typeof(CommandRejection).Assembly,                 // Sim
            typeof(Content.Loading.CsvTable).Assembly,         // Content
        }.Distinct();

        public static IEnumerable<Type> Enums =>
            Assemblies.SelectMany(a => a.GetTypes())
                .Where(t => t.IsEnum && t.IsPublic)
                .OrderBy(t => t.FullName, StringComparer.Ordinal);
    }

    [Category(TestCategories.Unit)]
    public class EnumConventionTests
    {
        [Test]
        public void Every_enum_defines_zero()
        {
            // `default(T)` and a zeroed struct field both produce 0. If no member has it, that
            // value is undefined and reaches a switch nobody wrote a case for.
            var problems = new List<string>();

            foreach (Type type in EnumConventions.Enums)
            {
                bool hasZero = Enum.GetValues(type).Cast<object>()
                    .Any(v => Convert.ToInt64(v) == 0L);

                if (!hasZero) problems.Add($"{type.Name} has no member with value 0.");
            }

            Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
        }

        [Test]
        public void No_enum_has_two_names_for_one_value()
        {
            // An alias is usually a typo — two members meant to be distinct, one of which
            // silently became the other. Where an alias is genuinely wanted, this test is the
            // place to record the exception.
            var problems = new List<string>();

            foreach (Type type in EnumConventions.Enums)
            {
                var byValue = new Dictionary<long, string>();

                foreach (string name in Enum.GetNames(type))
                {
                    long value = Convert.ToInt64(Enum.Parse(type, name));

                    if (byValue.TryGetValue(value, out string? existing))
                        problems.Add($"{type.Name}.{name} and .{existing} are both {value}.");
                    else
                        byValue.Add(value, name);
                }
            }

            Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
        }

        [Test]
        public void Enum_values_are_contiguous_from_zero()
        {
            // Several of these index arrays — Log's level table by LogChannel, for one. A gap
            // or a negative member turns an array read into a silent fallback.
            var problems = new List<string>();

            foreach (Type type in EnumConventions.Enums)
            {
                if (type.GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0) continue;

                long[] values = Enum.GetValues(type).Cast<object>()
                    .Select(Convert.ToInt64).Distinct().OrderBy(v => v).ToArray();

                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] == i) continue;

                    problems.Add($"{type.Name} is not contiguous from zero: expected {i}, found {values[i]}.");
                    break;
                }
            }

            Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
        }
    }

    /// <summary>
    /// Scans the source for casts that turn an integer into an enum. Functional, because it
    /// reads the repository rather than the compiled assemblies.
    /// </summary>
    [Category(TestCategories.Functional)]
    public class NoIntToEnumCastTests
    {
        /// <summary>
        /// Matches `(SomeEnum)x` where x is a number or a plain identifier — the shape that
        /// produces an undefined value. Casts of an array, as in `(Phase[])Enum.GetValues(...)`,
        /// do not match, and neither does an enum-to-int cast, which is always safe.
        /// </summary>
        private static readonly Regex Cast = new Regex(
            @"\((?<type>LogChannel|LogLevel|Phase|CommandRejection)\)\s*(?<operand>[A-Za-z_][A-Za-z0-9_.]*|\d+)",
            RegexOptions.Compiled);

        /// <summary>
        /// Casting the result of `Enum.GetValues` or `Enum.Parse` is how you enumerate an enum,
        /// and is checked by construction.
        /// </summary>
        private static readonly string[] Allowed = { "Enum.GetValues", "Enum.Parse", "Enum.ToObject" };

        private static string? RepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Assets")))
                directory = directory.Parent;

            return directory?.FullName;
        }

        [Test]
        public void No_source_file_casts_an_integer_to_an_enum()
        {
            string? root = RepositoryRoot();
            Assert.That(root, Is.Not.Null, "could not locate the repository root from the test directory");

            string[] files = Directory.GetFiles(Path.Combine(root!, "Assets"), "*.cs", SearchOption.AllDirectories);
            Assert.That(files.Length, Is.GreaterThan(0), "the scan found no source files, so it proves nothing");

            var problems = new List<string>();

            foreach (string file in files)
            {
                string[] lines = File.ReadAllLines(file);

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                        trimmed.StartsWith("///", StringComparison.Ordinal) ||
                        trimmed.StartsWith("*", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (Match match in Cast.Matches(line))
                    {
                        if (Allowed.Any(a => line.Contains(a, StringComparison.Ordinal))) continue;

                        problems.Add(
                            $"{Path.GetFileName(file)}({i + 1}): (" + match.Groups["type"].Value + ")" +
                            match.Groups["operand"].Value +
                            " — casting to an enum is unchecked and can produce a value no switch handles. " +
                            "Parse by name, or add a checked conversion.");
                    }
                }
            }

            Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
        }
    }
}
