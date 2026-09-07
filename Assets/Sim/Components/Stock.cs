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

        /// <summary>
        /// How much of this pile the port will not sell to the passing merchant (GDD §5.5).
        /// </summary>
        /// <remarks>
        /// State rather than content, which is the point of it. <c>keep</c> in <c>goods.csv</c>
        /// is where this starts, and for a neighbour it is where it stays; for the player it is
        /// a decision. Holding grain back is a week of income given up for a week of safety, and
        /// holding rum back is the difference between a good that is sold the day it is distilled
        /// and one that can be shipped somewhere — which is also the difference between drawing
        /// no attention and drawing some (§5.2.1).
        /// <para>
        /// What bounds it is warehouse capacity. That is the whole reason to build one, and the
        /// reason a raid on one would matter.
        /// </para>
        /// </remarks>
        public float Reserve;

        public void Write(IStateWriter writer)
        {
            writer.Write("good", GoodIndex);
            writer.Write("units", Units);
            writer.Write("reserve", Reserve);
        }
    }
}
