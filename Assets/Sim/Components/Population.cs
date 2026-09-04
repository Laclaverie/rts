using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Components
{
    /// <summary>
    /// The port's civilian population (GDD §5.2.2).
    /// </summary>
    /// <remarks>
    /// One per port, like <see cref="Treasury"/>. Commoners are a count rather than entities
    /// because §5.2.2 describes them as anonymous — "hundreds of anonymous bodies with a handful
    /// of named faces inside it" — and the named faces are <see cref="CrewMember"/>s, which are
    /// entities precisely because they are not anonymous.
    /// <para>
    /// This exists because the Phase 2 gate found the flagship system could not reach its own
    /// failure state. Every grievance pressure used to be a count of crew, so when the last crew
    /// member deserted, all three strata went quiet at once and the ladder walked back down to
    /// Calm on an empty port. A port with a population still has somebody to be angry.
    /// </para>
    /// </remarks>
    public struct Population : IComponentData
    {
        /// <summary>Civilians living in the port. They work the buildings and they eat.</summary>
        public int Commoners;

        /// <summary>
        /// Consecutive days on which some commoner went unfed. Reset by a day that feeds
        /// everyone.
        /// </summary>
        /// <remarks>
        /// Departure is driven by a streak rather than by a single bad day on purpose. A port
        /// that misses one meal has a grievance; a port that misses a week has an exodus, and
        /// the difference between those two is the whole of §5.2.3.
        /// </remarks>
        public int HungryDays;

        public void Write(IStateWriter writer)
        {
            writer.Write("commoners", Commoners);
            writer.Write("hungry_days", HungryDays);
        }
    }
}
