using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Components
{
    /// <summary>
    /// How much attention a port's wealth is drawing, 0..1 (GDD §5.2.1).
    /// </summary>
    /// <remarks>
    /// <strong>The other half of the design's central dilemma.</strong> §5.2 puts two pressures
    /// against each other: Unrest comes from inside and is driven by how you got your wealth,
    /// Heat comes from outside and is driven by the wealth itself. Neither is a wave and neither
    /// is scripted — both are read off world state.
    /// <para>
    /// It replaces the 2021 draft's bonuses for weak cities and penalties for strong ones, which
    /// punished playing well and read as arbitrary. Same anti-snowball function, made diegetic:
    /// fat convoys and full warehouses are <em>visible</em>, and what is visible is worth taking.
    /// The player is never punished for succeeding, they are given a consequence to manage.
    /// </para>
    /// <para>
    /// One per port, like the treasury. Neighbours draw attention on the same terms, which is
    /// what will make a rich rival's convoys worth watching when stances exist (§5.6).
    /// </para>
    /// </remarks>
    public struct Heat : IComponentData
    {
        /// <summary>0..1. Rises with what is on show, falls when there is less to see.</summary>
        public float Value;

        /// <summary>
        /// What today's wealth was worth in attention, before decay.
        /// </summary>
        /// <remarks>
        /// Kept so a readout can say <em>why</em> Heat is where it is rather than only what it
        /// is. §5.2.1 insists Heat must be readable — a pressure the player cannot see the cause
        /// of is the arbitrary penalty it was written to replace.
        /// </remarks>
        public float Drawn;

        /// <summary>Days since anybody raided this port's shipping.</summary>
        /// <remarks>
        /// Not a cooldown. It is here so the feed and a readout can tell the player that quiet
        /// is holding, which is the difference between a lull they can act on and a silence they
        /// cannot read.
        /// </remarks>
        public int DaysSinceRaid;

        public void Write(IStateWriter writer)
        {
            writer.Write("heat", Value);
            writer.Write("drawn", Drawn);
            writer.Write("since_raid", DaysSinceRaid);
        }
    }
}
