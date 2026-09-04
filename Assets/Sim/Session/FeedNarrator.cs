using RTS.Content.Registries;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Events;
using RTS.Sim.Systems;

namespace RTS.Sim.Session
{
    /// <summary>
    /// Turns events and commands into sentences a person can read.
    /// </summary>
    /// <remarks>
    /// In <c>Sim</c> rather than in a panel, so that what the player is told is a property of
    /// the game rather than of whatever is drawing it (ARCHITECTURE §2.2). A console harness
    /// and a Unity panel say the same words, and the wording has a headless test behind it.
    /// <para>
    /// Every line names a number. "Wages went unpaid" is a mood; "7 wages unpaid, 12 short" is
    /// something a player can act on, and the difference is the whole of §3.2 — a game that
    /// expects thought rather than reflexes has to give the player something to think with.
    /// </para>
    /// <para>
    /// An unknown payload falls back to its type name rather than being dropped. A feed that
    /// silently omits what it does not recognise would hide exactly the new system whose
    /// behaviour nobody has checked yet.
    /// </para>
    /// </remarks>
    public static class FeedNarrator
    {
        /// <summary>Describes one drained event, or returns false if it is not worth a line.</summary>
        public static bool TryDescribe(in Envelope envelope, BalanceTables balance,
            out string text, out FeedImportance importance)
        {
            importance = FeedImportance.Detail;

            if (envelope.TryGet(out WagesPaid paid))
            {
                text = $"paid {paid.Crew} crew, {paid.Coin} coin";
                return true;
            }

            if (envelope.TryGet(out WagesUnpaid unpaid))
            {
                importance = FeedImportance.Alarming;
                text = $"{unpaid.Crew} went unpaid — owed {unpaid.Owed}, paid {unpaid.Paid}";
                return true;
            }

            if (envelope.TryGet(out FoodShortfall hunger))
            {
                importance = FeedImportance.Alarming;
                text = $"{hunger.Crew} crew went hungry";
                return true;
            }

            if (envelope.TryGet(out CommonersWentHungry town))
            {
                importance = FeedImportance.Alarming;
                text = town.ConsecutiveDays > 1
                    ? $"{town.Commoners} townsfolk hungry, {town.ConsecutiveDays} days running"
                    : $"{town.Commoners} townsfolk went hungry";
                return true;
            }

            if (envelope.TryGet(out CommonersLeft left))
            {
                importance = FeedImportance.Alarming;
                text = $"{left.Left} left the port for good — {left.Remaining} remain";
                return true;
            }

            if (envelope.TryGet(out CrewDeserted deserted))
            {
                importance = FeedImportance.Alarming;
                text = $"{Role(balance, deserted.RoleIndex)} deserted — {deserted.Remaining} crew left";
                return true;
            }

            if (envelope.TryGet(out LadderMoved moved))
            {
                bool worse = moved.To > moved.From;
                importance = worse ? FeedImportance.Alarming : FeedImportance.Notable;
                // The grievance is in the line because the rung alone does not say how close
                // the next one is, and that is the whole of the decision the player faces.
                string grievance = (moved.Grievance * 100f).ToString("0") + "%";
                text = worse
                    ? $"unrest rose to {moved.To} — {Stratum(balance, moved.LeadingStratumIndex)} at {grievance}"
                    : $"unrest fell to {moved.To} — {grievance}";
                return true;
            }

            if (envelope.TryGet(out RiotSuppressed suppressed))
            {
                importance = FeedImportance.Notable;
                text = $"riot put down, {suppressed.Harshness.ToString().ToLowerInvariant()} — " +
                       $"{suppressed.Crew} crew lost loyalty";
                return true;
            }

            if (envelope.TryGet(out PropertyDamaged damaged))
            {
                importance = FeedImportance.Alarming;
                text = $"{damaged.Buildings} buildings damaged in the {damaged.Rung.ToString().ToLowerInvariant()}";
                return true;
            }

            if (envelope.TryGet(out BuildingDerelict derelict))
            {
                importance = FeedImportance.Alarming;
                text = $"the {Building(balance, derelict.DefinitionIndex)} fell derelict";
                return true;
            }

            if (envelope.TryGet(out BuildingMothballed mothballed))
            {
                importance = FeedImportance.Notable;
                text = mothballed.Mothballed
                    ? $"a building was shut, {mothballed.CrewReleased} crew released"
                    : "a building was reopened";
                return true;
            }

            if (envelope.TryGet(out UpkeepUnpaid upkeep))
            {
                importance = FeedImportance.Alarming;
                text = $"upkeep short by {upkeep.Owed - upkeep.Paid} — {upkeep.Decayed} buildings decayed";
                return true;
            }

            if (envelope.TryGet(out ShockStruck shock))
            {
                importance = FeedImportance.Alarming;
                text = $"{Shock(shock.Kind)} ({shock.Magnitude:0.#})";
                return true;
            }

            if (envelope.TryGet(out GoodsBought bought))
            {
                importance = FeedImportance.Notable;
                text = $"bought {bought.Units} {bought.Good} for {bought.Coin} coin";
                return true;
            }

            if (envelope.TryGet(out GoodsSold sold))
            {
                text = $"sold {sold.Units} units for {sold.Coin} coin";
                return true;
            }

            // Deliberately quiet: these fire every single day and say nothing a player would
            // act on. They stay in the event stream, where the log and the tests can see them.
            if (envelope.Is<UpkeepPaid>() || envelope.Is<LabourAllocated>() ||
                envelope.Is<CrewAssigned>())
            {
                text = null;
                return false;
            }

            // Not dropped. A feed that silently ignores what it does not recognise hides the
            // newest system, which is the one most likely to be misbehaving.
            text = envelope.PayloadType == null ? "something happened" : envelope.PayloadType.Name;
            return true;
        }

