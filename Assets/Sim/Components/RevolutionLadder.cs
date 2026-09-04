using RTS.Content.Registries;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Components
{
    /// <summary>
    /// Where the port sits on the ladder (GDD §5.2.2).
    /// </summary>
    /// <remarks>
    /// An explicit state machine, not a threshold read on demand. The rung is durable state:
    /// how long it has been held matters, hysteresis means the way down is not the way up, and
    /// Deposition is terminal. None of that survives being recomputed from grievance each time
    /// somebody asks.
    /// </remarks>
    public struct RevolutionLadder : IComponentData
    {
        public LadderRung Rung;

        /// <summary>Days spent on this rung. An agitator who has stood there a week is news.</summary>
        public int DaysAtRung;

        /// <summary>The angriest stratum when the rung was last set — who is driving this.</summary>
        public int LeadingStratumIndex;

        public void Write(IStateWriter writer)
        {
            writer.Write("rung", (int)Rung);
            writer.Write("days", DaysAtRung);
            writer.Write("leading", LeadingStratumIndex);
        }
    }
}
