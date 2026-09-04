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

        public void Write(IStateWriter writer)
        {
            writer.Write("stratum", StratumIndex);
            writer.Write("value", Value);
            writer.Write("baseline", Baseline);
        }
    }
}
