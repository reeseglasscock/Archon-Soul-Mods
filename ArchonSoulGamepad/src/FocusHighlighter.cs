using UnityEngine;
using UnityEngine.UI;

namespace ArchonSoulGamepad
{
    /// <summary>
    /// Draws the focus rectangle. This replaces the cursor entirely, so it has to
    /// be unambiguous: a bracketed border on a dedicated top-most overlay canvas.
    /// </summary>
    internal class FocusHighlighter
    {
        private Canvas _canvas;
        private RectTransform _root;
        private readonly Image[] _edges = new Image[4];
        private bool _visible;

        private Color _color = new Color(1f, 0.85f, 0.25f, 1f);
        private float _thickness = 3f;

        public void Configure(Color color, float thickness)
        {
            _color = color;
            _thickness = Mathf.Max(1f, thickness);
            if (_edges[0] != null)
                foreach (var e in _edges) if (e != null) e.color = _color;
        }

        private void EnsureBuilt()
        {
            if (_canvas != null) return;

            var go = new GameObject("ASG_FocusOverlay");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;

            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32760;
            // Deliberately no GraphicRaycaster: the overlay must never absorb hits.

            var holder = new GameObject("Frame", typeof(RectTransform));
            holder.transform.SetParent(go.transform, false);
            _root = (RectTransform)holder.transform;
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.zero;
            _root.pivot = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < 4; i++)
            {
                var edge = new GameObject("Edge" + i, typeof(RectTransform));
                edge.transform.SetParent(_root, false);
                var img = edge.AddComponent<Image>();
                img.color = _color;
                img.raycastTarget = false;
                _edges[i] = img;
            }

            SetVisible(false);
        }

        public void Show(Rect screenRect, bool armed = false, bool editing = false)
        {
            EnsureBuilt();
            SetVisible(true);

            // Breathe slightly so the focus reads clearly against busy backgrounds.
            float pulse = 0.75f + 0.25f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));

            Color c;
            if (armed) c = new Color(1f, 0.25f, 0.2f, _color.a);
            else if (editing) c = new Color(0.3f, 0.9f, 1f, _color.a);
            else c = _color;

            c.a = _color.a * (armed ? 1f : pulse);

            float pad = editing ? 7f : 4f;
            var r = new Rect(screenRect.x - pad, screenRect.y - pad,
                             screenRect.width + pad * 2f, screenRect.height + pad * 2f);

            _root.anchoredPosition = r.center;
            _root.sizeDelta = new Vector2(r.width, r.height);

            float w = r.width, h = r.height, t = editing ? _thickness + 1f : _thickness;

            // top, bottom, left, right
            Place(_edges[0], new Vector2(0f, h * 0.5f - t * 0.5f), new Vector2(w, t), c);
            Place(_edges[1], new Vector2(0f, -h * 0.5f + t * 0.5f), new Vector2(w, t), c);
            Place(_edges[2], new Vector2(-w * 0.5f + t * 0.5f, 0f), new Vector2(t, h), c);
            Place(_edges[3], new Vector2(w * 0.5f - t * 0.5f, 0f), new Vector2(t, h), c);
        }

        private void Place(Image img, Vector2 pos, Vector2 size, Color c)
        {
            if (img == null) return;
            var rt = (RectTransform)img.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            img.color = c;
        }

        public void Hide()
        {
            if (_canvas == null) return;
            SetVisible(false);
        }

        private void SetVisible(bool v)
        {
            if (_visible == v) return;
            _visible = v;
            if (_root != null) _root.gameObject.SetActive(v);
        }

        public void Destroy()
        {
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _root = null;
        }
    }
}
