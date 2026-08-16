using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AwayVR.Menu
{
    internal struct RowData
    {
        public string Label;
        public string Value;
        public bool IsSection;
        public bool HasSlider;
        public float Normalized;
    }

    /// <summary>
    /// A WorldSpace panel built in code. Since IMGUI is not composited into the eye
    /// textures on this version of Unity, the menu has to be ordinary geometry.
    ///
    /// Rows come from a widget pool refilled on every refresh, and are placed by hand
    /// rather than by a LayoutGroup: deterministic positions, no rebuild.
    /// </summary>
    internal class MenuUI
    {
        private const float W = 900f;
        private const float H = 620f;
        private const float RowH = 46f;
        private const float TopPad = 96f;
        private const float SidePad = 34f;
        private const int MaxRows = 10;
        private const int UiLayer = 5; // "UI"

        private static readonly Color ColPanel = new Color(0.055f, 0.065f, 0.085f, 0.96f);
        private static readonly Color ColAccent = new Color(0.30f, 0.62f, 1.00f, 1f);
        private static readonly Color ColRowSel = new Color(0.30f, 0.62f, 1.00f, 0.16f);
        private static readonly Color ColText = new Color(0.90f, 0.93f, 0.97f, 1f);
        private static readonly Color ColDim = new Color(0.55f, 0.60f, 0.70f, 1f);
        private static readonly Color ColTrack = new Color(1f, 1f, 1f, 0.13f);

        private class Row
        {
            public RectTransform Root;
            public Image Highlight;
            public Text Label;
            public Text Value;
            public RectTransform Track;
            public RectTransform Fill;
        }

        private GameObject _root;
        private RectTransform _rt;
        private Text _title, _footer;
        private readonly List<Row> _rows = new List<Row>();

        public bool Built { get { return _root != null; } }

        // ------------------------------------------------------------------

        public void Ensure(Transform rig)
        {
            if (rig == null) return;

            if (_root != null)
            {
                // The rig is rebuilt for each scene: follow the new one.
                if (_rt.parent != rig) _rt.SetParent(rig, false);
                return;
            }

            // The panel was a child of the rig, so it died with the previous scene.
            // Without this cleanup the pool kept dead widgets, and touching their
            // .gameObject raised a NullReferenceException on every frame.
            _rows.Clear();

            _root = new GameObject("AwayVR_Menu");
            _root.layer = UiLayer;

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 32000;

            _rt = (RectTransform)_root.transform;
            _rt.SetParent(rig, false);
            _rt.sizeDelta = new Vector2(W, H);
            _rt.pivot = new Vector2(0.5f, 0.5f);
            _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);

            // Background
            MakeImage("Panel", ColPanel, 0f, 0f, 0f, 0f);
            // Top rule, doubles as a title bar
            MakeImage("AccentBar", ColAccent, 0f, H - 5f, 0f, H);

            _title = MakeText("Title", 30, TextAnchor.MiddleLeft, ColText);
            Place(_title.rectTransform, SidePad, H - TopPad + 20f, W - SidePad, H - 26f);

            for (int i = 0; i < MaxRows; i++) _rows.Add(MakeRow(i));

            _footer = MakeText("Footer", 19, TextAnchor.LowerLeft, ColDim);
            Place(_footer.rectTransform, SidePad, 16f, W - SidePad, 92f);

            // Fallback scale: without it a panel not yet positioned would be 900 units
            // wide, which fills the entire field of view with white.
            _rt.localScale = Vector3.one * (1.4f / W);
            _root.SetActive(false);

            Plugin.Log.LogInfo("Menu VR construit (" + W + "x" + H + " px).");
        }

        private Row MakeRow(int index)
        {
            float top = H - TopPad - index * RowH;

            var go = new GameObject("Row" + index);
            go.layer = UiLayer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(_rt, false);
            SetRect(rt, SidePad * 0.5f, top - RowH + 4f, W - SidePad * 0.5f, top);

            var row = new Row { Root = rt };

            row.Highlight = MakeImageIn(rt, "Hl", ColRowSel);
            SetStretch(row.Highlight.rectTransform);

            row.Label = MakeTextIn(rt, "Label", 24, TextAnchor.MiddleLeft, ColText);
            SetRect(row.Label.rectTransform, 18f, 0f, 330f, RowH - 4f);

            row.Value = MakeTextIn(rt, "Value", 24, TextAnchor.MiddleRight, ColText);
            SetRect(row.Value.rectTransform, 620f, 0f, W - SidePad * 0.5f - 18f, RowH - 4f);

            var track = MakeImageIn(rt, "Track", ColTrack);
            row.Track = track.rectTransform;
            SetRect(row.Track, 350f, RowH * 0.5f - 5f, 600f, RowH * 0.5f + 5f);

            var fill = MakeImageIn(row.Track, "Fill", ColAccent);
            row.Fill = fill.rectTransform;
            row.Fill.anchorMin = new Vector2(0f, 0f);
            row.Fill.anchorMax = new Vector2(1f, 1f);
            row.Fill.offsetMin = Vector2.zero;
            row.Fill.offsetMax = Vector2.zero;

            return row;
        }

        // ------------------------------------------------------------------
        // Construction helpers
        // ------------------------------------------------------------------

        private Image MakeImage(string name, Color c, float l, float b, float r, float t)
        {
            var img = MakeImageIn(_rt, name, c);
            SetRect(img.rectTransform, l, b, r <= 0f ? W : r, t <= 0f ? H : t);
            if (r <= 0f && t <= 0f) SetStretch(img.rectTransform);
            return img;
        }

        private static Image MakeImageIn(RectTransform parent, string name, Color c)
        {
            var go = new GameObject(name);
            go.layer = UiLayer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        private Text MakeText(string name, int size, TextAnchor anchor, Color c)
        {
            return MakeTextIn(_rt, name, size, anchor, c);
        }

        private static Text MakeTextIn(RectTransform parent, string name, int size,
                                       TextAnchor anchor, Color c)
        {
            var go = new GameObject(name);
            go.layer = UiLayer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = size;
            t.alignment = anchor;
            t.color = c;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>Places a rect in pixel coordinates from the panel's bottom-left corner.</summary>
        private static void SetRect(RectTransform rt, float l, float b, float r, float t)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(l, b);
            rt.sizeDelta = new Vector2(r - l, t - b);
        }

        private static void SetStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rt, float l, float b, float r, float t)
        {
            SetRect(rt, l, b, r, t);
        }

        // ------------------------------------------------------------------
        // Update
        // ------------------------------------------------------------------

        public void Show(bool visible)
        {
            if (_root == null) return;
            if (_root.activeSelf != visible) _root.SetActive(visible);
        }

        public void SetHeader(string title, string footer)
        {
            if (_root == null) return;
            _title.text = title;
            _footer.text = footer;
        }

        public void SetRows(IList<RowData> data, int selected, int scrollTop)
        {
            if (_root == null) return;

            for (int i = 0; i < _rows.Count; i++)
            {
                int idx = scrollTop + i;
                var row = _rows[i];

                if (idx >= data.Count)
                {
                    row.Root.gameObject.SetActive(false);
                    continue;
                }

                row.Root.gameObject.SetActive(true);
                var d = data[idx];

                row.Highlight.enabled = idx == selected && !d.IsSection;

                if (d.IsSection)
                {
                    row.Label.text = "<b>" + d.Label.ToUpper() + "</b>";
                    row.Label.color = ColAccent;
                    row.Label.fontSize = 20;
                    row.Value.text = "";
                    row.Track.gameObject.SetActive(false);
                    continue;
                }

                row.Label.text = d.Label;
                row.Label.color = idx == selected ? ColText : ColDim;
                row.Label.fontSize = 24;
                row.Value.text = d.Value;

                row.Track.gameObject.SetActive(d.HasSlider);
                if (d.HasSlider)
                    row.Fill.anchorMax = new Vector2(Mathf.Clamp01(d.Normalized), 1f);
            }
        }

        /// <summary>
        /// Places the panel in front of the current gaze. Anchored to the rig rather than
        /// to the head: it stays where you opened it, so you can look away.
        /// </summary>
        public void PlaceInFront(Camera cam, Transform rig, float distance, float width, float vOffset)
        {
            if (_root == null || cam == null || rig == null) return;

            var fwd = rig.InverseTransformDirection(cam.transform.forward);
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();

            var camLocal = rig.InverseTransformPoint(cam.transform.position);

            _rt.localRotation = Quaternion.LookRotation(fwd);
            _rt.localPosition = camLocal + fwd * distance + Vector3.up * vOffset;
            _rt.localScale = Vector3.one * (width / W);
        }
    }
}
