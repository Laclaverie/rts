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
    /// An idle crew member still eats and is still paid. That is the point: labour you are not
    /// using is a cost, not a free reserve.
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
