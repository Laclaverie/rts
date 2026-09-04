using System;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Crew at the floor leave (GDD §5.4: "at the floor, crew desert").
    /// </summary>
    /// <remarks>
    /// This is the ratchet the cascade needs. Without it every consequence in the economy is a
    /// spring: morale returns when food does, condition returns when upkeep is paid, and a port
    /// always climbs back unless one single blow exceeds its reserves. Shocks then add rather
    /// than compound, and §5.2.3's claim — one is absorbed, several together are not — has no
    /// mechanism behind it.
    /// <para>
    /// Someone who leaves does not come back. Labour lost is production lost, which is income
    /// lost, which is more unpaid wages: the loop closes and turns a bad week into a spiral.
    /// </para>
    /// <para>
    /// At most one a day. A port that loses its whole crew the moment morale dips would be a
    /// cliff rather than a slope, and §5.2.3 wants a decline the player can see coming and act
    /// against — the exits exist precisely so it can be arrested.
    /// </para>
    /// </remarks>
    public sealed class DesertionSystem : ISystem
    {
        public const string SystemId = "Desertion";

        /// <summary>At or below this morale, someone has had enough.</summary>
        public const float MoraleFloor = 0.10f;

        /// <summary>At or below this loyalty, they are no longer yours.</summary>
        public const float LoyaltyFloor = 0.10f;

        /// <summary>Slope, not cliff.</summary>
        public const int MaxPerDay = 1;

        public string Id => SystemId;

        public void Run(World world, in Context ctx)
        {
            BalanceTables balance = ctx.Balance;
            if (balance == null) return;

            ComponentStore<CrewMember> crew = world.Store<CrewMember>();
            if (crew.Count == 0) return;

            int gone = 0;

            // Insertion order, so the same person leaves on the same day in every replay.
            for (int i = 0; i < crew.Count && gone < MaxPerDay; i++)
            {
                CrewMember member = crew.Values[i];
                if (member.Morale > MoraleFloor && member.Loyalty > LoyaltyFloor) continue;

                EntityId leaving = crew.Ids[i];
                int roleIndex = member.RoleIndex;

                // Read before the entity is destroyed: afterwards there is nothing to ask.
                EntityId port = Port.OwnerOf(world, leaving);

                world.DestroyEntity(leaving);
                gone++;

                ctx.Events.Emit(new CrewDeserted
                {
                    Port = port,
                    RoleIndex = roleIndex,
                    Morale = member.Morale,
                    Loyalty = member.Loyalty,
                    Remaining = crew.Count,
                });

                // The store shifted under the loop when the entry was removed; stepping back
                // keeps the next iteration on the entry that moved into this slot.
                i--;
            }
        }
    }

    /// <summary>Someone left. Their labour, and the output that depended on it, went with them.</summary>
    public struct CrewDeserted
    {
        /// <summary>Which city this happened to. One world holds several (§5.3).</summary>
        public EntityId Port;

        public int RoleIndex;
        public float Morale;
        public float Loyalty;
        public int Remaining;
    }
}
