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
        public Good(string id, int basePrice, float volatility, float heatPerUnit,
            GoodSupply supply, float keep, int sellPrice)
        {
            Id = id;
            BasePrice = basePrice;
            Volatility = volatility;
            HeatPerUnit = heatPerUnit;
            Supply = supply;
            Keep = keep;
            SellPrice = sellPrice;
        }

        public string Id { get; }

        /// <summary>Coin per unit at a neutral market.</summary>
        public int BasePrice { get; }

        /// <summary>0..1 — how far local supply moves the price.</summary>
        public float Volatility { get; }

        /// <summary>0..1 — attention drawn per unit (§5.2).</summary>
        public float HeatPerUnit { get; }

        public GoodSupply Supply { get; }

        /// <summary>
        /// Units held back before any is sold. Reserves in kind — food against a bad harvest,
        /// timber against a repair you have not needed yet.
        /// </summary>
        public float Keep { get; }

        /// <summary>
        /// What a passing merchant pays per unit. Less than <see cref="BasePrice"/>, because
        /// they have to carry it somewhere and sell it again.
        /// </summary>
        public int SellPrice { get; }

        public bool IsLocal => Supply == GoodSupply.Local;

        public override string ToString() => Id;
    }
}
