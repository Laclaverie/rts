using System;
using System.Collections.Generic;
using System.Linq;
using RTS.Sim.Engine.Randomness;

namespace RTS.Sim.Tests
{
    [Category(TestCategories.Unit)]
    public class RngTests
    {
        // ------------------------------------------------------------------- golden

        [Test]
        public void The_sequence_matches_the_golden_vector()
        {
            // Pins the algorithm itself. A change here is a save-format break (§6.1): every
            // stored command log replays against this sequence.
            var rng = new Rng(RngGoldenVectors.UIntSeed);

            uint[] drawn = Enumerable.Range(0, RngGoldenVectors.FirstEightUInts.Length)
                .Select(_ => rng.NextUInt())
                .ToArray();

            Assert.That(drawn, Is.EqualTo(RngGoldenVectors.FirstEightUInts));
        }

        [Test]
        public void Floats_match_the_golden_vector()
        {
            var rng = new Rng(RngGoldenVectors.FloatSeed);

            for (int i = 0; i < RngGoldenVectors.FirstFourFloats.Length; i++)
                Assert.That(rng.NextFloat(), Is.EqualTo(RngGoldenVectors.FirstFourFloats[i]).Within(1e-7f));
        }

        [Test]
        public void Ranged_draws_match_the_golden_vector()
        {
            var rng = new Rng(RngGoldenVectors.DieSeed);

            int[] rolls = Enumerable.Range(0, RngGoldenVectors.TenD20Rolls.Length)
                .Select(_ => rng.NextInt(1, 21))
                .ToArray();

            Assert.That(rolls, Is.EqualTo(RngGoldenVectors.TenD20Rolls));
        }

        [Test]
        public void Shuffle_matches_the_golden_vector()
        {
            var rng = new Rng(RngGoldenVectors.ShuffleSeed);
            var items = Enumerable.Range(0, 10).ToList();

            rng.Shuffle(items);

            Assert.That(items, Is.EqualTo(RngGoldenVectors.ShuffledTen));
        }

        // -------------------------------------------------------------- determinism

        [Test]
        public void The_same_seed_replays_the_same_sequence()
        {
            var a = new Rng(4242UL);
            var b = new Rng(4242UL);

            for (int i = 0; i < 200; i++)
                Assert.That(a.NextUInt(), Is.EqualTo(b.NextUInt()));
        }

        [Test]
        public void Different_seeds_diverge_immediately()
        {
            var a = new Rng(1UL);
            var b = new Rng(2UL);

            uint[] first = Enumerable.Range(0, 8).Select(_ => a.NextUInt()).ToArray();
            uint[] second = Enumerable.Range(0, 8).Select(_ => b.NextUInt()).ToArray();

            Assert.That(second, Is.Not.EqualTo(first));
        }

        [Test]
        public void Streams_of_one_seed_are_independent()
        {
            // Subsystems draw from different streams, so adding a draw in one does not shift
            // every other one and silently invalidate stored replays.
            var a = new Rng(500UL, stream: 1UL);
            var b = new Rng(500UL, stream: 2UL);

            uint[] first = Enumerable.Range(0, 8).Select(_ => a.NextUInt()).ToArray();
            uint[] second = Enumerable.Range(0, 8).Select(_ => b.NextUInt()).ToArray();

            Assert.That(second, Is.Not.EqualTo(first));
        }

        // ------------------------------------------------------------------ capture

        [Test]
        public void Capture_and_restore_reproduce_the_rest_of_the_stream()
        {
            // A snapshot restores the world; it must restore the stream position too, or the
            // next draw diverges (§6.1, §7.2).
            var rng = new Rng(31337UL);
            for (int i = 0; i < 10; i++) rng.NextUInt();

            RngState saved = rng.Capture();
            uint[] expected = Enumerable.Range(0, 20).Select(_ => rng.NextUInt()).ToArray();

            rng.Restore(saved);
            uint[] replayed = Enumerable.Range(0, 20).Select(_ => rng.NextUInt()).ToArray();

            Assert.That(replayed, Is.EqualTo(expected));
        }

        [Test]
        public void Restore_rewinds_the_draw_count_too()
        {
            var rng = new Rng(8UL);
            RngState atStart = rng.Capture();
            for (int i = 0; i < 5; i++) rng.NextUInt();

            Assert.That(rng.Draws, Is.EqualTo(5));

            rng.Restore(atStart);

            Assert.That(rng.Draws, Is.EqualTo(0));
        }

        [Test]
        public void Restoring_a_state_from_another_seed_or_stream_is_refused()
        {
            var mine = new Rng(1UL, stream: 1UL);
            RngState otherSeed = new Rng(2UL, stream: 1UL).Capture();
            RngState otherStream = new Rng(1UL, stream: 9UL).Capture();

            Assert.Throws<ArgumentException>(() => mine.Restore(otherSeed));
            Assert.Throws<ArgumentException>(() => mine.Restore(otherStream));
        }

