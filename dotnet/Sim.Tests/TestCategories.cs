namespace RTS.Sim.Tests
{
    /// <summary>
    /// The two kinds of test, from ARCHITECTURE §8.1 and §8.2. Every test fixture carries
    /// exactly one, and the two run separately.
    /// </summary>
    /// <remarks>
    /// The split is not about speed — both are milliseconds today. It is about what a red
    /// test <em>means</em>. A failing unit test says the code is wrong. A failing functional
    /// test usually says the code is fine and the world around it changed: a balance file was
    /// edited, a file moved, a schema drifted. Those are different jobs on different days, and
    /// mixing them makes a red build ambiguous.
    /// <para>
    /// NUnit categories rather than separate projects, because Unity's test runner reads the
    /// same attribute — one convention covers both suites. Split into its own project when
    /// functional tests need infrastructure the unit tests should not carry: replay corpora,
    /// long timeouts, saved sessions (§8.2).
    /// </para>
    /// </remarks>
    public static class TestCategories
    {
        /// <summary>
        /// No I/O, no clock, no environment. Given inputs, assert outputs. If it can fail on
        /// one machine and pass on another, it is not this.
        /// </summary>
        public const string Unit = "Unit";

        /// <summary>
        /// Touches something outside the code under test — the filesystem, a shipped balance
        /// file, later a replay corpus. Asserts that the code and the world still agree.
        /// </summary>
        public const string Functional = "Functional";

        /// <summary>
        /// Real signal, but not trustworthy enough to gate a build: timing and performance
        /// assertions, anything whose result depends on what else the machine was doing.
        /// Excluded from every default run; `tools\test -Flaky` is the only way to see it.
        /// </summary>
        /// <remarks>
        /// This is not a parking space for tests that fail. A test whose <em>claim</em> is
        /// sound but whose <em>mechanism</em> is unreliable should have its mechanism fixed —
        /// the determinism gate's negative test drove divergence from the wall clock, was
        /// flaky, and the answer was static mutable state, not this category.
        /// <para>
        /// Use it only where the unreliability is inherent to the environment. Prefer
        /// <c>Assert.Warn</c> over <c>Assert.That</c> inside one, so even the opt-in run
        /// reports rather than fails.
        /// </para>
        /// </remarks>
        public const string Flaky = "Flaky";
    }
}
