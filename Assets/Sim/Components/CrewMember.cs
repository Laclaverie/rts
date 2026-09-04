using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Components
{
    /// <summary>
    /// One named crew member (GDD §5.4). Morale and loyalty are separate on purpose.
    /// </summary>
    /// <remarks>
    /// Morale is how they feel about their circumstances — food, rest, recent events. Loyalty
    /// is how they feel about <em>you</em>. A well-fed crew can still resent you, and a hungry
    /// one can stay. Collapsing the two would remove the most interesting cases, including who
    /// stands where in a revolt (§5.2.2).
    /// </remarks>
    public struct CrewMember : IComponentData
    {
        /// <summary>Index into the crew roles registry, in file order.</summary>
        public int RoleIndex;

        /// <summary>0..1.</summary>
        public float Morale;

        /// <summary>0..1. To you specifically.</summary>
        public float Loyalty;

        public void Write(IStateWriter writer)
        {
            writer.Write("role", RoleIndex);
            writer.Write("morale", Morale);
            writer.Write("loyalty", Loyalty);
        }
    }
}
