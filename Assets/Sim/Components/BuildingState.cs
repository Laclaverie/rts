using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Components
{
    /// <summary>
    /// One built building. The definition it was built from lives in the registry; this is
    /// what is true of this instance today.
    /// </summary>
    public struct BuildingState : IComponentData
    {
        /// <summary>Index into the buildings registry, in file order.</summary>
        public int DefinitionIndex;

        /// <summary>
        /// 0..1. Falls when upkeep goes unpaid and scales output with it — the "buildings
        /// decay, capacity falls, income falls further" link of the cascade (§5.2.3).
        /// </summary>
        public float Condition;

        /// <summary>
        /// Mothballed: costs no upkeep and produces nothing. One of the explicit exits from the
        /// spiral (§5.2.3), and deliberate downsizing is meant to be respectable play.
        /// </summary>
        public bool Mothballed;

        public void Write(IStateWriter writer)
        {
            writer.Write("definition", DefinitionIndex);
            writer.Write("condition", Condition);
            writer.Write("mothballed", Mothballed);
        }
    }
}
