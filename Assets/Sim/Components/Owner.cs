using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Components
{
    /// <summary>
    /// Which port an entity belongs to (GDD §5.3).
    /// </summary>
    /// <remarks>
    /// Every crew member, building, stock pile, treasury, town and ladder is owned by exactly
    /// one port. Before this there was one port and ownership was implicit — "the treasury"
    /// meant the only one — and every system reached for <c>Values[0]</c>.
    /// <para>
    /// A component rather than a field on each of those types, because ownership is the same
    /// question for all of them and the answer belongs in one place (C2). It also means a
    /// system can ask "whose is this?" without knowing what "this" is.
    /// </para>
    /// <para>
    /// <strong>Trade only works because ports differ</strong> (§5.3). Ports that each produce
    /// some goods and demand others are what make a price differential exist, and finding and
    /// protecting one is the economic game. None of that is possible while the world holds a
    /// single port, which is why this is the first thing Phase 4 needs.
    /// </para>
    /// </remarks>
    public struct Owner : IComponentData
    {
        /// <summary>The port entity that owns this.</summary>
        public EntityId Port;

        public void Write(IStateWriter writer) => writer.Write("port", Port.Value);
    }
}
