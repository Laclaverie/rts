namespace RTS.Sim.Engine.Randomness
{
    /// <summary>
    /// Fixed outputs for fixed seeds, asserted by both test suites.
    /// </summary>
    /// <remarks>
    /// These live in <c>Sim</c> rather than in a test project on purpose: the headless suite
    /// runs on .NET and the EditMode suite runs on Unity's runtime, and both assert against
    /// <em>this same table</em>. That is what turns "we believe the sequence is stable across
    /// runtimes" into something a build can fail on.
    /// <para>
    /// A change here is a save-format break. Every stored command log replays against the
    /// sequence these numbers describe (§6.1), so if a refactor moves them, the honest fix is
    /// to restore the algorithm — not to update the table.
    /// </para>
    /// </remarks>
    public static class RngGoldenVectors
    {
        public const ulong UIntSeed = 12345UL;

        public static readonly uint[] FirstEightUInts =
        {
            1321476956u, 17539747u, 3348728241u, 2863338820u,
            85463406u, 1024873269u, 4179236141u, 1040420088u,
        };

        public const ulong FloatSeed = 12345UL;

        public static readonly float[] FirstFourFloats =
        {
            0.3076803f, 0.0040837526f, 0.7796865f, 0.666673f,
        };

        public const ulong DieSeed = 7UL;

        /// <summary>Ten rolls of NextInt(1, 21).</summary>
        public static readonly int[] TenD20Rolls = { 4, 2, 9, 2, 8, 18, 7, 8, 6, 6 };

        public const ulong ShuffleSeed = 99UL;

        /// <summary>Shuffle of 0..9.</summary>
        public static readonly int[] ShuffledTen = { 5, 0, 3, 1, 6, 9, 4, 8, 7, 2 };
    }
}
