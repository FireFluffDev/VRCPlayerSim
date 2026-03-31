/// <summary>
/// BotControlWindow — multiplayer ClientSim control panel.
///
/// Open via: VRCSim > Bot Controller  (or Ctrl+Shift+B)
///
/// Design philosophy:
///   The Game View IS the interaction surface — just like ClientSim.
///   Possess a bot, walk around, click stations, interact with objects.
///   This window handles everything you CAN'T do from Game View:
///   spawning bots, triggering VRChat network events, inspecting state.
///
/// Tabs:
///   Players  — spawn, possess, remove, late join
///   Simulate — master transfer, player events, sync, tick
///   Inspect  — station status, synced vars, state report, kinematic check
/// </summary>
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

namespace VRCSim
{
    public class BotControlWindow : EditorWindow
    {
        // ── Serialized state (survives domain reload) ──────────────
        [SerializeField] private string _newBotName = "";
        [SerializeField] private int _botCounter;
        [SerializeField] private int _activeTab;
        [SerializeField] private Vector3 _teleportPos;
        [SerializeField] private bool _showTeleport;
        [SerializeField] private bool _showDebugStations;

        // ── Transient state ────────────────────────────────────────
        private Vector2 _scrollPos;
        private Vector2 _inspectScroll;
        private string _stateReport;
        private GameObject _selectedInspectObj;

        // ── Cached GUIStyles (allocated once) ──────────────────────
        private GUIStyle _bannerLabel;
        private GUIStyle _bannerHint;
        private GUIStyle _sectionHeader;
        private bool _stylesBuilt;

        // ── Gizmo Colors ───────────────────────────────────────────
        private static readonly Color[] BotColors =
        {
            new(0.2f, 0.6f, 1f), new(1f, 0.4f, 0.3f),
            new(0.3f, 0.9f, 0.4f), new(1f, 0.8f, 0.2f),
            new(0.8f, 0.3f, 0.9f), new(0f, 0.9f, 0.9f),
            new(1f, 0.5f, 0f), new(0.6f, 0.6f, 0.6f),
        };

        private static readonly string[] TabNames = { "Players", "Simulate", "Inspect" };

        [MenuItem("VRCSim/Bot Controller %#b")]
        private static void Open()
        {
            var win = GetWindow<BotControlWindow>("VRCSim Bots");
            win.minSize = new Vector2(320, 280);
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            if (VRCSim.IsPossessing) VRCSim.Unpossess();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            _botCounter = 0;
            _stateReport = null;
            Repaint();
        }

        private void BuildStyles()
        {
            if (_stylesBuilt) return;
            _bannerLabel = new GUIStyle(EditorStyles.boldLabel)
                { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            _bannerHint = new GUIStyle(EditorStyles.miniLabel)
                { alignment = TextAnchor.MiddleCenter };
            _sectionHeader = new GUIStyle(EditorStyles.boldLabel)
                { fontSize = 11 };
            _stylesBuilt = true;
        }

        // ── Main GUI ───────────────────────────────────────────────

        private void OnGUI()
        {
            BuildStyles();
            HandleHotkeys();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode with ClientSim to use VRCSim.\n" +
                    "Spawn bots, possess them, play as them in Game View.",
                    MessageType.Info);
                return;
            }

            if (!VRCSim.IsReady)
            {
                EditorGUILayout.HelpBox(
                    "VRCSim not initialized. Click below or call VRCSim.Init().",
                    MessageType.Warning);
                if (GUILayout.Button("Initialize VRCSim", GUILayout.Height(28)))
                    VRCSim.Init();
                return;
            }

            DrawPossessionBanner();
            _activeTab = GUILayout.Toolbar(_activeTab, TabNames);
            EditorGUILayout.Space(4);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            switch (_activeTab)
            {
                case 0: DrawPlayersTab(); break;
                case 1: DrawSimulateTab(); break;
                case 2: DrawInspectTab(); break;
            }

            EditorGUILayout.EndScrollView();
            DrawStatusBar();
        }

