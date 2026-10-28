using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Gives every local player a camera and shares the screen between them (Roadmap 2.3, DECISIONS.md D-009):
    /// <list type="bullet">
    /// <item>1 player: one full-screen camera.</item>
    /// <item>2 players closer than <see cref="mergeDistance"/>: one shared camera on their midpoint.</item>
    /// <item>2 players further apart: the first player's camera draws to the screen and the second's into a texture.
    /// The composite overlay camera, stacked on the first, paints that texture over the second player's side of an
    /// angled line at right angles to the direction between them.</item>
    /// <item>3-4 players: a screen quarter each.</item>
    /// </list>
    /// <b>Not a singleton:</b> HUDs ask this component for their player's view with <see cref="TryGetView"/>
    /// (CLAUDE.md Laws #1 and #5).
    /// </summary>
    public sealed class SplitScreenDirector : MonoBehaviour
    {
        const int MaxSlots = 4;

        [SerializeField] LocalPlayerManager localPlayers;

        [Tooltip("Shows the world before anyone joins. Player cameras are copies of it, so they share its render settings.")]
        [SerializeField] Camera overviewCamera;

        [Tooltip("Overlay camera that draws only the composite quad, stacked on the first player's camera while the screen is split at an angle.")]
        [SerializeField] Camera compositeCamera;

        [Tooltip("Full-screen quad with the Split Screen Composite material, on a layer only the composite camera sees.")]
        [SerializeField] Renderer compositeQuad;

        [Tooltip("The one ear of the game. It follows the middle of the players.")]
        [SerializeField] AudioListener listener;

        [Header("Split")]
        [Tooltip("Players further apart than this (meters) get their own half of the screen.")]
        [SerializeField, Min(1f)] float mergeDistance = 15f;

        [Tooltip("Past the merge distance, how many more meters until each player can turn their view freely.")]
        [SerializeField, Min(0.1f)] float lookBlendRange = 7f;

        [Tooltip("Meters past the merge distance for the divider line to fade fully in.")]
        [SerializeField, Min(0.01f)] float dividerFadeDistance = 1f;

        [Tooltip("Divider line thickness in pixels.")]
        [SerializeField, Min(0f)] float dividerWidth = 4f;

        [Tooltip("Resolution of each split view compared to the screen. Lower it if split-screen FPS drops.")]
        [SerializeField, Range(0.5f, 1f)] float splitRenderScale = 1f;

        [Tooltip("While split, where each hero sits on their side, from the screen center (0) to the screen edge (1).")]
        [SerializeField, Range(0f, 0.9f)] float playerOffsetInHalf = 0.5f;

        [Tooltip("While split, how quickly the shared angle drifts toward the middle of the two players' own angles.")]
        [SerializeField, Min(0f)] float sharedAngleCatchUp = 2f;

        [Header("Camera")]
        [Tooltip("Camera distance from one player, or from two players standing together (meters).")]
        [SerializeField, Min(1f)] float baseDistance = 7f;

        [Tooltip("Extra distance per meter between two players, so the shared camera keeps both in frame.")]
        [SerializeField, Min(0f)] float pullBackPerMeter = 0.6f;

        [Tooltip("Cameras look at this height above the players' feet.")]
        [SerializeField] float lookHeight = 1.2f;

        [SerializeField, Range(-80f, 85f)] float startPitch = 35f;
        [SerializeField, Range(-80f, 85f)] float minPitch = 10f;
        [SerializeField, Range(-80f, 85f)] float maxPitch = 70f;

        [SerializeField, Min(0f)] float mouseDegreesPerPixel = 0.15f;
        [SerializeField, Min(0f)] float stickDegreesPerSecond = 180f;

        static readonly int ViewBId = Shader.PropertyToID("_ViewB");
        static readonly int SplitNormalId = Shader.PropertyToID("_SplitNormal");
        static readonly int DividerAlphaId = Shader.PropertyToID("_DividerAlpha");
        static readonly int DividerWidthId = Shader.PropertyToID("_DividerWidth");

        readonly PlayerCameraRig[] _rigs = new PlayerCameraRig[MaxSlots];
        readonly PlayerController[] _controllers = new PlayerController[MaxSlots];
        readonly LocalPlayerInput[] _inputs = new LocalPlayerInput[MaxSlots];
        readonly PlayerView[] _views = new PlayerView[MaxSlots];
        readonly int[] _active = new int[MaxSlots];
        int _activeCount;
        int _previousActiveCount = -1;

        float _sharedYaw;
        float _sharedPitch;
        SplitLayout _layout;
        Material _compositeMaterial;
        Camera _compositeBase;
        int _compositorLayerMask;
        bool _cursorReleased;

        /// <summary>How the screen is shared out this frame.</summary>
        public SplitLayout Layout => _layout;

        /// <summary>Where on screen the player in local <paramref name="slot"/> is shown. False if they have no view yet.</summary>
        public bool TryGetView(byte slot, out PlayerView view)
        {
            view = slot < MaxSlots ? _views[slot] : default;
            return view.Camera != null;
        }

        void Awake()
        {
            if (localPlayers == null || overviewCamera == null || compositeCamera == null || compositeQuad == null)
            {
                Debug.LogError($"{nameof(SplitScreenDirector)} needs the local players, overview camera, composite camera and composite quad.", this);
                enabled = false;
                return;
            }

            _compositeMaterial = compositeQuad.material;
            _compositorLayerMask = 1 << compositeQuad.gameObject.layer;
            _sharedPitch = startPitch;
            _sharedYaw = overviewCamera.transform.eulerAngles.y;
            compositeCamera.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Overlay;
            AttachComposite(null);
        }

        void LateUpdate()
        {
            CollectLocalPlayers();
            UpdateCursor();

            if (_activeCount != _previousActiveCount)
            {
                // Coming together as two: start the shared view from the first player's view.
                if (_activeCount == 2)
                {
                    _sharedYaw = _rigs[_active[0]].OwnYaw;
                    _sharedPitch = _rigs[_active[0]].OwnPitch;
                }
                _previousActiveCount = _activeCount;
            }

            for (int slot = 0; slot < MaxSlots; slot++)
                _views[slot] = default;

            switch (_activeCount)
            {
                case 0: ShowOverview(); break;
                case 1: UpdateSingle(_active[0]); break;
                case 2: UpdatePair(_active[0], _active[1]); break;
                default: UpdateGrid(); break;
            }

            PlaceListener();
        }

        /// <summary>Finds this PC's players that have landed, and makes sure each has a camera rig.</summary>
        void CollectLocalPlayers()
        {
            _activeCount = 0;
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                bool active = localPlayers.TryGetLocalPlayer(slot, out PlayerController controller, out LocalPlayerInput input)
                              && controller.HasLanded;
                _controllers[slot] = active ? controller : null;
                _inputs[slot] = active ? input : null;
                if (!active)
                {
                    if (_rigs[slot] != null)
                        _rigs[slot].Hide();
                    continue;
                }

                if (_rigs[slot] == null)
                    _rigs[slot] = CreateRig((byte)slot);
                _active[_activeCount++] = slot;
            }
        }

        PlayerCameraRig CreateRig(byte slot)
        {
            GameObject copy = Instantiate(overviewCamera.gameObject, transform);
            copy.tag = "Untagged";
            Camera camera = copy.GetComponent<Camera>();
            camera.cullingMask &= ~_compositorLayerMask;
            camera.depth = overviewCamera.depth + 1 + slot;

            PlayerCameraRig rig = copy.AddComponent<PlayerCameraRig>();
            rig.Initialize(slot, _activeCount > 0 ? _sharedYaw : overviewCamera.transform.eulerAngles.y, startPitch);
            rig.Hide();
            return rig;
        }

        void ShowOverview()
        {
            overviewCamera.rect = new Rect(0f, 0f, 1f, 1f);
            overviewCamera.enabled = true;
            AttachComposite(null);
            _layout = new SplitLayout { Mode = SplitMode.Overview };
        }

        void UpdateSingle(int slot)
        {
            overviewCamera.enabled = false;
            AttachComposite(null);
            UpdateOwnView(slot, new Rect(0f, 0f, 1f, 1f));
            _layout = new SplitLayout { Mode = SplitMode.Single };
        }

        void UpdateGrid()
        {
            AttachComposite(null);
            for (int i = 0; i < _activeCount; i++)
                UpdateOwnView(_active[i], SplitScreenMath.GridRect(i, _activeCount));

            // Three players leave a quarter free: show the overview there.
            overviewCamera.enabled = _activeCount < MaxSlots;
            if (overviewCamera.enabled)
                overviewCamera.rect = SplitScreenMath.GridRect(MaxSlots - 1, MaxSlots);
            _layout = new SplitLayout { Mode = SplitMode.Grid };
        }

        /// <summary>One player alone in their own rectangle, turning their own camera.</summary>
        void UpdateOwnView(int slot, Rect rect)
        {
            PlayerCameraRig rig = _rigs[slot];
            Vector2 look = ReadLookDegrees(_inputs[slot]);
            rig.OwnYaw += look.x;
            rig.OwnPitch = ClampPitch(rig.OwnPitch - look.y);

            rig.SetPose(FocusPoint(slot), rig.OwnYaw, rig.OwnPitch, baseDistance);
            rig.ShowOnScreen(rect);
            _inputs[slot].ViewYaw = rig.OwnYaw;
            _views[slot] = new PlayerView { LocalSlot = (byte)slot, Camera = rig.Camera, ScreenRect = rect };
        }

        void UpdatePair(int slotA, int slotB)
        {
            overviewCamera.enabled = false;
            PlayerCameraRig rigA = _rigs[slotA];
            PlayerCameraRig rigB = _rigs[slotB];
            float3 focusA = FocusPoint(slotA);
            float3 focusB = FocusPoint(slotB);
            float separation = math.distance(focusA, focusB);
            float freedom = SplitScreenMath.LookFreedom(separation, mergeDistance, lookBlendRange);

            // Look input turns each player's own view, and (while close) the shared view too.
            Vector2 lookA = ReadLookDegrees(_inputs[slotA]);
            Vector2 lookB = ReadLookDegrees(_inputs[slotB]);
            _sharedYaw += (lookA.x + lookB.x) * (1f - freedom);
            _sharedPitch = ClampPitch(_sharedPitch - (lookA.y + lookB.y) * (1f - freedom));
            rigA.OwnYaw += lookA.x;
            rigA.OwnPitch = ClampPitch(rigA.OwnPitch - lookA.y);
            rigB.OwnYaw += lookB.x;
            rigB.OwnPitch = ClampPitch(rigB.OwnPitch - lookB.y);

            if (freedom > 0f)
            {
                // Apart: the shared angle drifts to the middle of both views, so they meet halfway when merging again.
                float t = 1f - math.exp(-sharedAngleCatchUp * freedom * Time.unscaledDeltaTime);
                _sharedYaw += SplitScreenMath.DeltaAngle(_sharedYaw, SplitScreenMath.MidAngle(rigA.OwnYaw, rigB.OwnYaw)) * t;
                _sharedPitch = math.lerp(_sharedPitch, (rigA.OwnPitch + rigB.OwnPitch) * 0.5f, t);
            }
            else
            {
                rigA.OwnYaw = rigB.OwnYaw = _sharedYaw;
                rigA.OwnPitch = rigB.OwnPitch = _sharedPitch;
            }

            float yawA = SplitScreenMath.BlendAngle(_sharedYaw, rigA.OwnYaw, freedom);
            float yawB = SplitScreenMath.BlendAngle(_sharedYaw, rigB.OwnYaw, freedom);
            float pitchA = math.lerp(_sharedPitch, rigA.OwnPitch, freedom);
            float pitchB = math.lerp(_sharedPitch, rigB.OwnPitch, freedom);
            float distance = SplitScreenMath.ViewDistance(baseDistance, pullBackPerMeter, separation, mergeDistance);
            _inputs[slotA].ViewYaw = yawA;
            _inputs[slotB].ViewYaw = yawB;

            var fullScreen = new Rect(0f, 0f, 1f, 1f);
            if (!SplitScreenMath.IsSplit(separation, mergeDistance))
            {
                rigA.SetPose((focusA + focusB) * 0.5f, _sharedYaw, _sharedPitch, distance);
                rigA.ShowOnScreen(fullScreen);
                rigB.Hide();
                AttachComposite(null);

                _layout = new SplitLayout { Mode = SplitMode.Merged, Separation = separation };
                _views[slotA] = new PlayerView { LocalSlot = (byte)slotA, Camera = rigA.Camera, ScreenRect = fullScreen };
                _views[slotB] = new PlayerView { LocalSlot = (byte)slotB, Camera = rigA.Camera, ScreenRect = fullScreen };
                return;
            }

            // The split line is measured from the shared view, so it turns with the camera.
            Quaternion shared = Quaternion.Euler(_sharedPitch, _sharedYaw, 0f);
            float2 normal = SplitScreenMath.SplitNormal(focusA, focusB, shared * Vector3.right, shared * Vector3.up);

            // The first player draws straight to the screen; the second draws into a texture that the composite
            // camera paints over their side of the line.
            rigA.SetPose(SplitFocus(rigA, focusA, focusB, -normal, yawA, pitchA, distance, freedom), yawA, pitchA, distance);
            rigB.SetPose(SplitFocus(rigB, focusB, focusA, normal, yawB, pitchB, distance, freedom), yawB, pitchB, distance);
            int width = Mathf.Max(1, Mathf.RoundToInt(Screen.width * splitRenderScale));
            int height = Mathf.Max(1, Mathf.RoundToInt(Screen.height * splitRenderScale));
            rigA.ShowOnScreen(fullScreen);
            rigB.ShowInTexture(width, height);

            float dividerAlpha = math.saturate((separation - mergeDistance) / dividerFadeDistance);
            ShowComposite(rigA.Camera, rigB.Texture, normal, dividerAlpha);

            _layout = new SplitLayout { Mode = SplitMode.Voronoi, SplitNormal = normal, DividerAlpha = dividerAlpha, Separation = separation };
            _views[slotA] = new PlayerView { LocalSlot = (byte)slotA, Camera = rigA.Camera, ScreenRect = fullScreen, VoronoiNormal = normal, VoronoiSide = -1f };
            _views[slotB] = new PlayerView { LocalSlot = (byte)slotB, Camera = rigB.Camera, ScreenRect = fullScreen, VoronoiNormal = normal, VoronoiSide = 1f };
        }

        void ShowComposite(Camera baseCamera, Texture viewB, float2 normal, float dividerAlpha)
        {
            _compositeMaterial.SetTexture(ViewBId, viewB);
            _compositeMaterial.SetVector(SplitNormalId, new Vector4(normal.x, normal.y, 0f, 0f));
            _compositeMaterial.SetFloat(DividerAlphaId, dividerAlpha);
            _compositeMaterial.SetFloat(DividerWidthId, dividerWidth);

            // Stretch the quad over the composite camera's whole (orthographic) view.
            float height = compositeCamera.orthographicSize * 2f;
            compositeQuad.transform.localScale = new Vector3(height * compositeCamera.aspect, height, 1f);
            AttachComposite(baseCamera);
        }

        /// <summary>Stacks the composite overlay camera on top of <paramref name="baseCamera"/>, or turns it off when null.</summary>
        void AttachComposite(Camera baseCamera)
        {
            if (_compositeBase == baseCamera)
                return;
            if (_compositeBase != null)
                _compositeBase.GetUniversalAdditionalCameraData().cameraStack.Remove(compositeCamera);
            _compositeBase = baseCamera;
            if (baseCamera != null)
                baseCamera.GetUniversalAdditionalCameraData().cameraStack.Add(compositeCamera);
            compositeCamera.enabled = baseCamera != null;
        }

        /// <summary>
        /// Where a split camera looks, so its player stays in the middle of their own side of the line even after
        /// turning their view (see <see cref="SplitScreenMath.SplitFocus"/>).
        /// </summary>
        float3 SplitFocus(PlayerCameraRig rig, float3 self, float3 other, float2 ownSide, float yaw, float pitch, float distance, float freedom)
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            float halfHeight = distance * Mathf.Tan(rig.Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * Screen.width / Mathf.Max(1f, Screen.height);
            float edge = SplitScreenMath.ScreenEdgeDistance(ownSide, halfWidth, halfHeight);
            float screenOffset = math.min(mergeDistance * 0.5f, edge * playerOffsetInHalf);
            return SplitScreenMath.SplitFocus(self, other, mergeDistance, ownSide, rotation * Vector3.right, rotation * Vector3.up, screenOffset, freedom);
        }

        /// <summary>The point a player's camera looks at: a little above their feet.</summary>
        float3 FocusPoint(int slot) => (float3)_controllers[slot].transform.position + new float3(0f, lookHeight, 0f);

        /// <summary>This frame's look input in degrees: x = turn right, y = tilt up.</summary>
        Vector2 ReadLookDegrees(LocalPlayerInput input)
        {
            Vector2 look = input.ReadLook();
            if (input.UsesMouse)
                return Cursor.lockState == CursorLockMode.Locked ? look * mouseDegreesPerPixel : Vector2.zero;
            return look * (stickDegreesPerSecond * Time.unscaledDeltaTime);
        }

        float ClampPitch(float pitch) => math.clamp(pitch, minPitch, maxPitch);

        /// <summary>
        /// Locks the mouse while a keyboard + mouse player is in the game. Esc lets it go, and a click on the game
        /// takes it back.
        /// </summary>
        void UpdateCursor()
        {
            bool mousePlayer = false;
            for (int i = 0; i < _activeCount; i++)
                mousePlayer |= _inputs[_active[i]].UsesMouse;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                _cursorReleased = true;
            else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                _cursorReleased = false;

            bool locked = mousePlayer && !_cursorReleased && Application.isFocused;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void PlaceListener()
        {
            if (listener == null)
                return;
            if (_activeCount == 0)
            {
                listener.transform.SetPositionAndRotation(overviewCamera.transform.position, overviewCamera.transform.rotation);
                return;
            }

            float3 sum = float3.zero;
            for (int i = 0; i < _activeCount; i++)
                sum += FocusPoint(_active[i]);
            listener.transform.SetPositionAndRotation(sum / _activeCount, Quaternion.Euler(0f, _rigs[_active[0]].OwnYaw, 0f));
        }

        void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
