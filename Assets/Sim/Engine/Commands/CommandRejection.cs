namespace RTS.Sim.Engine.Commands
{
    /// <summary>
    /// Why a command was refused. <see cref="None"/> means it was not.
    /// </summary>
    /// <remarks>
    /// A code rather than a message, for three reasons that all trace back to the command log
    /// being a save artifact (ARCHITECTURE §6.1):
    /// <list type="number">
    /// <item>A save must not carry English prose that the UI has to show a French player.</item>
    /// <item>An interpolated message formats numbers with the machine's locale, and the log is
    /// part of the replay digest — the same command log would digest differently on two
    /// machines.</item>
    /// <item>A code can be asserted and switched on. A message can only be substring-matched,
    /// which breaks on rewording.</item>
    /// </list>
    /// <para>
    /// The human detail is not lost, it moves: a handler that wants to explain itself logs on
    /// <see cref="Diagnostics.LogChannel.Commands"/>. That text is for a developer reading a log
    /// file, never for a save or the digest.
    /// </para>
    /// <para>
    /// Values are engine-level on purpose. Phase 1 adds the ones the economy needs; keep them
    /// general enough that two systems refusing for the same reason use the same code.
    /// </para>
    /// </remarks>
    public enum CommandRejection
    {
        /// <summary>Not a rejection. The command is valid and will be applied.</summary>
        None = 0,

        /// <summary>The rules forbid it, and no more specific code fits.</summary>
        NotPermitted = 1,

        /// <summary>A referenced entity does not exist, or never did.</summary>
        InvalidTarget = 2,

        /// <summary>A referenced entity existed and has since been destroyed.</summary>
        TargetGone = 3,

        /// <summary>Not enough of something: coin, stock, crew, capacity.</summary>
        InsufficientResources = 4,

        /// <summary>A value is outside its allowed bounds.</summary>
        OutOfRange = 5,

        /// <summary>Already true. The command would change nothing.</summary>
        AlreadyInState = 6,

        /// <summary>Legal, but not yet — a cooldown, a phase, an unfinished process.</summary>
        NotYet = 7,

        /// <summary>The feature or system is turned off in this session.</summary>
        Unavailable = 8,
    }
}
