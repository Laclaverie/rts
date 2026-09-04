using RTS.Sim.Engine.Events;

namespace RTS.Sim.Engine.Pipeline
{
    /// <summary>
    /// Everything a system is allowed to reach that is not the world itself
    /// (ARCHITECTURE §4).
    /// </summary>
    /// <remarks>
    /// §4 also lists Config and Rng. Those types do not exist yet — they are later Phase 0
    /// bullets — and they are added here as they land rather than stubbed now.
    /// <para>
    /// A ref struct on purpose: it cannot be captured in a field, a closure, an iterator or
    /// an async method, so the compiler enforces §7.2's ban on coroutines and async holding
    /// sim state across frames.
    /// </para>
    /// </remarks>
    public readonly ref struct Context
    {
        public Context(int day, float dt, EventQueue events)
        {
            Day = day;
            Dt = dt;
            Events = events;
        }

        /// <summary>The in-game day. Advances at the DayBoundary phase.</summary>
        public readonly int Day;

        /// <summary>Fixed step for the Tick phase. Never frame time (§7.1). Zero at DayBoundary.</summary>
        public readonly float Dt;

        /// <summary>Emit-only. Systems report what they decided; they never subscribe here (§7).</summary>
        public readonly EventQueue Events;

        /// <summary>
        /// What is currently being attributed as the cause of emitted events (§6.2). Read-only
        /// to systems: it is set by whoever opened the scope — the dispatcher applying a
        /// command, or the phase runner. A system cannot set it, so it cannot get it wrong.
        /// </summary>
        public CauseId CurrentCause => Events == null ? CauseId.Root : Events.CurrentCause;
    }
}
