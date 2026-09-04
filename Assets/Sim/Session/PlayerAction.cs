using RTS.Sim.Engine.Commands;

namespace RTS.Sim.Session
{
    /// <summary>
    /// Something the player can do right now, and whether they can do it.
    /// </summary>
    /// <remarks>
    /// Built in <c>Sim</c> so that what the buttons are, what they are called, and whether they
    /// are available is game behaviour rather than panel behaviour (ARCHITECTURE §2.2). A front
    /// end draws this list and submits <see cref="Command"/>; it does not decide what is
    /// possible.
    /// <para>
    /// <strong>The rejection comes from the real handler.</strong> A greyed-out button whose
    /// reasoning was written a second time in the UI is a button that will eventually disagree
    /// with the command it issues — offering something the game refuses, or hiding something it
    /// would have allowed. Asking <see cref="GameSession.Validate"/> means the two cannot drift.
    /// </para>
    /// </remarks>
    public readonly struct PlayerAction
    {
        public PlayerAction(string group, string label, string detail, ICommand command,
            CommandRejection rejection)
        {
            Group = group;
            Label = label;
            Detail = detail;
            Command = command;
            Rejection = rejection;
        }

        /// <summary>Which heading this belongs under. Ordering within a group is stable.</summary>
        public readonly string Group;

        /// <summary>What the button says.</summary>
        public readonly string Label;

        /// <summary>What it applies to — the building's name, its staffing, its condition.</summary>
        public readonly string Detail;

        public readonly ICommand Command;

        /// <summary>Why it cannot be done, or <see cref="CommandRejection.None"/>.</summary>
        public readonly CommandRejection Rejection;

        public bool Enabled => Rejection == CommandRejection.None;

        /// <summary>
        /// A short reason a disabled action is disabled, for a tooltip.
        /// </summary>
        /// <remarks>
        /// Shown rather than left blank. A control that is grey for no stated reason teaches the
        /// player that the game is arbitrary, which is the opposite of what §3.2 wants from a
        /// game that expects thought.
        /// </remarks>
        public string Reason
        {
            get
            {
                switch (Rejection)
                {
                    case CommandRejection.None: return string.Empty;
                    case CommandRejection.NotYet: return "not yet";
                    case CommandRejection.TargetGone: return "it is gone";
                    case CommandRejection.InvalidTarget: return "not a valid target";
                    case CommandRejection.AlreadyInState: return "already so";
                    case CommandRejection.NotPermitted: return "there is no work here";
                    case CommandRejection.Unavailable: return "unavailable";
                    default: return Rejection.ToString();
                }
            }
        }

        public override string ToString() =>
            Enabled ? $"{Label} ({Detail})" : $"{Label} — {Reason}";
    }
}
