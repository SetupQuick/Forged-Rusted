using System;
using System.Collections.Generic;
using GameUI;
using Items;
using Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using World;
using World.Streaming;

namespace Game
{
    /// <summary>
    /// Main menu, hosting/joining, connecting screen, pause menu, settings
    /// and controls. Everything is built from code (no scene setup).
    ///
    /// While in the menu the world is generated offline around a slowly
    /// circling camera as a backdrop; hosting keeps that world, joining
    /// switches to the server's seed.
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        private enum State
        {
            MainMenu,
            Connecting,
            Playing,
            Paused
        }

        private const float OrbitRadius = 45f;
        private const float OrbitHeight = 22f;
        private const float OrbitSpeed = 2.5f; // degrees per second

        private State _state;
        private RectTransform _canvas;
        private GameObject _screen;
        private readonly Stack<Action> _back = new Stack<Action>();

        private Camera _menuCamera;
        private Vector3 _orbitCenter;
        private float _orbitAngle;
        private bool _backdropPlaced;

        private string _message = string.Empty;
        private NetworkRuntime _subscribedRuntime;

        public static GameFlow Instance { get; private set; }

        public bool IsPaused => _state == State.Paused;

        public bool InMenus => _state != State.Playing;

        private void Awake()
        {
            Instance = this;
            BuildCanvas();
            ShowMainMenu();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            UnsubscribeRuntime();
            PlayerInputReader.SetGameplayInputBlocked(this, false);
        }

        // ------------------------------------------------------------
        // Starting and leaving games
        // ------------------------------------------------------------

        public void Host(ushort port)
        {
            NetworkRuntime runtime = NetworkRuntime.Current;

            if (runtime == null)
            {
                ShowMainMenu("This scene has no network runtime.");
                return;
            }

            try
            {
                WorldManager.Active?.PrepareForHosting();
                runtime.StartRuntime(NetworkMode.ListenServer, string.Empty, port);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                runtime.Shutdown();
                ShowMainMenu($"Could not host on port {port}: {exception.Message}");
                return;
            }

            EnterPlaying();
        }

        public void Join(string address, ushort port)
        {
            NetworkRuntime runtime = NetworkRuntime.Current;

            if (runtime == null)
            {
                ShowMainMenu("This scene has no network runtime.");
                return;
            }

            try
            {
                runtime.StartRuntime(NetworkMode.Client, address, port);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                runtime.Shutdown();
                ShowMainMenu($"Could not connect: {exception.Message}");
                return;
            }

            SubscribeRuntime(runtime);
            ShowConnecting(address, port);
        }

        /// <summary>Leaves the current game and returns to the main menu.</summary>
        public void Leave(string message = "")
        {
            UnsubscribeRuntime();
            NetworkRuntime.Current?.Shutdown();

            if (GameHud.Instance != null)
                Destroy(GameHud.Instance.gameObject);

            ShowMainMenu(message);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void EnterPlaying()
        {
            _state = State.Playing;
            ClearScreen();
            PlayerInputReader.SetGameplayInputBlocked(this, false);
            _canvas.gameObject.SetActive(false);
        }

        private void SubscribeRuntime(NetworkRuntime runtime)
        {
            UnsubscribeRuntime();
            _subscribedRuntime = runtime;
            runtime.ClientStateChanged += OnClientStateChanged;
        }

        private void UnsubscribeRuntime()
        {
            if (_subscribedRuntime != null)
                _subscribedRuntime.ClientStateChanged -= OnClientStateChanged;

            _subscribedRuntime = null;
        }

        private void OnClientStateChanged(ClientConnectionState state)
        {
            if (state == ClientConnectionState.Connected && _state == State.Connecting)
            {
                EnterPlaying();
            }
            else if (state == ClientConnectionState.TimedOut)
            {
                Leave(_state == State.Connecting
                    ? "Could not reach the server (timed out)."
                    : "Lost connection to the server.");
            }
            else if (state == ClientConnectionState.Stopped && _state != State.MainMenu)
            {
                Leave("Disconnected from the server.");
            }
        }

        // ------------------------------------------------------------
        // Update
        // ------------------------------------------------------------

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !InventoryUI.EscapeHandledThisFrame)
                OnEscape();

