using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.SceneManagement;

using Fusion.VR.Cosmetics;
using Fusion.VR.Networking;
using Fusion.VR.Player;
using Fusion.VR.Saving;

namespace Fusion.VR
{
    [DisallowMultipleComponent]
    public class FusionVRManager : MonoBehaviour
    {
        private const string FusionVoiceClientTypeName = "Photon.Voice.Fusion.FusionVoiceClient";

        public static FusionVRManager Manager { get; private set; }
        public static bool IsSessionOperationInProgress { get; private set; }

        [Header("Photon")]
        public string FusionAppId;
        public string VoiceAppId;
        [Tooltip("Leave empty to let Photon choose the best region automatically.")]
        public string Region = string.Empty;

        [Header("Player")]
        public Transform Head;
        public Transform LeftHand;
        public Transform RightHand;
        public Color Colour = Color.black;
        [Tooltip("If left empty, no default username will be generated.")]
        public string DefaultUsername = "Worker";

        [Header("Networking")]
        public string DefaultQueue = "Default";
        public int DefaultRoomLimit = 6;
        [Tooltip("AutoHostOrClient makes the first player the host and later players clients.")]
        public GameMode NetworkingMode = GameMode.AutoHostOrClient;
        public NetworkPrefabRef NetworkedPlayerPrefab;
        [Tooltip("Prefab containing a NetworkRunner and FusionVRRunner. Photon Voice components are optional.")]
        public GameObject VoiceAndRunner;
        [Tooltip("Host migration is still experimental in this fork.")]
        public bool EnableHostMigration;

        [Header("Other")]
        public List<string> CosmeticSlots = new List<string>();
        [Tooltip("Connect to Photon when this manager starts.")]
        public bool ConnectOnAwake = true;
        [Tooltip("Join the default public queue after creating the runner.")]
        public bool JoinRoomOnConnect = true;

        [NonSerialized]
        public NetworkRunner Runner;
        [NonSerialized]
        public Component VoiceClient;
        [NonSerialized]
        public Dictionary<PlayerRef, NetworkObject> playerCache = new Dictionary<PlayerRef, NetworkObject>();
        [NonSerialized]
        public Dictionary<string, string> Cosmetics = new Dictionary<string, string>();

        public static Action<NetworkRunner> OnHostMigrationResume;

        private void Start()
        {
            if (Manager != null && Manager != this)
            {
                Debug.LogError("There can only be one FusionVRManager in a scene.", this);
                Destroy(gameObject);
                return;
            }

            Manager = this;
            DontDestroyOnLoad(gameObject);

            if (Head != null)
                DontDestroyOnLoad(Head.root.gameObject);

            LoadSavedLocalSettings();

            if (ConnectOnAwake)
                Connect();
        }

        private void OnDestroy()
        {
            if (Manager == this)
                Manager = null;
        }

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

            string savedCosmetics = PlayerPrefs.GetString("Cosmetics");
            if (!string.IsNullOrWhiteSpace(savedCosmetics))
                SetCosmetics(FusionVRPrefs.GetCosmetics(CosmeticSlots));

            Cosmetics ??= new Dictionary<string, string>();
        }

#if UNITY_EDITOR
        public void CheckDefaultValues()
        {
            CheckForRig(this);

            if (string.IsNullOrEmpty(FusionAppId))
                FusionAppId = Photon.Realtime.PhotonAppSettings.Instance.AppSettings.AppIdFusion;

            if (string.IsNullOrEmpty(VoiceAppId))
                VoiceAppId = Photon.Realtime.PhotonAppSettings.Instance.AppSettings.AppIdVoice;
        }

