namespace RTS.Content.Registries
{
    /// <summary>What one rung of the ladder means (GDD §5.2.2).</summary>
    public sealed class LadderStep : IHasId
    {
        public LadderStep(string id, LadderRung rung, float climbAt, float fallBelow,
            int daysToClimb, float outputMultiplier, float conditionDamage)
        {
            Id = id;
            Rung = rung;
            ClimbAt = climbAt;
            FallBelow = fallBelow;
            DaysToClimb = daysToClimb;
            OutputMultiplier = outputMultiplier;
            ConditionDamage = conditionDamage;
        }

        public string Id { get; }

        public LadderRung Rung { get; }

        /// <summary>Grievance at or above which the port moves up to this rung.</summary>
        public float ClimbAt { get; }

        /// <summary>
        /// Grievance below which the port drops out of this rung. Lower than
        /// <see cref="ClimbAt"/> on purpose: the gap is hysteresis, and without it a port on the
        /// boundary would flicker every day.
        /// </summary>
        public float FallBelow { get; }

        /// <summary>
        /// Days the port must hold the rung below before it can climb to this one.
        /// </summary>
        /// <remarks>
        /// Grievance saturates far faster than it decays — a day of total mismanagement can add
        /// most of it, while <c>decay_per_day</c> gives it back in fortieths. Without a dwell
        /// time the ladder climbs every single day once grievance is pinned high, and a port
        /// that reaches Riot arrives at Deposition three days later no matter what the player
        /// does. That is a timer wearing a costume, and §5.2.2's promise that every rung has an
        /// exit would be false for the top half of the ladder.
        /// <para>
        /// Only climbing is paced. Falling stays immediate, because a port whose cause has been
        /// fixed should be rewarded promptly and the hysteresis already stops it flickering.
        /// </para>
        /// </remarks>
        public int DaysToClimb { get; }

        /// <summary>What production is worth here. Slowdown is a rung, not a metaphor.</summary>
        public float OutputMultiplier { get; }

        /// <summary>Condition lost per building per day. Riots damage property.</summary>
        public float ConditionDamage { get; }

        public override string ToString() => Id;
    }
}