            if (_state == State.MainMenu || _state == State.Connecting || PlayerInventory.Local == null)
                UpdateBackdrop();
            else
                RemoveMenuCamera();

            if (_state != State.Playing)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void OnEscape()
        {
            switch (_state)
            {
                case State.Playing:
                    if (GameHud.Instance != null && GameHud.Instance.Inventory != null && GameHud.Instance.Inventory.IsOpen)
                        return;
                    ShowPauseMenu();
                    break;

                case State.Paused:
                    if (_back.Count > 0)
                        _back.Pop()();
                    else
                        Resume();
                    break;

                default:
                    if (_back.Count > 0)
                        _back.Pop()();
                    break;
            }
        }

        /// <summary>
        /// Generates the world offline and circles a camera over it while
        /// no game is running (menu backdrop).
        /// </summary>
        private void UpdateBackdrop()
        {
            WorldManager world = WorldManager.Active;
            NetworkRuntime runtime = NetworkRuntime.Current;

            if (world == null)
                return;

            if (!world.IsReady && (runtime == null || !runtime.IsRunning))
                world.InitializeWorld(world.Settings.Seed);

            if (!world.IsReady)
                return;

            if (_menuCamera == null)
            {
                var go = new GameObject("Menu Camera");
                go.tag = "MainCamera";
                _menuCamera = go.AddComponent<Camera>();
                _menuCamera.farClipPlane = 600f;
                go.AddComponent<AudioListener>();
                _backdropPlaced = false;
            }

            if (!_backdropPlaced)
            {
                _orbitCenter = world.FindSpawnPosition(Vector3.zero);
                _backdropPlaced = true;
            }

            ChunkStreamer streamer = FindFirstObjectByType<ChunkStreamer>();
            if (streamer != null)
                streamer.FallbackTarget = _menuCamera.transform;

            _orbitAngle += OrbitSpeed * Time.unscaledDeltaTime;
            float radians = _orbitAngle * Mathf.Deg2Rad;

            Vector3 position = _orbitCenter + new Vector3(Mathf.Cos(radians) * OrbitRadius, OrbitHeight, Mathf.Sin(radians) * OrbitRadius);
            _menuCamera.transform.position = position;
            _menuCamera.transform.LookAt(_orbitCenter + Vector3.up * 4f);
        }

        private void RemoveMenuCamera()
        {
            if (_menuCamera != null)
            {
                Destroy(_menuCamera.gameObject);
                _menuCamera = null;
            }
        }

        // ------------------------------------------------------------
        // Screens
        // ------------------------------------------------------------