        // ── Possession Banner ──────────────────────────────────────

        private void DrawPossessionBanner()
        {
            if (!VRCSim.IsPossessing) return;

            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.3f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = oldBg;

            var bot = VRCSim.PossessedBot;
            EditorGUILayout.LabelField(
                $"\ud83c\udfae POSSESSING: {bot.displayName}" +
                (bot.isMaster ? " \u2605 MASTER" : ""),
                _bannerLabel);
            EditorGUILayout.LabelField(
                "Game View = this bot. Click stations, interact, walk around." +
                " Tab = next, Esc = release.",
                _bannerHint);

            if (GUILayout.Button("Release (back to Player_1)"))
            {
                VRCSim.Unpossess();
                Repaint();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        // ════════════════════════════════════════════════════════════
        // TAB 0: PLAYERS
        // ════════════════════════════════════════════════════════════

        private void DrawPlayersTab()
        {
            // Spawn row
            EditorGUILayout.BeginHorizontal();
            _newBotName = EditorGUILayout.TextField(_newBotName,
                GUILayout.MinWidth(80));
            if (GUILayout.Button("Spawn", GUILayout.Width(55)))
            {
                string name = string.IsNullOrWhiteSpace(_newBotName)
                    ? $"Bot_{_botCounter++}" : _newBotName;
                VRCSim.SpawnPlayer(name);
                _newBotName = "";
            }
            if (GUILayout.Button("Remove All", GUILayout.Width(80)))
            {
                if (VRCSim.IsPossessing) VRCSim.Unpossess();
                VRCSim.RemoveAllPlayers();
            }
            EditorGUILayout.EndHorizontal();

            // Late join (spawn + sync — the real VRChat scenario)
            if (GUILayout.Button("Simulate Late Join (new bot)"))
            {
                var bot = VRCSim.SimulateLateJoin();
                if (bot != null)
                {
                    VRCSim.Possess(bot);
                    FocusOnBot(bot);
                }
            }

            EditorGUILayout.Space(4);

            // Bot list
            var bots = VRCSim.GetBots();
            if (bots.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No bots yet. Spawn one, then Possess to play as them.\n\n" +
                    "Once possessed, your Game View IS that bot \u2014\n" +
                    "walk around, click stations, interact with objects.\n" +
                    "Just like ClientSim, but you can switch between players.",
                    MessageType.None);
                return;
            }

            for (int i = 0; i < bots.Count; i++)
            {
                var bot = bots[i];
                if (bot == null || !bot.IsValid()) continue;

                bool isPossessed = VRCSim.IsPossessing
                    && VRCSim.PossessedBot.playerId == bot.playerId;

                EditorGUILayout.BeginHorizontal();
                Color c = BotColors[i % BotColors.Length];
                var oldColor = GUI.contentColor;
                GUI.contentColor = c;

                string label = bot.displayName;
                if (bot.isMaster) label += " \u2605";
                if (isPossessed) label = "\u25ba " + label;
                var style = isPossessed ? EditorStyles.boldLabel : EditorStyles.label;
                EditorGUILayout.LabelField(label, style, GUILayout.MinWidth(60));
                GUI.contentColor = oldColor;

                if (isPossessed)
                {
                    if (GUILayout.Button("Release", GUILayout.Width(55)))
                    { VRCSim.Unpossess(); Repaint(); }
                }
                else
                {
                    var oldBg = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
                    if (GUILayout.Button("Possess", GUILayout.Width(55)))
                    {
                        VRCSim.Possess(bot);
                        FocusOnBot(bot);
                        Repaint();
                    }
                    GUI.backgroundColor = oldBg;
                }

                if (GUILayout.Button("\ud83d\udcf7", GUILayout.Width(25)))
                    FocusOnBot(bot);
                if (GUILayout.Button("\u2715", GUILayout.Width(22)))
                {
                    if (isPossessed) VRCSim.Unpossess();
                    VRCSim.RemovePlayer(bot);
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        // ════════════════════════════════════════════════════════════
        // TAB 1: SIMULATE (VRChat network scenarios)
        // ════════════════════════════════════════════════════════════

        private void DrawSimulateTab()
        {
            // ── Master Transfer ────────────────────────────────────
            EditorGUILayout.LabelField("Master Transfer", _sectionHeader);
            var bots = VRCSim.GetBots();
            if (bots.Count == 0)
            {
                EditorGUILayout.LabelField("  No bots to transfer master to.");
            }
            else
            {
                foreach (var bot in bots)
                {
                    if (bot == null || !bot.IsValid()) continue;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(
                        $"  {bot.displayName}" +
                        (bot.isMaster ? " \u2605 current master" : ""),
                        GUILayout.MinWidth(120));
                    if (!bot.isMaster)
                    {
                        if (GUILayout.Button("Make Master", GUILayout.Width(90)))
                            VRCSim.TransferMaster(bot);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space(6);

            // ── Player Events ──────────────────────────────────────
            EditorGUILayout.LabelField("Player Events", _sectionHeader);
            EditorGUILayout.LabelField(
                "  Fire join/leave events without actually adding/removing a bot.",
                EditorStyles.wordWrappedMiniLabel);
            if (VRCSim.IsPossessing)
            {
                var bot = VRCSim.PossessedBot;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button($"Fire OnPlayerLeft ({bot.displayName})"))
                    VRCSim.SimulatePlayerLeft(bot);
                if (GUILayout.Button($"Fire OnPlayerJoined ({bot.displayName})"))
                    VRCSim.SimulatePlayerJoined(bot);
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.LabelField("  Possess a bot to fire events for it.");
            }

            EditorGUILayout.Space(6);

            // ── Sync ───────────────────────────────────────────────
            EditorGUILayout.LabelField("Sync & Tick", _sectionHeader);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Sync All to Clients"))
            {
                int count = 0;
                foreach (Component udon in SimReflection.FindAllUdonBehaviours())
                    if (SimReflection.GetSyncedVarNames(udon).Count > 0)
                    {
                        VRCSim.SyncToAll(((MonoBehaviour)udon).gameObject);
                        count++;
                    }
                Debug.Log($"[VRCSim] Synced {count} objects to all clients.");
            }
            if (GUILayout.Button("Tick All (_update)"))
                VRCSim.TickAll();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // ── Teleport (foldout — not primary workflow) ──────────
            _showTeleport = EditorGUILayout.Foldout(_showTeleport,
                "Teleport", true);
            if (_showTeleport && VRCSim.IsPossessing)
            {
                var bot = VRCSim.PossessedBot;
                if (bot.gameObject != null)
                {
                    Vector3 pos = bot.gameObject.transform.position;
                    EditorGUILayout.LabelField(
                        $"  Current: ({pos.x:F1}, {pos.y:F1}, {pos.z:F1})");
                }
                _teleportPos = EditorGUILayout.Vector3Field("Target", _teleportPos);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Go"))
                { VRCSim.Teleport(bot, _teleportPos); FocusOnBot(bot); }
                if (GUILayout.Button("Origin"))
                { VRCSim.Teleport(bot, Vector3.zero); FocusOnBot(bot); }
                if (GUILayout.Button("Spawn"))
                {
                    var spawn = GameObject.Find("SpawnPoint")
                        ?? GameObject.Find("Spawn");
                    VRCSim.Teleport(bot,
                        spawn != null ? spawn.transform.position : Vector3.zero);
                    FocusOnBot(bot);
                }
                EditorGUILayout.EndHorizontal();
            }
            else if (_showTeleport)
            {
                EditorGUILayout.LabelField("  Possess a bot first.");
            }
        }

        // ════════════════════════════════════════════════════════════
        // TAB 2: INSPECT
        // ════════════════════════════════════════════════════════════

        private void DrawInspectTab()
        {
            // ── Station Status (read-only) ─────────────────────────
            EditorGUILayout.LabelField("Station Occupancy", _sectionHeader);
            var stations = UnityEngine.Object.FindObjectsByType<
                VRC.SDK3.Components.VRCStation>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            if (stations.Length == 0)
            {
                EditorGUILayout.LabelField("  No VRCStations in scene.");
            }
            else
            {
                foreach (var station in stations)
                {
                    string occupant = FindOccupant(station);
                    EditorGUILayout.LabelField(
                        $"  {station.gameObject.name}: " +
                        (occupant != null ? $"\u2713 {occupant}" : "\u2014 empty"));
                }

                // Debug fallback: manual sit/exit (collapsed by default)
                _showDebugStations = EditorGUILayout.Foldout(
                    _showDebugStations, "Debug: Manual Sit/Exit", true);
                if (_showDebugStations && VRCSim.IsPossessing)
                {
                    var bot = VRCSim.PossessedBot;
                    foreach (var station in stations)
                    {
                        bool seated = VRCSim.IsPlayerInStation(
                            bot, station.gameObject);
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(
                            $"    {station.gameObject.name}",
                            GUILayout.MinWidth(100));
                        if (seated)
                        {
                            if (GUILayout.Button("Exit", GUILayout.Width(45)))
                                VRCSim.ExitStation(bot, station.gameObject);
                        }
                        else
                        {
                            if (GUILayout.Button("Sit", GUILayout.Width(45)))
                                VRCSim.SitInStation(bot, station.gameObject);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
                else if (_showDebugStations)
                {
                    EditorGUILayout.LabelField("    Possess a bot first.");
                }
            }

            EditorGUILayout.Space(6);

            // ── Synced Var Inspector ────────────────────────────────
            EditorGUILayout.LabelField("Synced Variable Inspector", _sectionHeader);
            _selectedInspectObj = (GameObject)EditorGUILayout.ObjectField(
                "GameObject", _selectedInspectObj, typeof(GameObject), true);

            if (_selectedInspectObj != null)
            {
                var vars = VRCSim.GetSyncedVars(_selectedInspectObj);
                if (vars.Count == 0)
                {
                    EditorGUILayout.LabelField("  No synced variables.");
                }
                else
                {
                    var owner = VRCSim.GetOwner(_selectedInspectObj);
                    EditorGUILayout.LabelField(
                        $"  Owner: {owner?.displayName ?? "none"}");
                    foreach (var kv in vars)
                    {
                        string val = kv.Value != null
                            ? FormatValue(kv.Value) : "null";
                        EditorGUILayout.LabelField($"  {kv.Key} = {val}");
                    }
                }
            }

            EditorGUILayout.Space(6);

            // ── State Report ───────────────────────────────────────
            EditorGUILayout.LabelField("State Report", _sectionHeader);
            if (GUILayout.Button("Generate"))
                _stateReport = VRCSim.GetStateReport();

            if (!string.IsNullOrEmpty(_stateReport))
            {
                _inspectScroll = EditorGUILayout.BeginScrollView(
                    _inspectScroll, GUILayout.MaxHeight(200));
                EditorGUILayout.TextArea(_stateReport, EditorStyles.helpBox);
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space(6);

            // ── Kinematic Validation ───────────────────────────────
            if (GUILayout.Button("Check Kinematic State (VRCObjectSync)"))
            {
                var issues = VRCSim.ValidateKinematic();
                if (issues.Count == 0)
                    Debug.Log("[VRCSim] Kinematic check: all OK.");
                else
                    foreach (var issue in issues)
                        Debug.LogWarning($"[VRCSim] {issue}");
            }
        }

        // ── Status Bar ─────────────────────────────────────────────

        private void DrawStatusBar()
        {
            var bots = VRCSim.GetBots();
            string status = VRCSim.IsPossessing
                ? $"Playing as {VRCSim.PossessedBot.displayName} \u2014 " +
                  $"{bots.Count} bot(s)"
                : $"VRCSim Ready \u2014 {bots.Count} bot(s)";
            EditorGUILayout.LabelField(status, EditorStyles.centeredGreyMiniLabel);
        }

        // ── Hotkeys ────────────────────────────────────────────────

        private void HandleHotkeys()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;
            if (!Application.isPlaying || !VRCSim.IsReady) return;

            if (e.keyCode == KeyCode.Tab && VRCSim.IsPossessing)
            { CyclePossession(); e.Use(); }
            else if (e.keyCode == KeyCode.Escape && VRCSim.IsPossessing)
            { VRCSim.Unpossess(); Repaint(); e.Use(); }
        }

        private void CyclePossession()
        {
            var bots = VRCSim.GetBots();
            if (bots.Count == 0) return;
            int idx = -1;
            if (VRCSim.IsPossessing)
                for (int i = 0; i < bots.Count; i++)
                    if (bots[i].playerId == VRCSim.PossessedBot.playerId)
                    { idx = i; break; }
            var next = bots[(idx + 1) % bots.Count];
            VRCSim.Possess(next);
            FocusOnBot(next);
            Repaint();
        }

        // ════════════════════════════════════════════════════════════
        // SCENE VIEW GIZMOS
        // ════════════════════════════════════════════════════════════

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!Application.isPlaying || !VRCSim.IsReady) return;

            var bots = VRCSim.GetBots();
            for (int i = 0; i < bots.Count; i++)
            {
                var bot = bots[i];
                if (bot?.gameObject == null) continue;

                bool isPossessed = VRCSim.IsPossessing
                    && VRCSim.PossessedBot.playerId == bot.playerId;
                Color color = BotColors[i % BotColors.Length];
                Vector3 pos = bot.gameObject.transform.position;

                // Sphere
                Handles.color = isPossessed
                    ? new Color(color.r, color.g, color.b, 0.9f)
                    : new Color(color.r, color.g, color.b, 0.35f);
                Handles.SphereHandleCap(0, pos, Quaternion.identity,
                    isPossessed ? 0.7f : 0.4f, EventType.Repaint);

                // Label
                string label = bot.displayName;
                if (bot.isMaster) label += " \u2605";
                if (isPossessed) label = "\u25ba " + label;
                Handles.Label(pos + Vector3.up * 1.2f, label,
                    new GUIStyle(EditorStyles.boldLabel)
                        { normal = { textColor = color },
                          fontSize = isPossessed ? 14 : 11 });

                // Click to possess
                if (Handles.Button(pos + Vector3.up * 0.5f, Quaternion.identity,
                        0.3f, 0.4f, Handles.SphereHandleCap))
                {
                    VRCSim.Possess(bot);
                    Repaint();
                }
            }

            if (bots.Count > 0) sceneView.Repaint();
        }

        // ── Helpers ────────────────────────────────────────────────

        private static void FocusOnBot(VRCPlayerApi bot)
        {
            if (bot?.gameObject == null) return;
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            sv.LookAt(bot.gameObject.transform.position, sv.rotation, 8f);
            sv.Repaint();
        }

        /// <summary>Find who's sitting in a station, or null.</summary>
        private static string FindOccupant(VRC.SDK3.Components.VRCStation station)
        {
            var helper = SimReflection.GetStationHelper(station.gameObject);
            if (helper == null) return null;
            var user = SimReflection.GetStationUser(helper);
            if (user == null || !user.IsValid()) return null;
            return user.displayName;
        }

        private static string FormatValue(object val)
        {
            if (val is float f) return f.ToString("F2");
            if (val is double d) return d.ToString("F2");
            if (val is int[] ia) return $"int[{ia.Length}]:{string.Join(",", ia)}";
            if (val is float[] fa)
            {
                var parts = new string[fa.Length];
                for (int i = 0; i < fa.Length; i++) parts[i] = fa[i].ToString("F1");
                return $"float[{fa.Length}]:{string.Join(",", parts)}";
            }
            if (val is bool[] ba) return $"bool[{ba.Length}]:{string.Join(",", ba)}";
            if (val is string[] sa) return $"string[{sa.Length}]:{string.Join(",", sa)}";
            if (val is Array arr)
                return $"{val.GetType().GetElementType()?.Name}[{arr.Length}]";
            return val.ToString();
        }
    }
}
#endif
