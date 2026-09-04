using System;
using System.Collections.Generic;

namespace RTS.Sim.Engine.Randomness
{
    /// <summary>
    /// The sim's only source of randomness: one seeded generator per world, its seed saved
    /// with the game (ARCHITECTURE §7.1).
    /// </summary>
    /// <remarks>
    /// <para><strong>Why not <c>System.Random</c>.</strong> Its algorithm is not stable across
    /// runtimes — .NET Framework/Mono and .NET Core 3.0+ produce different sequences from the
    /// same seed, and Microsoft documents the implementation as subject to change. <c>Sim</c>
    /// compiles under Unity's runtime <em>and</em> under .NET for the headless tests, so the
    /// same seed would diverge between them: tests would pass while the game replayed a
    /// different world. An explicit algorithm is the only way to hold §6.1's promise that a
    /// save is a seed plus a command log.</para>
    ///
    /// <para><strong>The algorithm is PCG-XSH-RR 32/64</strong> (O'Neill, 2014): 64 bits of
    /// state, a 32-bit output, one multiply and one rotate per draw. Chosen for being short
    /// enough to verify by reading and to pin with golden vectors, not for speed records.</para>
    ///
    /// <para><strong>Its state is sim state.</strong> §7.2 says all sim state lives in the
    /// world, and a generator half way through a sequence qualifies: a snapshot that restored
    /// the world but not the stream would diverge on the next draw. Hence
    /// <see cref="Capture"/> and <see cref="Restore"/>.</para>
    /// </remarks>
    public sealed class Rng
    {
        private const ulong Multiplier = 6364136223846793005UL;

        // Any odd increment selects a distinct stream. The default keeps a bare `new Rng(seed)`
        // reproducible without the caller thinking about streams at all.
        private const ulong DefaultStream = 1442695040888963407UL;

        private readonly ulong _increment;
        private ulong _state;

        /// <param name="seed">Saved with the game; replay starts from it (§6.1).</param>
        /// <param name="stream">
        /// Selects an independent sequence for the same seed. Give subsystems different
        /// streams so that adding a draw in one does not shift every other one — the usual way
        /// a "harmless" change silently invalidates every stored replay.
        /// </param>
        public Rng(ulong seed, ulong stream = DefaultStream)
        {
            Seed = seed;
            Stream = stream;

            // The increment must be odd for the LCG to reach full period.
            _increment = (stream << 1) | 1UL;

            _state = 0UL;
            Step();
            unchecked { _state += seed; }
            Step();
        }

        /// <summary>The seed this generator started from. Saved with the game.</summary>
        public ulong Seed { get; }

        /// <summary>Which independent sequence of that seed this generator draws from.</summary>
        public ulong Stream { get; }

        /// <summary>How many values have been drawn. Restoring rewinds this too.</summary>
        public long Draws { get; private set; }

        /// <summary>A uniform 32-bit value. Every other method is built on this one.</summary>
        public uint NextUInt()
        {
            ulong previous = _state;
            Step();
            Draws++;

            // XSH-RR: xorshift the high bits down, then rotate by the top 5 bits.
            uint xorshifted = (uint)(((previous >> 18) ^ previous) >> 27);
            int rotation = (int)(previous >> 59);
            return RotateRight(xorshifted, rotation);
        }

        /// <summary>A value in [0, maxExclusive).</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Must be positive.");

            return (int)NextBounded((uint)maxExclusive);
        }

        /// <summary>A value in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive,
                    $"Must be greater than minInclusive ({minInclusive}).");
            }

            uint span = (uint)((long)maxExclusive - minInclusive);
            return (int)(minInclusive + (long)NextBounded(span));
        }

        /// <summary>A value in [0, 1). Never exactly 1.</summary>
        public float NextFloat()
        {
            // 24 bits: exactly the float mantissa, so every result is representable and the
            // distribution has no gaps or duplicates from rounding.
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        /// <summary>A value in [min, max).</summary>
        public float NextFloat(float min, float max)
        {
            if (!(max > min))
                throw new ArgumentOutOfRangeException(nameof(max), max, $"Must be greater than min ({min}).");

            return min + NextFloat() * (max - min);
        }

        /// <summary>True with the given probability. 0 is never, 1 is always.</summary>
        public bool NextBool(float probability = 0.5f)
        {
            if (probability <= 0f) return false;
            if (probability >= 1f) return true;

            return NextFloat() < probability;
        }

        /// <summary>One item, uniformly. Throws on an empty list rather than returning default.</summary>
        public T Pick<T>(IReadOnlyList<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (items.Count == 0) throw new ArgumentException("Cannot pick from an empty list.", nameof(items));

            return items[NextInt(items.Count)];
        }

        /// <summary>Fisher-Yates, in place. The same seed always produces the same permutation.</summary>
        public void Shuffle<T>(IList<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                if (i == j) continue;

                T swap = items[i];
                items[i] = items[j];
                items[j] = swap;
            }
        }

        /// <summary>
        /// The generator's exact position, for a snapshot. Restoring it reproduces every
        /// subsequent draw (§6.1).
        /// </summary>
        public RngState Capture() => new RngState(Seed, Stream, _state, Draws);

        public void Restore(in RngState state)
        {
            if (state.Seed != Seed || state.Stream != Stream)
            {
                throw new ArgumentException(
                    $"State belongs to seed {state.Seed} stream {state.Stream}, not {Seed}/{Stream}. " +
                    "Restoring across streams would silently replay a different sequence.",
                    nameof(state));
            }

            _state = state.Position;
            Draws = state.Draws;
        }

        public override string ToString() => $"Rng(seed {Seed}, stream {Stream}, {Draws} draws)";

        private void Step()
        {
            unchecked { _state = _state * Multiplier + _increment; }
        }

        /// <summary>
        /// Uniform in [0, bound) with no modulo bias: values in the final partial block are
        /// rejected and redrawn. Bias here would quietly tilt every weighted event roll.
        /// </summary>
        private uint NextBounded(uint bound)
        {
            uint threshold = (uint)(-(int)bound) % bound;

            while (true)
            {
                uint draw = NextUInt();
                if (draw >= threshold) return draw % bound;
            }
        }

        // netstandard2.1 has no BitOperations.RotateRight.
        private static uint RotateRight(uint value, int rotation) =>
            (value >> rotation) | (value << ((-rotation) & 31));
    }

    /// <summary>A generator's exact position, so a snapshot can put it back (§6.1).</summary>
    public readonly struct RngState
    {
        public RngState(ulong seed, ulong stream, ulong position, long draws)
        {
            Seed = seed;
            Stream = stream;
            Position = position;
            Draws = draws;
        }

        public readonly ulong Seed;
        public readonly ulong Stream;
        public readonly ulong Position;
        public readonly long Draws;

        public override string ToString() => $"RngState(seed {Seed}, stream {Stream}, {Draws} draws)";
    }
}
