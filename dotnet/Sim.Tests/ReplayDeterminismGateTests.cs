using System;
using System.Collections.Generic;
using RTS.Content.Loading;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// The Phase 0 gate (BUILD_ORDER §2): the same seed and command log, run twice, must reach
    /// a byte-identical end state.
    /// </summary>
    /// <remarks>
    /// Everything downstream rests on this. §6.1 makes loading a save the same operation as
    /// replaying, so a divergence here is not a flaky test — it is a save that loads into a
    /// different world than the one the player left.
    /// </remarks>
    [Category(TestCategories.Unit)]
    public class ReplayDeterminismGateTests
    {
        // ------------------------------------------------------------------ fixtures

        private struct Stock : IComponentData
        {
            public int Units;
            public float Price;

            public void Write(IStateWriter writer)
            {
                writer.Write("units", Units);
                writer.Write("price", Price);
            }
        }

        private sealed record Deliver(int Units) : ICommand;

        private struct Delivered { public int Units; }

        private sealed class DeliverHandler : ICommandHandler
        {
            public Type CommandType => typeof(Deliver);

            public bool Validate(ICommand command, World world, in Context ctx, out string reason)
            {
                if (((Deliver)command).Units <= 0)
                {
                    reason = "a delivery must carry something";
                    return false;
                }

                reason = null;
                return true;
            }

            public void Apply(ICommand command, World world, in Context ctx)
            {
                EntityId port = world.Entities.Count > 0 ? world.Entities[0] : world.CreateEntity();
                if (!world.Has<Stock>(port)) world.Add(port, new Stock());

                ref Stock stock = ref world.GetRef<Stock>(port);
                stock.Units += ((Deliver)command).Units;

                ctx.Events.Emit(new Delivered { Units = stock.Units });
            }
        }

        /// <summary>Moves prices about using the seeded generator, so draws affect the state.</summary>
        private sealed class DriftPrices : ISystem
        {
            public string Id => "DriftPrices";

            public void Run(World world, in Context ctx)
            {
                ComponentStore<Stock> stocks = world.Store<Stock>();

                for (int i = 0; i < stocks.Count; i++)
                {
                    EntityId owner = stocks.Ids[i];
                    ref Stock stock = ref stocks.GetRef(owner);
                    stock.Price += ctx.Rng.NextFloat(-0.5f, 0.5f);
                }
            }
        }

        /// <summary>Deliberately non-deterministic, to prove the gate can fail.</summary>
        private sealed class WallClockDrift : ISystem
        {
            public string Id => "WallClockDrift";

            public void Run(World world, in Context ctx)
            {
                ComponentStore<Stock> stocks = world.Store<Stock>();

                for (int i = 0; i < stocks.Count; i++)
                {
                    ref Stock stock = ref stocks.GetRef(stocks.Ids[i]);
                    // Exactly what §7.1 forbids.
                    stock.Units += (int)(DateTime.UtcNow.Ticks % 7);
                }
            }
        }

        private static Pipeline BuildPipeline(params ISystem[] systems)
        {
            var rows = new List<string>();
            int order = 10;

            foreach (ISystem system in systems)
            {
                rows.Add($"DayBoundary,{order},{system.Id},true");
                order += 10;
            }

            CsvTable table = CsvTable.Parse(
                "phase,order,system,enabled\n" + string.Join("\n", rows) + "\n", "pipeline.csv");

            return Pipeline.Build(table, systems);
        }

        private static readonly ICommand[] ScriptCommands =
        {
            new Deliver(5),
            new Deliver(0),    // rejected, and still part of the record
            new Deliver(12),
            new Deliver(-3),   // rejected
            new Deliver(1),
        };

        // ---------------------------------------------------------------- the gate

        [Test]
        public void The_same_seed_and_command_log_reach_an_identical_end_state()
        {
            ReplayRun first = RunScriptWithDrain();
            ReplayRun second = RunScriptWithDrain();

            if (first.Digest() != second.Digest())
            {
                // The dump is the point: a digest mismatch alone would say nothing about where.
                Assert.Fail("Replay diverged.\n--- first ---\n" + first.Dump() +
                            "\n--- second ---\n" + second.Dump());
            }

            Assert.That(second.Digest(), Is.EqualTo(first.Digest()));
        }

        [Test]
        public void The_gate_can_fail()
        {
            // A gate that cannot fail is not a gate (BUILD_ORDER §1.6). A system reading the
            // wall clock is precisely what §7.1 forbids, and the digest must notice.
            ReplayRun first = RunScriptWithDrain(nonDeterministic: true);
            System.Threading.Thread.Sleep(2);
            ReplayRun second = RunScriptWithDrain(nonDeterministic: true);

            Assert.That(second.Digest(), Is.Not.EqualTo(first.Digest()),
                "a wall-clock read must break the digest, or the gate proves nothing");
        }

        [Test]
        public void A_different_seed_reaches_a_different_end_state()
        {
            ReplayRun first = RunScriptWithDrain(seed: 1UL);
            ReplayRun second = RunScriptWithDrain(seed: 2UL);

            Assert.That(second.Digest(), Is.Not.EqualTo(first.Digest()));
        }

        [Test]
        public void The_digest_covers_the_command_log_not_only_the_world()
        {
            // Two runs can reach the same world by different routes. Rejected commands change
            // the record without changing the world, and that is still a divergence.
            ReplayRun withRejects = RunScriptWithDrain();

            ReplayRun withoutRejects = StartRun(20260903UL);
            withoutRejects.Submit(new Deliver(5));
            withoutRejects.Submit(new Deliver(12));
            withoutRejects.Submit(new Deliver(1));
            withoutRejects.Run(days: 3);

            Assert.That(withRejects.World.Store<Stock>().Values[0].Units,
                Is.EqualTo(withoutRejects.World.Store<Stock>().Values[0].Units),
                "same world state");

            Assert.That(withoutRejects.Digest(), Is.Not.EqualTo(withRejects.Digest()),
                "but a different history, so a different digest");
        }

        [Test]
        public void The_digest_covers_the_generator_position()
        {
            ReplayRun run = StartRun(7UL);
            run.Run(days: 1);
            string before = run.Digest();

            run.Rng.NextUInt();

            Assert.That(run.Digest(), Is.Not.EqualTo(before),
                "an extra draw shifts every future value; a snapshot that missed it would diverge");
        }

        [Test]
        public void The_dump_is_diffable_and_names_what_diverged()
        {
            ReplayRun run = RunScriptWithDrain();
            string dump = run.Dump();

            Assert.That(dump, Does.Contain("Stock"));
            Assert.That(dump, Does.Contain("units"));
            Assert.That(dump, Does.Contain("rng"));
            Assert.That(dump, Does.Contain("commands"));
            Assert.That(dump, Does.Contain("Deliver"));
        }

        [Test]
        public void Floats_are_digested_by_bit_pattern_so_a_one_ulp_drift_is_caught()
        {
            var a = new HashStateWriter();
            var b = new HashStateWriter();

            a.Write("price", 1.0f);
            b.Write("price", 1.0000001f);

            Assert.That(b.Digest, Is.Not.EqualTo(a.Digest));
        }

        [Test]
        public void Structure_is_digested_not_just_values()
        {
            // The same numbers in a different shape must not collide.
            var a = new HashStateWriter();
            a.BeginSection("x");
            a.Write("v", 1);
            a.EndSection();
            a.Write("v", 2);

            var b = new HashStateWriter();
            b.Write("v", 1);
            b.BeginSection("x");
            b.Write("v", 2);
            b.EndSection();

            Assert.That(b.Digest, Is.Not.EqualTo(a.Digest));
        }

        // ------------------------------------------------------------------ helpers

        private static ReplayRun StartRun(ulong seed, bool nonDeterministic = false)
        {
            ISystem drift = nonDeterministic ? (ISystem)new WallClockDrift() : new DriftPrices();

            return ReplayRun.Start(
                seed,
                new ICommandHandler[] { new DeliverHandler() },
                dispatcher => BuildPipeline(new CommandDrainSystem(dispatcher), drift));
        }

        private static ReplayRun RunScriptWithDrain(ulong seed = 20260903UL, bool nonDeterministic = false)
        {
            ReplayRun run = StartRun(seed, nonDeterministic);
            foreach (ICommand command in ScriptCommands) run.Submit(command);
            run.Run(days: 3);
            return run;
        }
    }
}
