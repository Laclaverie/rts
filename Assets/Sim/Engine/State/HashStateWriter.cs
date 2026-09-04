using System;

namespace RTS.Sim.Engine.State
{
    /// <summary>
    /// Folds world state into a 64-bit digest (FNV-1a), for asserting that two runs produced
    /// the same thing.
    /// </summary>
    /// <remarks>
    /// FNV-1a because it is eight lines, has no lookup tables, and behaves identically on every
    /// runtime — the same reason <see cref="Randomness.Rng"/> does not use
    /// <c>System.Random</c>. This is a change detector, not a security hash.
    /// <para>
    /// Structure is hashed, not just values: section boundaries and names go in, so moving a
    /// value from one entity to another changes the digest even when the multiset of values
    /// does not.
    /// </para>
    /// </remarks>
    public sealed class HashStateWriter : IStateWriter
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        private ulong _hash = Offset;

        /// <summary>The digest so far.</summary>
        public ulong Hash => _hash;

        /// <summary>The digest as a stable 16-character string, for messages and logs.</summary>
        public string Digest => _hash.ToString("x16");

        public void BeginSection(string name)
        {
            FoldString("{");
            FoldString(name);
        }

        public void EndSection() => FoldString("}");

        public void Write(string name, int value)
        {
            FoldString(name);
            FoldULong(unchecked((ulong)(long)value));
        }

        public void Write(string name, long value)
        {
            FoldString(name);
            FoldULong(unchecked((ulong)value));
        }

        public void Write(string name, uint value)
        {
            FoldString(name);
            FoldULong(value);
        }

        public void Write(string name, ulong value)
        {
            FoldString(name);
            FoldULong(value);
        }

        public void Write(string name, bool value)
        {
            FoldString(name);
            FoldULong(value ? 1UL : 0UL);
        }

        public void Write(string name, float value)
        {
            FoldString(name);
            FoldULong(FloatBits(value));
        }

        public void Write(string name, string value)
        {
            FoldString(name);
            FoldString(value ?? "\0null");
        }

        public void Reset() => _hash = Offset;

        public override string ToString() => Digest;

        /// <summary>
        /// The exact bit pattern. Formatting the float as text would round, and rounding can
        /// hide precisely the small divergence this exists to catch.
        /// </summary>
        internal static uint FloatBits(float value)
        {
            // netstandard2.1 has BitConverter.SingleToInt32Bits; going through it keeps this
            // allocation-free and exact.
            return unchecked((uint)BitConverter.SingleToInt32Bits(value));
        }

        private void FoldString(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                // Char by char, so the result does not depend on the machine's text encoding.
                FoldByte((byte)(value[i] & 0xFF));
                FoldByte((byte)(value[i] >> 8));
            }

            FoldByte(0);
        }

        private void FoldULong(ulong value)
        {
            for (int shift = 0; shift < 64; shift += 8)
                FoldByte((byte)(value >> shift));
        }

        private void FoldByte(byte value)
        {
            unchecked
            {
                _hash ^= value;
                _hash *= Prime;
            }
        }
    }
}
