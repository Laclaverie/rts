using System.Collections.Generic;
using RTS.Sim.Components;
using RTS.Sim.Presentation;
using RTS.Sim.Session;
using UnityEngine;
using UnityEngine.UIElements;

// Unity 6 introduced UnityEngine.EntityId, which collides with ours by name. Aliased rather
// than renamed: the collision is on this side of the boundary, and a simulation type does not
// get renamed because an engine took the word. That the whole cost of the clash is one line in
// one file is the point of ARCHITECTURE §2.2 — Sim does not reference UnityEngine, so nothing
// over there had to know.
using EntityId = RTS.Sim.Engine.Entities.EntityId;

namespace RTS.Game.Presentation
{
    /// <summary>
    /// Draws the world: cities where the content puts them, and ships between them
    /// (BUILD_ORDER Phase 5). Deliberately ugly, like everything else on screen so far.
    /// </summary>
    /// <remarks>
    /// A binder, exactly like <see cref="PortPanel"/>. Where a city is, where a ship has got to
    /// and what clicking one means all come from <see cref="MapModel"/> and
    /// <see cref="GameSession"/>, which know nothing about Unity and are tested without one.
    /// What is decided here is what a place looks like — a colour, a radius, which way up the
    /// screen counts.
    /// <para>
    /// <strong>Why a map at all, when the list worked.</strong> P1 says wealth is cargo, cargo
    /// moves along a route, and anything on the map can be intercepted. A convoy that was a
    /// countdown in a list is a countdown; the same convoy as a ship halfway to Ironhold is
    /// something a player can imagine losing. Raids and escorts are Phase 4 and 6 business, but
    /// they attach to this.
    /// </para>
    /// </remarks>
    public sealed class MapPanel
    {
        /// <summary>
        /// How much of the view is left empty around the outermost cities, per side.
        /// </summary>
        /// <remarks>
        /// Without it the extreme cities sit exactly on the edges, half of each marker off
        /// screen, because the bounds are the cities themselves.
        /// </remarks>
        private const float Margin = 0.10f;

        /// <summary>
        /// How much is left clear down the left-hand side, for the port card to sit in.
        /// </summary>
        /// <remarks>
        /// Wider than the others because the card is there. The first screenshot of this panel
        /// had Coldwater and Fairhaven underneath it — drawn, labelled, and impossible to click,
        /// which is a worse failure than not drawing them at all.
        /// <para>
        /// A fraction rather than the card's measured width: the card is a Phase 3 scaffold due
        /// to be replaced, and a map that reached across the scene graph to measure it would
        /// have to be untangled first.
        /// </para>
        /// </remarks>
        private const float LeftMargin = 0.36f;

        private const float PortRadius = 9f;
        private const float ConvoyRadius = 4f;
        private const float BodyRadius = 3f;
        private const float FaceRadius = 4.5f;

        private static readonly Color Sea = new Color(0.07f, 0.10f, 0.16f);
        private static readonly Color Lane = new Color(1f, 1f, 1f, 0.14f);
        private static readonly Color Wake = new Color(0.55f, 0.78f, 1f, 0.45f);
        private static readonly Color Home = new Color(0.45f, 0.80f, 1f);
        private static readonly Color Neighbour = new Color(0.75f, 0.72f, 0.62f);
        private static readonly Color Highlight = Color.white;
        private static readonly Color Mine = new Color(0.55f, 0.85f, 1f);
        private static readonly Color Theirs = new Color(0.85f, 0.70f, 0.45f);
        private static readonly Color Rioter = new Color(0.90f, 0.35f, 0.30f);
        private static readonly Color Loyalist = new Color(0.55f, 0.80f, 0.95f);
        private static readonly Color Face = new Color(1f, 0.92f, 0.55f);

        private readonly GameSession _session;
        private readonly Dictionary<EntityId, VisualElement> _portMarkers =
            new Dictionary<EntityId, VisualElement>();
        private readonly Dictionary<EntityId, VisualElement> _convoyMarkers =
            new Dictionary<EntityId, VisualElement>();
        private readonly List<EntityId> _gone = new List<EntityId>();

        private VisualElement _lanes;
        private VisualElement _crowd;
        private VisualElement _cities;
        private VisualElement _ships;
        private MapModel _map = MapModel.Empty;
        private EntityId _drawnSelection;
        private int _drawnRisen;

        public MapPanel(GameSession session) => _session = session;

        public VisualElement Root { get; private set; }

        /// <summary>Raised when a click changed what is selected, so the orders can be redrawn.</summary>
        public System.Action SelectionChanged { get; set; }

