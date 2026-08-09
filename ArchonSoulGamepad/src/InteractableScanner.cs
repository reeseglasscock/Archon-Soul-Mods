using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ArchonSoulGamepad
{
    internal struct Focusable
    {
        public GameObject Go;
        public Rect ScreenRect;
        public Vector2 Center;
        public bool IsDiceSlot;
        public bool IsWidget;

        /// <summary>False for controls that exist only as a B-button action.</summary>
        public bool Navigable;
    }

    /// <summary>
    /// Finds everything on screen the player could meaningfully interact with.
    /// Rather than hard-coding the game's screens, we look for the interfaces the
    /// game already uses (IPointerClickHandler / IPointerDownHandler / Selectable)
    /// and then confirm each candidate is genuinely hittable with a raycast, which
    /// automatically handles modal popups, faded-out panels and disabled raycast targets.
    /// </summary>
    internal static class InteractableScanner
    {
        private static readonly List<MonoBehaviour> Buffer = new List<MonoBehaviour>();
        private static readonly List<Focusable> Result = new List<Focusable>();
        private static readonly Vector3[] Corners = new Vector3[4];

        // Menu items animate while hovered, so requiring a *static* rectangle makes
        // focus fight its own hover effect. Instead an element must simply have been
        // present in the previous scan, which still rejects transient objects and
        // panels mid-slide (those fail the on-screen test) without the feedback loop.
        private static HashSet<int> _prevSeen = new HashSet<int>();
        private static HashSet<int> _curSeen = new HashSet<int>();

        public static void ResetStability()
        {
            _prevSeen.Clear();
            _curSeen.Clear();
        }

        public static bool DebugRejects;
        private static readonly List<string> Rejects = new List<string>();

        public static string DrainRejects()
        {
            var s = string.Join(" | ", Rejects.ToArray());
            Rejects.Clear();
            return s;
        }

        private static bool Reject(GameObject go, string reason)
        {
            if (DebugRejects && Rejects.Count < 60)
                Rejects.Add(go.name + ":" + reason);
            return false;
        }

        public static List<Focusable> Scan(bool includeDiceSlots, GameObject keepAlways)
        {
            Result.Clear();
            _curSeen.Clear();
            var seen = new HashSet<GameObject>();

            // Walk root canvases rather than scene roots: UI parented under
            // DontDestroyOnLoad lives in a scene that SceneManager does not
            // enumerate, which would make whole in-run screens invisible here.
            Canvas[] canvases;
            try { canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None); }
            catch { canvases = new Canvas[0]; }

            ExaminedRoots = 0;

            foreach (var canvas in canvases)
            {
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                if (!canvas.isRootCanvas) continue;

                var root = canvas.gameObject;
                if (!root.activeInHierarchy) continue;
                ExaminedRoots++;

                Buffer.Clear();
                try { root.GetComponentsInChildren(false, Buffer); }
                catch { continue; }

                foreach (var mb in Buffer)
                {
                    if (mb == null) continue;
                    var go = mb.gameObject;
                    if (!go.activeInHierarchy) continue;
                    if (seen.Contains(go)) continue;

                    bool isSlot = GameBridge.IsDiceSlot(mb);
                    if (!Qualifies(mb, isSlot, includeDiceSlots))
                    {
                        if (DebugRejects && (mb is Selectable || mb is IPointerClickHandler))
                            Reject(go, "not-qualified(" + mb.GetType().Name + ")");
                        continue;
                    }

                    // Collapse settings rows (arrow buttons, slider handles) into
                    // the single widget the player thinks of as "the setting".
                    var widgetRoot = GameBridge.GetWidgetRoot(mb);
                    bool isWidget = widgetRoot != null;
                    if (isWidget) go = widgetRoot;
                    if (seen.Contains(go)) continue;

                    Rect rect;
                    if (!TryGetScreenRect(go, out rect)) { Reject(go, "no-rect"); continue; }
                    if (rect.width < 4f || rect.height < 4f) { Reject(go, "too-small"); continue; }

                    var center = rect.center;
                    if (!IsFullyOnScreen(rect, center))
                    {
                        Reject(go, string.Format("offscreen(rect={0},screen={1}x{2})",
                            rect, Screen.width, Screen.height));
                        continue;
                    }

                    int id = go.GetInstanceID();
                    _curSeen.Add(id);

                    // The focused element is exempt: it is the one we are
                    // actively hovering, so it is expected to be moving.
                    if (!_prevSeen.Contains(id) && go != keepAlways) { Reject(go, "new-this-scan"); continue; }

                    if (!IsHittable(go, center, rect) && !(isWidget && IsWidgetReachable(go)))
                    { Reject(go, "not-hittable"); continue; }

                    seen.Add(go);
                    Result.Add(new Focusable
                    {
                        Go = go,
                        ScreenRect = rect,
                        Center = center,
                        IsDiceSlot = isSlot,
                        IsWidget = isWidget,
                        Navigable = !GameBridge.IsBackControl(go)
                    });
                }
            }

            var swap = _prevSeen;
            _prevSeen = _curSeen;
            _curSeen = swap;

            return Result;
        }

        public static int ExaminedRoots { get; private set; }

        private static bool Qualifies(MonoBehaviour mb, bool isSlot, bool includeDiceSlots)
        {
            if (isSlot) return includeDiceSlots;

            var sel = mb as Selectable;
            if (sel != null) return sel.interactable && sel.IsActive();

            if (mb is IPointerClickHandler) return true;
            if (mb is IPointerDownHandler) return true;
            return false;
        }

        private static bool TryGetScreenRect(GameObject go, out Rect rect)
        {
            if (!TryGetRawScreenRect(go, out rect)) return false;

            // A settings row's own RectTransform covers only the label; its arrows
            // live outside it. Expand to enclose the controls so the focus outline
            // matches what the player sees as "the setting".
            if (GameBridge.IsEditableWidget(go))
            {
                var sels = go.GetComponentsInChildren<Selectable>(false);
                foreach (var sel in sels)
                {
                    if (sel == null || !sel.IsActive()) continue;
                    Rect r;
                    if (!TryGetRawScreenRect(sel.gameObject, out r)) continue;
                    rect = Encapsulate(rect, r);
                }
            }

            return true;
        }

        private static Rect Encapsulate(Rect a, Rect b)
        {
            float xMin = Mathf.Min(a.xMin, b.xMin);
            float yMin = Mathf.Min(a.yMin, b.yMin);
            float xMax = Mathf.Max(a.xMax, b.xMax);
            float yMax = Mathf.Max(a.yMax, b.yMax);
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        public static bool TryGetScreenRectFor(GameObject go, out Rect rect)
        {
            return TryGetScreenRect(go, out rect);
        }

        /// <summary>Screen position of an arbitrary transform, using its canvas camera.</summary>
        public static bool TryGetScreenPoint(Transform t, out Vector2 point)
        {
            point = Vector2.zero;
            if (t == null) return false;

            var canvas = t.GetComponentInParent<Canvas>();
            Camera cam = null;
            if (canvas != null)
            {
                var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
                if (root.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    cam = root.worldCamera;
                    if (cam == null) cam = Camera.main;
                }
            }
            else
            {
                cam = Camera.main;
            }

            try { point = RectTransformUtility.WorldToScreenPoint(cam, t.position); }
            catch { return false; }

            return true;
        }

        private static bool TryGetRawScreenRect(GameObject go, out Rect rect)
        {
            rect = default(Rect);
            var rt = go.transform as RectTransform;
            if (rt == null) return false;

            var canvas = rt.GetComponentInParent<Canvas>();
            if (canvas == null) return false;

            // Render mode and camera are only meaningful on the root canvas. In-run
            // screens nest canvases and are driven in Screen Space - Camera, so
            // reading these from the nearest canvas produced garbage coordinates and
            // made every control look unreachable.
            var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;

            Camera cam = null;
            if (root.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                cam = root.worldCamera;
                if (cam == null) cam = Camera.main;
            }

            try { rt.GetWorldCorners(Corners); }
            catch { return false; }

            var p0 = RectTransformUtility.WorldToScreenPoint(cam, Corners[0]);
            var p2 = RectTransformUtility.WorldToScreenPoint(cam, Corners[2]);

            float xMin = Mathf.Min(p0.x, p2.x);
            float yMin = Mathf.Min(p0.y, p2.y);
            rect = new Rect(xMin, yMin, Mathf.Abs(p2.x - p0.x), Mathf.Abs(p2.y - p0.y));
            return true;
        }

        /// <summary>
        /// Judged on the centre plus a visibility fraction, not on the whole
        /// rectangle. Real controls routinely overhang a screen edge by a few dozen
        /// pixels (the character select Start Run and Back buttons both do), and
        /// demanding full containment wrongly makes them unreachable. Panels that
        /// are genuinely slid away have their centre well outside the screen.
        /// </summary>
        private static bool IsFullyOnScreen(Rect rect, Vector2 center)
        {
            const float margin = 2f;
            if (center.x < margin || center.y < margin) return false;
            if (center.x > Screen.width - margin || center.y > Screen.height - margin) return false;

            float visW = Mathf.Min(rect.xMax, Screen.width) - Mathf.Max(rect.xMin, 0f);
            float visH = Mathf.Min(rect.yMax, Screen.height) - Mathf.Max(rect.yMin, 0f);
            if (visW <= 0f || visH <= 0f) return false;

            float area = rect.width * rect.height;
            return area <= 0f || (visW * visH) / area >= 0.25f;
        }

        /// <summary>
        /// A candidate only counts if a raycast at one of its sample points actually
        /// reaches it (or one of its relatives). This is what makes the scanner
        /// screen-agnostic: covered elements simply stop qualifying.
        /// </summary>
        private static bool IsHittable(GameObject go, Vector2 center, Rect rect)
        {
            if (Probe(go, center)) return true;

            float dx = rect.width * 0.25f;
            float dy = rect.height * 0.25f;

            if (Probe(go, new Vector2(center.x - dx, center.y))) return true;
            if (Probe(go, new Vector2(center.x + dx, center.y))) return true;
            if (Probe(go, new Vector2(center.x, center.y - dy))) return true;
            if (Probe(go, new Vector2(center.x, center.y + dy))) return true;

            return false;
        }

        /// <summary>
        /// A collapsed widget's centre is often an unclickable label, so reachability
        /// is judged by whether any of its own controls can be hit.
        /// </summary>
        private static bool IsWidgetReachable(GameObject root)
        {
            var children = root.GetComponentsInChildren<Selectable>(false);
            foreach (var sel in children)
            {
                if (sel == null || !sel.IsActive() || !sel.interactable) continue;

                Rect r;
                if (!TryGetScreenRect(sel.gameObject, out r)) continue;
                if (Probe(sel.gameObject, r.center)) return true;
            }
            return false;
        }

        private static bool Probe(GameObject go, Vector2 point)
        {
            var hit = PointerDispatcher.RaycastTop(point);
            if (hit == null) return false;
            return IsRelated(hit, go);
        }

        private static bool IsRelated(GameObject hit, GameObject target)
        {
            if (hit == target) return true;

            var t = hit.transform;
            while (t != null)
            {
                if (t.gameObject == target) return true;
                t = t.parent;
            }

            t = target.transform;
            while (t != null)
            {
                if (t.gameObject == hit) return true;
                t = t.parent;
            }

            return false;
        }
    }
}
