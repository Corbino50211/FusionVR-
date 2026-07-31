using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;

using Fusion.VR.Cosmetics;
using Fusion.VR.Networking;
using Fusion.VR.Player;
using Fusion.VR.Saving;

namespace Fusion.VR
{
    /// <summary>
    /// Central runtime manager for the FusionVR community fork.
    /// Uses the Fusion App ID configured through Fusion Hub and creates a runner at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public class FusionVRManager : MonoBehaviour
    {
        private const string FusionVoiceClientTypeName = "Photon.Voice.Fusion.FusionVoiceClient";

        public static FusionVRManager Manager { get; private set; }
        public static bool IsSessionOperationInProgress { get; private set; }
        public static Action<NetworkRunner> OnHostMigrationResume;

        [Header("Photon")]
        [Tooltip("Informational only in Fusion 2.1. Configure the real App ID in Tools > Fusion > Fusion Hub.")]
        public string FusionAppId;

        [Tooltip("Optional. Voice is not required for networking tests.")]
        public string VoiceAppId;

        [Tooltip("Leave empty to let Photon choose the best region automatically.")]
        public string Region = string.Empty;

        [Header("Local XR Rig")]
        public Transform Head;
        public Transform LeftHand;
        public Transform RightHand;
        public Color Colour = Color.black;
        public string DefaultUsername = "Worker";

        [Header("Networking")]
        public string DefaultQueue = "Default";
        public int DefaultRoomLimit = 6;
        public GameMode NetworkingMode = GameMode.AutoHostOrClient;
        public NetworkPrefabRef NetworkedPlayerPrefab;

        [Tooltip("Optional runner template. When empty, FusionVR creates the runner automatically.")]
        public GameObject VoiceAndRunner;

        [Tooltip("Host migration is experimental in this fork.")]
        public bool EnableHostMigration;

        [Header("Player Appearance")]
        public List<string> CosmeticSlots = new List<string>
        {
            "Head",
            "Face",
            "Body",
            "LeftHand",
            "RightHand"
        };

        [Header("Startup")]
        public bool ConnectOnAwake = true;
        public bool JoinRoomOnConnect = true;

        [NonSerialized] public NetworkRunner Runner;
        [NonSerialized] public Component VoiceClient;
        [NonSerialized] public Dictionary<PlayerRef, NetworkObject> playerCache = new Dictionary<PlayerRef, NetworkObject>();
        [NonSerialized] public Dictionary<string, string> Cosmetics = new Dictionary<string, string>();

        private void Awake()
        {
            if (Manager != null && Manager != this)
            {
                Debug.LogError("There can only be one FusionVRManager in a scene.", this);
                Destroy(gameObject);
                return;
            }

            Manager = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            LoadSavedLocalSettings();

            if (ConnectOnAwake)
                Connect();
        }

        private void OnDestroy()
        {
            if (Manager == this)
                Manager = null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Attempts to locate common XR Origin head and controller transforms.
        /// App IDs are configured through Fusion Hub in Fusion 2.1 and are not read here.
        /// </summary>
        public void CheckDefaultValues()
        {
            CheckForRig(this);
        }

        private static void CheckForRig(FusionVRManager manager)
        {
            GameObject[] objects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (manager.Head == null)
            {
                foreach (GameObject obj in objects)
                {
                    if (obj.name.Contains("Main Camera", StringComparison.OrdinalIgnoreCase) ||
                        obj.name.Equals("Camera", StringComparison.OrdinalIgnoreCase) ||
                        obj.name.Contains("Head", StringComparison.OrdinalIgnoreCase))
                    {
                        manager.Head = obj.transform;
                        break;
                    }
                }
            }

            if (manager.LeftHand == null)
            {
                foreach (GameObject obj in objects)
                {
                    bool isLeft = obj.name.Contains("Left", StringComparison.OrdinalIgnoreCase);
                    bool isHand = obj.name.Contains("Hand", StringComparison.OrdinalIgnoreCase) ||
                                  obj.name.Contains("Controller", StringComparison.OrdinalIgnoreCase);

                    if (isLeft && isHand)
                    {
                        manager.LeftHand = obj.transform;
                        break;
                    }
                }
            }

            if (manager.RightHand == null)
            {
                foreach (GameObject obj in objects)
                {
                    bool isRight = obj.name.Contains("Right", StringComparison.OrdinalIgnoreCase);
                    bool isHand = obj.name.Contains("Hand", StringComparison.OrdinalIgnoreCase) ||
                                  obj.name.Contains("Controller", StringComparison.OrdinalIgnoreCase);

                    if (isRight && isHand)
                    {
                        manager.RightHand = obj.transform;
                        break;
                    }
                }
            }
        }
#endif

        private void LoadSavedLocalSettings()
        {
            string savedUsername = PlayerPrefs.GetString("Username");

            if (string.IsNullOrWhiteSpace(savedUsername) && !string.IsNullOrWhiteSpace(DefaultUsername))
                savedUsername = $"{DefaultUsername}{GenerateRoomCode()}";

            if (!string.IsNullOrWhiteSpace(savedUsername))
                SetUsername(savedUsername);

            string savedColour = PlayerPrefs.GetString("Colour");
            if (!string.IsNullOrWhiteSpace(savedColour))
                SetColour(JsonUtility.FromJson<Color>(savedColour));

            Cosmetics = FusionVRPrefs.GetCosmetics(CosmeticSlots) ?? new Dictionary<string, string>();
        }

        public static bool Connect()
        {
            return Connect(Manager != null && Manager.JoinRoomOnConnect);
        }

        public static bool Connect(bool joinDefaultRoom)
        {
            if (Manager == null)
            {
                Debug.LogError("FusionVRManager is missing.");
                return false;
            }

            if (Manager.Runner != null)
            {
                Debug.LogWarning("FusionVR already has a runner.", Manager);
                return false;
            }

            if (!ValidateManagerForConnection())
                return false;

            GameObject runnerObject;

            if (Manager.VoiceAndRunner != null)
            {
                runnerObject = Instantiate(Manager.VoiceAndRunner);
                runnerObject.name = $"{Manager.VoiceAndRunner.name} (Runtime)";
            }
            else
            {
                runnerObject = new GameObject("FusionVR Runner (Runtime)");
            }

            DontDestroyOnLoad(runnerObject);

            NetworkRunner runner = runnerObject.GetComponent<NetworkRunner>();
            if (runner == null)
                runner = runnerObject.AddComponent<NetworkRunner>();

            FusionVRRunner callbacks = runnerObject.GetComponent<FusionVRRunner>();
            if (callbacks == null)
                callbacks = runnerObject.AddComponent<FusionVRRunner>();

            runner.AddCallbacks(callbacks);
            runner.ProvideInput = true;

            if (NetworkProjectConfig.Global != null)
                NetworkProjectConfig.Global.Simulation.HostMigration = Manager.EnableHostMigration;

            Manager.Runner = runner;
            Manager.VoiceClient = FindOptionalVoiceClient(runnerObject);

            if (!string.IsNullOrWhiteSpace(Manager.VoiceAppId) && Manager.VoiceClient == null)
            {
                Debug.LogWarning(
                    "A Voice App ID is assigned, but FusionVoiceClient is not present. " +
                    "Fusion networking will continue without voice.",
                    runnerObject
                );
            }

            Debug.Log("FusionVR runner created. Fusion App settings are loaded from Fusion Hub.", runnerObject);

            if (joinDefaultRoom)
                _ = JoinRandomRoom(Manager.DefaultQueue, Manager.DefaultRoomLimit);

            return true;
        }

        private static bool ValidateManagerForConnection()
        {
            if (Manager.Head == null || Manager.LeftHand == null || Manager.RightHand == null)
            {
                Debug.LogError("Assign the local Head, LeftHand, and RightHand transforms before connecting.", Manager);
                return false;
            }

            if (!Manager.NetworkedPlayerPrefab.IsValid)
            {
                Debug.LogError("Assign and bake the networked Player prefab on FusionVRManager.", Manager);
                return false;
            }

            return true;
        }

        private static Component FindOptionalVoiceClient(GameObject runnerObject)
        {
            Component[] components = runnerObject.GetComponentsInChildren<Component>(true);

            foreach (Component component in components)
            {
                if (component != null && component.GetType().FullName == FusionVoiceClientTypeName)
                    return component;
            }

            return null;
        }

        public static Task<bool> JoinRandomRoom(string queue, int maxPlayers)
        {
            string roomName = string.IsNullOrWhiteSpace(queue) ? "Default" : queue.Trim();
            return StartSession(roomName, maxPlayers, true);
        }

        public static Task<bool> JoinRandomRoom(string queue)
        {
            return JoinRandomRoom(queue, Manager != null ? Manager.DefaultRoomLimit : 6);
        }

        public static Task<bool> _JoinRandomRoom(string queue, int maxPlayers)
        {
            return JoinRandomRoom(queue, maxPlayers);
        }

        public static Task<bool> JoinPrivateRoom(string roomId, int maxPlayers)
        {
            return StartSession(roomId, maxPlayers, false);
        }

        public static Task<bool> JoinPrivateRoom(string roomId)
        {
            return JoinPrivateRoom(roomId, Manager != null ? Manager.DefaultRoomLimit : 6);
        }

        public static Task<bool> _JoinPrivateRoom(string roomCode, int maxPlayers)
        {
            return JoinPrivateRoom(roomCode, maxPlayers);
        }

        private static async Task<bool> StartSession(string roomName, int maxPlayers, bool isPublicQueue)
        {
            if (Manager == null || IsSessionOperationInProgress)
                return false;

            if (Manager.Runner == null && !Connect(false))
                return false;

            if (Manager.Runner.IsRunning)
            {
                Debug.LogWarning("FusionVR is already inside a session.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(roomName))
                roomName = "Default";

            IsSessionOperationInProgress = true;
            NetworkRunner runner = Manager.Runner;

            try
            {
                Dictionary<string, SessionProperty> properties = new Dictionary<string, SessionProperty>
                {
                    ["version"] = Application.version,
                    ["queue"] = isPublicQueue ? roomName : "private"
                };

                StartGameArgs args = new StartGameArgs
                {
                    GameMode = Manager.NetworkingMode,
                    SessionName = roomName,
                    SessionProperties = properties,
                    PlayerCount = Mathf.Clamp(maxPlayers, 1, 100),
                    IsOpen = true,
                    IsVisible = isPublicQueue,
                    EnableClientSessionCreation = true
                };

                StartGameResult result = await runner.StartGame(args);

                if (!result.Ok)
                {
                    Debug.LogError($"FusionVR failed to start session: {result.ShutdownReason}");
                    DestroyRunner(runner);
                    return false;
                }

                Debug.Log($"FusionVR joined '{roomName}' as {runner.GameMode}.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                DestroyRunner(runner);
                return false;
            }
            finally
            {
                IsSessionOperationInProgress = false;
            }
        }

        public static bool Disconnect()
        {
            if (Manager == null || Manager.Runner == null || IsSessionOperationInProgress)
                return false;

            _ = LeaveRoomAsync();
            return true;
        }

        public static void LeaveRoom()
        {
            if (!IsSessionOperationInProgress)
                _ = LeaveRoomAsync();
        }

        public static async Task LeaveRoomAsync()
        {
            if (Manager == null || Manager.Runner == null || IsSessionOperationInProgress)
                return;

            IsSessionOperationInProgress = true;
            NetworkRunner runner = Manager.Runner;

            try
            {
                if (runner.IsRunning)
                    await runner.Shutdown(shutdownReason: ShutdownReason.Ok);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                DestroyRunner(runner);
                IsSessionOperationInProgress = false;
            }
        }

        private static void DestroyRunner(NetworkRunner runner)
        {
            if (Manager != null && Manager.Runner == runner)
            {
                Manager.Runner = null;
                Manager.VoiceClient = null;
                Manager.playerCache.Clear();
            }

            if (runner != null)
                Destroy(runner.gameObject);
        }

        public static void SetUsername(string playerName)
        {
            if (Manager == null || string.IsNullOrWhiteSpace(playerName))
                return;

            playerName = playerName.Trim();
            if (playerName.Length > 32)
                playerName = playerName.Substring(0, 32);

            if (FusionVRPlayer.localPlayer != null && FusionVRPlayer.localPlayer.NickName.ToString() != playerName)
                FusionVRPlayer.localPlayer.RPCSetNickName(playerName);

            PlayerPrefs.SetString("Username", playerName);
            PlayerPrefs.Save();
        }

        public static void SetColour(Color playerColour)
        {
            if (Manager == null)
                return;

            Manager.Colour = playerColour;

            if (FusionVRPlayer.localPlayer != null && FusionVRPlayer.localPlayer.Colour != playerColour)
                FusionVRPlayer.localPlayer.RPCSetColour(playerColour);

            PlayerPrefs.SetString("Colour", JsonUtility.ToJson(playerColour));
            PlayerPrefs.Save();
        }

        public static void SetCosmetics(string slotName, string cosmeticName)
        {
            if (Manager == null || string.IsNullOrWhiteSpace(slotName))
                return;

            Manager.Cosmetics[slotName] = cosmeticName ?? string.Empty;
            SendCosmeticsToLocalPlayer();
        }

        public static void SetCosmetics(Dictionary<string, string> playerCosmetics)
        {
            if (Manager == null)
                return;

            Manager.Cosmetics = playerCosmetics != null
                ? new Dictionary<string, string>(playerCosmetics)
                : new Dictionary<string, string>();

            SendCosmeticsToLocalPlayer();
        }

        private static void SendCosmeticsToLocalPlayer()
        {
            if (Manager == null)
                return;

            if (FusionVRPlayer.localPlayer != null)
                FusionVRPlayer.localPlayer.RPCSetCosmetics(CosmeticSlot.CopyFrom(Manager.Cosmetics).ToArray());

            FusionVRPrefs.SaveCosmetics(Manager.Cosmetics);
        }

        public static bool HasCosmeticOn(string slotName, string cosmeticName)
        {
            return Manager != null &&
                   Manager.Cosmetics.TryGetValue(slotName, out string selectedCosmetic) &&
                   selectedCosmetic == cosmeticName;
        }

        public static void LoadPlayer()
        {
            if (Manager == null)
                return;

            string savedUsername = PlayerPrefs.GetString("Username");
            if (!string.IsNullOrWhiteSpace(savedUsername))
                SetUsername(savedUsername);

            string savedColour = PlayerPrefs.GetString("Colour");
            if (!string.IsNullOrWhiteSpace(savedColour))
                SetColour(JsonUtility.FromJson<Color>(savedColour));

            SetCosmetics(FusionVRPrefs.GetCosmetics(Manager.CosmeticSlots));
        }

        public static string GenerateRoomCode()
        {
            return UnityEngine.Random.Range(10000, 100000).ToString();
        }
    }
}