        /// <summary>
        /// Raised when a city already selected is clicked again.
        /// </summary>
        /// <remarks>
        /// The way into a city. The map does not know what looking inside one means — it reports
        /// that the player asked twice, and something else decides that is a door.
        /// </remarks>
        public System.Action<EntityId> SelectionRepeated { get; set; }

        /// <summary>The world as it was last drawn. For tests, and for anything else that asks.</summary>
        public MapModel Map => _map;

        public VisualElement Build()
        {
            Root = new VisualElement();
            Root.style.position = Position.Absolute;
            Root.style.left = 0;
            Root.style.top = 0;
            Root.style.right = 0;
            Root.style.bottom = 0;
            Root.style.backgroundColor = Sea;

            // Clicking the water looks back at your own city, which is the way out of a
            // selection without hunting for the right marker.
            Root.RegisterCallback<ClickEvent>(_ => Choose(_session.PlayerPort));

            _lanes = new VisualElement();
            Fill(_lanes);
            _lanes.pickingMode = PickingMode.Ignore;
            _lanes.generateVisualContent += DrawLanes;
            Root.Add(_lanes);

            _cities = new VisualElement();
            Fill(_cities);
            _cities.pickingMode = PickingMode.Ignore;
            Root.Add(_cities);

            // Under the ships and the cities, so a marker is never lost inside its own mob.
            _crowd = new VisualElement();
            Fill(_crowd);
            _crowd.pickingMode = PickingMode.Ignore;
            Root.Add(_crowd);

            _ships = new VisualElement();
            Fill(_ships);
            _ships.pickingMode = PickingMode.Ignore;
            Root.Add(_ships);

            Refresh();
            return Root;
        }

        /// <summary>
        /// Rebuilds the cities. They only change when the world does, which is once a day at
        /// most, so this is called on the same beat as the rest of the panel.
        /// </summary>
        public void Refresh()
        {
            _map = MapModel.Of(_session.World, _session.Balance, _session.Clock.DayProgress);
            _drawnSelection = _session.Selected;
            _drawnRisen = Risen();

            _cities.Clear();
            _portMarkers.Clear();

            foreach (MapPort port in _map.Ports)
            {
                VisualElement marker = PortMarker(in port);
                _portMarkers[port.Id] = marker;
                _cities.Add(marker);
            }

            Tick();
        }

        /// <summary>
        /// Moves the ships. Called every frame, unlike everything else on screen.
        /// </summary>
        /// <remarks>
        /// The one thing here that has to run at frame rate: a convoy that only moved on the day
        /// boundary would jump five times and arrive, which is the list again with a longer
        /// animation. The position it moves to is still computed in <c>Sim</c> from the day
        /// fraction; nothing derived from a frame reaches the world (§7.1).
        /// </remarks>
        public void Tick()
        {
            if (_ships == null) return;

            // Whoever changed the selection, the highlight follows it. The map used to redraw
            // only on clicks it handled itself, so a selection changed from anywhere else — a
            // keyboard shortcut, a console, whatever comes next — left the ring on the wrong
            // city with nothing to say it was wrong.
            if (_drawnSelection != _session.Selected)
            {
                Refresh();
                return;
            }

            _map = MapModel.Of(_session.World, _session.Balance, _session.Clock.DayProgress);

            foreach (MapConvoy convoy in _map.Convoys)
            {
                if (!_convoyMarkers.TryGetValue(convoy.Id, out VisualElement marker))
                {
                    marker = ConvoyMarker(in convoy);
                    _convoyMarkers[convoy.Id] = marker;
                    _ships.Add(marker);
                }

                Place(marker, convoy.At, ConvoyRadius);
                marker.tooltip = $"{convoy.Units:0.#} {Good(convoy.GoodIndex)}, " +
                                 $"{convoy.DaysRemaining}d out";
            }

            DrawCrowd();
            Prune();
            _lanes.MarkDirtyRepaint();

            // A city that has just risen, or just gone quiet, changes colour. Checked here
            // rather than on the day boundary because the crowd is what says so, and the crowd
            // is read every frame anyway.
            if (Risen() != _drawnRisen) Refresh();
        }

