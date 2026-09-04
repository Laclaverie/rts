using System;
using System.Collections.Generic;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;
using RTS.Sim.Engine.Time;
using RTS.Sim.Scenarios;
using RTS.Sim.Systems;

namespace RTS.Sim.Session
{
    /// <summary>
    /// A playable game: a world, a clock, and the commands a player can issue.
    /// </summary>
    /// <remarks>
    /// <strong>This is the game. Unity is a renderer.</strong> Nothing here references
    /// UnityEngine, and the assembly it lives in cannot — <c>Sim.asmdef</c> sets
    /// <c>noEngineReferences</c>, so an accidental <c>using UnityEngine</c> is a compile error
    /// rather than a discipline problem (ARCHITECTURE C5, §2).
    /// <para>
    /// The reason is not purity. An engine is a dependency that will one day need upgrading on
    /// somebody else's schedule — a security fix, a dropped platform, a licence change — and the
    /// cost of that upgrade should be proportional to how much of the game lives inside it.
    /// Here it is the scene bootstrap and the panel binder, both of which are small enough to
    /// rewrite in an afternoon. Everything that took real thought is on the other side of this
    /// line, compiled and tested by a plain <c>dotnet test</c> that never launches an editor.
    /// </para>
    /// <para>
    /// A front end has to do four things: advance time, read state, issue commands, and see what
    /// happened. All four are here, so a console, a test, or a different engine can drive the
    /// game without knowing anything Unity-shaped.
    /// </para>
    /// </remarks>
    public sealed class GameSession
    {
        private readonly List<Readout> _readouts = new List<Readout>();

        private GameSession(ReplayRun run, Clock clock, BalanceTables balance)
        {
            Run = run;
            Clock = clock;
            Balance = balance;
        }

        public ReplayRun Run { get; }

        public Clock Clock { get; }

        public BalanceTables Balance { get; }

        public World World => Run.World;

        /// <summary>The in-game day. Starts at 1.</summary>
        public int Day => Run.Day;

        /// <summary>Days that have passed since the front end last asked. For animation.</summary>
        public int DaysLastAdvanced { get; private set; }

        /// <summary>
        /// Starts a session on the shipped content.
        /// </summary>
        /// <param name="pipelineCsv">The text of <c>pipeline.csv</c>, read by the caller.</param>
        /// <remarks>
        /// Text rather than a path, because <c>Sim</c> has no business knowing where files live
        /// — under Unity that is StreamingAssets, in a test it is the build output, and neither
        /// should leak in here (§5.2).
        /// </remarks>
        public static GameSession Start(BalanceTables balance, Clock clock, string pipelineCsv,
            PortScenario scenario, ulong seed = 1)
        {
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            if (pipelineCsv == null) throw new ArgumentNullException(nameof(pipelineCsv));
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));

            ReplayRun run = ReplayRun.Start(
                seed,
                PlayerCommands(),
                dispatcher => ScenarioRunner.BuildPipeline(pipelineCsv, dispatcher),
                scenario.Build(balance),
                balance);

