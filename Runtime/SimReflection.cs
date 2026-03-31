using System;
using System.Collections.Generic;
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
        // ── Per-UdonBehaviour caches ────────────────────────────────
        private static readonly Dictionary<int, List<string>> _syncedVarNameCache = new();

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
        // Cached field-infos for singleton + player-manager access.
        // Resolved once in Initialize() — live GetField() on every call
        // is unnecessary reflection overhead.
        private static FieldInfo _csInstanceField;
        private static FieldInfo _csPlayerManagerField;

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
        private static MethodInfo _ubRunEvent;          // RunEvent(string)
        private static MethodInfo _ubRunEventWithArgs;  // RunEvent(string, params ValueTuple<string,object>[])
        private static MethodInfo _ubGetProgramVariable;
        private static MethodInfo _ubTryGetProgramVariable;
        private static MethodInfo _ubSetProgramVariable;
        private static MethodInfo _ubSendCustomEvent;
        private static PropertyInfo _ubSyncMetadataTable;
        private static MethodInfo _syncTableGetAll;
        private static PropertyInfo _syncMetaName;

        // ── VRCPlayerApi ───────────────────────────────────────────
        private static FieldInfo _playerIsLocal;

        private static bool _initialized;
        private static string _initError;

        /// <summary>True when reflection initialization completed without errors.</summary>
        public static bool IsReady => _initialized && _initError == null;
        /// <summary>Error message from initialization, or null on success.</summary>
        public static string InitError => _initError;

        /// <summary>Resolve all reflection targets. Returns true on success.</summary>
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
                _handlerOnStationEnter = _stationHandlerInterface.GetMethod(
                    "OnStationEnter");
                _handlerOnStationExit = _stationHandlerInterface.GetMethod(
                    "OnStationExit");

                // ── UdonBehaviour methods ──────────────────────────
                // These have generic overloads — must filter to non-generic
                _ubGetProgramVariable = FindNonGenericMethod(
                    _udonBehaviourType, "GetProgramVariable", 1);
                _ubTryGetProgramVariable = FindNonGenericMethod(
                    _udonBehaviourType, "TryGetProgramVariable", 2);
                _ubSetProgramVariable = FindNonGenericMethod(
                    _udonBehaviourType, "SetProgramVariable", 2);
                _ubSendCustomEvent = FindNonGenericMethod(
                    _udonBehaviourType, "SendCustomEvent", 1);

                // RunEvent: resolve BOTH overloads.
                //   RunEvent(string)          — parameterless (_update, _fixedUpdate, etc.)
                //   RunEvent(string, params ValueTuple<string,object>[]) — with args
                //     (_onPlayerLeft, _onPlayerJoined, _onStationEntered, etc.)
                foreach (var m in _udonBehaviourType.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "RunEvent" || m.IsGenericMethod) continue;
                    var parms = m.GetParameters();
                    if (parms.Length == 1 && parms[0].ParameterType == typeof(string))
                        _ubRunEvent = m;
                    else if (parms.Length == 2 && parms[0].ParameterType == typeof(string))
                        _ubRunEventWithArgs = m;
                }

                // ── Sync metadata (best-effort — not fatal if missing) ──
                _ubSyncMetadataTable = _udonBehaviourType.GetProperty(
                    "SyncMetadataTable",
                    BindingFlags.Public | BindingFlags.Instance);
                if (_ubSyncMetadataTable != null)
                {
                    var tableType = _ubSyncMetadataTable.PropertyType;
                    _syncTableGetAll = tableType.GetMethod("GetAllSyncMetadata");
                    var metaType = ResolveType(
                        "VRC.Udon.Common.UdonSyncMetadata, VRC.Udon.Common");
                    _syncMetaName = metaType.GetProperty("Name",
                        BindingFlags.Public | BindingFlags.Instance);
                }

                // ── ClientSimMain singleton + player-manager fields ────────
                _csInstanceField = _clientSimMainType.GetField(
                    "_instance",
                    BindingFlags.NonPublic | BindingFlags.Static);
                _csPlayerManagerField = _clientSimMainType.GetField(
                    "_playerManager",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                // ── VRCPlayerApi ───────────────────────────────────────────
                _playerIsLocal = typeof(VRCPlayerApi).GetField(
                    "isLocal",
                    BindingFlags.Public | BindingFlags.Instance);

                // ── Validate critical members ──────────────────────
                ValidateNotNull(_spawnRemotePlayer, "ClientSimMain.SpawnRemotePlayer");
                ValidateNotNull(_removePlayer, "ClientSimMain.RemovePlayer");
                ValidateNotNull(_csInstanceField, "ClientSimMain._instance");
                ValidateNotNull(_csPlayerManagerField, "ClientSimMain._playerManager");
                ValidateNotNull(_pmMasterId, "PlayerManager._masterID");
                ValidateNotNull(_pmLocalPlayerId, "PlayerManager._localPlayerID");
                ValidateNotNull(_pmLocalPlayer, "PlayerManager._localPlayer");
                ValidateNotNull(_shUsingPlayer, "StationHelper._usingPlayer");
                ValidateNotNull(_handlerOnStationEnter, "IClientSimStationHandler.OnStationEnter");
                ValidateNotNull(_handlerOnStationExit, "IClientSimStationHandler.OnStationExit");
                ValidateNotNull(_ubGetProgramVariable, "UdonBehaviour.GetProgramVariable");
                ValidateNotNull(_ubSetProgramVariable, "UdonBehaviour.SetProgramVariable");
                ValidateNotNull(_ubSendCustomEvent, "UdonBehaviour.SendCustomEvent");
                ValidateNotNull(_ubRunEvent, "UdonBehaviour.RunEvent");
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

        /// <summary>Reset init state so Init() can be called again.</summary>
        public static void Reset()
        {
            _initialized = false;
            _initError = null;
        }

        // ── Public Accessors ───────────────────────────────────────

        /// <summary>Call ClientSimMain.SpawnRemotePlayer to create a new bot.</summary>
        public static void SpawnRemotePlayer(string name)
        {
            _spawnRemotePlayer.Invoke(null, new object[] { name });
        }

        /// <summary>Call ClientSimMain.RemovePlayer to destroy a bot.</summary>
        public static void RemovePlayer(VRCPlayerApi player)
        {
            _removePlayer.Invoke(null, new object[] { player });
        }

        /// <summary>Get the ClientSimPlayerManager instance via reflection.</summary>
        public static object GetPlayerManager()
        {
            var instance = GetClientSimInstance();
            return instance == null ? null : _csPlayerManagerField?.GetValue(instance);
        }

        /// <summary>Get the ClientSimMain singleton instance.</summary>
        public static object GetClientSimInstance() => _csInstanceField?.GetValue(null);

        // ── PlayerManager State ────────────────────────────────────

        /// <summary>Read the master player ID from the PlayerManager.</summary>
        public static int GetMasterId(object pm) =>
            (int)_pmMasterId.GetValue(pm);

        /// <summary>Write the master player ID on the PlayerManager.</summary>
        public static void SetMasterId(object pm, int id) =>
            _pmMasterId.SetValue(pm, id);

        /// <summary>Read the local player ID from the PlayerManager.</summary>
        public static int GetLocalPlayerId(object pm) =>
            (int)_pmLocalPlayerId.GetValue(pm);

        /// <summary>Write the local player ID on the PlayerManager.</summary>
        public static void SetLocalPlayerId(object pm, int id) =>
            _pmLocalPlayerId.SetValue(pm, id);

        /// <summary>Read the local player reference from the PlayerManager.</summary>
        public static VRCPlayerApi GetLocalPlayer(object pm) =>
            (VRCPlayerApi)_pmLocalPlayer.GetValue(pm);

        /// <summary>Write the local player reference on the PlayerManager.</summary>
        public static void SetLocalPlayer(object pm, VRCPlayerApi player) =>
            _pmLocalPlayer.SetValue(pm, player);

        // ── VRCPlayerApi ───────────────────────────────────────────

        /// <summary>Read the isLocal field on a VRCPlayerApi.</summary>
        public static bool GetIsLocal(VRCPlayerApi player) =>
            (bool)_playerIsLocal.GetValue(player);

        /// <summary>Write the isLocal field on a VRCPlayerApi.</summary>
        public static void SetIsLocal(VRCPlayerApi player, bool value) =>
            _playerIsLocal.SetValue(player, value);

        // ── Station Helper ─────────────────────────────────────────

        /// <summary>Read the _usingPlayer field from a ClientSimStationHelper.</summary>
        public static VRCPlayerApi GetStationUser(Component helper) =>
            (VRCPlayerApi)_shUsingPlayer.GetValue(helper);

        /// <summary>Write the _usingPlayer field on a ClientSimStationHelper.</summary>
        public static void SetStationUser(Component helper, VRCPlayerApi player) =>
            _shUsingPlayer.SetValue(helper, player);

        /// <summary>Get the ClientSimStationHelper component on a station GameObject.</summary>
        public static Component GetStationHelper(GameObject obj) =>
            obj.GetComponent(_stationHelperType);

        /// <summary>Invoke OnStationEnter on all IClientSimStationHandler components.</summary>
        public static void FireStationEnterHandlers(GameObject stationObj,
            VRCStation station)
        {
            var handlers = stationObj.GetComponents(_stationHandlerInterface);
            foreach (var handler in handlers)
                _handlerOnStationEnter.Invoke(handler, new object[] { station });
        }

        /// <summary>Invoke OnStationExit on all IClientSimStationHandler components.</summary>
        public static void FireStationExitHandlers(GameObject stationObj,
            VRCStation station)
        {
            var handlers = stationObj.GetComponents(_stationHandlerInterface);
            foreach (var handler in handlers)
                _handlerOnStationExit.Invoke(handler, new object[] { station });
        }

        // ── UdonBehaviour ──────────────────────────────────────────

        /// <summary>The resolved System.Type for VRC.Udon.UdonBehaviour.</summary>
        public static Type UdonBehaviourType => _udonBehaviourType;

        /// <summary>Read an Udon program variable. Throws if the variable doesn't exist.</summary>
        public static object GetProgramVariable(Component udon, string name) =>
            _ubGetProgramVariable.Invoke(udon, new object[] { name });

        /// <summary>
        /// Check if variable exists and get its value without logging errors.
        /// Returns true if the variable exists, false otherwise.
        /// </summary>
        public static bool TryGetProgramVariable(Component udon, string name,
            out object value)
        {
            var args = new object[] { name, null };
            bool result = (bool)_ubTryGetProgramVariable.Invoke(udon, args);
            value = args[1];
            return result;
        }

        /// <summary>Write an Udon program variable by name.</summary>
        public static void SetProgramVariable(Component udon, string name,
            object value) =>
            _ubSetProgramVariable.Invoke(udon, new object[] { name, value });

        /// <summary>Send a custom event to an UdonBehaviour (calls the named method).</summary>
        public static void SendCustomEvent(Component udon, string eventName) =>
            _ubSendCustomEvent.Invoke(udon, new object[] { eventName });

        /// <summary>Get all UdonBehaviour components on a GameObject.</summary>
        public static Component[] GetUdonBehaviours(GameObject obj) =>
            obj.GetComponents(_udonBehaviourType);

        /// <summary>Get the first UdonBehaviour component on a GameObject.</summary>
        public static Component GetUdonBehaviour(GameObject obj) =>
            obj.GetComponent(_udonBehaviourType);

        /// <summary>
        /// Execute an Udon program event (e.g. "_update", "_onDeserialization").
        /// Unlike SendCustomEvent, this goes through the program runner.
        /// </summary>
        public static void RunEvent(Component udon, string eventName)
        {
            // _ubRunEvent is validated in Initialize() — null here means Init was never called.
            if (_ubRunEvent == null)
            {
                Debug.LogError("[VRCSim] RunEvent called before Init() — call VRCSim.Init() in test SetUp");
                return;
            }
            _ubRunEvent.Invoke(udon, new object[] { eventName });
        }

        /// <summary>
        /// Execute an Udon program event with named arguments.
        /// Mirrors how VRChat passes parameters to lifecycle events:
        ///   _onPlayerLeft     → ("player", VRCPlayerApi)
        ///   _onPlayerJoined   → ("player", VRCPlayerApi)
        ///   _onStationEntered → ("player", VRCPlayerApi)
        /// Falls back to SetProgramVariable + RunEvent if the with-args
        /// overload is unavailable (older UdonBehaviour builds).
        /// </summary>
        public static void RunEventWithArgs(Component udon, string eventName,
            params (string, object)[] args)
        {
            if (_ubRunEventWithArgs != null)
            {
                _ubRunEventWithArgs.Invoke(udon, new object[] { eventName, args });
            }
            else
            {
                foreach (var (name, value) in args)
                    SetProgramVariable(udon, name, value);
                RunEvent(udon, eventName);
            }
        }

        /// <summary>
        /// Get the names of all [UdonSynced] variables on an UdonBehaviour.
        /// Returns empty list if sync metadata is unavailable.
        /// </summary>
        public static List<string> GetSyncedVarNames(Component udon)
        {
            int id = udon.GetInstanceID();
            if (_syncedVarNameCache.TryGetValue(id, out var cached))
                return cached;

            var names = new List<string>();
            if (_ubSyncMetadataTable == null || _syncTableGetAll == null)
                return names;
            var table = _ubSyncMetadataTable.GetValue(udon);
            if (table == null) return names;
            var allMeta = _syncTableGetAll.Invoke(table, null)
                as System.Collections.IEnumerable;
            if (allMeta == null) return names;
            foreach (var meta in allMeta)
            {
                var name = _syncMetaName?.GetValue(meta) as string;
                if (name != null) names.Add(name);
            }
            _syncedVarNameCache[id] = names;
            return names;
        }

        /// <summary>
        /// Find ALL UdonBehaviours in the scene (active only).
        /// </summary>
        public static Component[] FindAllUdonBehaviours()
        {
#pragma warning disable CS0618
            var objs = UnityEngine.Object.FindObjectsOfType(_udonBehaviourType);
#pragma warning restore CS0618
            var result = new Component[objs.Length];
            for (int i = 0; i < objs.Length; i++)
                result[i] = (Component)objs[i];
            return result;
        }

        /// <summary>
        /// Fire OnOwnershipRequest on an UdonBehaviour.
        /// Returns true if transfer is allowed (or if the event doesn't exist).
        /// Uses Udon's standard event parameter symbols:
        ///   onOwnershipRequestRequester, onOwnershipRequestNewOwner.
        /// </summary>
        public static bool FireOwnershipRequest(Component udon,
            VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner)
        {
            try
            {
                // Return-value symbol exists only if the script overrides OnOwnershipRequest
                if (!TryGetProgramVariable(udon,
                        "__0__onOwnershipRequest__ret", out _))
                    return true;

                SetProgramVariable(udon,
                    "onOwnershipRequestRequester", requestingPlayer);
                SetProgramVariable(udon,
                    "onOwnershipRequestNewOwner", requestedOwner);
                RunEvent(udon, "_onOwnershipRequest");

                var result = GetProgramVariable(udon,
                    "__0__onOwnershipRequest__ret");
                return result is bool b ? b : true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[VRCSim] FireOwnershipRequest: handler threw, defaulting to allow: {e.Message}");
                return true;
            }
        }

        /// <summary>
        /// Fire OnOwnershipTransferred on an UdonBehaviour.
        /// In VRChat, this fires after ownership has changed and the
        /// new owner is passed as the "player" parameter.
        /// </summary>
        public static void FireOwnershipTransferred(Component udon,
            VRCPlayerApi newOwner)
        {
            // VRChat passes the new owner as the "player" parameter.
            // Use RunEventWithArgs so the UdonBehaviour receives it.
            RunEventWithArgs(udon, "_onOwnershipTransferred", ("player", (object)newOwner));
        }

        // ── Scene Helpers ──────────────────────────────────────────

        /// <summary>
        /// Build the full hierarchy path of a Transform.
        /// Shared by SimNetwork and SimSnapshot — lives here to avoid duplication.
        /// </summary>
        public static string GetPath(Transform t)
        {
            var parts = new List<string>();
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        // ── Helpers ────────────────────────────────────────────────

        private static Type ResolveType(string assemblyQualifiedName)
        {
            var type = Type.GetType(assemblyQualifiedName);
            if (type == null)
                throw new Exception($"Type not found: {assemblyQualifiedName}");
            return type;
        }

        /// <summary>
        /// Find a non-generic method by name and parameter count.
        /// Avoids AmbiguousMatchException from generic overloads.
        /// </summary>
        private static MethodInfo FindNonGenericMethod(Type type, string name,
            int paramCount)
        {
            foreach (var m in type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name == name && !m.IsGenericMethod
                    && m.GetParameters().Length == paramCount)
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
