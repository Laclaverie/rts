namespace RTS.Sim.Engine.Commands
{
    /// <summary>
    /// The only way into the sim. Player input, AI and campaign scripts all enter identically
    /// (ARCHITECTURE §6), which is what lets scripted and emergent content interleave without
    /// a second code path.
    /// </summary>
    /// <remarks>
    /// A marker, not behaviour: commands are <em>data</em>, so they serialise, and a save is a
    /// seed plus the command log (§6.1). Anything that cannot be written down and replayed
    /// does not belong in a command — no delegates, no references to live objects, no
    /// wall-clock values.
    /// <para>
    /// Implementations should be <c>sealed record</c> classes: value equality and a readable
    /// ToString for the log come free, and the input-rate allocation is irrelevant.
    /// </para>
    /// </remarks>
    public interface ICommand
    {
    }
}
