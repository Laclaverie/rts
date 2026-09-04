using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Sells the surplus, and buys food the port cannot grow. Its only trade until routes exist.
    /// </summary>
    /// <remarks>
    /// Deliberately the smallest thing that makes the Phase 1 gate answerable. Without income
    /// the port is bankrupt on day seven of every run regardless of play, and "a single shock
    /// is always survivable" (§5.2.3) has nothing to be survivable against.
    /// <para>
    /// A passing merchant, not a market: one fixed price per good, no local supply effect, no
    /// neighbours, no routes. §5.3's real trade — ports differing, prices moving, a
    /// differential worth protecting — is the economic game and arrives with routes. This is
    /// the floor beneath it.
    /// </para>
    /// <para>
    /// Runs last at the day boundary, at the position §4.2's own sketch gives Market. Today's
    /// production is therefore sold today, but the coin arrives after today's wages were paid:
    /// income always funds tomorrow, which is precisely what makes reserves the real resource.
    /// </para>
    /// </remarks>
    public sealed class MarketSystem : ISystem
    {
        public const string SystemId = "Market";

        public string Id => SystemId;

        /// <summary>
        /// Goods the merchant will sell the port as well as buy from it.
        /// </summary>
        /// <remarks>
        /// Only what a passing ship would plausibly carry to a hungry colony, which for now is
        /// food. Buying timber or iron would let a port skip having industry at all, and the
        /// point of §5.5's buildings is that what the port makes is a decision.
        /// </remarks>
        public const string BuyableGood = "food";

        public void Run(World world, in Context ctx)
        {
            BalanceTables balance = ctx.Balance;
            if (balance == null || !Port.HasTreasury(world)) return;

            Buy(world, balance, ctx);

            int earned = 0;
            int unitsSold = 0;

            for (int goodIndex = 0; goodIndex < balance.Goods.Count; goodIndex++)
            {
                Good good = balance.Goods[goodIndex];
                if (good.SellPrice <= 0) continue;

                float held = Port.UnitsOf(world, goodIndex);
                float above = held - good.Keep;
                if (above < 1f) continue;

                // Whole units only. Coin is an integer, and half a barrel is not a sale.
                int sellable = (int)above;

                Port.Take(world, goodIndex, sellable);
                earned += sellable * good.SellPrice;
                unitsSold += sellable;
            }

            if (earned <= 0) return;

            ref Treasury treasury = ref Port.Treasury(world);

            // Income does not service arrears. That was tried and it was wrong: paying the
            // backlog first left nothing for today's wages, which added to the backlog, so a
            // single missed payday became permanent. §5.2.3 is explicit that one bad event is
            // absorbed and recovered from *always* — collapse comes from correlated shocks, not
            // from one.
            //
            // Arrears is therefore a record of what went unpaid, not a debt that eats income.
            // Phase 2 reads it as grievance (§5.2.2); whether back pay can be settled, and at
            // what price in loyalty, is a design decision that belongs with unrest.
            treasury.Coin += earned;

            ctx.Events.Emit(new GoodsSold { Coin = earned, Units = unitsSold });
        }

        /// <summary>
        /// Tops the food store back up to what the port wants to keep, as far as coin allows.
        /// </summary>
        /// <remarks>
        /// This exists because a port with a population to feed made reserves meaningless
        /// without it. The Phase 1 gate rests on "one shock is survivable, three are not, and
        /// the difference is the slack you kept" (§5.2.3) — but once commoners eat, the binding
        /// constraint is food rather than coin, and a treasury of four hundred died exactly as
        /// fast as one of ninety. Slack the player cannot spend is not slack.
        /// <para>
        /// Buying at <c>base_price</c> and selling at <c>sell_price</c> is a wide spread — four
        /// coin against one for food — and deliberately so. Importing what you should be growing
        /// is meant to be an emergency, not a business model, and a port that answers every
        /// famine by buying its way out will bleed to death slowly instead of quickly.
        /// </para>
        /// <para>
        /// Runs before selling, so a port cannot fund today's food out of today's sales. Buying
        /// is spending yesterday's money, which keeps reserves the resource that matters.
        /// </para>
        /// </remarks>
        private static void Buy(World world, BalanceTables balance, in Context ctx)
        {
            int goodIndex = ConsumptionSystem.IndexOf(balance, BuyableGood);
            if (goodIndex < 0) return;

            Good good = balance.Goods[goodIndex];
            if (good.BasePrice <= 0) return;

            float held = Port.UnitsOf(world, goodIndex);
            float short_ = good.Keep - held;
            if (short_ < 1f) return;

            ref Treasury treasury = ref Port.Treasury(world);
            if (treasury.Coin < good.BasePrice) return;

            int affordable = treasury.Coin / good.BasePrice;
            int wanted = (int)short_;
            int bought = affordable < wanted ? affordable : wanted;
            if (bought <= 0) return;

            treasury.Coin -= bought * good.BasePrice;
            Port.Add(world, goodIndex, bought);

            ctx.Events.Emit(new GoodsBought
            {
                Coin = bought * good.BasePrice,
                Units = bought,
                Good = good.Id,
            });
        }
    }
}
