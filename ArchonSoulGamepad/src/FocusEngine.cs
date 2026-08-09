using System.Collections.Generic;
using UnityEngine;

namespace ArchonSoulGamepad
{
    /// <summary>
    /// Holds which element has focus and moves that focus spatially.
    /// There is no cursor: the focus rectangle is the entire interaction model,
    /// and the synthetic pointer simply follows it.
    /// </summary>
    internal class FocusEngine
    {
        private readonly List<Focusable> _candidates = new List<Focusable>();

        public GameObject Focused { get; private set; }
        public Rect FocusedRect { get; private set; }
        public Vector2 Center { get; private set; }
        public int CandidateCount { get { return _candidates.Count; } }

        /// <summary>Finds this screen's back/close control, if it has one.</summary>
        public bool TryGetBackControl(out GameObject go, out Vector2 center)
        {
            for (int i = 0; i < _candidates.Count; i++)
            {
                if (GameBridge.IsBackControl(_candidates[i].Go))
                {
                    go = _candidates[i].Go;
                    center = _candidates[i].Center;
                    return true;
                }
            }
            go = null;
            center = Vector2.zero;
            return false;
        }

        public string DescribeCandidates()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _candidates.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(_candidates[i].Go != null ? _candidates[i].Go.name : "<null>");
            }
            return sb.ToString();
        }

        public void DumpCandidateRects()
        {
            for (int i = 0; i < _candidates.Count; i++)
            {
                var c = _candidates[i];
                Plugin.LogInfo(string.Format("[rect] {0,-28} x{1,6:0}..{2,-6:0} y{3,6:0}..{4,-6:0} c=({5:0},{6:0})",
                    c.Go != null ? c.Go.name : "<null>",
                    c.ScreenRect.xMin, c.ScreenRect.xMax,
                    c.ScreenRect.yMin, c.ScreenRect.yMax,
                    c.Center.x, c.Center.y));
            }
        }

        /// <summary>Focuses the Nth candidate whose name matches, ordered top to bottom.</summary>
        public bool FocusByNameOccurrence(string namePart, int occurrence)
        {
            var matches = new List<int>();
            for (int i = 0; i < _candidates.Count; i++)
            {
                var go = _candidates[i].Go;
                if (go == null) continue;
                if (go.name.IndexOf(namePart, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    matches.Add(i);
            }

            if (matches.Count == 0) return false;
            matches.Sort((a, b) => _candidates[b].Center.y.CompareTo(_candidates[a].Center.y));

            int idx = Mathf.Clamp(occurrence, 1, matches.Count) - 1;
            SetFocus(matches[idx]);
            return true;
        }

        public bool FocusObject(GameObject go)
        {
            if (go == null) return false;
            for (int i = 0; i < _candidates.Count; i++)
            {
                if (_candidates[i].Go == go) { SetFocus(i); return true; }
            }
            return false;
        }

        public bool FocusByName(string namePart)        {
            for (int i = 0; i < _candidates.Count; i++)
            {
                var go = _candidates[i].Go;
                if (go == null) continue;
                if (go.name.IndexOf(namePart, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SetFocus(i);
                    return true;
                }
            }
            return false;
        }

        private float _nextScan;
        private float _nextEmptyReport;
        private const float ScanInterval = 0.15f;

        public void Rescan(bool includeDiceSlots, bool force = false)
        {
            if (!force && Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;

            // A held spell or modification component takes over navigation entirely.
            if (GameBridge.IsDraggingSpell() && BuildSpellAnchorCandidates())
            {
                ValidateFocus();
                return;
            }

            if (BuildFaceSlotCandidates())
            {
                ValidateFocus();
                return;
            }

            if (BuildComponentDropCandidates())
            {
                ValidateFocus();
                return;
            }

            _candidates.Clear();
            _candidates.AddRange(InteractableScanner.Scan(includeDiceSlots, Focused));

            // A screen with nothing focusable is always a bug. Immediately re-scan
            // with reject reporting so the log explains itself without anyone
            // having to reproduce it with a debug flag turned on.
            if (_candidates.Count == 0 && Time.unscaledTime >= _nextEmptyReport && !GameBridge.IsDraggingComponent())
            {
                _nextEmptyReport = Time.unscaledTime + 5f;
                InteractableScanner.DebugRejects = true;
                InteractableScanner.Scan(includeDiceSlots, Focused);
                InteractableScanner.DebugRejects = false;

                var rejects = InteractableScanner.DrainRejects();
                var msg = "no focusable elements found (canvases=" +
                          InteractableScanner.ExaminedRoots + "). rejects: " + rejects;

                // Nothing rejected means there was genuinely nothing to find,
                // which is normal on loading and splash screens.
                if (string.IsNullOrEmpty(rejects)) Plugin.LogDiag(msg);
                else Plugin.LogWarn(msg);

                _candidates.Clear();
                _candidates.AddRange(InteractableScanner.Scan(includeDiceSlots, Focused));
            }

            if (includeDiceSlots) RestrictToDropTargets();

            RestrictToTargetableUnits();

            FilterTopMenu();

            ValidateFocus();
        }

        /// <summary>
        /// The run's top bar is either the only thing selectable or entirely
        /// excluded, never mixed in with a screen's own controls.
        /// </summary>
        public bool TopMenuMode;

        private readonly List<Focusable> _topFiltered = new List<Focusable>();

        private void FilterTopMenu()
        {
            if (!GameBridge.HasTopMenu()) return;

            _topFiltered.Clear();
            for (int i = 0; i < _candidates.Count; i++)
            {
                if (GameBridge.IsTopMenuElement(_candidates[i].Go) == TopMenuMode)
                    _topFiltered.Add(_candidates[i]);
            }

            // Outside top-menu mode, a screen made up entirely of top bar controls
            // would otherwise leave nothing selectable at all.
            if (!TopMenuMode && _topFiltered.Count == 0) return;

            _candidates.Clear();
            _candidates.AddRange(_topFiltered);
        }

        private readonly List<GameObject> _targetUnits = new List<GameObject>();
        private readonly List<Focusable> _targetFiltered = new List<Focusable>();

        /// <summary>
        /// While a spell is asking for a target, only the units it can hit are
        /// selectable. Existing candidates are filtered rather than rebuilt, so the
        /// reachability work already done still applies.
        /// </summary>
        private void RestrictToTargetableUnits()
        {
            if (!GameBridge.TryGetTargetableUnits(_targetUnits)) return;

            _targetFiltered.Clear();
            for (int i = 0; i < _candidates.Count; i++)
            {
                var go = _candidates[i].Go;
                if (go == null) continue;

                foreach (var unit in _targetUnits)
                {
                    if (go == unit || go.transform.IsChildOf(unit.transform))
                    {
                        _targetFiltered.Add(_candidates[i]);
                        break;
                    }
                }
            }

            if (_targetFiltered.Count == 0) return;

            _candidates.Clear();
            _candidates.AddRange(_targetFiltered);
        }

        private readonly List<Transform> _faceSlotBuffer = new List<Transform>();

        /// <summary>Slot positions available while reordering the die's faces.</summary>
        public List<Transform> FaceSlots { get { return _faceSlotBuffer; } }

        private bool BuildFaceSlotCandidates()
        {
            if (!GameBridge.TryGetFaceSlotAnchors(_faceSlotBuffer)) return false;

            _candidates.Clear();

            foreach (var t in _faceSlotBuffer)
            {
                Vector2 sp;
                if (!InteractableScanner.TryGetScreenPoint(t, out sp)) continue;
                if (sp.x < 0f || sp.y < 0f || sp.x > Screen.width || sp.y > Screen.height) continue;

                _candidates.Add(new Focusable
                {
                    Go = t.gameObject,
                    ScreenRect = new Rect(sp.x - 55f, sp.y - 70f, 110f, 140f),
                    Center = sp,
                    IsDiceSlot = false,
                    IsWidget = false,
                    Navigable = true
                });
            }

            return true;
        }

        private readonly List<GameObject> _componentTargets = new List<GameObject>();

        /// <summary>
        /// Restricts focus to the slots a dragged face, rune or body can go into.
        /// </summary>
        private bool BuildComponentDropCandidates()
        {
            if (!GameBridge.TryGetDraggedComponentTargets(_componentTargets)) return false;

            _candidates.Clear();

            foreach (var go in _componentTargets)
            {
                Rect rect;
                if (!InteractableScanner.TryGetScreenRectFor(go, out rect)) continue;
                if (rect.width < 2f || rect.height < 2f) continue;

                var center = rect.center;
                if (center.x < 0f || center.y < 0f || center.x > Screen.width || center.y > Screen.height) continue;

                _candidates.Add(new Focusable
                {
                    Go = go,
                    ScreenRect = rect,
                    Center = center,
                    IsDiceSlot = false,
                    IsWidget = false,
                    Navigable = true
                });
            }

            // Deliberately true even with nothing to show. A face with no die to
            // modify has nowhere to go, and leaving the candidate list empty is
            // what stops focus falling back to the dice bag.
            return true;
        }

        private readonly List<Transform> _anchorBuffer = new List<Transform>();

        /// <summary>
        /// Replaces the candidate set with the spell screen's fixed slot anchors
        /// while a spell is held, so focus cannot chase the spells as they reflow.
        /// </summary>
        private bool BuildSpellAnchorCandidates()
        {
            if (!GameBridge.TryGetSpellSlotAnchors(_anchorBuffer)) return false;

            _candidates.Clear();

            foreach (var t in _anchorBuffer)
            {
                Vector2 sp;
                if (!InteractableScanner.TryGetScreenPoint(t, out sp)) continue;
                if (sp.x < 0f || sp.y < 0f || sp.x > Screen.width || sp.y > Screen.height) continue;

                var rect = new Rect(sp.x - 70f, sp.y - 90f, 140f, 180f);
                _candidates.Add(new Focusable
                {
                    Go = t.gameObject,
                    ScreenRect = rect,
                    Center = sp,
                    IsDiceSlot = false,
                    IsWidget = false,
                    Navigable = true
                });
            }

            return _candidates.Count > 0;
        }

        private readonly List<Focusable> _dropTargets = new List<Focusable>();

        /// <summary>
        /// While a die is held, focus is limited to places it can actually go.
        /// Otherwise the d-pad wanders across unrelated buttons and the player has
        /// to aim a die at a slot by eye, which is exactly what a cursor-free
        /// scheme is meant to avoid. Falls back progressively so a screen we do not
        /// understand never becomes unusable.
        /// </summary>
        private void RestrictToDropTargets()
        {
            _dropTargets.Clear();

            for (int i = 0; i < _candidates.Count; i++)
                if (_candidates[i].IsDiceSlot && GameBridge.SlotAcceptsCarriedDice(_candidates[i].Go))
                    _dropTargets.Add(_candidates[i]);

            if (_dropTargets.Count == 0)
                for (int i = 0; i < _candidates.Count; i++)
                    if (_candidates[i].IsDiceSlot)
                        _dropTargets.Add(_candidates[i]);

            if (_dropTargets.Count == 0) return;

            _candidates.Clear();
            _candidates.AddRange(_dropTargets);
        }

        /// <summary>
        /// Automatic acquisition must never select something that quits the game.
        /// Focus moves on its own whenever a screen changes, so a quit control being
        /// picked up implicitly is how an unattended press destroys a run. It stays
        /// reachable by deliberate directional input.
        /// </summary>
        private bool AutoAcquirable(int index)
        {
            return _candidates[index].Navigable && !GameBridge.IsDestructive(_candidates[index].Go);
        }

        public bool Pinned;

        private void ValidateFocus()
        {
            if (Focused != null)
            {
                for (int i = 0; i < _candidates.Count; i++)
                {
                    if (_candidates[i].Go == Focused)
                    {
                        FocusedRect = _candidates[i].ScreenRect;
                        Center = _candidates[i].Center;
                        return;
                    }
                }

                // While a widget is being edited focus must not wander, even if a
                // rescan briefly loses sight of it.
                if (Pinned && Focused.activeInHierarchy) return;
            }

            // Focus was lost (screen changed, element despawned). Re-acquire the
            // nearest sensible target to where the player was last looking.
            AcquireNearest(Focused != null ? Center : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        }

        public void AcquireNearest(Vector2 near)
        {
            int best = -1;
            float bestDist = float.MaxValue;

            for (int i = 0; i < _candidates.Count; i++)
            {
                if (!AutoAcquirable(i)) continue;
                float d = (_candidates[i].Center - near).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = i; }
            }

            SetFocus(best);
        }

        /// <summary>Prefer dice slots when the player is carrying a die.</summary>
        public void AcquirePreferred(Vector2 near, bool preferDiceSlots)
        {
            if (!preferDiceSlots) { AcquireNearest(near); return; }

            int best = -1;
            float bestDist = float.MaxValue;

            for (int i = 0; i < _candidates.Count; i++)
            {
                if (!_candidates[i].IsDiceSlot) continue;
                float d = (_candidates[i].Center - near).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            if (best >= 0) SetFocus(best);
            else AcquireNearest(near);
        }

        private void SetFocus(int index)
        {
            if (index < 0 || index >= _candidates.Count)
            {
                Focused = null;
                FocusedRect = default(Rect);
                return;
            }

            Focused = _candidates[index].Go;
            FocusedRect = _candidates[index].ScreenRect;
            Center = _candidates[index].Center;
        }

        /// <summary>
        /// Directional move, resolved in two passes. First we only consider targets
        /// that actually line up with the current one on the cross axis — pressing
        /// left from a slider should reach the control beside it, never a nearer
        /// row sitting diagonally below. Only if nothing lines up do we fall back to
        /// a wider cone, and finally to wrapping.
        /// </summary>
        public bool Move(Vector2 dir)
        {
            if (_candidates.Count == 0) return false;

            if (Focused == null)
            {
                AcquireNearest(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
                return Focused != null;
            }

            bool vertical = Mathf.Abs(dir.y) > Mathf.Abs(dir.x);

            int best = FindAligned(dir, vertical);
            if (best < 0) best = FindInCone(dir, vertical);
            if (best < 0) best = FindWrap(dir, vertical);
            if (best < 0) return false;

            SetFocus(best);
            return true;
        }

        /// <summary>Nearest candidate that genuinely overlaps on the cross axis.</summary>
        private int FindAligned(Vector2 dir, bool vertical)
        {
            int best = -1;
            float bestAlong = float.MaxValue;

            float ownExtent = vertical ? FocusedRect.width : FocusedRect.height;

            for (int i = 0; i < _candidates.Count; i++)
            {
                var c = _candidates[i];
                if (c.Go == Focused || !c.Navigable) continue;

                float along = Vector2.Dot(c.Center - Center, dir);
                if (along <= 1f) continue;

                float overlap = PerpendicularOverlap(FocusedRect, c.ScreenRect, vertical);
                float otherExtent = vertical ? c.ScreenRect.width : c.ScreenRect.height;
                float needed = Mathf.Min(20f, 0.3f * Mathf.Min(ownExtent, otherExtent));

                if (overlap < needed) continue;

                if (along < bestAlong) { bestAlong = along; best = i; }
            }

            return best;
        }

        private int FindInCone(Vector2 dir, bool vertical)
        {
            int best = -1;
            float bestScore = float.MaxValue;

            for (int i = 0; i < _candidates.Count; i++)
            {
                var c = _candidates[i];
                if (c.Go == Focused || !c.Navigable) continue;

                float along = Vector2.Dot(c.Center - Center, dir);
                if (along <= 1f) continue;

                float perpGap = PerpendicularGap(FocusedRect, c.ScreenRect, vertical);
                if (perpGap > along * 2f + 50f) continue;

                float score = along + perpGap * 3f;
                if (score < bestScore) { bestScore = score; best = i; }
            }

            return best;
        }

        /// <summary>Length of shared span on the cross axis; negative means a gap.</summary>
        private static float PerpendicularOverlap(Rect a, Rect b, bool vertical)
        {
            if (vertical) return Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            return Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        }

        /// <summary>Zero when the rectangles overlap on the cross axis.</summary>
        private static float PerpendicularGap(Rect a, Rect b, bool vertical)
        {
            if (vertical)
            {
                if (a.xMax > b.xMin && a.xMin < b.xMax) return 0f;
                return a.xMin > b.xMax ? a.xMin - b.xMax : b.xMin - a.xMax;
            }

            if (a.yMax > b.yMin && a.yMin < b.yMax) return 0f;
            return a.yMin > b.yMax ? a.yMin - b.yMax : b.yMin - a.yMax;
        }

        private int FindWrap(Vector2 dir, bool vertical)
        {
            int best = -1;
            float bestProj = float.MaxValue;

            for (int i = 0; i < _candidates.Count; i++)
            {
                var c = _candidates[i];
                if (c.Go == Focused || !c.Navigable) continue;

                float perpGap = PerpendicularGap(FocusedRect, c.ScreenRect, vertical);
                if (perpGap > 60f) continue;

                float proj = Vector2.Dot(c.Center, dir);
                if (proj < bestProj) { bestProj = proj; best = i; }
            }

            return best;
        }

        public void Clear()
        {
            Focused = null;
            _candidates.Clear();
            FocusedRect = default(Rect);
        }

        /// <summary>
        /// Jumps between clusters of interactables (dice pool, spell slots, action
        /// buttons...). Clusters are derived from shared parent containers, which
        /// matches how the game's screens are actually laid out, so this behaves
        /// like a hand-authored navigation map without hard-coding each screen.
        /// </summary>
        public bool CycleGroup(int direction)
        {
            if (_candidates.Count == 0 || Focused == null) return false;

            var groups = new List<Transform>();
            var groupCenters = new List<Vector2>();

            for (int i = 0; i < _candidates.Count; i++)
            {
                if (!_candidates[i].Navigable) continue;
                var parent = GroupKey(_candidates[i].Go);
                if (parent == null) continue;

                int idx = groups.IndexOf(parent);
                if (idx < 0) { groups.Add(parent); groupCenters.Add(_candidates[i].Center); }
            }

            if (groups.Count < 2) return false;

            var currentKey = GroupKey(Focused);
            int cur = groups.IndexOf(currentKey);
            if (cur < 0) cur = 0;

            int next = ((cur + direction) % groups.Count + groups.Count) % groups.Count;
            var targetGroup = groups[next];

            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _candidates.Count; i++)
            {
                if (GroupKey(_candidates[i].Go) != targetGroup) continue;
                float d = (_candidates[i].Center - Center).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = i; }
            }

            if (best < 0) return false;
            SetFocus(best);
            return true;
        }

        private static Transform GroupKey(GameObject go)
        {
            if (go == null) return null;
            var t = go.transform.parent;
            return t != null ? t : go.transform;
        }
    }
}
