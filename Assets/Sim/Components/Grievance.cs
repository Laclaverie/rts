using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Components
{
    /// <summary>
    /// How angry one stratum is, 0..1 (GDD §5.2.2).
    /// </summary>
    /// <remarks>
    /// One entity per stratum, like a stock pile: the component stays flat, iteration stays
    /// ordered, and a grievance can later belong to a neighbouring port without changing shape.
    /// Neighbour ports run the same system, which is what turns their crises into the player's
    /// opportunities.
    /// </remarks>
    public struct Grievance : IComponentData
    {
        /// <summary>Index into the strata registry, in file order.</summary>
        public int StratumIndex;

        /// <summary>0..1. Rises with what happened today, falls slowly when nothing does.</summary>
        public float Value;

        /// <summary>
        /// The floor this stratum's grievance decays towards. Repression raises it permanently
        /// (§5.2.2): crushing a riot buys quiet now and costs a worse starting point forever.
        /// </summary>
        public float Baseline;

        /// <summary>
        /// Days left in which this stratum keeps its head down after being put down by force.
        /// </summary>
        /// <remarks>
        /// Repression needs a window, not a single subtraction. Grievance is capped at 1.00, so
        /// a port in a spiral re-saturates the day after it is crushed and the relief buys
        /// nothing at all — measured at twelve days to leave a riot either way, which makes the
        /// permanent floor a pure loss and repression a trap. §5.2.2 wants it viable.
        /// <para>
        /// While this is above zero the day's pressures do not land: people are still hungry and
        /// still unpaid, and they still say nothing. Decay continues, so the port genuinely
        /// calms — and when the window closes the grievance comes back on top of a floor that
        /// never leaves.
        /// </para>
        /// </remarks>
        public int CowedDays;

        public void Write(IStateWriter writer)
        {
            writer.Write("stratum", StratumIndex);
            writer.Write("value", Value);
            writer.Write("baseline", Baseline);
            writer.Write("cowed", CowedDays);
        }
    }
}
