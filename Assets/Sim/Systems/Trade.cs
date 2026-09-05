using System;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Buy goods from another city. Coin now, cargo in days (GDD §5.3, P1).
    /// </summary>
    /// <remarks>
    /// The player's answer to a good they cannot make. Saltmarsh has no mine and a workshop that
    /// wants iron every day; Ironhold has iron and not enough bread. This is the sentence that
    /// connects them.
    /// <para>
    /// Paid on dispatch, so the cargo crosses at the buyer's risk. You have bought it — losing it
    /// is losing yours, which is what P1 means by prosperity being exposed.
    /// </para>
    /// </remarks>
    public sealed class BuyFrom : ICommand
    {
        public BuyFrom(EntityId seller, int goodIndex, float units)
        {
            Seller = seller;
            GoodIndex = goodIndex;
            Units = units;
        }

        public EntityId Seller { get; }

        public int GoodIndex { get; }

        public float Units { get; }

        public override string ToString() => $"BuyFrom({Seller.Value}, {Units:0.#})";
    }

    /// <summary>
    /// Sell goods to another city. Cargo now, coin when it lands.
    /// </summary>
    /// <remarks>
    /// The other half, and the one that turns Saltmarsh's spare bread into somebody else's
    /// problem solved. Paid on arrival, so the crossing is at the seller's risk: goods that
    /// never arrive were never sold.
    /// </remarks>
    public sealed class SellTo : ICommand
    {
        public SellTo(EntityId buyer, int goodIndex, float units)
        {
            Buyer = buyer;
            GoodIndex = goodIndex;
            Units = units;
        }

        public EntityId Buyer { get; }

        public int GoodIndex { get; }

        public float Units { get; }

        public override string ToString() => $"SellTo({Buyer.Value}, {Units:0.#})";
    }

    /// <summary>
    /// Takes the coin and the goods, and puts a convoy on the water.
    /// </summary>
    /// <remarks>
    /// Prices are the same as the passing merchant's: <c>base_price</c> to buy, <c>sell_price</c>
    /// to sell. So a route offers no price advantage yet — what it offers is <em>access</em>, and
    /// for iron that is everything, because no merchant carries it and Saltmarsh has no mine.
    /// <para>
    /// §5.3's real trade, where ports differ in price and finding the differential is the game,
    /// needs local supply to move prices. That is its own piece of work; this is the ship.
    /// </para>
    /// </remarks>
    public sealed class BuyFromHandler : ICommandHandler
    {
        public Type CommandType => typeof(BuyFrom);

        public CommandRejection Validate(ICommand command, World world, in Context ctx)
        {
            var buy = (BuyFrom)command;
            BalanceTables balance = ctx.Balance;

            if (balance == null) return CommandRejection.Unavailable;
            if (buy.Units <= 0f) return CommandRejection.InvalidTarget;
            if (buy.GoodIndex < 0 || buy.GoodIndex >= balance.Goods.Count)
                return CommandRejection.InvalidTarget;

            if (!world.IsAlive(buy.Seller)) return CommandRejection.TargetGone;
            if (!world.Has<PortState>(buy.Seller)) return CommandRejection.InvalidTarget;

            EntityId buyer = Port.Player(world);
            if (buy.Seller == buyer) return CommandRejection.InvalidTarget;

            // What the seller can spare, not what it holds. A city that sold the grain it was
            // going to eat would starve to fill an order, which no city would agree to.
            Good good = balance.Goods[buy.GoodIndex];
            float spare = Port.UnitsOf(world, buy.Seller, buy.GoodIndex) - good.Keep;
            if (spare < buy.Units) return CommandRejection.NotYet;

            if (!Port.HasTreasury(world, buyer)) return CommandRejection.Unavailable;
            if (Port.Treasury(world, buyer).Coin < Cost(good, buy.Units))
                return CommandRejection.Unavailable;

            return CommandRejection.None;
        }

        public void Apply(ICommand command, World world, in Context ctx)
        {
            var buy = (BuyFrom)command;
            Good good = ctx.Balance.Goods[buy.GoodIndex];

            EntityId buyer = Port.Player(world);
            int cost = Cost(good, buy.Units);

            Port.Take(world, buy.Seller, buy.GoodIndex, buy.Units);
            Port.Treasury(world, buyer).Coin -= cost;
            Port.Treasury(world, buy.Seller).Coin += cost;

            // Owned by the buyer: they have paid, so what is on the water is theirs to lose.
            ConvoySystem.Dispatch(world, ctx.Balance, buy.Seller, buyer, buy.GoodIndex,
                buy.Units, coinOnArrival: 0, owner: buyer, ctx);
        }

        /// <summary>What a quantity costs at the seller's gate.</summary>
        public static int Cost(Good good, float units) =>
            (int)Math.Ceiling(good.BasePrice * units);
    }

    /// <summary>
    /// Sends goods to another city, to be paid for when they land.
    /// </summary>
    public sealed class SellToHandler : ICommandHandler
    {
        public Type CommandType => typeof(SellTo);

        public CommandRejection Validate(ICommand command, World world, in Context ctx)
        {
            var sell = (SellTo)command;
            BalanceTables balance = ctx.Balance;

            if (balance == null) return CommandRejection.Unavailable;
            if (sell.Units <= 0f) return CommandRejection.InvalidTarget;
            if (sell.GoodIndex < 0 || sell.GoodIndex >= balance.Goods.Count)
                return CommandRejection.InvalidTarget;

            if (!world.IsAlive(sell.Buyer)) return CommandRejection.TargetGone;
            if (!world.Has<PortState>(sell.Buyer)) return CommandRejection.InvalidTarget;

            EntityId seller = Port.Player(world);
            if (sell.Buyer == seller) return CommandRejection.InvalidTarget;

            // The player may sell into their own reserve if they choose — it is their city and
            // their judgement. What they may not do is sell what they do not have.
            if (Port.UnitsOf(world, seller, sell.GoodIndex) < sell.Units)
                return CommandRejection.NotYet;

            return CommandRejection.None;
        }

        public void Apply(ICommand command, World world, in Context ctx)
        {
            var sell = (SellTo)command;
            Good good = ctx.Balance.Goods[sell.GoodIndex];

            EntityId seller = Port.Player(world);
            int payment = (int)Math.Floor(good.SellPrice * sell.Units);

            Port.Take(world, seller, sell.GoodIndex, sell.Units);

            // Owned by the seller: unpaid goods on the water are still theirs.
            ConvoySystem.Dispatch(world, ctx.Balance, seller, sell.Buyer, sell.GoodIndex,
                sell.Units, coinOnArrival: payment, owner: seller, ctx);
        }
    }
}
