using RTS.Sim.Components;
using RTS.Sim.Presentation;
using RTS.Sim.Session;
using UnityEngine;
using UnityEngine.UIElements;

using EntityId = RTS.Sim.Engine.Entities.EntityId;

namespace RTS.Game.Presentation
{
    /// <summary>
    /// One city, close up: the square, the town around it, and whoever is standing in it
    /// (BUILD_ORDER Phase 5's gate).
    /// </summary>
    /// <remarks>
    /// The map answers where everything is. This answers what is happening here, and it exists
    /// because the phase's gate is that <em>the revolt reads as an event, not a number</em> —
    /// which the map could not deliver. At map scale a revolt is a city that has turned red and
    /// twelve dots three pixels across. No amount of zooming makes that a crowd with faces in
    /// it; the crowd needs somewhere to be.
    /// <para>
    /// A binder like the others. Where the buildings stand, where the bodies are and who they
    /// are all come from <see cref="PortModel"/>, which is tested without an engine. What is
    /// decided here is how big a person is drawn.
    /// </para>
    /// <para>
    /// <strong>Which view you are looking through is not game state.</strong> Selection went
    /// into <see cref="GameSession"/> because it changes which orders exist; this changes
    /// nothing about what is legal or what the game offers, so it stays here. That is the line:
    /// state that changes what the game does belongs to the sim, state that changes only what
    /// you are looking at does not.
    /// </para>
    /// </remarks>
    public sealed class PortView
    {
        /// <summary>
        /// How much of the square's half-width the town is drawn across.
        /// </summary>
        /// <remarks>
        /// Most of it. The first value left the town a small cluster in the middle of a large
        /// empty view, which reads as a diagram of a port rather than as being in one.
        /// </remarks>
        private const float Fill = 0.72f;

        /// <summary>
        /// How much of the width down the left the port card is given.
        /// </summary>
        /// <remarks>
        /// The card floats over this view so its orders stay reachable from inside a city, which
        /// means the town has to be drawn beside it rather than under it. The first version
        /// centred the town in the whole window and put the workshop behind the readouts.
        /// </remarks>
        private const float CardShare = 0.34f;

        /// <summary>Room at the top for the heading, which the town otherwise grew into.</summary>
        private const float HeadingBand = 56f;

        private const float BodyRadius = 6f;
        private const float FaceRadius = 9f;

        private static readonly Color Ground = new Color(0.10f, 0.10f, 0.12f);
        private static readonly Color Stone = new Color(0.30f, 0.29f, 0.27f);
        private static readonly Color Seat = new Color(0.52f, 0.45f, 0.30f);
        private static readonly Color Shut = new Color(0.20f, 0.20f, 0.22f);
        private static readonly Color Ink = new Color(0.86f, 0.86f, 0.84f);
        private static readonly Color Faint = new Color(0.55f, 0.55f, 0.55f);
        private static readonly Color Rioter = new Color(0.90f, 0.35f, 0.30f);
        private static readonly Color Loyalist = new Color(0.55f, 0.80f, 0.95f);
        private static readonly Color Hurt = new Color(0.85f, 0.55f, 0.30f);

        private readonly GameSession _session;

        private VisualElement _square;
        private VisualElement _town;
        private VisualElement _crowd;
        private Label _heading;
        private PortModel _port = PortModel.Empty;

        public PortView(GameSession session) => _session = session;

        public VisualElement Root { get; private set; }

        /// <summary>Which city is being looked at, or None when the map is up.</summary>
        public EntityId Looking { get; private set; }

        /// <summary>Raised when the view is opened or closed, so the rest of the screen can react.</summary>
        public System.Action Changed { get; set; }

        /// <summary>What was drawn last. For tests, and anything else that asks.</summary>
        public PortModel Port => _port;

        public bool IsOpen => !Looking.IsNone;

        public VisualElement Build()
        {
            Root = new VisualElement();
            Root.style.position = Position.Absolute;
            Root.style.left = 0;
            Root.style.top = 0;
            Root.style.right = 0;
            Root.style.bottom = 0;
            Root.style.backgroundColor = Ground;
            Root.style.display = DisplayStyle.None;

            // Clicking the ground goes back out. The city is the thing you are inside, so
            // leaving it is the obvious gesture and does not need a button to find.
            Root.RegisterCallback<ClickEvent>(_ => Close());

            _heading = new Label();
            _heading.style.position = Position.Absolute;
            _heading.style.top = 12;
            _heading.style.color = Ink;
            _heading.style.fontSize = 15;
            _heading.pickingMode = PickingMode.Ignore;
            Root.Add(_heading);

            // A square box in the middle of the view, sized to the shorter side. Everything is
            // then placed as a percentage of it, so the town stays round in a window of any
            // shape. Positioning as a percentage of the view itself would have been a percentage
            // of the width one way and of the height the other, which stretches the square into
            // an oval the moment the window is not.
            _square = new VisualElement();
            _square.style.position = Position.Absolute;
            _square.pickingMode = PickingMode.Ignore;
            Root.Add(_square);

            Root.RegisterCallback<GeometryChangedEvent>(_ => Fit());

            _town = Layer();
            _square.Add(_town);

            _crowd = Layer();
            _square.Add(_crowd);

            return Root;
        }