        /// <summary>Describes a command the player issued, including one that was refused.</summary>
        /// <remarks>
        /// Rejections are shown rather than swallowed. A button that appears to do nothing is
        /// the worst outcome available: the player cannot tell a refused order from a broken
        /// one, and stops trusting the controls.
        /// </remarks>
        public static string Describe(in CommandLogEntry entry, out FeedImportance importance)
        {
            string what = Name(entry.Command);

            if (entry.Applied)
            {
                importance = FeedImportance.Notable;
                return "you ordered: " + what;
            }

            importance = FeedImportance.Alarming;
            return $"refused: {what} ({Reason(entry.Rejection)})";
        }

        private static string Name(ICommand command)
        {
            switch (command)
            {
                case SuppressRiot suppress:
                    return $"put down the riot, {suppress.Harshness.ToString().ToLowerInvariant()}";
                case MothballBuilding mothball:
                    return mothball.Mothballed ? "shut a building" : "reopen a building";
                case AssignCrew _:
                    return "move a crew member";
                case Shock shock:
                    return Shock(shock.Kind);
                default:
                    return command == null ? "something" : command.GetType().Name;
            }
        }

        private static string Reason(CommandRejection rejection)
        {
            switch (rejection)
            {
                case CommandRejection.NotYet: return "not yet";
                case CommandRejection.TargetGone: return "it is gone";
                case CommandRejection.InvalidTarget: return "not a valid target";
                case CommandRejection.AlreadyInState: return "already so";
                case CommandRejection.Unavailable: return "unavailable";
                default: return rejection.ToString();
            }
        }

        private static string Shock(ShockKind kind)
        {
            switch (kind)
            {
                case ShockKind.HarvestFailure: return "the harvest failed";
                case ShockKind.Storm: return "a storm struck";
                case ShockKind.Theft: return "coin was stolen";
                case ShockKind.Desertion: return "crew slipped away";
                default: return kind.ToString();
            }
        }

        private static string Role(BalanceTables balance, int index) =>
            balance != null && index >= 0 && index < balance.CrewRoles.Count
                ? "a " + balance.CrewRoles[index].Id
                : "a crew member";

        private static string Building(BalanceTables balance, int index) =>
            balance != null && index >= 0 && index < balance.Buildings.Count
                ? balance.Buildings[index].Id
                : "building";

        private static string Stratum(BalanceTables balance, int index) =>
            balance != null && index >= 0 && index < balance.Strata.Count
                ? balance.Strata[index].Id
                : "somebody";
    }
}
