namespace RTS.Sim.Engine.Pipeline
{
    /// <summary>
    /// Everything a system is allowed to reach that is not the world itself
    /// (ARCHITECTURE §4).
    /// </summary>
    /// <remarks>
    /// §4 also lists Config, Events and Rng. Those types do not exist yet — they are later
    /// Phase 0 bullets — and they are added here as they land rather than stubbed now.
    /// <para>
    /// A ref struct on purpose: it cannot be captured in a field, a closure, an iterator or
    /// an async method, so the compiler enforces §7.2's ban on coroutines and async holding
    /// sim state across frames.
    /// </para>
    /// </remarks>
    public readonly ref struct Context
    {
        public Context(int day, float dt)
        {
            Day = day;
            Dt = dt;
        }

        /// <summary>The in-game day. Advances at the DayBoundary phase.</summary>
        public readonly int Day;

        /// <summary>Fixed step for the Tick phase. Never frame time (§7.1). Zero at DayBoundary.</summary>
        public readonly float Dt;
    }
}
