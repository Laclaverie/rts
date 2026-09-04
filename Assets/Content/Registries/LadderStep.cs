namespace RTS.Content.Registries
{
    /// <summary>What one rung of the ladder means (GDD §5.2.2).</summary>
    public sealed class LadderStep : IHasId
    {
        public LadderStep(string id, LadderRung rung, float climbAt, float fallBelow,
            float outputMultiplier, float conditionDamage)
        {
            Id = id;
            Rung = rung;
            ClimbAt = climbAt;
            FallBelow = fallBelow;
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

        /// <summary>What production is worth here. Slowdown is a rung, not a metaphor.</summary>
        public float OutputMultiplier { get; }

        /// <summary>Condition lost per building per day. Riots damage property.</summary>
        public float ConditionDamage { get; }

        public override string ToString() => Id;
    }
}
