using System;
using System.Reflection;
using UnityEngine;
using VRC.SDKBase;

namespace VRCSim
{
    /// <summary>
    /// Cached reflection accessors for ClientSim internals.
    /// Isolated here so SDK version breaks are easy to diagnose and fix.
    /// </summary>
    public static class SimReflection
    {
        // ── Type cache ─────────────────────────────────────────────
        private static Type _clientSimMainType;
        private static Type _playerManagerType;
        private static Type _stationHelperType;
        private static Type _udonHelperType;
        private static Type _stationHandlerInterface;
        private static Type _udonBehaviourType;

        // ── ClientSimMain ──────────────────────────────────────────
        private static MethodInfo _spawnRemotePlayer;
        private static MethodInfo _removePlayer;

        // ── ClientSimPlayerManager fields ──────────────────────────
        private static FieldInfo _pmMasterId;
        private static FieldInfo _pmLocalPlayerId;
        private static FieldInfo _pmLocalPlayer;

        // ── ClientSimStationHelper ─────────────────────────────────
        private static FieldInfo _shUsingPlayer;

        // ── IClientSimStationHandler ───────────────────────────────
        private static MethodInfo _handlerOnStationEnter;
        private static MethodInfo _handlerOnStationExit;

        // ── UdonBehaviour ──────────────────────────────────────────
        private static MethodInfo _ubRunEvent;
        private static MethodInfo _ubGetProgramVariable;
        private static MethodInfo _ubSetProgramVariable;
        private static MethodInfo _ubSendCustomEvent;

        // ── VRCPlayerApi ───────────────────────────────────────────
        private static FieldInfo _playerIsLocal;

        private static bool _initialized;
        private static string _initError;

        public static bool IsReady => _initialized && _initError == null;
        public static string InitError => _initError;

