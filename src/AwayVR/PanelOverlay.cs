using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// A render pass dedicated to the mod's panels, shielded from the game's effects.
    ///
    /// A world-space canvas is drawn by the main camera, so it inherits all of that camera's
    /// post-processing. The diary applies a full-screen blur, and bloom and occlusion run
    /// permanently — the HUD and the dialogue box picked all of it up even though nothing
    /// should affect them.
    ///
    /// So we isolate the panels on a layer of our own, removed from the main camera's
    /// culling mask and rendered by a secondary camera sharing the same pose. That camera
    /// clears depth only, carries no effects, and renders afterwards: the panels are
    /// composited on top of the finished image, perfectly sharp.
    /// </summary>
    internal static class PanelOverlay
    {
        private const string CameraName = "AwayVR_PanelCamera";

        /// <summary>Layer reserved for the panels, or -1 while none is free.</summary>
        public static int Layer { get; private set; } = -1;

        private static Camera _cam;

        /// <summary>Transform of the panel camera: the panels' parent.</summary>
        public static Transform Anchor { get { return _cam != null ? _cam.transform : null; } }

        /// <summary>
        /// Looks for an unnamed layer, therefore one the game does not use. We start from
        /// the top: low layers belong to Unity and to the project, high ones are usually
        /// left blank. Taking a named layer would risk hiding actual scenery.
        /// </summary>
        private static int FindFreeLayer()
        {
            for (int i = 31; i >= 8; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return -1;
        }

        public static void Ensure(Camera main)
        {
            if (main == null) return;

            if (Layer < 0)
            {
                Layer = FindFreeLayer();
                if (Layer < 0)
                {
                    Plugin.Log.LogWarning("No free layer: the panels will remain subject to "
                                          + "the camera's effects.");
                    return;
                }
                Plugin.Log.LogInfo("VR panels isolated on layer " + Layer + ".");
            }

            // A SIBLING of the main camera, never its CHILD. Unity applies the head pose to
            // every stereo camera: parented under the main camera, this one inherited that
            // pose through its parent AND received it a second time. The motion was
            // doubled, which reads as an inversion when you tilt your head. Under the same
            // parent, both receive the same pose exactly once.
            var parent = main.transform.parent;

            if (_cam != null && _cam.transform.parent == parent)
            {
                Synchronise(main);
                return;
            }

            if (_cam != null) Object.Destroy(_cam.gameObject);

            var go = new GameObject(CameraName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = main.transform.localPosition;
            go.transform.localRotation = main.transform.localRotation;
            go.transform.localScale = Vector3.one;

            _cam = go.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.Depth;
            _cam.cullingMask = 1 << Layer;
            _cam.depth = main.depth + 10f;
            _cam.stereoTargetEye = StereoTargetEyeMask.Both;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;

            Synchronise(main);
        }

        /// <summary>
        /// Aligns the panel camera with the main one. Call EVERY FRAME, in LateUpdate, once
        /// the head pose has been written.
        ///
        /// I had relied on Unity to apply the same pose to both stereo cameras. Nothing
        /// guarantees that, and the near plane staying at its original value proved it:
        /// synchronisation only happened at creation, before the main camera had even been
        /// configured. A misaligned camera displaces everything it renders — the HUD looked
        /// too high, and ended up covering the menu entirely.
        ///
        /// The copy is idempotent: if Unity already poses this camera we write the same
        /// value, otherwise we correct it. Either way the result is right.
        /// </summary>
        public static void Synchronise(Camera main)
        {
            if (_cam == null || main == null) return;

            var a = _cam.transform;
            var b = main.transform;
            if (a.parent == b.parent)
            {
                a.localPosition = b.localPosition;
                a.localRotation = b.localRotation;
            }
            else
            {
                a.position = b.position;
                a.rotation = b.rotation;
            }

            // Same projection: a different field of view or near plane would make the panels
            // drift against the scenery.
            _cam.fieldOfView = main.fieldOfView;
            _cam.nearClipPlane = main.nearClipPlane;
            _cam.farClipPlane = main.farClipPlane;
            _cam.depth = main.depth + 10f;
        }

        /// <summary>Moves the object and its whole subtree onto the panel layer.</summary>
        public static void Adopt(GameObject go)
        {
            if (go == null || Layer < 0) return;
            if (go.layer == Layer) return;

            go.layer = Layer;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = Layer;
        }

        public static void Forget()
        {
            if (_cam != null) Object.Destroy(_cam.gameObject);
            _cam = null;
        }
    }
}
