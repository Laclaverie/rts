using System.Collections.Generic;
using NUnit.Framework;
using RTS.Sim.Engine.Randomness;

namespace RTS.Game.Tests
{
    /// <summary>
    /// The same golden vectors the headless suite asserts, run on Unity's runtime.
    /// </summary>
    /// <remarks>
    /// This is the only place the cross-runtime claim is actually tested. `Sim` compiles under
    /// .NET for the headless suite and under Unity's runtime for the game, and §6.1 promises a
    /// save is a seed plus a command log — which is only true if both runtimes draw the same
    /// sequence from the same seed. `System.Random` does not (its algorithm differs between
    /// .NET Framework/Mono and .NET Core 3.0+), which is why <see cref="Rng"/> implements the
    /// algorithm explicitly. If that reasoning is ever wrong, these tests are what says so.
    /// </remarks>
    [Category("Functional")]
    public class RngCrossRuntimeTests
    {
        [Test]
        public void UInts_match_the_golden_vector_on_this_runtime()
        {
            var rng = new Rng(RngGoldenVectors.UIntSeed);

            for (int i = 0; i < RngGoldenVectors.FirstEightUInts.Length; i++)
            {
                Assert.That(rng.NextUInt(), Is.EqualTo(RngGoldenVectors.FirstEightUInts[i]),
                    $"draw {i} diverges from the headless runtime — saves would not replay");
            }
        }

        [Test]
        public void Floats_match_the_golden_vector_on_this_runtime()
        {
            var rng = new Rng(RngGoldenVectors.FloatSeed);

            for (int i = 0; i < RngGoldenVectors.FirstFourFloats.Length; i++)
                Assert.That(rng.NextFloat(), Is.EqualTo(RngGoldenVectors.FirstFourFloats[i]).Within(1e-7f));
        }

        [Test]
        public void Ranged_draws_match_the_golden_vector_on_this_runtime()
        {
            var rng = new Rng(RngGoldenVectors.DieSeed);

            for (int i = 0; i < RngGoldenVectors.TenD20Rolls.Length; i++)
                Assert.That(rng.NextInt(1, 21), Is.EqualTo(RngGoldenVectors.TenD20Rolls[i]));
        }

        [Test]
        public void Shuffle_matches_the_golden_vector_on_this_runtime()
        {
            var rng = new Rng(RngGoldenVectors.ShuffleSeed);
            var items = new List<int>();
            for (int i = 0; i < 10; i++) items.Add(i);

            rng.Shuffle(items);

            Assert.That(items, Is.EqualTo(RngGoldenVectors.ShuffledTen));
        }
    }
}