        public static bool Initialize()
        {
            if (_initialized) return _initError == null;
            _initialized = true;

            try
            {
                // ── Resolve types ──────────────────────────────────
                _clientSimMainType = ResolveType(
                    "VRC.SDK3.ClientSim.ClientSimMain, VRC.ClientSim");
                _playerManagerType = ResolveType(
                    "VRC.SDK3.ClientSim.ClientSimPlayerManager, VRC.ClientSim");
                _stationHelperType = ResolveType(
                    "VRC.SDK3.ClientSim.ClientSimStationHelper, VRC.ClientSim");
                _udonHelperType = ResolveType(
                    "VRC.SDK3.ClientSim.ClientSimUdonHelper, VRC.ClientSim");
                _udonBehaviourType = ResolveType(
                    "VRC.Udon.UdonBehaviour, VRC.Udon");
                _stationHandlerInterface = ResolveType(
                    "VRC.SDK3.ClientSim.IClientSimStationHandler, VRC.ClientSim");

                // ── ClientSimMain methods ──────────────────────────
                _spawnRemotePlayer = _clientSimMainType.GetMethod(
                    "SpawnRemotePlayer",
                    BindingFlags.Public | BindingFlags.Static);
                _removePlayer = _clientSimMainType.GetMethod(
                    "RemovePlayer",
                    BindingFlags.Public | BindingFlags.Static);

                // ── PlayerManager fields ───────────────────────────
                _pmMasterId = _playerManagerType.GetField(
                    "_masterID",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _pmLocalPlayerId = _playerManagerType.GetField(
                    "_localPlayerID",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _pmLocalPlayer = _playerManagerType.GetField(
                    "_localPlayer",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                // ── StationHelper fields ───────────────────────────
                _shUsingPlayer = _stationHelperType.GetField(
                    "_usingPlayer",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                // ── IClientSimStationHandler methods ───────────────
                // These fire station events through the REAL pipeline,
                // which calls RunEvent with Networking.LocalPlayer
                _handlerOnStationEnter = _stationHandlerInterface.GetMethod(
                    "OnStationEnter");
                _handlerOnStationExit = _stationHandlerInterface.GetMethod(
                    "OnStationExit");

                // ── UdonBehaviour methods ──────────────────────────
                _ubGetProgramVariable = ResolveMethod(_udonBehaviourType,
                    "GetProgramVariable", new[] { typeof(string) });
                _ubSetProgramVariable = ResolveMethod(_udonBehaviourType,
                    "SetProgramVariable", new[] { typeof(string), typeof(object) });
                _ubSendCustomEvent = ResolveMethod(_udonBehaviourType,
                    "SendCustomEvent", new[] { typeof(string) });

                // RunEvent: find the overload with (string, params ValueTuple[])
                foreach (var m in _udonBehaviourType.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "RunEvent") continue;
                    var parms = m.GetParameters();
                    if (parms.Length >= 1 && parms[0].ParameterType == typeof(string))
                    {
                        _ubRunEvent = m;
                        break;
                    }
                }

                // ── VRCPlayerApi ───────────────────────────────────
                _playerIsLocal = typeof(VRCPlayerApi).GetField(
                    "isLocal",
                    BindingFlags.Public | BindingFlags.Instance);

                // ── Validate critical members ──────────────────────
                ValidateNotNull(_spawnRemotePlayer, "ClientSimMain.SpawnRemotePlayer");
                ValidateNotNull(_removePlayer, "ClientSimMain.RemovePlayer");
                ValidateNotNull(_pmMasterId, "PlayerManager._masterID");
                ValidateNotNull(_pmLocalPlayerId, "PlayerManager._localPlayerID");
                ValidateNotNull(_pmLocalPlayer, "PlayerManager._localPlayer");
                ValidateNotNull(_shUsingPlayer, "StationHelper._usingPlayer");
                ValidateNotNull(_handlerOnStationEnter, "IClientSimStationHandler.OnStationEnter");
                ValidateNotNull(_handlerOnStationExit, "IClientSimStationHandler.OnStationExit");
                ValidateNotNull(_ubGetProgramVariable, "UdonBehaviour.GetProgramVariable");
                ValidateNotNull(_ubSetProgramVariable, "UdonBehaviour.SetProgramVariable");
                ValidateNotNull(_ubSendCustomEvent, "UdonBehaviour.SendCustomEvent");
                ValidateNotNull(_playerIsLocal, "VRCPlayerApi.isLocal");

                return true;
            }
            catch (Exception e)
            {
                _initError = $"SimReflection init failed: {e.Message}";
                Debug.LogError($"[VRCSim] {_initError}");
                return false;
            }
        }

        // ── Public Accessors ───────────────────────────────────────

        public static void SpawnRemotePlayer(string name)
        {
            _spawnRemotePlayer.Invoke(null, new object[] { name });
        }

        public static void RemovePlayer(VRCPlayerApi player)
        {
            _removePlayer.Invoke(null, new object[] { player });
        }

        public static object GetPlayerManager()
        {
            var instance = GetClientSimInstance();
            if (instance == null) return null;
            var pmField = _clientSimMainType.GetField(
                "_playerManager",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return pmField?.GetValue(instance);
        }

        public static object GetClientSimInstance()
        {
            var instanceField = _clientSimMainType.GetField(
                "_instance",
                BindingFlags.NonPublic | BindingFlags.Static);
            return instanceField?.GetValue(null);
        }

        // ── PlayerManager State ────────────────────────────────────

        public static int GetMasterId(object pm) =>
            (int)_pmMasterId.GetValue(pm);

        public static void SetMasterId(object pm, int id) =>
            _pmMasterId.SetValue(pm, id);

        public static int GetLocalPlayerId(object pm) =>
            (int)_pmLocalPlayerId.GetValue(pm);

        public static void SetLocalPlayerId(object pm, int id) =>
            _pmLocalPlayerId.SetValue(pm, id);

        public static VRCPlayerApi GetLocalPlayer(object pm) =>
            (VRCPlayerApi)_pmLocalPlayer.GetValue(pm);

        public static void SetLocalPlayer(object pm, VRCPlayerApi player) =>
            _pmLocalPlayer.SetValue(pm, player);

        // ── VRCPlayerApi ───────────────────────────────────────────

        public static bool GetIsLocal(VRCPlayerApi player) =>
            (bool)_playerIsLocal.GetValue(player);

        public static void SetIsLocal(VRCPlayerApi player, bool value) =>
            _playerIsLocal.SetValue(player, value);

        // ── Station Helper ─────────────────────────────────────────

        public static VRCPlayerApi GetStationUser(Component helper) =>
            (VRCPlayerApi)_shUsingPlayer.GetValue(helper);

        public static void SetStationUser(Component helper, VRCPlayerApi player) =>
            _shUsingPlayer.SetValue(helper, player);

        public static Component GetStationHelper(GameObject obj) =>
            obj.GetComponent(_stationHelperType);

        /// <summary>
        /// Fire OnStationEnter through the real event pipeline.
        /// All IClientSimStationHandler components on the object will
        /// receive the event. This includes ClientSimUdonHelper, which
        /// fires RunEvent with Networking.LocalPlayer as the player.
        /// </summary>
        public static void FireStationEnterHandlers(GameObject stationObj,
            VRCStation station)
        {
            var handlers = stationObj.GetComponents(_stationHandlerInterface);
            foreach (var handler in handlers)
            {
                _handlerOnStationEnter.Invoke(handler, new object[] { station });
            }
        }

        /// <summary>
        /// Fire OnStationExit through the real event pipeline.
        /// </summary>
        public static void FireStationExitHandlers(GameObject stationObj,
            VRCStation station)
        {
            var handlers = stationObj.GetComponents(_stationHandlerInterface);
            foreach (var handler in handlers)
            {
                _handlerOnStationExit.Invoke(handler, new object[] { station });
            }
        }

        // ── UdonBehaviour ──────────────────────────────────────────

        public static Type UdonBehaviourType => _udonBehaviourType;

        public static object GetProgramVariable(Component udon, string name) =>
            _ubGetProgramVariable.Invoke(udon, new object[] { name });

        public static void SetProgramVariable(Component udon, string name,
            object value) =>
            _ubSetProgramVariable.Invoke(udon, new object[] { name, value });

        public static void SendCustomEvent(Component udon, string eventName) =>
            _ubSendCustomEvent.Invoke(udon, new object[] { eventName });

        public static Component[] GetUdonBehaviours(GameObject obj) =>
            obj.GetComponents(_udonBehaviourType);

        public static Component GetUdonBehaviour(GameObject obj) =>
            obj.GetComponent(_udonBehaviourType);

        // ── Helpers ────────────────────────────────────────────────

        private static Type ResolveType(string assemblyQualifiedName)
        {
            var type = Type.GetType(assemblyQualifiedName);
            if (type == null)
                throw new Exception($"Type not found: {assemblyQualifiedName}");
            return type;
        }

        private static MethodInfo ResolveMethod(Type type, string name,
            Type[] paramTypes)
        {
            var method = type.GetMethod(name, paramTypes);
            if (method != null) return method;

            // Fallback: match by name and param count
            foreach (var m in type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name == name && !m.IsGenericMethod
                    && m.GetParameters().Length == paramTypes.Length)
                    return m;
            }
            return null;
        }

        private static void ValidateNotNull(object obj, string name)
        {
            if (obj == null)
                throw new Exception($"Required member not found: {name}");
        }
    }
}
