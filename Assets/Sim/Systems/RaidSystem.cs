using System;
using System.Collections.Generic;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Somebody takes what is on the water (GDD P1, §5.2.1).
    /// </summary>
    /// <remarks>
    /// <strong>P1's last unkept promise.</strong> "Wealth is cargo, cargo moves along a route on
    /// the map, and anything on the map can be intercepted. Prosperity is therefore exposed by
    /// construction." Convoys have been sailing since Phase 4 and nothing has ever taken one, so
    /// the exposure was a claim rather than a fact and every route was a delay with no risk in it.
    /// <para>
    /// The chance is read off Heat, which is read off what the port has on show. Nothing is
    /// scripted and nothing escalates on a timer: a port that ships bread is never raided, and a
    /// port that ships spice is raided because of what it chose to carry. §5.2.1's whole point is
    /// that this is a consequence to manage rather than a punishment for succeeding.
    /// </para>
    /// <para>
    /// Runs before the convoys sail, so a raid catches a ship at sea rather than one that landed
    /// this morning. A cargo taken from a convoy on its final day would be a delivery the player
    /// watched arrive and then lose.
    /// </para>
    /// </remarks>
    public sealed class RaidSystem : ISystem
    {
        public const string SystemId = "Raid";

        private readonly List<EntityId> _emptied = new List<EntityId>();

        public string Id => SystemId;

        public void Run(World world, in Context ctx)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (ctx.Balance == null || ctx.Rng == null) return;

            ComponentStore<Convoy> convoys = world.Store<Convoy>();
            if (convoys.Count == 0) return;

            HeatRules rules = ctx.Balance.Heat;
            _emptied.Clear();

            // Collected first: taking cargo can destroy a convoy, and the store shifts under an
            // index-based loop when it does.
            var afloat = new List<EntityId>();
            for (int i = 0; i < convoys.Count; i++) afloat.Add(convoys.Ids[i]);

            for (int i = 0; i < afloat.Count; i++) Try(world, afloat[i], rules, ctx);

            for (int i = 0; i < _emptied.Count; i++)
                if (world.IsAlive(_emptied[i])) world.DestroyEntity(_emptied[i]);
        }

        private void Try(World world, EntityId id, HeatRules rules, in Context ctx)
        {
            if (!world.TryGet(id, out Convoy convoy) || convoy.Units <= 0f) return;

            EntityId owner = Port.OwnerOf(world, id);
            float heat = HeatSystem.Of(world, owner);
            if (heat <= 0f) return;

            bool escorted = EscortSystem.IsEscorting(world, owner);
            float chance = heat * rules.RaidChanceAtFull;
            if (escorted) chance *= rules.EscortRaidMultiplier;

            // Drawn every day for every convoy whether or not it matters, so that adding or
            // removing an escort cannot change how many numbers come out of the generator and
            // silently move every later roll in the run (§7.1).
            if (!ctx.Rng.NextBool(chance)) return;

            float taken = convoy.Units * rules.RaidTakes;
            if (taken <= 0f) return;

            ref Convoy hold = ref world.Store<Convoy>().GetRef(id);
            hold.Units -= taken;

            // A sale is paid on arrival for what arrives. Anything else would have the buyer
            // paying for barrels somebody else is drinking.
            if (hold.CoinOnArrival > 0)
                hold.CoinOnArrival = (int)Math.Floor(hold.CoinOnArrival * (1f - rules.RaidTakes));

            if (hold.Units <= 0.01f) _emptied.Add(id);

            EntityId heatEntity = HeatSystem.HeatOf(world, owner);
            if (!heatEntity.IsNone) world.Store<Heat>().GetRef(heatEntity).DaysSinceRaid = 0;

            ctx.Events.Emit(new ConvoyRaided
            {
                Port = owner,
                GoodIndex = convoy.GoodIndex,
                Taken = taken,
                Left = hold.Units,
                Escorted = escorted,
                DaysOut = convoy.DaysRemaining,
            });
        }
    }

    /// <summary>Somebody took part of a cargo.</summary>
    public struct ConvoyRaided
    {
        /// <summary>Whose convoy it was — the city that bore the risk.</summary>
        public EntityId Port;

        public int GoodIndex;
        public float Taken;

        /// <summary>What is still in the hold. Zero means the convoy is gone.</summary>
        public float Left;

        public bool Escorted;
        public int DaysOut;
    }

    /// <summary>
    /// Stand escorts, or stand them down (GDD §5.2.1, §5.2).
    /// </summary>
    /// <remarks>
    /// A standing posture rather than a decision per shipment, because that is what §5.2's table
    /// describes — "escort convoys" sits beside "fortify" and "hire guards" as a way of living,
    /// not as a click. It also keeps the orders list one line instead of doubling every trade
    /// route on it.
    /// </remarks>
    public sealed class SetEscort : ICommand
    {
        public SetEscort(bool escorting) => Escorting = escorting;

        public bool Escorting { get; }

        public override string ToString() => Escorting ? "SetEscort(on)" : "SetEscort(off)";
    }

    /// <summary>Charges for the escorts that are standing.</summary>
    /// <remarks>
    /// <strong>This is where the two pressures meet.</strong> §5.2 says reducing Heat raises
    /// Unrest, and here is the mechanism with nothing invented for it: escorts are paid in coin,
    /// coin spent is coin that is not in the treasury on payday, an unpaid wage feeds grievance
    /// the same day (§5.2.3), and grievance is the ladder. No tax system is needed, because coin
    /// was always the thing both pressures were competing for.
    /// <para>
    /// Charged per convoy per day, so the bill is the size of what is being protected. A port
    /// running one small route pays almost nothing; a port with four fat convoys out pays like
    /// one.
    /// </para>
    /// <para>
    /// Runs before Wages, so the escort is paid out of the same morning's treasury the crew are
    /// about to be paid from. Charging after would let a port pay everyone and then discover it
    /// owed for the guard, which hides the choice being made.
    /// </para>
    /// </remarks>
    public sealed class EscortSystem : ISystem
    {
        public const string SystemId = "Escort";

        public string Id => SystemId;

        public void Run(World world, in Context ctx)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (ctx.Balance == null) return;

            int perDay = ctx.Balance.Heat.EscortCoinPerDay;
            if (perDay <= 0) return;

            ReadOnlySpan<EntityId> ports = Port.All(world);
            for (int i = 0; i < ports.Length; i++)
            {
                EntityId port = ports[i];
                if (!IsEscorting(world, port)) continue;

                int convoys = Convoys(world, port);
                if (convoys == 0) continue;

                int bill = perDay * convoys;
                if (!Port.HasTreasury(world, port)) continue;

                ref Treasury treasury = ref Port.Treasury(world, port);

                // Whatever can be paid is paid. An escort that vanished the day the treasury ran
                // short would protect a port exactly when it could least afford to lose a cargo,
                // and a port that went into debt for it would have a second kind of arrears that
                // nothing else in the game has.
                int paid = bill > treasury.Coin ? treasury.Coin : bill;
                treasury.Coin -= paid;

                ctx.Events.Emit(new EscortPaid
                {
                    Port = port, Convoys = convoys, Coin = paid, Short = bill - paid,
                });
            }
        }

        /// <summary>Whether a city is paying for escorts.</summary>
        public static bool IsEscorting(World world, EntityId port)
        {
            if (port.IsNone || !world.TryGet(port, out PortState state)) return false;

            return state.Escorting;
        }

        private static int Convoys(World world, EntityId port)
        {
            ComponentStore<Convoy> convoys = world.Store<Convoy>();
            int count = 0;

            for (int i = 0; i < convoys.Count; i++)
                if (Port.BelongsTo(world, convoys.Ids[i], port)) count++;

            return count;
        }
    }

    /// <summary>The guard was paid, or as much of them as there was coin for.</summary>
    public struct EscortPaid
    {
        public EntityId Port;
        public int Convoys;
        public int Coin;

        /// <summary>What could not be paid.</summary>
        public int Short;
    }

    /// <summary>Turns escorts on and off.</summary>
    public sealed class SetEscortHandler : ICommandHandler
    {
        public Type CommandType => typeof(SetEscort);

        public CommandRejection Validate(ICommand command, World world, in Context ctx)
        {
            var set = (SetEscort)command;
            EntityId port = Port.Player(world);

            if (!world.Has<PortState>(port)) return CommandRejection.Unavailable;
            if (EscortSystem.IsEscorting(world, port) == set.Escorting)
                return CommandRejection.AlreadyInState;

            return CommandRejection.None;
        }

        public void Apply(ICommand command, World world, in Context ctx)
        {
            var set = (SetEscort)command;
            EntityId port = Port.Player(world);

            world.Store<PortState>().GetRef(port).Escorting = set.Escorting;

            ctx.Events.Emit(new EscortStanding { Port = port, Escorting = set.Escorting });
        }
    }

    /// <summary>Escorts were stood up, or stood down.</summary>
    public struct EscortStanding
    {
        public EntityId Port;
        public bool Escorting;
    }
}