        /// <summary>
        /// The bodies in every square that has one.
        /// </summary>
        /// <remarks>
        /// Rebuilt outright rather than tracked per body, unlike the convoys. A crowd exists for
        /// a handful of days, is bounded at dozens by content, and every one of them moves every
        /// frame — the bookkeeping to reuse the elements would cost more than the elements do,
        /// and it is the kind of cleverness that outlives the reason for it.
        /// <para>
        /// The named faces are drawn larger and last, so the handful of them are not lost in the
        /// crowd. §5.2.2 is specific that a mob is anonymous bodies <em>with a handful of named
        /// faces inside it</em>, and a picture where you cannot pick them out is only the first
        /// half of that.
        /// </para>
        /// </remarks>
        private void DrawCrowd()
        {
            _crowd.Clear();
            if (_map.Crowd.Count == 0) return;

            for (int i = 0; i < _map.Crowd.Count; i++)
            {
                MapBody body = _map.Crowd[i];
                if (body.IsNamed) continue;

                _crowd.Add(Body(in body));
            }

            for (int i = 0; i < _map.Crowd.Count; i++)
            {
                MapBody body = _map.Crowd[i];
                if (!body.IsNamed) continue;

                _crowd.Add(Body(in body));
            }
        }

        private VisualElement Body(in MapBody body)
        {
            float radius = body.IsNamed ? FaceRadius : BodyRadius;

            var dot = new VisualElement();
            dot.style.position = Position.Absolute;
            dot.style.width = radius * 2f;
            dot.style.height = radius * 2f;
            dot.style.backgroundColor = body.IsNamed
                ? Face
                : body.Side == MobSide.Loyalist ? Loyalist : Rioter;
            Round(dot, radius);
            dot.pickingMode = PickingMode.Ignore;

            // A named face keeps its side as an outline, so which way the carpenter went is
            // readable without a tooltip.
            if (body.IsNamed)
            {
                Color edge = body.Side == MobSide.Loyalist ? Loyalist : Rioter;
                dot.style.borderTopWidth = 2;
                dot.style.borderRightWidth = 2;
                dot.style.borderBottomWidth = 2;
                dot.style.borderLeftWidth = 2;
                dot.style.borderTopColor = edge;
                dot.style.borderRightColor = edge;
                dot.style.borderBottomColor = edge;
                dot.style.borderLeftColor = edge;
            }

            Place(dot, body.At, radius);
            return dot;
        }

        /// <summary>Drops the markers of convoys that have landed.</summary>
        private void Prune()
        {
            _gone.Clear();

            foreach (KeyValuePair<EntityId, VisualElement> pair in _convoyMarkers)
            {
                bool afloat = false;
                for (int i = 0; i < _map.Convoys.Count && !afloat; i++)
                    afloat = _map.Convoys[i].Id == pair.Key;

                if (!afloat) _gone.Add(pair.Key);
            }

            foreach (EntityId id in _gone)
            {
                _ships.Remove(_convoyMarkers[id]);
                _convoyMarkers.Remove(id);
            }
        }

        /// <summary>
        /// Which cities have somebody in the square, as a bitmask over the map's own order.
        /// </summary>
        /// <remarks>
        /// Cheap enough to ask every frame, and it is what decides whether the city markers need
        /// rebuilding. Beyond sixty-four cities it stops distinguishing them, which is a long way
        /// past the five §8.1 asks for and would be a wrong answer rather than a crash.
        /// </remarks>
        private int Risen()
        {
            int mask = 0;

            for (int i = 0; i < _map.Crowd.Count; i++)
            {
                for (int p = 0; p < _map.Ports.Count && p < 31; p++)
                    if (_map.Ports[p].Id == _map.Crowd[i].Port) mask |= 1 << p;
            }

            return mask;
        }

        private bool HasCrowd(EntityId port)
        {
            for (int i = 0; i < _map.Crowd.Count; i++)
                if (_map.Crowd[i].Port == port) return true;

            return false;
        }

        private VisualElement PortMarker(in MapPort port)
        {
            bool selected = port.Id == _session.Selected;

            // Not read from the rung. §5.6 makes a neighbour's unrest something you buy with a
            // stance or a scout, and this is the one thing about it anybody can see from the
            // water: there are people in their square. The crowd says so, so the marker may.
            bool risen = HasCrowd(port.Id);

            var dot = new VisualElement();
            dot.style.position = Position.Absolute;
            dot.style.width = PortRadius * 2f;
            dot.style.height = PortRadius * 2f;
            dot.style.backgroundColor = risen ? Rioter : port.IsPlayer ? Home : Neighbour;
            Round(dot, PortRadius);

            dot.style.borderTopWidth = 2;
            dot.style.borderRightWidth = 2;
            dot.style.borderBottomWidth = 2;
            dot.style.borderLeftWidth = 2;

            Color edge = selected ? Highlight : Color.clear;
            dot.style.borderTopColor = edge;
            dot.style.borderRightColor = edge;
            dot.style.borderBottomColor = edge;
            dot.style.borderLeftColor = edge;

            var label = new Label(port.Name);
            label.style.position = Position.Absolute;
            label.style.left = PortRadius * 2f + 4f;
            label.style.top = -2f;
            label.style.color = selected ? Highlight : new Color(0.85f, 0.85f, 0.85f);
            label.pickingMode = PickingMode.Ignore;
            dot.Add(label);

            EntityId id = port.Id;
            dot.RegisterCallback<ClickEvent>(e => { Choose(id); e.StopPropagation(); });
            dot.tooltip = port.Name;

            Place(dot, port.At, PortRadius);
            return dot;
        }

