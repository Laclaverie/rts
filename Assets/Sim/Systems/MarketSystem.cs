using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Sells the surplus. The port's only income until routes exist.
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

        public void Run(World world, in Context ctx)
        {
            BalanceTables balance = ctx.Balance;
            if (balance == null || !Port.HasTreasury(world)) return;

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
    }
}
