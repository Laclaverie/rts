using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Components
{
    /// <summary>
    /// Which building a crew member works. Absent means idle.
    /// </summary>
    /// <remarks>
    /// Assignment exists because a port-wide labour pool made the cascade lie. With a pool,
    /// staffing was total effort over total producers clamped to 1, so a port with more crew
    /// than it needed sat at the cap: surplus crew drew wages and added nothing, and losing one
    /// made the port *richer*. That inverts §5.2.3's labour link, where fewer crew must mean
    /// less production and less income.
    /// <para>
    /// Named individuals working named buildings is also what §5.4 describes, and what the
    /// <c>AssignCrew</c> command of §6 assumes.
    /// </para>
    /// <para>
    /// An idle crew member still eats and is still paid, and that is the point — but the point
    /// is that idle labour is <em>priced</em>, not that it is a mistake. Hiring more people than
    /// there is work for is a real position: a skilled hand a rival wanted, a specialist for the
    /// building that is not finished yet, loyalty bought before it is needed (§5.4). The wages
    /// make it a decision instead of free.
    /// </para>
    /// </remarks>
    public struct Assignment : IComponentData
    {
        /// <summary>The building worked. <see cref="EntityId.None"/> is idle.</summary>
        public EntityId Building;

        public bool IsIdle => Building.IsNone;

        public void Write(IStateWriter writer) => writer.Write("building", Building.Value);
    }
}
