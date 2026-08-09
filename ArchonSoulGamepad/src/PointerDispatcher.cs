using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ArchonSoulGamepad
{
    /// <summary>
    /// Sends uGUI pointer events straight into the game's existing handlers.
    /// This is deliberately independent of any input module or OS cursor:
    /// we synthesise the exact events a real mouse would have produced.
    /// </summary>
    internal static class PointerDispatcher
    {
        private static readonly List<GameObject> HoverChain = new List<GameObject>();
        private static readonly List<GameObject> TempChain = new List<GameObject>();
        private static readonly List<RaycastResult> RaycastBuffer = new List<RaycastResult>();

        public static GameObject CurrentHover { get; private set; }

        private static PointerEventData NewEvent(Vector2 pos, PointerEventData.InputButton button)
        {
            var es = EventSystem.current;
            if (es == null) return null;

            return new PointerEventData(es)
            {
                position = pos,
                pointerId = button == PointerEventData.InputButton.Right ? -2 : -1,
                button = button,
                clickCount = 1,
                clickTime = Time.unscaledTime,
                delta = Vector2.zero,
                pressPosition = pos,
                useDragThreshold = false
            };
        }

        /// <summary>
        /// Mirrors EventSystem hover semantics: enter/exit are propagated through
        /// the ancestor chain, which many of the game's tooltip and highlight
        /// components rely on (they live on parent objects).
        /// </summary>
        public static void SetHover(GameObject target, Vector2 pos)
        {
            if (CurrentHover == target) return;

            var data = NewEvent(pos, PointerEventData.InputButton.Left);
            if (data == null) return;

            BuildChain(target, TempChain);

            for (int i = 0; i < HoverChain.Count; i++)
            {
                var go = HoverChain[i];
                if (go == null) continue;
                if (TempChain.Contains(go)) continue;
                Safe(() => ExecuteEvents.Execute(go, data, ExecuteEvents.pointerExitHandler));
            }

            for (int i = TempChain.Count - 1; i >= 0; i--)
            {
                var go = TempChain[i];
                if (go == null) continue;
                if (HoverChain.Contains(go)) continue;
                Safe(() => ExecuteEvents.Execute(go, data, ExecuteEvents.pointerEnterHandler));
            }

            HoverChain.Clear();
            HoverChain.AddRange(TempChain);
            CurrentHover = target;
        }

        public static void ClearHover()
        {
            SetHover(null, VirtualPointer.Position);
        }

        /// <summary>
        /// Full down -> up -> click sequence. pointerPress and eligibleForClick must be
        /// set or UnityEngine.UI.Button silently ignores the click.
        /// </summary>
        public static void Click(GameObject target, Vector2 pos, PointerEventData.InputButton button)
        {
            if (target == null) return;

            var data = NewEvent(pos, button);
            if (data == null) return;

            var press = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) ?? target;

            data.pointerPress = press;
            data.rawPointerPress = target;
            data.pointerCurrentRaycast = Raycast(pos, target);
            data.pointerPressRaycast = data.pointerCurrentRaycast;
            data.eligibleForClick = true;

            Safe(() => ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerDownHandler));
            Safe(() => ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerUpHandler));

            // Only the resolved handler receives the click. Firing submit as well
            // would activate UnityEngine.UI.Button twice for a single press.
            Safe(() => ExecuteEvents.Execute(press, data, ExecuteEvents.pointerClickHandler));
        }

        /// <summary>Press without release, used when the game expects a held button.</summary>
        public static void Down(GameObject target, Vector2 pos, PointerEventData.InputButton button)
        {
            if (target == null) return;
            var data = NewEvent(pos, button);
            if (data == null) return;
            data.pointerPress = target;
            data.rawPointerPress = target;
            data.eligibleForClick = true;
            Safe(() => ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerDownHandler));
        }

        public static void Up(GameObject target, Vector2 pos, PointerEventData.InputButton button)
        {
            if (target == null) return;
            var data = NewEvent(pos, button);
            if (data == null) return;
            data.pointerPress = target;
            data.rawPointerPress = target;
            data.eligibleForClick = true;
            Safe(() => ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerUpHandler));
        }

        public static RaycastResult Raycast(Vector2 pos, GameObject fallback)
        {
            var es = EventSystem.current;
            if (es == null) return default(RaycastResult);

            var data = NewEvent(pos, PointerEventData.InputButton.Left);
            if (data == null) return default(RaycastResult);

            RaycastBuffer.Clear();
            try { es.RaycastAll(data, RaycastBuffer); }
            catch { return default(RaycastResult); }

            if (RaycastBuffer.Count > 0) return RaycastBuffer[0];
            return default(RaycastResult);
        }

        /// <summary>Top-most raycast hit at a screen point, or null.</summary>
        public static GameObject RaycastTop(Vector2 pos)
        {
            var r = Raycast(pos, null);
            return r.gameObject;
        }

        private static void BuildChain(GameObject target, List<GameObject> into)
        {
            into.Clear();
            var t = target != null ? target.transform : null;
            while (t != null)
            {
                into.Add(t.gameObject);
                t = t.parent;
            }
        }

        private static void Safe(Action a)
        {
            try { a(); }
            catch (Exception e) { Plugin.LogDebug("pointer dispatch error: " + e.Message); }
        }

        public static void Reset()
        {
            HoverChain.Clear();
            CurrentHover = null;
        }
    }
}
