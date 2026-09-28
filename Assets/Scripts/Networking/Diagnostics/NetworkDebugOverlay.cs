using UnityEngine;
using UnityEngine.InputSystem;

namespace Networking
{
    [DisallowMultipleComponent]
    public sealed class NetworkDebugOverlay : MonoBehaviour
    {
        private const float PanelWidth = 350f;
        private const float PanelHeight = 315f;
        private const float GraphHeight = 80f;

        private readonly Vector3[] _graphPoints =
            new Vector3[NetworkDiagnostics.CorrectionHistoryCapacity];

        private GUIStyle _labelStyle;
        private GUIStyle _warningStyle;
        private Texture2D _background;
        private Material _lineMaterial;

        private NetworkRuntime _runtime;
        private float _smoothedFrameTime = 1f / 60f;
        private bool _visible;

        public void Initialize(
            NetworkRuntime runtime,
            bool visible)
        {
            _runtime = runtime;
            _visible = visible;
        }

        private void Update()
        {
            _smoothedFrameTime = Mathf.Lerp(
                _smoothedFrameTime,
                Mathf.Max(Time.unscaledDeltaTime, 0.00001f),
                0.1f);

            if (Keyboard.current?.f3Key
                    .wasPressedThisFrame == true)
            {
                _visible = !_visible;
            }
        }

        private void OnGUI()
        {
            if (!_visible ||
                _runtime == null ||
                _runtime.Diagnostics == null)
            {
                return;
            }

            EnsureGuiResources();

            NetworkDiagnostics diagnostics =
                _runtime.Diagnostics;

            Rect panel = new Rect(
                12f,
                12f,
                PanelWidth,
                PanelHeight);

            GUI.DrawTexture(panel, _background);

            GUILayout.BeginArea(
                new Rect(
                    panel.x + 12f,
                    panel.y + 10f,
                    panel.width - 24f,
                    panel.height - 20f));

            GUILayout.Label(
                "NETWORK DIAGNOSTICS  [F3]",
                _labelStyle);

            GUILayout.Label(
                $"FPS: {1f / _smoothedFrameTime:F0}    " +
                $"Server tick: {diagnostics.ServerTick}",
                _labelStyle);

            GUILayout.Label(
                $"RTT: {diagnostics.RoundTripTimeMilliseconds:F1} ms",
                _labelStyle);

            GUIStyle pendingStyle =
                diagnostics.PendingInputCount >
                    _runtime.PendingInputWarningThreshold
                    ? _warningStyle
                    : _labelStyle;

            GUILayout.Label(
                $"Pending inputs: {diagnostics.PendingInputCount}    " +
                $"Sent: {diagnostics.LatestSentInputSequence}    " +
                $"Ack: {diagnostics.LatestAcknowledgedInputSequence}",
                pendingStyle);

            GUILayout.Label(
                $"Correction: {diagnostics.LatestCorrectionDistance:F4} m    " +
                $"Max: {diagnostics.MaximumCorrectionDistance:F4} m",
                _labelStyle);

            GUILayout.Label(
                $"Packets S/R: {diagnostics.PacketsSent} / " +
                $"{diagnostics.PacketsReceived}    " +
                $"Bytes S/R: {diagnostics.BytesSent} / " +
                $"{diagnostics.BytesReceived}",
                _labelStyle);

            GUILayout.Label(
                $"Sim drops/reorders: " +
                $"{diagnostics.SimulatedPacketsDropped} / " +
                $"{diagnostics.SimulatedPacketsReordered}",
                _labelStyle);

            NetworkSimulationSettings simulation =
                _runtime.NetworkSimulation;

            GUILayout.Label(
                simulation.Enabled
                    ? $"SIM ON: {simulation.LatencyMilliseconds:F0} ms + " +
                      $"{simulation.JitterMilliseconds:F0} ms jitter, " +
                      $"{simulation.PacketLossPercent:F1}% loss, " +
                      $"{simulation.ReorderingPercent:F1}% reorder"
                    : "SIM OFF",
                simulation.Enabled
                    ? _warningStyle
                    : _labelStyle);

            GUILayout.Space(4f);
            GUILayout.Label(
                "Pre-restore correction history",
                _labelStyle);

            Rect graph = GUILayoutUtility.GetRect(
                PanelWidth - 30f,
                GraphHeight);

            DrawGraph(graph, diagnostics);
            GUILayout.EndArea();
        }

        private void DrawGraph(
            Rect rect,
            NetworkDiagnostics diagnostics)
        {
            GUI.Box(rect, GUIContent.none);

            if (Event.current.type != EventType.Repaint)
                return;

            int count = diagnostics.CorrectionHistoryCount;
            if (count < 2)
                return;

            float maximum = 0.01f;

            for (int index = 0; index < count; index++)
            {
                maximum = Mathf.Max(
                    maximum,
                    diagnostics.GetCorrectionHistory(index));
            }

            for (int index = 0; index < count; index++)
            {
                float x = rect.x +
                    ((float)index / (count - 1)) * rect.width;

                float normalized =
                    diagnostics.GetCorrectionHistory(index) /
                    maximum;

                float y = rect.yMax -
                    (normalized * (rect.height - 2f)) - 1f;

                _graphPoints[index] = new Vector3(x, y, 0f);
            }

            if (_lineMaterial == null)
            {
                Shader shader = Shader.Find(
                    "Hidden/Internal-Colored");

                if (shader == null)
                    return;

                _lineMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            _lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            GL.Begin(GL.LINES);
            GL.Color(new Color(0.2f, 0.9f, 1f, 1f));

            for (int index = 1; index < count; index++)
            {
                GL.Vertex(_graphPoints[index - 1]);
                GL.Vertex(_graphPoints[index]);
            }

            GL.End();
            GL.PopMatrix();
        }

        private void EnsureGuiResources()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    normal = { textColor = Color.white }
                };

                _warningStyle = new GUIStyle(_labelStyle)
                {
                    normal = { textColor = new Color(1f, 0.65f, 0.2f) }
                };
            }

            if (_background == null)
            {
                _background = new Texture2D(1, 1)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };

                _background.SetPixel(
                    0,
                    0,
                    new Color(0f, 0f, 0f, 0.82f));

                _background.Apply();
            }
        }

        private void OnDestroy()
        {
            if (_background != null)
                Destroy(_background);

            if (_lineMaterial != null)
                Destroy(_lineMaterial);
        }
    }
}
