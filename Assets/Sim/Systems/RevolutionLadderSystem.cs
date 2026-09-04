using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Moves the port up and down the revolution ladder (GDD §5.2.2).
    /// </summary>
    /// <remarks>
    /// <para><strong>One rung a day, in either direction.</strong> Escalation that skips rungs
    /// is a spawn table; every rung being visible for at least a day is what gives a player
    /// something to act on, and what makes the ladder readable rather than a number crossing a
    /// line.</para>
    ///
    /// <para><strong>The angriest stratum drives it.</strong> Not an average: a port whose crew
    /// are furious is in trouble even if its commoners are content, and averaging would let one
    /// contented group hide another's fury. Which stratum is leading is recorded, because the
    /// agitator of rung 3 has to come from somewhere.</para>
    ///
    /// <para><strong>Falling is as real as climbing.</strong> §5.2.2 says every rung has an
    /// exit, and the Phase 2 gate is that a port can be driven into revolt and pulled back out.
    /// The hysteresis in ladder.csv is what stops the way down being the same line as the way
    /// up, which would flicker.</para>
    ///
    /// <para><strong>Deposition is terminal.</strong> It is the failure state, not a bad mood,
    /// so the port does not climb back out of it by feeding people.</para>
    /// </remarks>
    public sealed class RevolutionLadderSystem : ISystem
    {
        public const string SystemId = "RevolutionLadder";

        public string Id => SystemId;

        public void Run(World world, in Context ctx)
        {
            BalanceTables balance = ctx.Balance;
            if (balance == null || balance.Ladder.Count == 0) return;

            ComponentStore<RevolutionLadder> ladders = world.Store<RevolutionLadder>();
            if (ladders.Count == 0) return;

            float worst = 0f;
            int leading = 0;
            FindAngriest(world, ref worst, ref leading);

            EntityId entity = ladders.Ids[0];
            ref RevolutionLadder ladder = ref ladders.GetRef(entity);

            if (ladder.Rung == LadderRung.Deposition)
            {
                ladder.DaysAtRung++;
                return;
            }

            LadderRung wanted = Next(balance, ladder.Rung, worst);

            if (wanted == ladder.Rung)
            {
                ladder.DaysAtRung++;
            }
            else
            {
                LadderRung from = ladder.Rung;
                ladder.Rung = wanted;
                ladder.DaysAtRung = 0;
                ladder.LeadingStratumIndex = leading;

                ctx.Events.Emit(new LadderMoved
                {
                    From = from,
                    To = wanted,
                    Grievance = worst,
                    LeadingStratumIndex = leading,
                });
            }

            Apply(world, balance, ladder.Rung, ctx);
        }

        /// <summary>The rung one step towards where this grievance belongs.</summary>
        internal static LadderRung Next(BalanceTables balance, LadderRung current, float grievance)
        {
            int index = (int)current;

            // Climb: the rung above is earned.
            if (index + 1 < balance.Ladder.Count)
            {
                LadderStep above = balance.Ladder[index + 1];
                if (grievance >= above.ClimbAt && above.ClimbAt > 0f) return above.Rung;
            }

            // Fall: this rung is no longer held. Calm has nowhere to go.
            if (index > 0)
            {
                LadderStep here = balance.Ladder[index];
                if (grievance < here.FallBelow) return balance.Ladder[index - 1].Rung;
            }

            return current;
        }

        private static void FindAngriest(World world, ref float worst, ref int leading)
        {
            ComponentStore<Grievance> grievances = world.Store<Grievance>();

            for (int i = 0; i < grievances.Count; i++)
            {
                Grievance grievance = grievances.Values[i];
                if (grievance.Value <= worst) continue;

                worst = grievance.Value;
                leading = grievance.StratumIndex;
            }
        }

        /// <summary>What being on this rung does to the port today.</summary>
        private static void Apply(World world, BalanceTables balance, LadderRung rung, in Context ctx)
        {
            LadderStep step = balance.Ladder[(int)rung];
            if (step.ConditionDamage <= 0f) return;

            ComponentStore<BuildingState> buildings = world.Store<BuildingState>();
            int damaged = 0;

            for (int i = 0; i < buildings.Count; i++)
            {
                ref BuildingState state = ref buildings.GetRef(buildings.Ids[i]);
                if (state.Mothballed) continue;

                state.Condition = ConsumptionSystem.Clamp01(state.Condition - step.ConditionDamage);
                damaged++;
            }

            if (damaged > 0)
                ctx.Events.Emit(new PropertyDamaged { Rung = rung, Buildings = damaged });
        }
    }

    /// <summary>The port changed rung. Up or down — both are worth reporting.</summary>
    public struct LadderMoved
    {
        public LadderRung From;
        public LadderRung To;
        public float Grievance;
        public int LeadingStratumIndex;
    }

    /// <summary>Buildings were damaged by unrest.</summary>
    public struct PropertyDamaged
    {
        public LadderRung Rung;
        public int Buildings;
    }
}
