using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// A render pass dedicated to the mod's panels, shielded from the game's effects - the
    /// diary's full-screen blur and the permanent bloom were landing on the HUD.
    ///
    /// The panels live on a layer of their own, removed from the main camera's mask and drawn
    /// by a sibling camera with the same pose, which clears depth only and carries no effects.
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
        /// Aligns the panel camera with the main one. Call every frame: nothing guarantees
        /// Unity poses both stereo cameras alike, and a misaligned camera displaces
        /// everything it renders. The copy is idempotent.
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
