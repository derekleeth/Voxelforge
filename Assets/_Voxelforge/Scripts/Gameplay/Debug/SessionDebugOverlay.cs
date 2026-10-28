using System.Text;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>A corner readout of the host and every player, for play-testing. Press F3 to hide it.</summary>
    public sealed class SessionDebugOverlay : MonoBehaviour
    {
        [SerializeField] GameSession session;

        [SerializeField] bool visible = true;

        readonly StringBuilder _text = new StringBuilder();
        GUIStyle _style;
        float _smoothedDeltaTime = 1f / 60f;

        void Update()
        {
            _smoothedDeltaTime = Mathf.Lerp(_smoothedDeltaTime, Time.unscaledDeltaTime, 0.05f);
            if (UnityEngine.InputSystem.Keyboard.current?.f3Key.wasPressedThisFrame == true)
                visible = !visible;
        }

        void OnGUI()
        {
            if (!visible || session == null || session.NetworkManager == null)
                return;

            _style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = true };

            var network = session.NetworkManager;
            _text.Clear();
            _text.Append($"<b>{(network.IsHost ? "Host" : network.IsServer ? "Server" : network.IsClient ? "Client" : "Offline")}</b>");
            if (network.IsListening)
                _text.Append($"  client {network.LocalClientId}");
            _text.Append($"   {1f / _smoothedDeltaTime:F0} FPS");

            foreach (PlayerController player in session.Players.Players)
            {
                if (player == null)
                    continue;

                Vector3 p = player.transform.position;
                string state = player.IsWaitingForGround ? "waiting for ground" : player.Grounded ? "grounded" : "airborne";
                _text.Append($"\n{player.PlayerId}  slot {player.LocalSlot}  ({p.x:F1}, {p.y:F1}, {p.z:F1})  {state}");
            }

            GUILayout.BeginArea(new Rect(10, 10, 460, 200));
            GUILayout.Label(_text.ToString(), _style);
            GUILayout.EndArea();
        }
    }
}
