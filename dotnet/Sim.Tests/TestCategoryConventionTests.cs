using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// Guards the split itself. Unit and functional runs are both filtered, so a fixture with
    /// no category runs in <em>neither</em> — it does not fail, it disappears, which is worse.
    /// </summary>
    [Category(TestCategories.Unit)]
    public class TestCategoryConventionTests
    {
        private static readonly string[] Valid = { TestCategories.Unit, TestCategories.Functional };

        private static IEnumerable<Type> Fixtures =>
            Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract)
                .Where(t => t.GetMethods().Any(m =>
                    m.GetCustomAttributes(typeof(TestAttribute), false).Length > 0 ||
                    m.GetCustomAttributes(typeof(TestCaseAttribute), false).Length > 0))
                .OrderBy(t => t.FullName, StringComparer.Ordinal);

        private static string[] CategoriesOf(Type fixture) =>
            fixture.GetCustomAttributes(typeof(CategoryAttribute), true)
                .Cast<CategoryAttribute>()
                .Select(c => c.Name)
                .ToArray();

        [Test]
        public void Every_fixture_declares_exactly_one_known_category()
        {
            var problems = new List<string>();

            foreach (Type fixture in Fixtures)
            {
                string[] categories = CategoriesOf(fixture);

                if (categories.Length == 0)
                {
                    problems.Add($"{fixture.Name} has no [Category]. Add TestCategories.Unit or " +
                                 "TestCategories.Functional, or it runs in neither suite.");
                    continue;
                }

                if (categories.Length > 1)
                {
                    problems.Add($"{fixture.Name} declares {categories.Length} categories " +
                                 $"({string.Join(", ", categories)}). A test is one kind or the other.");
                    continue;
                }

                if (!Valid.Contains(categories[0]))
                {
                    problems.Add($"{fixture.Name} uses unknown category '{categories[0]}'. " +
                                 $"Expected one of {string.Join(", ", Valid)}.");
                }
            }

            Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
        }

        [Test]
        public void The_two_categories_together_account_for_every_fixture()
        {
            Type[] all = Fixtures.ToArray();
            int categorised = all.Count(f => CategoriesOf(f).Length == 1);

            Assert.That(categorised, Is.EqualTo(all.Length));
            Assert.That(all.Length, Is.GreaterThan(0), "reflection found no fixtures at all");
        }
    }
}
