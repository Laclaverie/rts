using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Components
{
    /// <summary>
    /// One port in the world: the player's, or a neighbour's (GDD §5.3, §5.2.2).
    /// </summary>
    /// <remarks>
    /// Neighbours are not a lighter model of a port — they are ports. §5.2.2 is explicit that
    /// the same unrest system runs for them, which is what turns their internal crises into the
    /// player's opportunities, and calls it "the single highest payoff-per-line system in the
    /// document". Making every system take an owner rather than assuming one port is what buys
    /// that: five ports cost what one did.
    /// <para>
    /// <see cref="IsPlayer"/> decides whose numbers the panel shows and whose commands the
    /// player may issue. It is not a difference in simulation — a neighbour starves, riots and
    /// deposes its governor by the same rules, and nothing in <c>Sim</c> checks this flag except
    /// to answer "which one am I looking at".
    /// </para>
    /// </remarks>
    public struct PortState : IComponentData
    {
        /// <summary>Index into the ports registry, in file order.</summary>
        public int DefinitionIndex;

        /// <summary>Whether this is the port the player runs.</summary>
        public bool IsPlayer;

        public void Write(IStateWriter writer)
        {
            writer.Write("definition", DefinitionIndex);
            writer.Write("player", IsPlayer);
        }
    }
}
