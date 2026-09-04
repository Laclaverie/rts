using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;

namespace RTS.Sim.Systems
{
    /// <summary>How a port is doing, in one word.</summary>
    public enum PortCondition
    {
        /// <summary>Solvent, maintained, and the crew are fed and paid.</summary>
        Healthy = 0,

        /// <summary>Losing ground — something is unpaid or decaying — but still standing.</summary>
        Struggling = 1,

        /// <summary>Past the point where it can pull itself back.</summary>
        Collapsed = 2,
    }

    /// <summary>
    /// Classifies a port, so the cascade gate can assert on "recovered" and "collapsed" rather
    /// than on raw numbers that change with every balance edit.
    /// </summary>
    /// <remarks>
    /// The thresholds are here rather than in the tests deliberately. A gate written against
    /// specific coin and morale values would need re-editing on every tuning pass until someone
    /// deleted it; a gate written against these words survives tuning, and if the words stop
    /// meaning what they say then <em>that</em> is the thing to fix.
    /// <para>
    /// This reads the world and decides nothing. It is not a system and never runs in the
    /// pipeline.
    /// </para>
    /// </remarks>
    public static class PortHealth
    {
        /// <summary>Below this average condition, the buildings are visibly failing.</summary>
        public const float FailingCondition = 0.35f;

        /// <summary>Below this average morale, the crew are not really working.</summary>
        public const float FailingMorale = 0.35f;

        public static PortCondition Of(World world, BalanceTables balance)
        {
            PortReport report = PortReport.Of(world, balance, day: 0);

            ComponentStore<CrewMember> crew = world.Store<CrewMember>();

            // No crew and no coin is not "struggling", it is over.
            if (crew.Count == 0 && report.Coin <= 0) return PortCondition.Collapsed;

            bool broke = report.Coin <= 0;
            bool failingBuildings = report.Buildings > 0 && report.AverageCondition < FailingCondition;
            bool failingCrew = crew.Count > 0 && report.AverageMorale < FailingMorale;

            // Collapsed means it cannot pull itself back: no reserves to pay with, and the two
            // things that would earn the reserves — buildings and people — are both past
            // working properly. Any one of those alone is recoverable, which is the whole point
            // of §5.2.3: one bad thing is absorbed, several together are not.
            if (broke && failingBuildings && failingCrew) return PortCondition.Collapsed;

            if (broke || report.Arrears > 0 || failingBuildings || failingCrew)
                return PortCondition.Struggling;

            return PortCondition.Healthy;
        }
    }
}
