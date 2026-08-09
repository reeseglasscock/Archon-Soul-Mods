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
        private const float ScanInterval = 0.15f;

        public void Rescan(bool includeDiceSlots, bool force = false)
        {
            if (!force && Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;

            _candidates.Clear();
            _candidates.AddRange(InteractableScanner.Scan(includeDiceSlots, Focused));

            ValidateFocus();
        }

        /// <summary>
        /// Automatic acquisition must never select something that quits the game.
        /// Focus moves on its own whenever a screen changes, so a quit control being
        /// picked up implicitly is how an unattended press destroys a run. It stays
        /// reachable by deliberate directional input.
        /// </summary>
        private bool AutoAcquirable(int index)
        {
            return !GameBridge.IsDestructive(_candidates[index].Go);
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
                if (c.Go == Focused) continue;

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
                if (c.Go == Focused) continue;

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
                if (c.Go == Focused) continue;

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
