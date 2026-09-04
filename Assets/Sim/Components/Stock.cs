using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Components
{
    /// <summary>
    /// A pile of one good. One entity per good, rather than one component holding all of them.
    /// </summary>
    /// <remarks>
    /// A component is a struct, so a component holding every good would need an array — a
    /// reference type shared between copies, and awkward to write out deterministically. One
    /// entity per pile keeps the component flat, keeps iteration ordered, and means a pile can
    /// later belong to a warehouse, a ship or a port without changing its shape.
    /// <para>
    /// <see cref="GoodIndex"/> indexes the goods registry in file order, which is stable across
    /// runs and cheap to serialise. The good's id is not stored: it would be the same string
    /// repeated in every save.
    /// </para>
    /// </remarks>
    public struct Stock : IComponentData
    {
        public int GoodIndex;

        public float Units;

        public void Write(IStateWriter writer)
        {
            writer.Write("good", GoodIndex);
            writer.Write("units", Units);
        }
    }
}
