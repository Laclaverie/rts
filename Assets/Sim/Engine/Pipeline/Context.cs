using RTS.Content.Registries;
using RTS.Sim.Engine.Events;
using RTS.Sim.Engine.Randomness;

namespace RTS.Sim.Engine.Pipeline
{
    /// <summary>
    /// Everything a system is allowed to reach that is not the world itself
    /// (ARCHITECTURE §4).
    /// </summary>
    /// <remarks>
    /// §4's `Config` is <see cref="Balance"/>: the loaded, cross-checked balance tables. It is
    /// nullable because most of the engine's own tests have no content to load, and a system
    /// that needs it will fail loudly on the first access rather than silently on a default.
    /// <para>
    /// A ref struct on purpose: it cannot be captured in a field, a closure, an iterator or
    /// an async method, so the compiler enforces §7.2's ban on coroutines and async holding
    /// sim state across frames.
    /// </para>
    /// </remarks>
    public readonly ref struct Context
    {
        public Context(int day, float dt, EventQueue events, Rng rng = null, BalanceTables balance = null)
        {
            Day = day;
            Dt = dt;
            Events = events;
            Rng = rng;
            Balance = balance;
        }

        /// <summary>The in-game day. Advances at the DayBoundary phase.</summary>
        public readonly int Day;

        /// <summary>Fixed step for the Tick phase. Never frame time (§7.1). Zero at DayBoundary.</summary>
        public readonly float Dt;

        /// <summary>Emit-only. Systems report what they decided; they never subscribe here (§7).</summary>
        public readonly EventQueue Events;

        /// <summary>
        /// The world's seeded generator — the only randomness a system may use. There is no
        /// UnityEngine.Random and no System.Random anywhere in Sim (§7.1); a save is a seed
        /// plus a command log, so a draw from anywhere else corrupts saves rather than merely
        /// flaking a test.
        /// </summary>
        public readonly Rng Rng;

        /// <summary>
        /// All tuned numbers (§5). Immutable and swapped whole on hot reload (§5.4), never
        /// mutated, so a system reading it mid-day cannot see a half-updated table.
        /// </summary>
        public readonly BalanceTables Balance;

        /// <summary>
        /// What is currently being attributed as the cause of emitted events (§6.2). Read-only
        /// to systems: it is set by whoever opened the scope — the dispatcher applying a
        /// command, or the phase runner. A system cannot set it, so it cannot get it wrong.
        /// </summary>
        public CauseId CurrentCause => Events == null ? CauseId.Root : Events.CurrentCause;
    }
}
