namespace RTS.Content.Registries
{
    /// <summary>Where a good comes from.</summary>
    public enum GoodSupply
    {
        /// <summary>Produced somewhere in the port. Must have a producing building.</summary>
        Local = 0,

        /// <summary>
        /// Arrives only by trade, so it has no local producer — and, for a pure trade good,
        /// no local consumer either (GDD §5.3).
        /// </summary>
        ImportOnly = 1,
    }

    /// <summary>
    /// One tradable commodity (GDD §5.3). Coin is not one of these; it is the currency.
    /// </summary>
    public sealed class Good : IHasId
    {
        public Good(string id, int basePrice, float volatility, float heatPerUnit, GoodSupply supply)
        {
            Id = id;
            BasePrice = basePrice;
            Volatility = volatility;
            HeatPerUnit = heatPerUnit;
            Supply = supply;
        }

        public string Id { get; }

        /// <summary>Coin per unit at a neutral market.</summary>
        public int BasePrice { get; }

        /// <summary>0..1 — how far local supply moves the price.</summary>
        public float Volatility { get; }

        /// <summary>0..1 — attention drawn per unit (§5.2).</summary>
        public float HeatPerUnit { get; }

        public GoodSupply Supply { get; }

        public bool IsLocal => Supply == GoodSupply.Local;

        public override string ToString() => Id;
    }
}