        private void BuildCanvas()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem");
                events.AddComponent<EventSystem>();
                events.AddComponent<InputSystemUIInputModule>();
                DontDestroyOnLoad(events);
            }

            var go = new GameObject("Menus", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            _canvas = (RectTransform)go.transform;
        }

        private RectTransform NewScreen(float dim)
        {
            ClearScreen();
            _canvas.gameObject.SetActive(true);

            Image background = UiBuilder.Image("Screen", _canvas, new Color(0.03f, 0.04f, 0.06f, dim), raycast: true);
            UiBuilder.Stretch(background.rectTransform);
            _screen = background.gameObject;

            return background.rectTransform;
        }

        private void ClearScreen()
        {
            if (_screen != null)
                Destroy(_screen);

            _screen = null;
        }

        public void ShowMainMenu(string message = "")
        {
            _state = State.MainMenu;
            _back.Clear();
            _message = message ?? string.Empty;
            PlayerInputReader.SetGameplayInputBlocked(this, true);

            RectTransform screen = NewScreen(0.25f);

            // Title on the left over the world backdrop.
            TextMeshProUGUI title = UiBuilder.Text("Title", screen, 92, TextAlignmentOptions.TopLeft, UiBuilder.TextColor);
            UiBuilder.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(110f, -110f), new Vector2(1200f, 120f));
            title.text = "FORGED <color=#E0A040>&</color> RUSTED";
            title.fontStyle = FontStyles.Bold;
            UiBuilder.AddOutline(title, new Color(0f, 0f, 0f, 0.6f), 4f);

            TextMeshProUGUI subtitle = UiBuilder.Text("Subtitle", screen, 26, TextAlignmentOptions.TopLeft, UiBuilder.SubtleTextColor);
            UiBuilder.Place(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(116f, -232f), new Vector2(1000f, 40f));
            subtitle.text = "A multiplayer voxel adventure";
            UiBuilder.AddOutline(subtitle, new Color(0f, 0f, 0f, 0.6f), 2f);

            RectTransform column = MenuWidgets.Column(screen, 14f, 440f);
            UiBuilder.Place(column, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(110f, -60f), new Vector2(440f, 0f));
            column.pivot = new Vector2(0f, 1f);
            column.anchoredPosition = new Vector2(110f, 120f);

            MenuWidgets.Button(column, "Host Game", ShowHostScreen, primary: true, height: 64f);
            MenuWidgets.Button(column, "Join Game", ShowJoinScreen, height: 64f);
            MenuWidgets.Button(column, "Settings", () => ShowSettings(() => ShowMainMenu()), height: 64f);
            MenuWidgets.Button(column, "Controls", () => ShowControls(() => ShowMainMenu()), height: 64f);
            MenuWidgets.Button(column, "Quit", Quit, height: 64f);

            string problems = GameBootstrap.SceneProblems;
            string text = string.IsNullOrEmpty(problems) ? _message : (_message + "\n" + problems).Trim();

            if (!string.IsNullOrEmpty(text))
            {
                TextMeshProUGUI info = UiBuilder.Text("Message", screen, 22, TextAlignmentOptions.BottomLeft, new Color(1f, 0.55f, 0.45f, 1f));
                UiBuilder.Place(info.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(110f, 90f), new Vector2(1400f, 120f));
                info.text = text;
                UiBuilder.AddOutline(info, new Color(0f, 0f, 0f, 0.7f), 2f);
            }

            TextMeshProUGUI version = UiBuilder.Text("Version", screen, 16, TextAlignmentOptions.BottomRight, new Color(1f, 1f, 1f, 0.5f));
            UiBuilder.Place(version.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 16f), new Vector2(600f, 30f));
            version.text = $"{Application.version}  |  network protocol v{NetworkProtocol.Version}";
        }

        private void ShowHostScreen()
        {
            RectTransform screen = NewScreen(0.55f);
            _back.Push(() => ShowMainMenu());

            Image panel = MenuWidgets.Panel(screen, new Vector2(640f, 380f));
            RectTransform column = MenuWidgets.Column(panel.rectTransform, 12f, 560f);
            UiBuilder.Place(column, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(560f, 0f));

            MenuWidgets.Header(column, "Host a game", 34f);
            MenuWidgets.Label(column, "Others on your network join with your IP address.", 19f);

            string port = GameSettings.LastPort.ToString();
            MenuWidgets.TextField(column, "Port", port, GameBootstrap.DefaultPort.ToString(), v => port = v, TMP_InputField.ContentType.IntegerNumber);

            MenuWidgets.Button(column, "Start", () =>
            {
                ushort p = ParsePort(port);
                GameSettings.LastPort = p;
                GameSettings.Save();
                Host(p);
            }, primary: true);

            MenuWidgets.Button(column, "Back", () => ShowMainMenu());
        }

        private void ShowJoinScreen()
        {
            RectTransform screen = NewScreen(0.55f);
            _back.Push(() => ShowMainMenu());

            Image panel = MenuWidgets.Panel(screen, new Vector2(640f, 440f));
            RectTransform column = MenuWidgets.Column(panel.rectTransform, 12f, 560f);
            UiBuilder.Place(column, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(560f, 0f));

            MenuWidgets.Header(column, "Join a game", 34f);

            string address = GameSettings.LastAddress;
            string port = GameSettings.LastPort.ToString();

            MenuWidgets.TextField(column, "Server address", address, "127.0.0.1", v => address = v.Trim());
            MenuWidgets.TextField(column, "Port", port, GameBootstrap.DefaultPort.ToString(), v => port = v, TMP_InputField.ContentType.IntegerNumber);

            MenuWidgets.Button(column, "Join", () =>
            {
                if (string.IsNullOrWhiteSpace(address))
                    address = "127.0.0.1";

                ushort p = ParsePort(port);
                GameSettings.LastAddress = address;
                GameSettings.LastPort = p;
                GameSettings.Save();
                Join(address, p);
            }, primary: true);

            MenuWidgets.Button(column, "Back", () => ShowMainMenu());
        }

        private void ShowConnecting(string address, ushort port)
        {
            _state = State.Connecting;
            _back.Clear();
            _back.Push(() => Leave("Connection cancelled."));

            RectTransform screen = NewScreen(0.55f);
            Image panel = MenuWidgets.Panel(screen, new Vector2(620f, 240f));
            RectTransform column = MenuWidgets.Column(panel.rectTransform, 16f, 540f);
            UiBuilder.Place(column, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(540f, 0f));

            MenuWidgets.Label(column, $"Connecting to {address}:{port}...", 26f, TextAlignmentOptions.Center);
            MenuWidgets.Button(column, "Cancel", () => Leave("Connection cancelled."));
        }

        private void ShowPauseMenu()
        {
            _state = State.Paused;
            _back.Clear();
            PlayerInputReader.SetGameplayInputBlocked(this, true);

            RectTransform screen = NewScreen(0.55f);
            Image panel = MenuWidgets.Panel(screen, new Vector2(520f, 470f));
            RectTransform column = MenuWidgets.Column(panel.rectTransform, 14f, 440f);
            UiBuilder.Place(column, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(440f, 0f));

            MenuWidgets.Header(column, "Paused", 36f);
            MenuWidgets.Button(column, "Resume", Resume, primary: true);
            MenuWidgets.Button(column, "Settings", () => ShowSettings(ShowPauseMenu));
            MenuWidgets.Button(column, "Controls", () => ShowControls(ShowPauseMenu));

            NetworkRuntime runtime = NetworkRuntime.Current;
            string leaveText = runtime != null && runtime.RunsServer ? "Stop hosting" : "Disconnect";
            MenuWidgets.Button(column, leaveText, () => Leave());
            MenuWidgets.Button(column, "Quit game", Quit);
        }

        public void Resume()
        {
            _back.Clear();
            EnterPlaying();
        }

        private void ShowControls(Action back)
        {
            RectTransform screen = NewScreen(0.6f);
            _back.Push(back);

            Image panel = MenuWidgets.Panel(screen, new Vector2(760f, 860f));
            RectTransform column = MenuWidgets.Column(panel.rectTransform, 2f, 680f);
            UiBuilder.Place(column, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(680f, 0f));

            MenuWidgets.Header(column, "Controls", 34f);

            foreach ((string action, string keys) in Controls)
            {
                if (keys == null)
                    MenuWidgets.Header(column, action, 22f);
                else
                    MenuWidgets.KeyValue(column, action, keys);
            }

            MenuWidgets.Label(column, "Mouse sensitivity and invert Y are in Settings > Controls.", 17f);
            MenuWidgets.Button(column, "Back", back);
        }

        private static readonly (string, string)[] Controls =
        {
            ("Movement", null),
            ("Move", "W A S D / arrow keys"),
            ("Look around", "Mouse"),
            ("Jump / swim up", "Space"),
            ("Sprint", "Left Shift"),
            ("Crouch / dive", "C / Left Ctrl"),
            ("World", null),
            ("Break block", "Left mouse (hold)"),
            ("Place block", "Right mouse (hold)"),
            ("Pick up item (offline)", "E"),
            ("Inventory", null),
            ("Select hotbar slot", "1 - 9 / mouse wheel"),
            ("Open / close inventory", "Tab / I"),
            ("Move stack / split / quick move", "Click / right-click / Shift+click"),
            ("Other", null),
            ("Pause menu / back", "Esc"),
            ("Network statistics", "F3")
        };

        private void ShowSettings(Action back)
        {
            ShowSettingsTab(back, 0);
        }

        private void ShowSettingsTab(Action back, int tab)
        {
            RectTransform screen = NewScreen(0.6f);
            _back.Push(() =>
            {
                GameSettings.Save();
                back();
            });

            Image panel = MenuWidgets.Panel(screen, new Vector2(820f, 900f));
            RectTransform column = MenuWidgets.Column(panel.rectTransform, 8f, 740f);
            UiBuilder.Place(column, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(740f, 0f));

            MenuWidgets.Header(column, "Settings", 34f);

            // Tabs
            RectTransform tabs = UiBuilder.Rect("Tabs", column);
            tabs.sizeDelta = new Vector2(0f, 50f);
            var tabLayout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            tabLayout.spacing = 8f;
            tabLayout.childControlWidth = true;
            tabLayout.childForceExpandWidth = true;
            tabLayout.childControlHeight = true;

            string[] names = { "Graphics", "Audio", "Controls", "Interface" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                MenuWidgets.Button(tabs, names[i], () => { _back.Pop(); ShowSettingsTab(back, index); }, primary: i == tab, height: 48f);
            }

            switch (tab)
            {
                case 0: GraphicsSettings(column); break;
                case 1: AudioSettings(column); break;
                case 2: ControlSettings(column); break;
                default: InterfaceSettings(column); break;
            }

            RectTransform buttons = UiBuilder.Rect("Buttons", panel.rectTransform);
            UiBuilder.Place(buttons, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(740f, 54f));
            var buttonLayout = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            buttonLayout.spacing = 12f;
            buttonLayout.childControlWidth = true;
            buttonLayout.childForceExpandWidth = true;
            buttonLayout.childControlHeight = true;

            MenuWidgets.Button(buttons, "Reset to defaults", () =>
            {
                GameSettings.ResetToDefaults();
                _back.Pop();
                ShowSettingsTab(back, tab);
            });

            MenuWidgets.Button(buttons, "Back", () => _back.Pop()(), primary: true);
        }

        private static void GraphicsSettings(Transform column)
        {
            MenuWidgets.Selector(column, "Display mode", new[] { "Fullscreen", "Borderless window", "Window" }, (int)GameSettings.DisplayMode, i =>
            {
                GameSettings.DisplayMode = (DisplayMode)i;
                GameSettings.Apply();
            });

            Resolution[] resolutions = UniqueResolutions();
            var labels = new List<string> { "Native" };
            int current = 0;

            for (int i = 0; i < resolutions.Length; i++)
            {
                labels.Add($"{resolutions[i].width} x {resolutions[i].height}");

                if (resolutions[i].width == GameSettings.ResolutionWidth && resolutions[i].height == GameSettings.ResolutionHeight)
                    current = i + 1;
            }

            MenuWidgets.Selector(column, "Resolution", labels.ToArray(), current, i =>
            {
                GameSettings.ResolutionWidth = i == 0 ? 0 : resolutions[i - 1].width;
                GameSettings.ResolutionHeight = i == 0 ? 0 : resolutions[i - 1].height;
                GameSettings.Apply();
            });

            MenuWidgets.Toggle(column, "V-Sync", GameSettings.VSync, v => { GameSettings.VSync = v; GameSettings.Apply(); });

            string[] fpsLabels = Array.ConvertAll(GameSettings.FpsOptions, f => f == 0 ? "Unlimited" : f.ToString());
            int fpsIndex = Math.Max(0, Array.IndexOf(GameSettings.FpsOptions, GameSettings.MaxFps));
            MenuWidgets.Selector(column, "Frame rate limit (no V-Sync)", fpsLabels, fpsIndex, i =>
            {
                GameSettings.MaxFps = GameSettings.FpsOptions[i];
                GameSettings.Apply();
            });

            string[] qualities = QualitySettings.names;
            if (qualities.Length > 0)
            {
                int quality = GameSettings.QualityLevel >= 0 ? GameSettings.QualityLevel : QualitySettings.GetQualityLevel();
                MenuWidgets.Selector(column, "Quality preset", qualities, quality, i =>
                {
                    GameSettings.QualityLevel = i;
                    GameSettings.Apply();
                });
            }

            MenuWidgets.Slider(column, "Render distance", GameSettings.MinRenderDistance, GameSettings.MaxRenderDistance, GameSettings.RenderDistance,
                v => $"{(int)v} chunks", v => { GameSettings.RenderDistance = (int)v; GameSettings.Apply(); }, wholeNumbers: true);

            MenuWidgets.Selector(column, "Shadows", new[] { "Off", "Low", "Medium", "High" }, (int)GameSettings.Shadows, i =>
            {
                GameSettings.Shadows = (ShadowQuality)i;
                GameSettings.Apply();
            });

            MenuWidgets.Toggle(column, "Post-processing (bloom, colour)", GameSettings.PostProcessing, v => { GameSettings.PostProcessing = v; GameSettings.Apply(); });

            MenuWidgets.Slider(column, "Field of view", 50f, 110f, GameSettings.FieldOfView,
                v => $"{Mathf.RoundToInt(v)}°", v => { GameSettings.FieldOfView = v; GameSettings.Apply(); }, wholeNumbers: true);
        }

        private static void AudioSettings(Transform column)
        {
            MenuWidgets.Slider(column, "Master volume", 0f, 1f, GameSettings.MasterVolume,
                v => $"{Mathf.RoundToInt(v * 100)}%", v => { GameSettings.MasterVolume = v; GameSettings.Apply(); });

            MenuWidgets.Slider(column, "Effects volume", 0f, 1f, GameSettings.EffectsVolume,
                v => $"{Mathf.RoundToInt(v * 100)}%", v => { GameSettings.EffectsVolume = v; GameSettings.Apply(); });

            MenuWidgets.Label(column, "Music volume will appear here once the game has music.", 17f);
        }

        private static void ControlSettings(Transform column)
        {
            MenuWidgets.Slider(column, "Mouse sensitivity", 0.1f, 4f, GameSettings.MouseSensitivity,
                v => v.ToString("0.00"), v => { GameSettings.MouseSensitivity = v; GameSettings.Apply(); });

            MenuWidgets.Toggle(column, "Invert mouse Y", GameSettings.InvertY, v => { GameSettings.InvertY = v; GameSettings.Apply(); });

            MenuWidgets.Label(column, "Key bindings are listed under Controls in the menu.", 17f);
        }

        private static void InterfaceSettings(Transform column)
        {
            MenuWidgets.Toggle(column, "Show FPS counter", GameSettings.ShowFps, v => { GameSettings.ShowFps = v; GameSettings.Apply(); });
            MenuWidgets.Toggle(column, "Show network statistics (F3)", GameSettings.ShowNetworkStats, v => { GameSettings.ShowNetworkStats = v; GameSettings.Apply(); });
        }

        private static Resolution[] UniqueResolutions()
        {
            var unique = new List<Resolution>();
            var seen = new HashSet<long>();

            foreach (Resolution resolution in Screen.resolutions)
            {
                long key = (long)resolution.width << 32 | (uint)resolution.height;
                if (seen.Add(key))
                    unique.Add(resolution);
            }

            return unique.ToArray();
        }

        private static ushort ParsePort(string text)
        {
            return ushort.TryParse(text, out ushort port) && port > 0 ? port : GameBootstrap.DefaultPort;
        }
    }
}