            return new GameSession(run, clock, balance);
        }

        /// <summary>
        /// Every lever a player has. The same list the scenario runner uses, because a scenario
        /// is a recording of a session and the two must accept the same commands (§6.1).
        /// </summary>
        public static ICommandHandler[] PlayerCommands() => new ICommandHandler[]
        {
            new ShockHandler(),
            new SuppressRiotHandler(),
            new AssignCrewHandler(),
            new MothballBuildingHandler(),
        };

        /// <summary>
        /// Hands the clock real time and runs whatever whole days come back.
        /// </summary>
        /// <remarks>
        /// The only place real time touches the game, and it touches it as an integer. What the
        /// front end does with frames, vsync or a stalled breakpoint stays on the front end's
        /// side of this call — which is why a session played at ×4 and a headless replay of its
        /// command log reach the same digest.
        /// </remarks>
        public int Advance(float realSeconds)
        {
            int days = Clock.Advance(realSeconds);

            for (int i = 0; i < days; i++)
            {
                Run.AdvanceDay();
                Run.Events.Drain();
            }

            DaysLastAdvanced = days;
            return days;
        }

        /// <summary>Advances exactly one day, whatever the clock says. For a step button.</summary>
        public void Step()
        {
            Run.AdvanceDay();
            Run.Events.Drain();
            DaysLastAdvanced = 1;
        }

        /// <summary>
        /// Queues a player's command. It is validated and applied at the drain, not here.
        /// </summary>
        /// <remarks>
        /// Input never writes world state (§2, §6). It produces a command, the command lands in
        /// the log, and the log plus the seed is the save — so anything a player can do is
        /// replayable by construction.
        /// </remarks>
        public void Submit(ICommand command) => Run.Submit(command);

        /// <summary>The day's numbers, computed once for whoever is drawing them.</summary>
        public PortReport Report() => PortReport.Of(World, Balance, Day);

        /// <summary>
        /// The state readouts BUILD_ORDER asks Phase 3 for, as labelled strings.
        /// </summary>
        /// <remarks>
        /// Formatted here rather than in the renderer so that what the player is told is a
        /// property of the game and not of the engine drawing it. A console harness and a Unity
        /// panel show the same words in the same order, and changing them is a one-file change
        /// with a headless test behind it.
        /// <para>
        /// The list is rebuilt into the same buffer each call: this runs every frame, and a
        /// front end should not have to think about the garbage a readout makes.
        /// </para>
        /// </remarks>
        public IReadOnlyList<Readout> Readouts()
        {
            _readouts.Clear();

            PortReport report = Report();

            _readouts.Add(new Readout("Day", report.Day.ToString()));
            _readouts.Add(new Readout("Coin", report.Coin.ToString()));

            if (report.Arrears > 0)
                _readouts.Add(new Readout("Unpaid", report.Arrears.ToString()));

            _readouts.Add(new Readout("Upkeep", UpkeepPerDay().ToString() + "/day"));
            _readouts.Add(new Readout("Crew", report.Crew.ToString()));
            _readouts.Add(new Readout("Town", Commoners().ToString()));
            _readouts.Add(new Readout("Unemployed", LabourSystem.UnemployedIn(World).ToString()));
            _readouts.Add(new Readout("Morale", Percent(report.AverageMorale)));
            _readouts.Add(new Readout("Condition", Percent(report.AverageCondition)));

            for (int i = 0; i < Balance.Goods.Count && i < report.Stock.Count; i++)
            {
                float units = report.Stock[i];
                if (units <= 0f && Balance.Goods[i].Supply == GoodSupply.ImportOnly) continue;

                _readouts.Add(new Readout(Balance.Goods[i].Id, units.ToString("0.0")));
            }

            _readouts.Add(new Readout("Unrest", report.Rung.ToString()));

            for (int i = 0; i < Balance.Strata.Count && i < report.Grievance.Count; i++)
                _readouts.Add(new Readout(Balance.Strata[i].Id, Percent(report.Grievance[i])));

            return _readouts;
        }

        /// <summary>What the port owes in upkeep every day, shut buildings excepted.</summary>
        public int UpkeepPerDay()
        {
            ComponentStore<BuildingState> buildings = World.Store<BuildingState>();
            int total = 0;

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingState state = buildings.Values[i];
                if (state.Mothballed) continue;

                total += Balance.Buildings[state.DefinitionIndex].UpkeepCoin;
            }

            return total;
        }

        public int Commoners() => LabourSystem.CommonersIn(World);

        private static string Percent(float value) => (value * 100f).ToString("0") + "%";
    }

    /// <summary>One labelled number for a front end to draw.</summary>
    public readonly struct Readout
    {
        public Readout(string label, string value)
        {
            Label = label;
            Value = value;
        }

        public readonly string Label;
        public readonly string Value;

        public override string ToString() => Label + " " + Value;
    }
}