        /// <summary>
        /// Looks inside a city, if the player is allowed to.
        /// </summary>
        /// <remarks>
        /// The permission is <see cref="GameSession.CanLookInside"/>'s to give: what a player
        /// may know about a neighbour is §5.6's intelligence game, not a panel's decision.
        /// Refused quietly, because asking to look at a city you have no eyes in is a reasonable
        /// thing to try rather than a mistake.
        /// </remarks>
        public void Open(EntityId port)
        {
            if (!_session.CanLookInside(port)) return;

            Looking = port;
            Root.style.display = DisplayStyle.Flex;

            Refresh();
            Changed?.Invoke();
        }

        /// <summary>Goes back out to the map.</summary>
        public void Close()
        {
            if (!IsOpen) return;

            Looking = EntityId.None;
            Root.style.display = DisplayStyle.None;
            _port = PortModel.Empty;

            Changed?.Invoke();
        }

        /// <summary>
        /// Redraws the town. Buildings change once a day at most, so this is on the same beat as
        /// the rest of the screen.
        /// </summary>
        public void Refresh()
        {
            if (!IsOpen) return;

            Read();

            _town.Clear();
            for (int i = 0; i < _port.Buildings.Count; i++)
                _town.Add(BuildingMarker(_port.Buildings[i]));

            _heading.text = _port.Crowd.Count > 0
                ? $"{_port.Name} — {_port.Rung}, {_port.Crowd.Count} in the square"
                : $"{_port.Name} — {_port.Rung}";

            DrawCrowd();
        }

        /// <summary>
        /// Moves the crowd. Every frame, like the ships on the map.
        /// </summary>
        /// <remarks>
        /// A crowd that only moved on the day boundary would cross the square in six jumps,
        /// which is the readout again with a longer animation. Where it moves to is still
        /// computed in <c>Sim</c> from the clock's fraction of a day; a frame reaches the
        /// drawing and never the world (§7.1).
        /// </remarks>
        public void Tick()
        {
            if (!IsOpen) return;

            int before = _port.Buildings.Count;
            Read();

            // A building lost or mothballed changes the town, which is rare enough to check for
            // rather than redraw for.
            if (_port.Buildings.Count != before)
            {
                Refresh();
                return;
            }

            DrawCrowd();
        }

        /// <summary>Keeps the square square, whatever shape the window is.</summary>
        private void Fit()
        {
            Rect area = Root.contentRect;
            if (area.width <= 0f || area.height <= 0f) return;

            Rect box = SquareIn(area);

            _square.style.width = box.width;
            _square.style.height = box.height;
            _square.style.left = box.x;
            _square.style.top = box.y;

            _heading.style.left = area.width * CardShare + 16f;
        }

        /// <summary>
        /// The largest square that fits the part of the view the town actually has: right of the
        /// port card and below the heading.
        /// </summary>
        /// <remarks>
        /// Separated from the element it is applied to so it can be checked. Whether a town comes
        /// out round on a wide monitor, and whether half of it is behind the readouts, are
        /// questions with right answers that otherwise only a running editor at one particular
        /// window size could answer.
        /// </remarks>
        public static Rect SquareIn(Rect area)
        {
            float left = area.width * CardShare;
            float width = Mathf.Max(area.width - left, 1f);
            float height = Mathf.Max(area.height - HeadingBand, 1f);
            float side = Mathf.Min(width, height);

            return new Rect(
                left + ((width - side) * 0.5f),
                HeadingBand + ((height - side) * 0.5f),
                side,
                side);
        }

        private void Read() =>
            _port = PortModel.Of(_session.World, _session.Balance, Looking,
                _session.Clock.DayProgress);

        private void DrawCrowd()
        {
            _crowd.Clear();

            // Bodies first and faces last, so the handful of named ones are not buried. §5.2.2
            // is specific that a mob is anonymous bodies *with a handful of named faces inside
            // it*, and a picture you cannot pick them out of is only the first half of that.
            for (int i = 0; i < _port.Crowd.Count; i++)
                if (!_port.Crowd[i].IsNamed) _crowd.Add(BodyMarker(_port.Crowd[i]));

            for (int i = 0; i < _port.Crowd.Count; i++)
                if (_port.Crowd[i].IsNamed) _crowd.Add(BodyMarker(_port.Crowd[i]));
        }

