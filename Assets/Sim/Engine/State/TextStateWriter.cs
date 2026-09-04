using System.Text;

namespace RTS.Sim.Engine.State
{
    /// <summary>
    /// Writes world state as indented text, so a failed determinism check can be diffed
    /// instead of merely reported.
    /// </summary>
    /// <remarks>
    /// This is the half of the gate that makes a failure actionable. "Digest a1b2… != c3d4…"
    /// says the sim diverged; two of these run through a diff say <em>where</em>, which is the
    /// difference between a five-minute fix and an afternoon.
    /// </remarks>
    public sealed class TextStateWriter : IStateWriter
    {
        private readonly StringBuilder _text = new StringBuilder();
        private int _depth;

        public void BeginSection(string name)
        {
            Indent();
            _text.Append(name).Append(':').Append('\n');
            _depth++;
        }

        public void EndSection()
        {
            if (_depth > 0) _depth--;
        }

        public void Write(string name, int value) => Line(name, value.ToString(Invariant));

        public void Write(string name, long value) => Line(name, value.ToString(Invariant));

        public void Write(string name, uint value) => Line(name, value.ToString(Invariant));

        public void Write(string name, ulong value) => Line(name, value.ToString(Invariant));

        public void Write(string name, bool value) => Line(name, value ? "true" : "false");

        /// <summary>Value and bit pattern: readable, and exact enough to spot a 1-ulp drift.</summary>
        public void Write(string name, float value) =>
            Line(name, value.ToString("R", Invariant) + " (0x" + HashStateWriter.FloatBits(value).ToString("x8") + ")");

        public void Write(string name, string value) => Line(name, value ?? "<null>");

        public override string ToString() => _text.ToString();

        private static System.Globalization.CultureInfo Invariant =>
            System.Globalization.CultureInfo.InvariantCulture;

        private void Line(string name, string value)
        {
            Indent();
            _text.Append(name).Append(" = ").Append(value).Append('\n');
        }

        private void Indent() => _text.Append(' ', _depth * 2);
    }
}
