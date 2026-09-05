using System;
using System.Collections.Generic;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Engine.Randomness;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Rung 5 as a crowd in the square, not a word in a readout (GDD §5.2.2, §6.4).
    /// </summary>
    /// <remarks>
    /// The ladder's top rung is called Uprising, and until now that is all it was: a string in a
    /// state machine, distinguishable from Riot only by a production multiplier. §5.2.2 asks for
    /// something else — "a mob is hundreds of anonymous bodies with a handful of named faces
    /// inside it" — and BUILD_ORDER's gate for this phase is that <em>the revolt reads as an
    /// event, not a number</em>. Bodies that gather, converge and disperse are what makes the
    /// difference; the arithmetic underneath is unchanged.
    /// <para>
    /// <strong>It moves in days, not frames.</strong> ARCHITECTURE §4.2 sketched this as a Tick
    /// system on real time, and that phase is still empty. Steering on frames would put frame
    /// time into the world: the crowd's positions are world state, they go into the digest, and
    /// a revolt that came out differently depending on how long the player watched it is exactly
    /// what §7.1 and the scenario corpus exist to prevent. So a day is divided into
    /// <see cref="MobRules.StepsPerDay"/> equal sub-steps, all of them run at the day boundary,
    /// and a renderer interpolates between yesterday's crowd and today's. The picture is smooth
    /// and the simulation is still whole days.
    /// </para>
    /// <para>
    /// <strong>No flow field.</strong> §8.1 says start at dozens and measure before optimising,
    /// and a flow field is the optimisation. Dozens of agents steering straight at a single
    /// target, pushed apart by their neighbours, is a hundred lines rather than a subsystem —
    /// and the target is one point in an open square, which is the case a flow field buys least
    /// on. When the measurement says otherwise, the grid goes in behind this same interface.
    /// </para>
    /// </remarks>
    public sealed class MobSystem : ISystem
    {
        public const string SystemId = "Mob";

        /// <summary>The rung at which the crowd comes out.</summary>
        /// <remarks>
        /// Uprising, because that is the rung §5.2.2 describes as a mob. A riot is localised
        /// property damage and does not put the whole port in the square.
        /// </remarks>
        public const LadderRung MustersAt = LadderRung.Uprising;

        /// <summary>How hard neighbours push each other apart, per step.</summary>
        /// <remarks>
        /// Without it every body converges on the same point and the crowd draws as one dot,
        /// which is the number again with extra steps.
        /// </remarks>
        private const float Shoulder = 0.34f;

        private readonly List<EntityId> _scratch = new List<EntityId>();

        /// <summary>
        /// Which port each body belongs to, refreshed once a day rather than asked per pair.
        /// </summary>
        /// <remarks>
        /// §8.1 says measure before optimising, so this was measured: a full crowd of sixty
        /// bodies cost 4.9 ms a day, and almost none of it was the arithmetic. Separation
        /// compares every body with every other one, and each comparison was doing two component
        /// lookups to ask whose city they were in. Hoisting that here took it to 2.2 ms; taking
        /// the store's spans once instead of per comparison took it to 1.1 ms.
        /// <para>
        /// Worth writing down because what the measurement pointed at was not the quadratic loop
        /// everybody expects. A flow field would have replaced the cheap half and left both of
        /// the real costs exactly where they were.
        /// </para>
        /// </remarks>
        private EntityId[] _owners = new EntityId[0];

        public string Id => SystemId;

        public void Run(World world, in Context ctx)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            MobRules rules = ctx.Balance == null ? MobRules.Default : ctx.Balance.Mob;

            // Asked port by port, not by walking the ladder store: the ladder hangs off a
            // port rather than sitting on it, so a ladder entity is not a city. Reading it as
            // one put the crowd on an entity that was not a port and mustered nobody at all.
            ReadOnlySpan<EntityId> ports = Port.All(world);
            for (int i = 0; i < ports.Length; i++)
            {
                EntityId port = ports[i];
                bool wanted = RevolutionLadderSystem.RungOf(world, port) >= MustersAt;
                bool present = Bodies(world, port) > 0;

                if (wanted && !present) Muster(world, port, rules, ctx);
                else if (!wanted && present) Disperse(world, port, ctx);
            }

            Step(world, rules);
        }

        // ------------------------------------------------------------------ mustering

        /// <summary>
        /// Puts the port on the street.
        /// </summary>
        /// <remarks>
        /// The crowd is the commoners, because §5.2.2 is explicit that they are the bodies and
        /// the crew are the faces. A port whose commoners have all starved away or left has
        /// nobody to riot with, which is the same finding the Phase 2 gate turned up from the
        /// other end: a ruin with nobody in it cannot be angry.
        /// </remarks>
        private static void Muster(World world, EntityId port, MobRules rules, in Context ctx)
        {
            int commoners = LabourSystem.CommonersIn(world, port);
            int bodies = (int)(commoners * rules.BodiesPerCommoner);
            if (bodies > rules.MaximumBodies) bodies = rules.MaximumBodies;

            Rng rng = ctx.Rng;
            int loyalists = 0;
            int turned = 0;

            // The named faces first, so a crowd of nothing but crew is still a crowd. Each one
            // decides for themselves: §5.2.2 says named crew choose sides individually, and §5.4
            // keeps loyalty separate from morale so that a hungry crew member who trusts you can
            // stand while a comfortable one who does not, does not.
            ComponentStore<CrewMember> crew = world.Store<CrewMember>();
            for (int i = 0; i < crew.Count; i++)
            {
                EntityId member = crew.Ids[i];
                if (!Port.BelongsTo(world, member, port)) continue;

                MobSide side = crew.Values[i].Loyalty >= rules.LoyaltyToStand
                    ? MobSide.Loyalist
                    : MobSide.Rioter;

                if (side == MobSide.Loyalist) loyalists++;
                else turned++;

                Place(world, port, side, member, rules, rng);

                ctx.Events.Emit(new CrewChoseSide
                {
                    Port = port,
                    Crew = member,
                    RoleIndex = crew.Values[i].RoleIndex,
                    Side = side,
                    Loyalty = crew.Values[i].Loyalty,
                });
            }

            for (int i = 0; i < bodies; i++)
                Place(world, port, MobSide.Rioter, EntityId.None, rules, rng);

            ctx.Events.Emit(new MobMustered
            {
                Port = port,
                Bodies = bodies,
                Loyalists = loyalists,
                CrewTurned = turned,
            });
        }

        private static void Place(World world, EntityId port, MobSide side, EntityId crew,
            MobRules rules, Rng rng)
        {
            // Loyalists form on the line they are holding; everyone else arrives from the edges.
            float radius = side == MobSide.Loyalist ? rules.PressRadius : rules.MusterRadius;
            float angle = rng == null ? 0f : rng.NextFloat(0f, 6.2831853f);

            // Scattered inward a little, so the crowd is a crowd rather than a ring of dots at
            // one radius.
            float spread = rng == null ? 1f : rng.NextFloat(0.55f, 1f);
            float x = (float)Math.Cos(angle) * radius * spread;
            float y = (float)Math.Sin(angle) * radius * spread;

            EntityId body = world.CreateEntity();
            world.Add(body, new MobAgent
            {
                X = x,
                Y = y,
                PreviousX = x,
                PreviousY = y,
                Side = side,
                Crew = crew,
            });
            world.Add(body, new Owner { Port = port });
        }

        private void Disperse(World world, EntityId port, in Context ctx)
        {
            _scratch.Clear();

            ComponentStore<MobAgent> agents = world.Store<MobAgent>();
            for (int i = 0; i < agents.Count; i++)
                if (Port.BelongsTo(world, agents.Ids[i], port)) _scratch.Add(agents.Ids[i]);

            for (int i = 0; i < _scratch.Count; i++) world.DestroyEntity(_scratch[i]);

            ctx.Events.Emit(new MobDispersed { Port = port, Bodies = _scratch.Count });
        }

        // -------------------------------------------------------------------- steering

        /// <summary>
        /// Moves every body one day's worth, in equal sub-steps.
        /// </summary>
        /// <remarks>
        /// Rioters walk at the longhouse — the port's own centre — and stop where the line is.
        /// Loyalists hold that line and do not advance. Neighbours push each other apart, which
        /// is the whole of the crowd behaviour: it is what turns a convergent swarm into
        /// something with a shape, and it costs one pass over the port's own bodies.
        /// </remarks>
        private void Step(World world, MobRules rules)
        {
            ComponentStore<MobAgent> agents = world.Store<MobAgent>();
            if (agents.Count == 0) return;

            if (_owners.Length < agents.Count) _owners = new EntityId[agents.Count * 2];

            // Both of these build a fresh span on every access, so they are taken once and
            // passed down. Indexing Values inside the separation loop was constructing one per
            // comparison — forty thousand of them a day, and the largest single cost in the
            // whole system once the owner lookups were out.
            ReadOnlySpan<EntityId> ids = agents.Ids;
            ReadOnlySpan<MobAgent> values = agents.Values;

            for (int i = 0; i < ids.Length; i++)
            {
                ref MobAgent agent = ref agents.GetRef(ids[i]);
                agent.PreviousX = agent.X;
                agent.PreviousY = agent.Y;
                _owners[i] = Port.OwnerOf(world, ids[i]);
            }

            float step = rules.Speed;

            for (int pass = 0; pass < rules.StepsPerDay; pass++)
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    ref MobAgent agent = ref agents.GetRef(ids[i]);

                    float dx = 0f;
                    float dy = 0f;

                    if (agent.Side == MobSide.Rioter)
                    {
                        float distance = Length(agent.X, agent.Y);

                        // Inward until the line, and pushed back out if the crush carries
                        // somebody past it. The second half is not decoration: a crowd is
                        // denser than the circumference it is pressing against, so without a
                        // barrier the ones at the front were shoved through the longhouse and
                        // out the other side by the ones behind them. What should be a line
                        // holding is a crowd walking through it.
                        if (distance > 1e-4f)
                        {
                            float sign = distance > rules.PressRadius ? -1f : 1f;
                            dx = sign * agent.X / distance;
                            dy = sign * agent.Y / distance;
                        }
                    }

                    Shoulders(values, _owners, i, ref dx, ref dy);

                    float push = Length(dx, dy);
                    if (push <= 1e-4f) continue;

                    agent.X += dx / push * step;
                    agent.Y += dy / push * step;
                }
            }
        }

        /// <summary>
        /// Adds the shove of everyone standing too close, from the same port only.
        /// </summary>
        /// <remarks>
        /// Same port only, or two cities revolting at once would have their crowds elbowing each
        /// other across the sea. Every body carries an <see cref="Owner"/> for exactly this.
        /// </remarks>
        private static void Shoulders(ReadOnlySpan<MobAgent> agents, EntityId[] owners,
            int index, ref float dx, ref float dy)
        {
            EntityId port = owners[index];
            float selfX = agents[index].X;
            float selfY = agents[index].Y;

            for (int j = 0; j < agents.Length; j++)
            {
                if (j == index) continue;
                if (owners[j] != port) continue;

                float ax = selfX - agents[j].X;
                float ay = selfY - agents[j].Y;
                float distance = Length(ax, ay);

                if (distance >= Shoulder) continue;

                if (distance <= 1e-4f)
                {
                    // Standing in exactly the same spot. Nudge by index so the result is the
                    // same on every machine — a random jitter here would be a source of
                    // divergence in the one place the corpus cannot tolerate one.
                    dx += ((index % 2) == 0 ? 1f : -1f) * 0.5f;
                    dy += ((index % 3) == 0 ? 1f : -1f) * 0.5f;
                    continue;
                }

                float strength = (Shoulder - distance) / Shoulder;
                dx += ax / distance * strength;
                dy += ay / distance * strength;
            }
        }

        private static float Length(float x, float y) => (float)Math.Sqrt((x * x) + (y * y));

        /// <summary>How many bodies one port has on the street.</summary>
        public static int Bodies(World world, EntityId port)
        {
            ComponentStore<MobAgent> agents = world.Store<MobAgent>();
            int count = 0;

            for (int i = 0; i < agents.Count; i++)
                if (Port.BelongsTo(world, agents.Ids[i], port)) count++;

            return count;
        }

        /// <summary>How many of them are standing with you.</summary>
        public static int Loyalists(World world, EntityId port)
        {
            ComponentStore<MobAgent> agents = world.Store<MobAgent>();
            int count = 0;

            for (int i = 0; i < agents.Count; i++)
            {
                if (agents.Values[i].Side != MobSide.Loyalist) continue;
                if (Port.BelongsTo(world, agents.Ids[i], port)) count++;
            }

            return count;
        }
    }

    /// <summary>The port is in the square.</summary>
    public struct MobMustered
    {
        public EntityId Port;

        /// <summary>Anonymous bodies, not counting the named crew who joined them.</summary>
        public int Bodies;

        public int Loyalists;

        /// <summary>Named crew who chose the crowd.</summary>
        public int CrewTurned;
    }

    /// <summary>The square is empty again.</summary>
    public struct MobDispersed
    {
        public EntityId Port;
        public int Bodies;
    }

    /// <summary>One named crew member decided where to stand (GDD §5.2.2, §5.4).</summary>
    public struct CrewChoseSide
    {
        public EntityId Port;
        public EntityId Crew;
        public int RoleIndex;
        public MobSide Side;
        public float Loyalty;
    }
}