        private VisualElement ConvoyMarker(in MapConvoy convoy)
        {
            var dot = new VisualElement();
            dot.style.position = Position.Absolute;
            dot.style.width = ConvoyRadius * 2f;
            dot.style.height = ConvoyRadius * 2f;
            dot.style.backgroundColor = convoy.IsPlayers ? Mine : Theirs;
            Round(dot, ConvoyRadius);
            dot.pickingMode = PickingMode.Ignore;

            return dot;
        }

        /// <summary>
        /// The sea lanes a convoy is currently on, drawn behind everything.
        /// </summary>
        /// <remarks>
        /// Only lanes with a ship on them. Every city joined to every other is a mesh that says
        /// nothing; a line that appears when you commit to a crossing is the commitment §5.1
        /// wants a route to be.
        /// </remarks>
        private void DrawLanes(MeshGenerationContext context)
        {
            if (_map.Convoys.Count == 0) return;

            Rect area = _lanes.contentRect;
            if (area.width <= 0f || area.height <= 0f) return;

            Painter2D painter = context.painter2D;
            painter.lineWidth = 1f;
            painter.strokeColor = Lane;

            for (int i = 0; i < _map.Convoys.Count; i++)
            {
                MapConvoy convoy = _map.Convoys[i];

                painter.BeginPath();
                painter.MoveTo(Pixels(convoy.From, area));
                painter.LineTo(Pixels(convoy.To, area));
                painter.Stroke();
            }

            painter.strokeColor = Wake;
            painter.lineWidth = 2f;

            for (int i = 0; i < _map.Convoys.Count; i++)
            {
                MapConvoy convoy = _map.Convoys[i];

                painter.BeginPath();
                painter.MoveTo(Pixels(convoy.From, area));
                painter.LineTo(Pixels(convoy.At, area));
                painter.Stroke();
            }
        }

        private void Choose(EntityId port)
        {
            if (!_session.Select(port))
            {
                // Already selected. Asking for the same city twice is asking to go in.
                if (port == _session.Selected && !port.IsNone) SelectionRepeated?.Invoke(port);
                return;
            }

            Refresh();
            SelectionChanged?.Invoke();
        }

        /// <summary>
        /// Puts an element at a place in the world, centred on it.
        /// </summary>
        /// <remarks>
        /// Percentages rather than pixels, so the map fills whatever it is given without anyone
        /// having to be told the window resized. The offset in pixels re-centres the marker on
        /// the point, since a percentage positions its top-left corner.
        /// </remarks>
        private void Place(VisualElement element, in MapPoint at, float radius)
        {
            MapPoint unit = _map.Bounds.Normalize(at);

            element.style.left = Length.Percent(Inset(unit.X) * 100f);

            // Flipped: the content counts northward and the screen counts down. The model
            // deliberately does not do this — a printout or a minimap drawn the other way would
            // want it the other way, and only the thing drawing knows.
            element.style.top = Length.Percent(InsetY(1f - unit.Y) * 100f);

            element.style.marginLeft = -radius;
            element.style.marginTop = -radius;
        }

        private Vector2 Pixels(in MapPoint at, in Rect area)
        {
            MapPoint unit = _map.Bounds.Normalize(at);

            return new Vector2(
                area.x + (Inset(unit.X) * area.width),
                area.y + (InsetY(1f - unit.Y) * area.height));
        }

        private static float Inset(float unit) =>
            LeftMargin + (unit * (1f - LeftMargin - Margin));

        private static float InsetY(float unit) => Margin + (unit * (1f - (2f * Margin)));

        private string Good(int index)
        {
            var goods = _session.Balance.Goods;
            return index >= 0 && index < goods.Count ? goods[index].Id : "cargo";
        }

        private static void Fill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0;
            element.style.top = 0;
            element.style.right = 0;
            element.style.bottom = 0;
        }

        private static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