        private VisualElement BuildingMarker(in PortBuilding building)
        {
            float size = building.IsSeat ? 46f : 34f;

            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.width = size;
            box.style.height = size;
            box.style.backgroundColor = building.Mothballed
                ? Shut
                : building.IsSeat ? Seat : Stone;

            box.style.borderTopLeftRadius = 3;
            box.style.borderTopRightRadius = 3;
            box.style.borderBottomLeftRadius = 3;
            box.style.borderBottomRightRadius = 3;

            // Damage shows on the building rather than in a number beside it. §5.2.2 has a riot
            // taking condition off every day, and a town visibly coming apart is the difference
            // between a consequence and a log line.
            if (building.Condition < 0.999f)
            {
                Color edge = Color.Lerp(Hurt, Stone, building.Condition);
                box.style.borderTopWidth = 2;
                box.style.borderRightWidth = 2;
                box.style.borderBottomWidth = 2;
                box.style.borderLeftWidth = 2;
                box.style.borderTopColor = edge;
                box.style.borderRightColor = edge;
                box.style.borderBottomColor = edge;
                box.style.borderLeftColor = edge;
            }

            var label = new Label(building.Name);
            label.style.position = Position.Absolute;
            label.style.top = size + 2f;
            label.style.left = -8f;
            label.style.fontSize = 10;
            label.style.color = building.Mothballed ? Faint : Ink;
            label.pickingMode = PickingMode.Ignore;
            box.Add(label);

            box.tooltip = building.Mothballed
                ? building.Name + " — shut"
                : $"{building.Name} — {building.Workers}/{building.Staff} worked, " +
                  $"{building.Condition * 100f:0}%";

            Place(box, building.At, size * 0.5f);
            return box;
        }

        private VisualElement BodyMarker(in PortBody body)
        {
            float radius = body.IsNamed ? FaceRadius : BodyRadius;
            Color colour = body.Side == MobSide.Loyalist ? Loyalist : Rioter;

            var dot = new VisualElement();
            dot.style.position = Position.Absolute;
            dot.style.width = radius * 2f;
            dot.style.height = radius * 2f;
            dot.style.backgroundColor = colour;
            dot.style.borderTopLeftRadius = radius;
            dot.style.borderTopRightRadius = radius;
            dot.style.borderBottomLeftRadius = radius;
            dot.style.borderBottomRightRadius = radius;
            dot.pickingMode = PickingMode.Ignore;

            if (body.IsNamed)
            {
                dot.style.borderTopWidth = 2;
                dot.style.borderRightWidth = 2;
                dot.style.borderBottomWidth = 2;
                dot.style.borderLeftWidth = 2;
                dot.style.borderTopColor = Ink;
                dot.style.borderRightColor = Ink;
                dot.style.borderBottomColor = Ink;
                dot.style.borderLeftColor = Ink;

                // The whole reason for the close-up. At map scale a face is a slightly larger
                // dot; here it is the carpenter, and which way they went is legible.
                var label = new Label(body.Name);
                label.style.position = Position.Absolute;
                label.style.left = radius * 2f + 2f;
                label.style.top = -1f;
                label.style.fontSize = 10;
                label.style.color = colour;
                label.pickingMode = PickingMode.Ignore;
                dot.Add(label);
            }

            Place(dot, body.At, radius);
            return dot;
        }

        /// <summary>
        /// Puts something at a place in the square, centred on it.
        /// </summary>
        /// <remarks>
        /// Percentages of <see cref="_square"/>, which is square by construction, so a crowd is
        /// round on a wide monitor and on a tall one.
        /// </remarks>
        private void Place(VisualElement element, in MapPoint at, float radius)
        {
            float span = _port.Radius <= 0f ? 1f : _port.Radius;

            element.style.left = new StyleLength(new Length(
                50f + (at.X / span * Fill * 100f), LengthUnit.Percent));

            // Flipped: the model counts northward and the screen counts down.
            element.style.top = new StyleLength(new Length(
                50f - (at.Y / span * Fill * 100f), LengthUnit.Percent));

            element.style.marginLeft = -radius;
            element.style.marginTop = -radius;
        }

        private static VisualElement Layer()
        {
            var layer = new VisualElement();
            layer.style.position = Position.Absolute;
            layer.style.left = 0;
            layer.style.top = 0;
            layer.style.right = 0;
            layer.style.bottom = 0;
            layer.pickingMode = PickingMode.Ignore;
            return layer;
        }
    }
}
