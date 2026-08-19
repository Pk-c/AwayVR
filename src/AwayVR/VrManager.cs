using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace AwayVR
{
    /// <summary>
    /// XR life cycle plus construction of the camera rig for each scene.
    /// </summary>
    public class VrManager : MonoBehaviour
    {
        public const string RigName = "AwayVR_CameraRig";

        /// <summary>True once the XR device is loaded and active.</summary>
        public static bool VrActive { get; private set; }

        public static VrManager Instance { get; private set; }

        public static Transform Rig { get; private set; }
        public static Camera MainCamera { get; private set; }

        /// <summary>
        /// True when the scene is a GAMEPLAY scene rather than a menu or a screen.
        ///
        /// We used to rely on ShowPanels being present: measurement showed it survives the
        /// load into a game, so the HUD believed itself permanently in a menu and stayed
        /// visible. The player controller, on the other hand, exists only where you play.
        /// </summary>
        public static bool InGame { get; private set; }

        /// <summary>Rig local position without height offset, captured on setup.</summary>
        private static Vector3 _rigBasePos;
        /// <summary>Culling mask before any hiding, so it can be recomputed every frame.</summary>
        private static int _baseMask;
        private static Camera _maskCam;

        /// <summary>
        /// Objects detached from the camera whose bounds we could not measure yet: their
        /// renderer was not active at adoption time. We try again.
        /// </summary>
        private static readonly System.Collections.Generic.List<Transform> _toNormalise =
            new System.Collections.Generic.List<Transform>();

        /// <summary>Full-screen quads moved onto a virtual screen, so they can be toggled.</summary>
        private static readonly System.Collections.Generic.List<Transform> _screens =
            new System.Collections.Generic.List<Transform>();

        /// <summary>Shared anchor for the virtual screens, driven like the mod's panels.</summary>
        private static Transform _screenAnchor;

        private static void Log(string m) => Plugin.Log.LogInfo(m);
        private static void Warn(string m) => Plugin.Log.LogWarning(m);
        private static void Err(string m) => Plugin.Log.LogError(m);

        private IEnumerator Start()
        {
            Instance = this;
            SceneManager.sceneLoaded += OnSceneLoaded;
            yield return StartCoroutine(InitXr());
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.onBeforeRender -= PlaceBeforeRender;
        }

        private void OnEnable() { Application.onBeforeRender += PlaceBeforeRender; }
        private void OnDisable() { Application.onBeforeRender -= PlaceBeforeRender; }

        /// <summary>
        /// Re-places everything that must sit exactly where the head is. LateUpdate is too
        /// early: Unity re-latches the head pose after it, so anything compensating for a
        /// head or hand rotation there works from a value one frame stale.
        ///
        /// Placement only - damping integrates over deltaTime and would advance twice.
        /// </summary>
        private void PlaceBeforeRender()
        {
            if (!VrActive) return;

            PanelOverlay.Synchronise(MainCamera);
            GazeFollow.Refresh(PanelOverlay.Anchor);
            FollowVirtualScreens();

            ImguiCapture.Place();
            HudCapture.Place();
            VrFade.Place();
            Grenades.Place();
            FpsCounter.Place();
        }

        // ------------------------------------------------------------------
        // XR initialisation
        // ------------------------------------------------------------------

        private IEnumerator InitXr()
        {
            var wanted = Plugin.CfgDevice.Value;

            var supported = XRSettings.supportedDevices;
            Log("XR devices declared by the build: [" + string.Join(", ", supported ?? new string[0]) + "]");

            if (!ContainsDevice(supported, wanted))
            {
                Err("'" + wanted + "' is not in enabledVRDevices. Away_Data/globalgamemanagers has not "
                    + "been patched: VR cannot start.");
                yield break;
            }

            // Unity 2017's built-in VR only runs on Direct3D 11. Under D3D12 the subsystem
            // refuses to initialise without so much as an error message, which makes the
            // failure very hard to read: so we name it here.
            var gfx = SystemInfo.graphicsDeviceType;
            Log("Graphics API: " + gfx + " - " + SystemInfo.graphicsDeviceVersion);
            if (gfx != GraphicsDeviceType.Direct3D11)
            {
                Err("Unity 2017 VR requires Direct3D 11, but the game is running on " + gfx + ".");
                Err("Put Direct3D11 back at the head of m_GraphicsAPIs in Away_Data/globalgamemanagers "
                    + "(tools/patch_ggm.py), or launch the game with -force-d3d11.");
                yield break;
            }

            Log("HMD present before loading: " + XRDevice.isPresent);
            Log("Loading device '" + wanted + "'...");
            XRSettings.LoadDeviceByName(wanted);
            yield return null;

            XRSettings.enabled = true;
            yield return null;

            // The device may take a few frames to come up (SteamVR starting).
            for (int i = 0; i < 300 && !XRSettings.isDeviceActive; i++)
                yield return null;

            if (!XRSettings.isDeviceActive || XRSettings.loadedDeviceName != wanted)
            {
                Err("XR activation failed. loadedDeviceName='" + XRSettings.loadedDeviceName
                    + "' isDeviceActive=" + XRSettings.isDeviceActive
                    + " XRDevice.isPresent=" + XRDevice.isPresent
                    + ". Is the runtime (SteamVR) running and the headset plugged in?");
                yield break;
            }

            VrActive = true;

            // Always recentred: this game fixes the character's eye height, and floor-based
            // tracking would leave the view floating above or below it.
            XRDevice.SetTrackingSpaceType(TrackingSpaceType.Stationary);

            XRSettings.eyeTextureResolutionScale = Plugin.CfgResolutionScale.Value;
            Visuals.Apply(true);

            // The VR compositor sets its own pace: the game's vsync has to go, and the game
            // must keep running even without window focus.
            QualitySettings.vSyncCount = 0;
            Application.runInBackground = true;

            if (Plugin.CfgOpenVrBridge.Value) OpenVrBridge.Probe();

            InputTracking.Recenter();
            RequestCentre();

            Log("=== VR ACTIVE ===");
            Log("  device      : " + XRSettings.loadedDeviceName);
            Log("  model       : " + XRDevice.model);
            Log("  refresh     : " + XRDevice.refreshRate.ToString("0.#") + " Hz");
            Log("  eye texture : " + XRSettings.eyeTextureWidth + "x" + XRSettings.eyeTextureHeight
                + " (scale " + XRSettings.eyeTextureResolutionScale.ToString("0.##") + ")");
            Log("  tracking    : " + XRDevice.GetTrackingSpaceType());

            SetupScene("init");
        }

        /// <summary>
        /// Replaces Unity's default skybox with a black background.
        ///
        /// Screens with no scenery - the rewards screen after a death, for instance - leave
        /// Unity's procedural skybox in place. Flat, it goes unnoticed behind the interface;
        /// in VR you end up standing in an empty blue sky, which breaks the scene entirely.
        /// We touch ONLY the default skybox: the ones the game chose itself are intended
        /// scenery.
        /// </summary>
        private static void NeutraliseDefaultSkybox(Camera cam)
        {
            if (cam == null) return;

            var sky = RenderSettings.skybox;
            bool parDefaut = sky == null
                             || sky.name.StartsWith("Default-Skybox", System.StringComparison.OrdinalIgnoreCase);
            if (!parDefaut) return;

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            Log("  default skybox replaced with a black background.");
        }

        private static bool ContainsDevice(string[] devices, string name)
        {
            if (devices == null) return false;
            for (int i = 0; i < devices.Length; i++)
                if (string.Equals(devices[i], name, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        // ------------------------------------------------------------------
        // Per-scene rig
        // ------------------------------------------------------------------

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!VrActive) return;
            Weapons.Forget();
            Weapons.OnSceneLoaded();
            PlayerBody.Forget();
            VrFade.OnSceneLoaded();
            CameraEffects.ForgetOriginals();
            LayerBisect.Reset();
            RootBisect.Reset();
            GameState.OnSceneLoaded();
            Swing.OnSceneLoaded();
            Trails.Forget();
            // Shield is NOT cleared here: weapons_sword raises OnEnable while the scene is
            // still loading, so clearing afterwards wiped the binding it had just made.
            // A destroyed sword nulls its own reference, which is all the cleanup needed.
            _fpc = null;
            Refraction.Forget();
            WeaponEffects.Forget();
            Grenades.Forget();
            RoomScale.Forget();
            _toNormalise.Clear();
            _screens.Clear();
            StartCoroutine(SetupSceneDeferred(scene.name));
        }

        private IEnumerator SetupSceneDeferred(string sceneName)
        {
            // Let the game's Start() methods create or find the cameras.
            yield return null;
            yield return null;
            SetupScene(sceneName);
        }

        private void SetupScene(string sceneName)
        {
            var cam = FindMainCamera();
            if (cam == null)
            {
                if (Plugin.CfgVerbose.Value)
                    Log("Scene '" + sceneName + "': no usable camera.");
                return;
            }

            MainCamera = cam;
            BuildRig(cam.transform);
            Hands.Ensure(Rig);
            AdoptCameraChildren(cam.transform);

            PanelOverlay.Ensure(cam);

            InGame = Object.FindObjectOfType<
                UnityStandardAssets.Characters.FirstPerson.FirstPersonController>() != null;

            // A near plane at five centimetres. Closer and the depth buffer loses precision
            // across the whole scene; further and your own hands are clipped away.
            cam.nearClipPlane = 0.05f;
            NeutraliseDefaultSkybox(cam);
            cam.stereoTargetEye = StereoTargetEyeMask.Both;

            ApplyWeaponsCameraMode(cam);

            // Captured after the merge: otherwise the weapons layer would be dropped on the
            // first mask recomputation.
            if (_maskCam != cam)
            {
                _maskCam = cam;
                _baseMask = cam.cullingMask;
            }

            // Re-applied per scene: the game calls SetQualityLevel on its own, which resets
            // every one of these.
            Visuals.Apply(Plugin.CfgVerbose.Value);

            PlayerBody.Apply(cam, Plugin.CfgVerbose.Value, true);
            Weapons.Apply(Plugin.CfgWeaponAttach.Value, Plugin.CfgVerbose.Value);

            HudCapture.OnSceneLoaded();
            int hud = CanvasTools.Apply(Plugin.CfgVerbose.Value);
            if (hud > 0) Log("  " + hud + " screen canvas(es) attached to the UI camera.");

            if (Plugin.CfgRecenterOnLoad.Value)
            {
                // Also cancels the yaw offset accumulated before the spawn.
                XRDevice.SetTrackingSpaceType(TrackingSpaceType.Stationary);
                InputTracking.Recenter();
                RequestCentre();
            }

            Log("Scene '" + sceneName + "' : rig VR installe sous " + Hierarchy.Path(cam.transform)
                + "  rig pos=" + Rig.localPosition.ToString("0.000"));
            if (Plugin.CfgVerbose.Value) Diagnostics.DumpScene();
        }

        /// <summary>Reapplies every setting, after a change made from the menu.</summary>
        public void ReapplyScene()
        {
            if (!VrActive) return;
            SetupScene(SceneManager.GetActiveScene().name);
        }

        /// <summary>
        /// Menu and cutscene scenes have no FirstPersonCharacter: without a fallback no rig
        /// was built there and nothing was converted, hence an invisible menu and a video
        /// glued to the headset.
        /// </summary>
        private static Camera FindMainCamera()
        {
            var go = GameObject.Find("FirstPersonCharacter");
            if (go != null)
            {
                var c = go.GetComponent<Camera>();
                if (c != null) return c;
            }

            if (Camera.main != null) return Camera.main;

            Camera best = null;
            foreach (var c in FindObjectsOfType<Camera>())
            {
                if (!c.enabled || c.targetTexture != null) continue;
                if (best == null || c.depth > best.depth) best = c;
            }
            return best;
        }

        /// <summary>
        /// Everything the game parents to the camera is locked to the head: video quad,
        /// panels, reticles. In VR that is the worst case, the image stops responding to
        /// your gaze. We reattach them to the rig: they stay in front of the player but stop
        /// following the head, so you can finally look at them.
        /// </summary>
        private void AdoptCameraChildren(Transform camT)
        {
            if (Rig == null) return;

            // During play the camera carries gameplay elements (weapon holders, impact FX,
            // wings) that the viewmodel already handles: we only detach in menu and cutscene
            // scenes, where the full-screen-glued-to-the-head problem actually arises.
            // InGame is the same question, already answered once per sweep from a cached
            // reference. Asking it again here meant a second full scan of every loaded object
            // for a value we were holding.
            if (InGame) return;

            for (int i = camT.childCount - 1; i >= 0; i--)
            {
                var child = camT.GetChild(i);
                var n = child.name;

                // The viewmodel has its own handling, and our objects are already on the rig.
                if (n == "Weapons_Camera" || n.StartsWith("AwayVR_")) continue;

                // We keep the local values: the object ends up straight ahead of the rig,
                // rather than frozen wherever the head happened to be at adoption time.
                child.SetParent(Rig, false);
                if (!NormalizeIfFullscreen(child, n) && !_toNormalise.Contains(child))
                    _toNormalise.Add(child);
            }
        }

        /// <summary>
        /// Eye height as the game intends it. The camera's local position is overwritten by
        /// the headset too, so it is unusable; FirstPersonController keeps a copy taken in
        /// its Start(), before tracking ever intervenes.
        /// </summary>
        private static Vector3 AuthoredEyeOffset(Transform camT)
        {
            var fpc = FindObjectOfType<UnityStandardAssets.Characters.FirstPerson.FirstPersonController>();
            if (fpc != null)
            {
                var f = HarmonyLib.AccessTools.Field(
                    typeof(UnityStandardAssets.Characters.FirstPerson.FirstPersonController),
                    "m_OriginalCameraPosition");
                if (f != null)
                {
                    var p = (Vector3)f.GetValue(fpc);
                    if (p.sqrMagnitude > 1e-6f)
                    {
                        Log("  eye height taken from the game: " + p.ToString("0.000"));
                        return p;
                    }
                }
            }
            // Scenes without a player: we keep the current position, for want of better.
            return camT.localPosition;
        }

        /// <summary>
        /// A full-screen quad (the title menu video) is sized to fill the view from a few
        /// centimetres away. World-locked as is, it stays a wall pressed against your face.
        /// We move it onto a virtual screen of reasonable size and distance, preserving its
        /// aspect ratio.
        /// </summary>
        private static Transform EnsureScreenAnchor()
        {
            if (_screenAnchor != null && _screenAnchor.parent == Rig) return _screenAnchor;

            var go = new GameObject("AwayVR_ScreenAnchor");
            _screenAnchor = go.transform;
            _screenAnchor.SetParent(Rig, false);
            return _screenAnchor;
        }

        /// <summary>
        /// Makes the virtual screens follow exactly like the HUD panel.
        ///
        /// The title poster and the menu have to stay together: they are designed to be seen
        /// as one. So they do not merely use similar settings, they share the SAME source -
        /// GazeFollow for the orientation, and the HUD's own distance and width for the
        /// geometry. Two independent follows, however carefully tuned, always ended up a few
        /// degrees apart, and that is immediately visible.
        /// </summary>
        private static void FollowVirtualScreens()
        {
            if (_screenAnchor == null || MainCamera == null || Rig == null) return;

            // World pose taken straight from the shared follow, then converted into the
            // rig's space: the anchor lives under the rig, but its placement must not
            // depend on the rig's own orientation.
            _screenAnchor.position = GazeFollow.Origin;
            _screenAnchor.rotation = GazeFollow.Rotation;
        }

        private bool NormalizeIfFullscreen(Transform child, string name)
        {
            Bounds b;
            if (!TryBounds(child, out b)) return false;   // renderer pas encore actif

            float w = b.size.x;
            if (w < 1.5f)
            {
                // Small object (reticle, icon): world-locking is enough, we leave it alone.
                if (Plugin.CfgVerbose.Value) Log("  detached from the head: " + name);
                return true;
            }

            if (!_screens.Contains(child))
            {
                _screens.Add(child);
            }

            // Same width as the HUD panel, so poster and menu match by construction
            // rather than by two settings that have to be kept in step by hand.
            float k = Plugin.CfgHudWidth.Value / w;
            child.localScale = child.localScale * k;
            // We leave the rotation alone: a quad has a front face, and resetting it to
            // identity flips it over, which renders it invisible or white.

            if (!TryBounds(child, out b)) return false;

            // Recentre on the bounds' real pivot, which is not necessarily the origin.
            var camLocal = MainCamera != null
                ? Rig.InverseTransformPoint(MainCamera.transform.position)
                : Vector3.zero;

            // In front of the GAZE, not along the rig's +Z. The rig is forced level and its
            // forward has nothing to do with the direction being looked at: the virtual
            // screen could therefore end up beside or behind the player, which made the menu
            // video look as though it never played. Same fix as for the HUD anchor.
            var avant = Vector3.forward;
            if (MainCamera != null)
            {
                avant = Rig.InverseTransformDirection(MainCamera.transform.forward);
                avant.y = 0f;
                if (avant.sqrMagnitude < 1e-6f) avant = Vector3.forward;
                avant.Normalize();
            }

            var target = Rig.TransformPoint(
                new Vector3(camLocal.x, camLocal.y, camLocal.z)
                + avant * Plugin.CfgHudDistance.Value);
            child.position += target - b.center;

            // Attached to the shared anchor, world pose preserved: the offset measured here
            // becomes a local one, and the screen will then follow the gaze without us ever
            // touching its rotation - a quad has a front face, and resetting it to identity
            // would flip it.
            child.SetParent(EnsureScreenAnchor(), true);

            Log("  virtual screen: " + name + "  largeur " + w.ToString("0.0")
                + "m -> " + Plugin.CfgHudWidth.Value.ToString("0.0")
                + "m at " + Plugin.CfgHudDistance.Value.ToString("0.0") + "m");
            return true;
        }


        /// <summary>Another attempt at the objects whose bounds came back empty.</summary>
        private void RetryNormalize()
        {
            for (int i = _toNormalise.Count - 1; i >= 0; i--)
            {
                var t = _toNormalise[i];
                if (t == null) { _toNormalise.RemoveAt(i); continue; }
                if (NormalizeIfFullscreen(t, t.name)) _toNormalise.RemoveAt(i);
            }
        }

        private static bool TryBounds(Transform t, out Bounds b)
        {
            b = new Bounds();
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any;
        }

        private void BuildRig(Transform camT)
        {
            var parent = camT.parent;

            // Already grafted?
            if (parent != null && parent.name == RigName)
            {
                Rig = parent;
                return;
            }

            var rigGo = new GameObject(RigName);
            var rigT = rigGo.transform;
            rigT.SetParent(parent, false);
            rigT.localScale = Vector3.one;

            // CAREFUL: the camera's local transform is driven by the headset. Copying it as
            // is would freeze the head's orientation at spawn time into the rig, roll
            // included, and the scenery would stay tilted forever. The anchor must be level:
            // only the headset is allowed to contribute pitch and roll.
            rigT.localRotation = Quaternion.identity;
            rigT.localPosition = AuthoredEyeOffset(camT);

            // worldPositionStays:false -> we keep control of the local transform and zero
            // it: from now on the headset pose is what fills the camera's local values.
            camT.SetParent(rigT, false);
            camT.localPosition = Vector3.zero;
            camT.localRotation = Quaternion.identity;
            camT.localScale = Vector3.one;

            Rig = rigT;
            _rigBasePos = rigT.localPosition;
        }

        /// <summary>
        /// Settings reapplied every frame, so they move live while you adjust them from
        /// the menu.
        ///
        /// The old version added the height offset to the current position, so it stacked up
        /// on every scene rebuild: we now always start again from the base position captured
        /// at setup.
        /// </summary>
        private static float _lastLoggedScale = -1f;

        /// <summary>Logs the world IPD: the only measurement proving the scale has effect.</summary>
        private static void LogWorldScale(float scale)
        {
            if (MainCamera == null || Mathf.Approximately(_lastLoggedScale, scale)) return;
            _lastLoggedScale = scale;

            var l = MainCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.GetColumn(3);
            var r = MainCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.GetColumn(3);
            Log("World scale = " + scale.ToString("0.00")
                + "   world IPD = " + Vector3.Distance(l, r).ToString("0.0000") + " m");
        }

        /// <summary>
        /// Horizontal offset that puts the camera over the capsule, in rig space.
        ///
        /// The headset reports the head relative to the centre of the play area, so this is
        /// minus that position - without it the capsule sits wherever the room's centre happens
        /// to be, up to a couple of metres from the player, scraping geometry out of view.
        ///
        /// Sampled on the frame AFTER a recentre is asked for: InputTracking.Recenter only takes
        /// effect on the next pose, so reading it immediately would capture the old one and the
        /// correction would be applied twice.
        /// </summary>
        private Vector3 _centreFlat;
        private bool _centrePending;

        internal void RequestCentre()
        {
            _centrePending = true;
        }

        private void UpdateCentre()
        {
            if (!Plugin.CfgCentreOnBody.Value) { _centreFlat = Vector3.zero; return; }
            if (!_centrePending) return;

            _centrePending = false;
            var head = InputTracking.GetLocalPosition(XRNode.Head);
            _centreFlat = new Vector3(-head.x, 0f, -head.z);

            // The room-scale compensation was accumulated against the old centre.
            RoomScale.Forget();

            Log("Head centred on the body: offset=" + _centreFlat.ToString("0.000"));
        }

        /// <summary>
        /// Camera to capsule, horizontally, in metres. Zero is centred.
        ///
        /// Every term that separates the two has to be in here. Leaving out the room-scale
        /// compensation - the one that cancels physical walking - measured how far the player had
        /// walked across the room instead, and reported a metre and a half of offset that was
        /// not there.
        /// </summary>
        internal static float HeadOffset
        {
            get
            {
                if (!VrActive || MainCamera == null || Rig == null) return 0f;
                var p = Rig.localPosition + MainCamera.transform.localPosition - _rigBasePos;
                return new Vector2(p.x, p.z).magnitude;
            }
        }

        private void ApplyLive()
        {
            if (!VrActive || Rig == null) return;

            UpdateCentre();

            Rig.localPosition = _rigBasePos + Vector3.up * Plugin.CfgHeightOffset.Value
                               + _centreFlat + RoomScale.Offset;

            float scale = Mathf.Max(0.01f, Plugin.CfgWorldScale.Value);
            if (!Mathf.Approximately(Rig.localScale.x, scale))
            {
                Rig.localScale = new Vector3(scale, scale, scale);
                LogWorldScale(scale);
            }

            // Manual bisection drives the mask itself for the duration of its sweep.
            // Viewmodel pose every frame: the offsets are adjusted live.
            Weapons.Pose();
            OpenVrBridge.Tick();
            ControllerProbe.Tick();


            // Continuous HUD follow. Framerate-independent exponential damping:
            // 1 - exp(-k*dt) gives the same response at 72 as at 144 frames per second.

            if (MainCamera == null || _maskCam != MainCamera) return;

            int mask = _baseMask;

            // The mod's panels must NEVER go through the main camera: that is the whole
            // point of their dedicated pass, otherwise they would pick up its effects.
            if (PanelOverlay.Layer >= 0) mask &= ~(1 << PanelOverlay.Layer);

            // Manual bisection, driven from the menu.
            if (LayerBisect.Current >= 0) mask &= ~(1 << LayerBisect.Current);

            if (MainCamera.cullingMask != mask) MainCamera.cullingMask = mask;
        }

        /// <summary>Merged weapons camera, watched because the game switches it back on.</summary>
        private static Camera _weaponsCam;
        private static UnityStandardAssets.Characters.FirstPerson.FirstPersonController _fpc;

        /// <summary>Root of the player hierarchy, for searches that only concern the body.</summary>
        public static Transform PlayerRoot
        {
            get { return _fpc != null ? _fpc.transform.root : null; }
        }

        /// <summary>
        /// The weapons camera must draw nothing while still RENDERING. Its layer is merged
        /// into the main camera, so letting it draw shows the weapon twice - but disabling it
        /// kills the per-character full-screen filters, which the game hangs on this very
        /// camera and which need OnRenderImage to run.
        ///
        /// An empty culling mask gives both. See also WeaponEffects, which moves those
        /// filters elsewhere so the camera can be switched off outright.
        /// </summary>
        private static void KeepWeaponsCameraBlind()
        {
            if (_weaponsCam == null) return;

            if (Plugin.CfgWeaponsCameraOff.Value)
            {
                // The effects move BEFORE the camera goes: they are what kept it alive.
                WeaponEffects.Install(_weaponsCam, MainCamera);
                WeaponEffects.Sync();
                if (_weaponsCam.enabled) _weaponsCam.enabled = false;
                return;
            }

            if (WeaponEffects.Installed) WeaponEffects.Forget();

            // Re-enabled if the game switched it off: its effects need it running.
            if (!_weaponsCam.enabled) _weaponsCam.enabled = true;

            if (_weaponsCam.cullingMask != 0)
            {
                _weaponsCam.cullingMask = 0;
                if (Plugin.CfgVerbose.Value)
                    Log("Weapons_Camera given content again by the game: blinded once more.");
            }

        }

        private void ApplyWeaponsCameraMode(Camera mainCam)
        {
            var wGo = GameObject.Find("Weapons_Camera");
            if (wGo == null) return;
            var wCam = wGo.GetComponent<Camera>();
            if (wCam == null) return;

            _weaponsCam = wCam;

            // The weapons camera draws its layers on top, with its own near plane. In stereo
            // that overlay is flattened onto the screen: we fold its layers into the main
            // camera so the weapons live in the world.
            //
            // This was once a three-way choice. The other two were measured and dropped:
            // keeping the camera gives the doubled arm, disabling it kills the per-character
            // full-screen effects, which live on it and nowhere else.
            mainCam.cullingMask |= wCam.cullingMask;
            Log("  Weapons_Camera merged (mask added: 0x" + wCam.cullingMask.ToString("X") + ")");
            // Blinded, not disabled: see KeepWeaponsCameraBlind. The mask has to be read
            // before it is cleared, hence the order here.
            wCam.cullingMask = 0;
            wCam.enabled = true;
        }

        private static bool IsDescendantOf(Transform t, Transform ancestor)
        {
            for (var p = t.parent; p != null; p = p.parent)
                if (p == ancestor) return true;
            return false;
        }

        // ------------------------------------------------------------------
        // Shortcuts
        // ------------------------------------------------------------------

        private float _nextEffectSweep;

        private void LateUpdate()
        {
            // Detected in LateUpdate: the swing arms the next frame, so every script in the
            // game sees it once, whatever their order.
            // After the Animator has been evaluated: this is the only point where we can
            // cancel the translation it writes onto the viewmodel.
            Weapons.Fixer();

            // Before every consumer: the flags it publishes are read by the fade, the melee
            // detection, the player body and the weapon holder check, all of which used to
            // discover the same events by sweeping the scene on their own timer.
            GameState.Tick();

            Swing.Tick();
            RoomScale.Tick();
            WalkProbe.Tick();

            // Panels placed AFTER the head pose has been updated. In Update we computed
            // their position from a pose one frame stale, and the panel shook on every head
            // movement - the same reason that forces the viewmodel correction to happen here
            // rather than in Update.
            KeepWeaponsCameraBlind();

            // Before placing the panels: they are positioned relative to the main camera
            // and rendered by the panel camera, and the two must coincide.
            PanelOverlay.Synchronise(MainCamera);

            // Shared follow computed FIRST: the panels and the virtual screens both
            // read it, so it has to be up to date before either is placed.
            GazeFollow.Update(PanelOverlay.Anchor, Plugin.CfgHudFollowSpeed.Value);
            FollowVirtualScreens();

            ImguiCapture.Tick();
            HudCapture.Tick();
            VrFade.Tick();
            Grenades.Tick();
            FpsCounter.Tick();
            Trails.Tick();
            Shield.Tick();
            Refraction.Tick();
        }

        private void Update()
        {
            ApplyLive();

            // Applied live so it can be judged from inside the headset. Unity reallocates
            // the eye textures on the next frame, so the change is visible at once.
            if (!Mathf.Approximately(XRSettings.eyeTextureResolutionScale,
                                     Plugin.CfgResolutionScale.Value))
                XRSettings.eyeTextureResolutionScale = Plugin.CfgResolutionScale.Value;

            if (Input.GetKeyDown(Plugin.CfgRecenterKey.Value))
            {
                InputTracking.Recenter();
                RequestCentre();
                Log("Vue recentree.");
            }

            if (Input.GetKeyDown(Plugin.CfgDiagKey.Value))
                Diagnostics.DumpScene();


            // Several of the game's scripts switch effects back on during play (going
            // underwater, damage filters, pause...), so we sweep behind them.
            if (VrActive && Time.unscaledTime >= _nextEffectSweep)
            {
                _nextEffectSweep = Time.unscaledTime + 0.5f;
                // ONE pass over the cameras for all of it. Each world brings its own
                // camera carrying its own copies of these effects, and the game switches
                // several of them back on as it goes, so the sweep has to keep happening -
                // but it has no reason to happen six times over.
                CameraEffects.Sweep(Plugin.CfgDisableBloom.Value,
                                    Plugin.CfgDisableColorGrading.Value,
                                    Plugin.CfgDisableTemporalAA.Value,
                                    Plugin.CfgDisableOcclusion.Value,
                                    Plugin.CfgDisableGlobalFog.Value,
                                    Plugin.CfgDisableDepthOfField.Value,
                                    Plugin.CfgDisableBlink.Value,
                                    !Plugin.CfgCharacterEffects.Value);
                Visuals.Apply(false);

                // UI_hide_map re-enables the minimap whenever you leave a cave, and scenes
                // create canvases of their own: we sweep behind them.
                CanvasTools.Apply(false);

                // Re-evaluated on every sweep, not only at scene setup: the player can
                // vanish without a scene change - on death the progression screen appears
                // while InGame would have stayed true, and the HUD there would have demanded
                // a grip to show up.
                // Cached rather than searched every sweep: FindObjectOfType walks every
                // loaded object, and this one is a single component that survives for the
                // whole scene. The reference goes null on its own when the player is
                // destroyed, which is exactly the event we are watching for.
                if (_fpc == null)
                    _fpc = Object.FindObjectOfType<
                        UnityStandardAssets.Characters.FirstPerson.FirstPersonController>();
                InGame = _fpc != null;

                // A new weapons camera can appear along with a respawn.
                if (_weaponsCam == null && MainCamera != null)
                    ApplyWeaponsCameraMode(MainCamera);

                // weapon_selector enables and disables weapons as the game goes on: each
                // new weapon arrives with its own repositioning scripts.
                Weapons.Apply(Plugin.CfgWeaponAttach.Value, false);

                // The player body is re-enabled as characters are switched.
                PlayerBody.Apply(MainCamera, false);

                // The menu video quad is created after the scene has loaded.
                if (MainCamera != null) AdoptCameraChildren(MainCamera.transform);
                RetryNormalize();
            }
        }
    }

    internal static class Hierarchy
    {
        public static string Path(Transform t)
        {
            if (t == null) return "<null>";
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
    }
}



