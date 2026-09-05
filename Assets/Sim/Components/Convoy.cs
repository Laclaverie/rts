using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Components
{
    /// <summary>
    /// Cargo between two cities (GDD P1, §5.1).
    /// </summary>
    /// <remarks>
    /// <strong>Pillar P1: wealth is cargo, and cargo travels.</strong> "No abstract income tick.
    /// Wealth is cargo, cargo moves along a route on the map, and anything on the map can be
    /// intercepted. Prosperity is therefore <em>exposed</em> by construction."
    /// <para>
    /// A convoy is that exposure made into an entity. It exists for days, it is somewhere, and
    /// it holds goods that belong to nobody's warehouse while it does. Raids, escorts and
    /// blockades all attach to this later; none of them would have anything to attach to if
    /// buying iron simply moved a number.
    /// </para>
    /// <para>
    /// §5.1 wants a round trip measured in days so that committing a route is a real commitment
    /// rather than a toggle. The days come from the distance between the two cities, which is in
    /// <c>ports.csv</c> — so when a map exists it reads the same numbers and cannot disagree
    /// with the journey.
    /// </para>
    /// </remarks>
    public struct Convoy : IComponentData
    {
        /// <summary>Where it set out from.</summary>
        public EntityId Origin;

        /// <summary>Where it is going. Cargo lands in this city's store.</summary>
        public EntityId Destination;

        /// <summary>Index into the goods registry.</summary>
        public int GoodIndex;

        public float Units;

        /// <summary>
        /// Coin paid to <see cref="Origin"/> when the cargo lands, or zero if the deal was
        /// already settled.
        /// </summary>
        /// <remarks>
        /// A purchase pays on dispatch and the goods travel at the buyer's risk — you have
        /// bought them, so losing them is losing yours. A sale travels at the seller's risk and
        /// is paid on arrival, for the same reason from the other end. Either way the party who
        /// stands to lose is the one who owns what is on the water, which is what makes P1's
        /// "prosperity is exposed" true rather than decorative.
        /// </remarks>
        public int CoinOnArrival;

        /// <summary>Days still to sail. It lands when this reaches zero.</summary>
        public int DaysRemaining;

        /// <summary>How long the whole journey is, for a progress readout.</summary>
        public int TotalDays;

        public void Write(IStateWriter writer)
        {
            writer.Write("origin", Origin.Value);
            writer.Write("destination", Destination.Value);
            writer.Write("good", GoodIndex);
            writer.Write("units", Units);
            writer.Write("coin_on_arrival", CoinOnArrival);
            writer.Write("days_remaining", DaysRemaining);
            writer.Write("total_days", TotalDays);
        }
    }
}
