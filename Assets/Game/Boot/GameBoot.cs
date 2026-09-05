using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Game.Presentation;
using RTS.Sim.Engine.Diagnostics;
using RTS.Sim.Engine.Time;
using RTS.Sim.Session;
using RTS.Sim.Systems;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace RTS.Game.Boot
{
    /// <summary>
    /// The composition root: loads content, starts a session, and feeds it real time.
    /// </summary>
    /// <remarks>
    /// The whole of Unity's involvement in the game, and it is meant to stay this size. It reads
    /// files because only Unity knows where StreamingAssets is, it hands
    /// <see cref="UnityEngine.Time.deltaTime"/> to the session because only Unity has a frame,
    /// and it owns a panel because something has to draw. Everything else is on the other side
    /// of <see cref="GameSession"/>, in an assembly that cannot reference UnityEngine at all.
    /// <para>
    /// Exempt from the reuse constraint (ARCHITECTURE §2.1) and from nothing else: a composition
    /// root is allowed to know about everything it wires, and is the one place that may.
    /// </para>
    /// </remarks>
    [AddComponentMenu("RTS/Game Boot")]
    public sealed class GameBoot : MonoBehaviour
    {
        private GameSession _session;
        private PortPanel _panel;
        private MapPanel _map;

        /// <summary>The running game, for anything else in the scene that needs to read it.</summary>
        public GameSession Session => _session;

        private void Awake()
        {
            // Logging installs itself before the scene loads, so it is already running here.
            var report = new ValidationReport();

            BalanceTables balance = BalanceTables.Load(new BalanceSources
            {
                Goods = BalanceFiles.ReadCsv(BalanceTables.GoodsFile),
                Buildings = BalanceFiles.ReadCsv(BalanceTables.BuildingsFile),
                CrewRoles = BalanceFiles.ReadCsv(BalanceTables.CrewRolesFile),
                Strata = BalanceFiles.ReadCsv(BalanceTables.StrataFile),
                Ladder = BalanceFiles.ReadCsv(BalanceTables.LadderFile),
                Repression = BalanceFiles.ReadCsv(BalanceTables.RepressionFile),
                Ports = BalanceFiles.ReadCsv(BalanceTables.PortsFile),
            }, report);

            Clock clock = Clock.Load(ConfigFiles.ReadCsv(ConfigFiles.ClockFile), report);

            // Loudly, and before anything runs. Content that does not load is a fixable mistake
            // in a file; content that half-loads is a port whose numbers are quietly wrong.
            report.ThrowIfInvalid();

            // No world passed: the session builds the whole map from ports.csv.
            _session = GameSession.Start(
                balance,
                clock,
                BalanceFiles.ReadText("pipeline.csv"));

            Log.Info(LogChannel.Boot,
                $"session started: day {_session.Day}, {balance.Ports.Count} cities, " +
                $"{clock.SecondsPerDay}s per day");

            BuildPanel();
        }

        private void Update()
        {
            // The only line in the project where a frame rate meets the game, and it meets it
            // as an integer number of days. What the machine was doing between days cannot
            // reach the world, which is what makes a played session replay (§6.1, §7.1).
            if (_session.Advance(UnityEngine.Time.deltaTime) > 0)
            {
                _panel.Refresh();

                // The map too, not only the ships. Cities do not move, but what is highlighted
                // and what a marker says can change without anyone having clicked one, and a
                // map that only redrew on its own clicks would drift out of step with the card
                // beside it.
                _map?.Refresh();
            }

            // Ships move between day boundaries; nothing else on screen does. What they move by
            // is the clock's fraction of a day, computed in Sim — a frame reaches the drawing
            // and never the world (§7.1).
            _map?.Tick();

            ReadKeys();
        }

        /// <summary>
        /// Keyboard for the controls that matter. Space pauses, because §3.2 makes pause the
        /// mechanism rather than a convenience, and a mechanism should not need the mouse.
        /// </summary>
        /// <remarks>
        /// Read through the Input System package rather than <c>UnityEngine.Input</c>: this
        /// project has active input handling switched to the package, and the legacy class
        /// throws on every call rather than returning false. It cost a play-mode session to
        /// find, because the panel drew perfectly while the exception fired sixty times a
        /// second behind it.
        /// </remarks>
        private void ReadKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                _session.Clock.TogglePause();
                _panel.Refresh();
                return;
            }

            if (keyboard.periodKey.wasPressedThisFrame)
            {
                _session.Step();
                _panel.Refresh();
                return;
            }

            for (int i = 0; i < _session.Clock.Speeds.Count && i < 9; i++)
            {
                if (!keyboard[Key.Digit1 + i].wasPressedThisFrame) continue;

                _session.Clock.Speed = _session.Clock.Speeds[i];
                _panel.Refresh();
                return;
            }
        }

        private void BuildPanel()
        {
            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>(PanelSettingsResource);

            if (document.panelSettings == null)
            {
                Log.Error(LogChannel.Boot,
                    $"no PanelSettings at Resources/{PanelSettingsResource}; the panel cannot draw.");
                return;
            }

            // The map first, so it lies behind: the port panel is a floating card over the
            // world rather than a column beside it.
            _map = new MapPanel(_session);
            document.rootVisualElement.Add(_map.Build());

            _panel = new PortPanel(_session);
            document.rootVisualElement.Add(_panel.Build());

            // Clicking a city changes which orders exist, so the card has to redraw. The map
            // does not know what a panel is; it says that something changed.
            _map.SelectionChanged = () => _panel.Refresh();
        }

        /// <summary>
        /// Loaded from Resources rather than an inspector reference, so the scene is one object
        /// with one component and nothing to wire by hand. A scaffold that needs a checklist to
        /// reassemble is a scaffold that rots.
        /// </summary>
        public const string PanelSettingsResource = "PortPanelSettings";
    }
}