        // ------------------------------------------------------------------- ranges

        [Test]
        public void NextInt_stays_inside_its_bound()
        {
            var rng = new Rng(11UL);

            for (int i = 0; i < 5000; i++)
            {
                int value = rng.NextInt(10);
                Assert.That(value, Is.InRange(0, 9));
            }
        }

        [Test]
        public void NextInt_with_a_range_stays_inside_it()
        {
            var rng = new Rng(12UL);

            for (int i = 0; i < 5000; i++)
            {
                int value = rng.NextInt(-5, 6);
                Assert.That(value, Is.InRange(-5, 5));
            }
        }

        [Test]
        public void NextInt_covers_every_value_in_its_range()
        {
            var rng = new Rng(13UL);
            var seen = new HashSet<int>();

            for (int i = 0; i < 2000; i++) seen.Add(rng.NextInt(6));

            Assert.That(seen.OrderBy(v => v), Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
        }

        [Test]
        public void Bounded_draws_are_not_visibly_biased()
        {
            // Rejection sampling, not modulo. A bias here would quietly tilt every weighted
            // event roll in the game. 60000 draws over 6 buckets: expect 10000 each.
            var rng = new Rng(14UL);
            var counts = new int[6];

            for (int i = 0; i < 60000; i++) counts[rng.NextInt(6)]++;

            foreach (int count in counts)
                Assert.That(count, Is.InRange(9400, 10600), string.Join(", ", counts));
        }

        [Test]
        public void NextFloat_is_in_zero_to_one_exclusive()
        {
            var rng = new Rng(15UL);

            for (int i = 0; i < 20000; i++)
            {
                float value = rng.NextFloat();
                Assert.That(value, Is.GreaterThanOrEqualTo(0f));
                Assert.That(value, Is.LessThan(1f));
            }
        }

        [Test]
        public void NextFloat_with_a_range_stays_inside_it()
        {
            var rng = new Rng(16UL);

            for (int i = 0; i < 5000; i++)
            {
                float value = rng.NextFloat(-2.5f, 7.5f);
                Assert.That(value, Is.GreaterThanOrEqualTo(-2.5f));
                Assert.That(value, Is.LessThan(7.5f));
            }
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void NextInt_rejects_a_non_positive_bound(int bound)
        {
            var rng = new Rng(17UL);

            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(bound));
        }

        [Test]
        public void NextInt_rejects_an_inverted_range()
        {
            var rng = new Rng(18UL);

            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 4));
        }

        // -------------------------------------------------------------------- bool

        [Test]
        public void NextBool_honours_certainty_without_drawing()
        {
            var rng = new Rng(19UL);

            Assert.That(rng.NextBool(0f), Is.False);
            Assert.That(rng.NextBool(1f), Is.True);
            Assert.That(rng.Draws, Is.EqualTo(0), "a certain outcome must not consume the stream");
        }

        [Test]
        public void NextBool_is_roughly_fair()
        {
            var rng = new Rng(20UL);
            int trues = 0;

            for (int i = 0; i < 20000; i++)
                if (rng.NextBool(0.25f)) trues++;

            Assert.That(trues, Is.InRange(4600, 5400));
        }

        // -------------------------------------------------------------------- lists

        [Test]
        public void Shuffle_is_a_permutation()
        {
            var rng = new Rng(21UL);
            var items = Enumerable.Range(0, 50).ToList();

            rng.Shuffle(items);

            Assert.That(items.OrderBy(v => v), Is.EqualTo(Enumerable.Range(0, 50)));
        }

        [Test]
        public void Pick_returns_a_member_and_refuses_an_empty_list()
        {
            var rng = new Rng(22UL);
            var items = new[] { "a", "b", "c" };

            for (int i = 0; i < 100; i++)
                Assert.That(items, Does.Contain(rng.Pick(items)));

            Assert.Throws<ArgumentException>(() => rng.Pick(new string[0]));
            Assert.Throws<ArgumentNullException>(() => rng.Pick<string>(null));
        }

        [Test]
        public void Shuffle_of_zero_or_one_item_is_a_no_op()
        {
            var rng = new Rng(23UL);
            var empty = new List<int>();
            var single = new List<int> { 42 };

            rng.Shuffle(empty);
            rng.Shuffle(single);

            Assert.That(empty, Is.Empty);
            Assert.That(single, Is.EqualTo(new[] { 42 }));
            Assert.That(rng.Draws, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ counting

        [Test]
        public void Draws_counts_every_value_taken_from_the_stream()
        {
            var rng = new Rng(24UL);

            rng.NextUInt();
            rng.NextFloat();
            rng.NextInt(10);

            Assert.That(rng.Draws, Is.GreaterThanOrEqualTo(3),
                "rejection sampling may draw more than once, never fewer");
        }
    }
}