        private static void CheckForRig(FusionVRManager manager)
        {
            GameObject[] objects = FindObjectsOfType<GameObject>();

            if (manager.Head == null)
            {
                foreach (GameObject obj in objects)
                {
                    if (obj.name.Contains("Camera", StringComparison.OrdinalIgnoreCase) ||
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

        /// <summary>
        /// Creates a new Fusion runner and optionally joins the default public queue.
        /// </summary>
        public static bool Connect()
        {
            return Connect(Manager != null && Manager.JoinRoomOnConnect);
        }

        /// <summary>
        /// Creates a new Fusion runner. Set joinDefaultRoom to false for host migration or custom joining.
        /// </summary>
        public static bool Connect(bool joinDefaultRoom)
        {
            if (Manager == null)
            {
                Debug.LogError("FusionVRManager is missing.");
                return false;
            }

            if (Manager.Runner != null)
            {
                Debug.LogError("FusionVR already has a runner. Shut it down before creating another one.");
                return false;
            }

            if (!ValidateManagerForConnection())
                return false;

            GameObject runnerObject = Instantiate(Manager.VoiceAndRunner);
            runnerObject.name = $"{Manager.VoiceAndRunner.name} (Runtime)";
            DontDestroyOnLoad(runnerObject);

            NetworkRunner runner = runnerObject.GetComponent<NetworkRunner>();
            if (runner == null)
            {
                Debug.LogError("The runner prefab does not contain a NetworkRunner.", runnerObject);
                Destroy(runnerObject);
                return false;
            }

            FusionVRRunner callbacks = runnerObject.GetComponent<FusionVRRunner>();
            if (callbacks == null)
            {
                callbacks = runnerObject.AddComponent<FusionVRRunner>();
                Debug.LogWarning("FusionVRRunner was missing from the runner prefab and was added at runtime.", runnerObject);
            }

            runner.AddCallbacks(callbacks);
            NetworkProjectConfig.Global.Simulation.HostMigration = Manager.EnableHostMigration;

            Photon.Realtime.PhotonAppSettings.Instance.AppSettings.AppIdFusion = Manager.FusionAppId;
            Photon.Realtime.PhotonAppSettings.Instance.AppSettings.AppIdVoice = Manager.VoiceAppId;
            Photon.Realtime.PhotonAppSettings.Instance.AppSettings.FixedRegion = Manager.Region ?? string.Empty;

            Manager.Runner = runner;
            Manager.Runner.ProvideInput = true;
            Manager.VoiceClient = FindOptionalVoiceClient(runnerObject);

            if (!string.IsNullOrWhiteSpace(Manager.VoiceAppId) && Manager.VoiceClient == null)
            {
                Debug.LogWarning(
                    "A Voice App ID is assigned, but FusionVoiceClient is not installed or is missing from the runner prefab. " +
                    "Fusion networking will continue without voice.",
                    runnerObject
                );
            }

            Debug.Log("FusionVR runner created.", runnerObject);

            if (joinDefaultRoom)
                _ = JoinRandomRoom(Manager.DefaultQueue, Manager.DefaultRoomLimit);

            return true;
        }

        private static bool ValidateManagerForConnection()
        {
            if (string.IsNullOrWhiteSpace(Manager.FusionAppId))
            {
                Debug.LogError("Please enter a Fusion App ID on FusionVRManager.", Manager);
                return false;
            }

            if (Manager.VoiceAndRunner == null)
            {
                Debug.LogError("The runner prefab is not assigned on FusionVRManager.", Manager);
                return false;
            }

            if (Manager.Head == null || Manager.LeftHand == null || Manager.RightHand == null)
            {
                Debug.LogError("Assign the local Head, LeftHand, and RightHand transforms before connecting.", Manager);
                return false;
            }

            return true;
        }

        private static Component FindOptionalVoiceClient(GameObject runnerObject)
        {
            Component[] components = runnerObject.GetComponents<Component>();

            foreach (Component component in components)
            {
                if (component != null && component.GetType().FullName == FusionVoiceClientTypeName)
                    return component;
            }

            return null;
        }

        public static void SetUsername(string playerName)
        {
            if (Manager == null || string.IsNullOrWhiteSpace(playerName))
                return;

            const int maxNameLength = 32;
            playerName = playerName.Trim();

            if (playerName.Length > maxNameLength)
                playerName = playerName.Substring(0, maxNameLength);

            if (FusionVRPlayer.localPlayer != null &&
                FusionVRPlayer.localPlayer.NickName.ToString() != playerName)
            {
                FusionVRPlayer.localPlayer.RPCSetNickName(playerName);
            }

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

        /// <summary>
        /// Shuts down the local runner and leaves the current Fusion session.
        /// </summary>
        public static bool Disconnect()
        {
            if (Manager == null || Manager.Runner == null || IsSessionOperationInProgress)
                return false;

            _ = LeaveRoomAsync();
            return true;
        }

        public static Task<bool> JoinRandomRoom(string queue, int maxPlayers)
        {
            return StartSession(null, queue, maxPlayers);
        }

        public static Task<bool> JoinRandomRoom(string queue)
        {
            return JoinRandomRoom(queue, Manager.DefaultRoomLimit);
        }

        // Kept for compatibility with projects already calling the old internal-style method.
        public static Task<bool> _JoinRandomRoom(string queue, int maxPlayers)
        {
            return JoinRandomRoom(queue, maxPlayers);
        }

        public static Task<bool> JoinPrivateRoom(string roomId, int maxPlayers)
        {
            return StartSession(roomId, null, maxPlayers);
        }

        public static Task<bool> JoinPrivateRoom(string roomId)
        {
            return JoinPrivateRoom(roomId, Manager.DefaultRoomLimit);
        }

        // Kept for compatibility with projects already calling the old internal-style method.
        public static Task<bool> _JoinPrivateRoom(string roomCode, int maxPlayers)
        {
            return JoinPrivateRoom(roomCode, maxPlayers);
        }

        private static async Task<bool> StartSession(string sessionName, string queue, int maxPlayers)
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

            IsSessionOperationInProgress = true;
            NetworkRunner runner = Manager.Runner;

            try
            {
                Dictionary<string, SessionProperty> roomProperties = new Dictionary<string, SessionProperty>
                {
                    ["version"] = Application.version
                };

                if (!string.IsNullOrWhiteSpace(queue))
                    roomProperties["queue"] = queue;

                NetworkSceneManagerDefault sceneManager =
                    runner.GetComponent<NetworkSceneManagerDefault>() ??
                    runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

                StartGameArgs args = new StartGameArgs
                {
                    GameMode = Manager.NetworkingMode,
                    SessionName = string.IsNullOrWhiteSpace(sessionName) ? null : sessionName,
                    SessionProperties = roomProperties,
                    PlayerCount = Mathf.Clamp(maxPlayers, 1, 100),
                    SceneManager = sceneManager,
                    IsOpen = true,
                    IsVisible = true
                };

                Scene activeScene = SceneManager.GetActiveScene();
                if (activeScene.buildIndex >= 0)
                {
                    NetworkSceneInfo sceneInfo = new NetworkSceneInfo();
                    sceneInfo.AddSceneRef(SceneRef.FromIndex(activeScene.buildIndex), LoadSceneMode.Single);
                    args.Scene = sceneInfo;
                }
                else
                {
                    Debug.LogWarning(
                        $"Scene '{activeScene.name}' is not in Build Settings. Scene NetworkObjects will not be registered."
                    );
                }

                runner.ProvideInput = true;
                StartGameResult result = await runner.StartGame(args);

                if (!result.Ok)
                {
                    Debug.LogError($"FusionVR failed to start session: {result.ShutdownReason}");
                    DestroyRunner(runner);
                    return false;
                }

                Debug.Log(
                    string.IsNullOrWhiteSpace(sessionName)
                        ? $"FusionVR joined public queue '{queue}' as {runner.GameMode}."
                        : $"FusionVR joined room '{sessionName}' as {runner.GameMode}."
                );

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

        public static string GenerateRoomCode()
        {
            return UnityEngine.Random.Range(10000, 100000).ToString();
        }

        public static void LoadPlayer()
        {
            if (Manager == null)
                return;

            string savedUsername = PlayerPrefs.GetString("Username");
            if (!string.IsNullOrWhiteSpace(savedUsername))
                SetUsername(savedUsername);

            SetColour(Manager.Colour);
            SetCosmetics(Manager.Cosmetics);
        }
    }
}
