namespace RTS.Sim.Engine.State
{
    /// <summary>
    /// Where world state is written when it is being digested or snapshotted.
    /// </summary>
    /// <remarks>
    /// Two implementations exist for one reason: a hash tells you the run diverged, and tells
    /// you nothing about where. <see cref="HashStateWriter"/> answers "did it change" cheaply;
    /// <see cref="TextStateWriter"/> produces something diffable for when the answer is yes.
    /// A gate whose failure message is "two 64-bit numbers differ" wastes the failure.
    /// <para>
    /// Every method takes a name as well as a value. Names cost nothing in the hash — they are
    /// hashed too — and they are what makes the text form readable.
    /// </para>
    /// </remarks>
    public interface IStateWriter
    {
        /// <summary>Opens a named section. Sections may nest.</summary>
        void BeginSection(string name);

        void EndSection();

        void Write(string name, int value);

        void Write(string name, long value);

        void Write(string name, uint value);

        void Write(string name, ulong value);

        void Write(string name, bool value);

        /// <summary>
        /// A float, written by its exact bit pattern rather than a formatted decimal. Two
        /// different NaNs, or +0 and -0, must not compare equal by accident, and no rounding
        /// may hide a divergence.
        /// </summary>
        void Write(string name, float value);

        void Write(string name, string value);
    }
}
