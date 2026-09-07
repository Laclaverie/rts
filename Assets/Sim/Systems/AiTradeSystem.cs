using System;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// The other cities trade with each other (GDD §5.3, §5.2.2).
    /// </summary>
    /// <remarks>
    /// Until now four of the five cities were scenery. They produced, ate, grew angry and could
    /// be bought from, but they never did anything, so Ironhold piled up iron nobody would ever
    /// want and Coldwater ran short of bread with a granary three days away. §5.3's premise is
    /// that trade works because ports differ; a world where only one of them trades is a world
    /// where that difference does nothing.
    /// <para>
    /// <strong>One rule.</strong> A city with more of something than it needs sends a parcel to
    /// whichever city has least of it. That is the whole intelligence, and it is enough: the
    /// specialisations in <c>ports.csv</c> already point the traffic — Ironhold has iron and
    /// wants bread, Fairhaven the reverse — so the routes that appear are the ones the content
    /// implies rather than ones a script chose.
    /// </para>
    /// <para>
    /// <strong>Not commands.</strong> The player's trade goes through <see cref="BuyFrom"/> and
    /// <see cref="SellTo"/> so it lands in the command log and replays; a neighbour's does not,
    /// because it is not input. It is a function of world state like every other system, so a
    /// replay reproduces it by re-running rather than by remembering, and the log stays a record
    /// of what the <em>player</em> did (§6.1).
    /// </para>
    /// <para>
    /// <strong>Between neighbours only.</strong> Nobody ships to the player's city. A neighbour
    /// selling you grain would take coin out of your treasury on arrival for a purchase you
    /// never agreed to, and being able to refuse is the difference between a trade and a tax.
    /// What an approach from a neighbour should look like is a §5.6 question — stances, offers,
    /// reputation — and it wants designing rather than falling out of this.
    /// </para>
    /// </remarks>
    public sealed class AiTradeSystem : ISystem
    {
        public const string SystemId = "AiTrade";

        public string Id => SystemId;

        public void Run(World world, in Context ctx)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (ctx.Balance == null) return;

            TradeAiRules rules = ctx.Balance.TradeAi;
            if (rules.Parcel <= 0f) return;

            EntityId player = Port.Player(world);
            ReadOnlySpan<EntityId> ports = Port.All(world);

            for (int i = 0; i < ports.Length; i++)
            {
                EntityId seller = ports[i];
                if (seller == player) continue;
                if (Convoys(world, seller) >= rules.ConvoysPerCity) continue;

                Ship(world, seller, player, ports, rules, ctx);
            }
        }

        /// <summary>
        /// Sends one parcel of whatever this city can most afford to lose, to whoever has least.
        /// </summary>
        private static void Ship(World world, EntityId seller, EntityId player,
            ReadOnlySpan<EntityId> ports, TradeAiRules rules, in Context ctx)
        {
            BalanceTables balance = ctx.Balance;

            int bestGood = -1;
            float bestSurplus = 0f;

            for (int good = 0; good < balance.Goods.Count; good++)
            {
                float spare = Port.UnitsOf(world, seller, good) - balance.Goods[good].Keep;
                if (spare < rules.Spare) continue;
                if (spare <= bestSurplus) continue;

                bestSurplus = spare;
                bestGood = good;
            }

            if (bestGood < 0) return;

            // Whoever has least of it, which is the city that will feel the delivery most. Ties
            // go to the first in creation order, so the same world always makes the same choice.
            EntityId buyer = EntityId.None;
            float leanest = float.MaxValue;

            for (int i = 0; i < ports.Length; i++)
            {
                EntityId candidate = ports[i];
                if (candidate == seller || candidate == player) continue;

                float held = Port.UnitsOf(world, candidate, bestGood);
                if (held >= leanest) continue;

                leanest = held;
                buyer = candidate;
            }

            if (buyer.IsNone) return;

            // Only if it is actually short. Shipping to a city that has plenty is traffic for
            // its own sake, and the map would fill with ships carrying coals to Newcastle.
            if (leanest >= balance.Goods[bestGood].Keep) return;

            int payment = (int)Math.Floor(balance.Goods[bestGood].SellPrice * rules.Parcel);
            if (!Port.HasTreasury(world, buyer) || Port.Treasury(world, buyer).Coin < payment)
                return;

            Port.Take(world, seller, bestGood, rules.Parcel);

            // Paid on arrival, at the seller's risk, exactly as the player's own sales are. The
            // same convoys, the same crossing times, the same raiders.
            ConvoySystem.Dispatch(world, balance, seller, buyer, bestGood, rules.Parcel,
                coinOnArrival: payment, owner: seller, ctx);
        }

        /// <summary>How many convoys a city already has at sea.</summary>
        public static int Convoys(World world, EntityId port)
        {
            ComponentStore<Convoy> convoys = world.Store<Convoy>();
            int count = 0;

            for (int i = 0; i < convoys.Count; i++)
                if (Port.BelongsTo(world, convoys.Ids[i], port)) count++;

            return count;
        }
    }
}
