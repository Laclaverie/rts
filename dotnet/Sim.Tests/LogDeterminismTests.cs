using System;
using System.Collections.Generic;
using RTS.Content.Loading;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Diagnostics;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// Logging sits outside the determinism contract on purpose — it is static mutable state,
    /// which §7.1 otherwise forbids — and the rule that makes that safe is that it may not
    /// influence sim state. This is what checks the rule held.
    /// </summary>
    /// <remarks>
    /// The hazard is real and specific: <c>Log.On(...)</c> returns a bool, so a system
    /// <em>could</em> do state-changing work inside the guard. Nothing structural prevents it.
    /// Replaying the same seed and command log with logging on and off, and comparing digests,
    /// is what would catch it — and a save that loads differently depending on whether the
    /// player had logging enabled would be a spectacular bug to diagnose from a report.
    /// </remarks>
    [Category(TestCategories.Unit)]
    [NonParallelizable]
    public class LogDeterminismTests
    {
        private const LogChannel Channel = LogChannel.Commands;

        private struct Ledger : IComponentData
        {
            public int Total;

            public void Write(IStateWriter writer) => writer.Write("total", Total);
        }

        private sealed record Add(int Amount) : ICommand;

        private sealed class AddHandler : ICommandHandler
        {
            public Type CommandType => typeof(Add);

            public CommandRejection Validate(ICommand command, World world, in Context ctx) =>
                ((Add)command).Amount == 0 ? CommandRejection.OutOfRange : CommandRejection.None;

            public void Apply(ICommand command, World world, in Context ctx)
            {
                EntityId target = world.Entities.Count > 0 ? world.Entities[0] : world.CreateEntity();
                if (!world.Has<Ledger>(target)) world.Add(target, new Ledger());

                world.GetRef<Ledger>(target).Total += ((Add)command).Amount;

                Log.Info(Channel, $"applied {command}");
            }
        }

        /// <summary>Logs on every path, including inside a guard, as a real system would.</summary>
        private sealed class ChattySystem : ISystem
        {
            public string Id => "Chatty";

            public void Run(World world, in Context ctx)
            {
                Log.Debug(Channel, $"day {ctx.Day} begins");

                ComponentStore<Ledger> ledgers = world.Store<Ledger>();

                if (Log.On(Channel, LogLevel.Trace))
                {
                    // Work done only when logging is on — and it must not touch the world.
                    int sum = 0;
                    for (int i = 0; i < ledgers.Count; i++) sum += ledgers.Values[i].Total;
                    Log.Trace(Channel, $"{ledgers.Count} ledgers totalling {sum}");
                }

                for (int i = 0; i < ledgers.Count; i++)
                {
                    ref Ledger ledger = ref ledgers.GetRef(ledgers.Ids[i]);
                    ledger.Total += ctx.Rng.NextInt(1, 4);
                }

                Log.Debug(Channel, "day ends");
            }
        }

        private static string RunOnce(bool logging, LogLevel level)
        {
            Log.ClearSinks();
            Log.Enabled = logging;

            if (logging)
            {
                Log.AddSink(new CaptureLogSink());
                Log.SetLevel(Channel, level);
            }

            try
            {
                ReplayRun run = ReplayRun.Start(
                    424242UL,
                    new ICommandHandler[] { new AddHandler() },
                    dispatcher => Pipeline.Build(
                        CsvTable.Parse(
                            "phase,order,system,enabled\n" +
                            "DayBoundary,10," + CommandDrainSystem.SystemId + ",true\n" +
                            "DayBoundary,20,Chatty,true\n",
                            "pipeline.csv"),
                        new ISystem[] { new CommandDrainSystem(dispatcher), new ChattySystem() }));

                foreach (ICommand command in new ICommand[] { new Add(5), new Add(0), new Add(3) })
                    run.Submit(command);

                run.Run(days: 4);
                return run.Digest();
            }
            finally
            {
                Log.ClearSinks();
                Log.Enabled = true;
                Log.SetLevel(Channel, LogLevel.Info);
            }
        }

        [Test]
        public void Logging_does_not_change_the_simulation()
        {
            string silent = RunOnce(logging: false, LogLevel.Off);
            string noisy = RunOnce(logging: true, LogLevel.Trace);

            Assert.That(noisy, Is.EqualTo(silent),
                "the same seed and command log must reach the same state whether or not anyone was watching");
        }

        [Test]
        public void The_verbosity_level_does_not_change_the_simulation()
        {
            // The Trace guard in ChattySystem runs extra code at Trace and not at Error. If
            // that code ever touched the world, this is what would notice.
            string quiet = RunOnce(logging: true, LogLevel.Error);
            string verbose = RunOnce(logging: true, LogLevel.Trace);

            Assert.That(verbose, Is.EqualTo(quiet));
        }

        [Test]
        public void Logging_really_was_on_for_the_noisy_run()
        {
            // Otherwise the two tests above would pass by logging nothing in both cases, which
            // is the way this check quietly stops meaning anything.
            Log.ClearSinks();
            Log.Enabled = true;
            var sink = new CaptureLogSink();
            Log.AddSink(sink);
            Log.SetLevel(Channel, LogLevel.Trace);

            try
            {
                ReplayRun run = ReplayRun.Start(
                    1UL,
                    new ICommandHandler[] { new AddHandler() },
                    dispatcher => Pipeline.Build(
                        CsvTable.Parse(
                            "phase,order,system,enabled\nDayBoundary,10,Chatty,true\n", "pipeline.csv"),
                        new ISystem[] { new ChattySystem() }));

                run.Run(days: 2);

                Assert.That(sink.Count, Is.GreaterThan(0), "no lines were captured");
            }
            finally
            {
                Log.ClearSinks();
                Log.SetLevel(Channel, LogLevel.Info);
            }
        }
    }
}
