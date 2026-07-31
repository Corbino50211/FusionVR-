using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.SceneManagement;

using Photon.Voice.Fusion;

using Fusion.VR.Player;
using Fusion.VR.Cosmetics;
using Fusion.VR.Saving;

namespace Fusion.VR
{
    [DisallowMultipleComponent]
    public class FusionVRManager : MonoBehaviour
    {
        public static FusionVRManager Manager { get; private set; }

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
        [Tooltip("AutoClientOrHost makes the first player the host and later players clients.")]
        public GameMode NetworkingMode = GameMode.AutoClientOrHost;
        public NetworkPrefabRef NetworkedPlayerPrefab;
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
        public FusionVoiceClient VoiceClient;
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

            Debug.Log("FusionVR attempted to fill the manager's default references.", this);
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

            if (string.IsNullOrWhiteSpace(Manager.FusionAppId))
            {
                Debug.LogError("Please enter a Fusion App ID on FusionVRManager.", Manager);
                return false;
            }

            if (Manager.VoiceAndRunner == null)
            {
                Debug.LogError("VoiceAndRunner prefab is not assigned on FusionVRManager.", Manager);
                return false;
            }

            GameObject voiceAndRunner = Instantiate(Manager.VoiceAndRunner);
            DontDestroyOnLoad(voiceAndRunner);

            NetworkRunner runner = voiceAndRunner.GetComponent<NetworkRunner>();
            if (runner == null)
            {
                Debug.LogError("The VoiceAndRunner prefab does not contain a NetworkRunner.", voiceAndRunner);
                Destroy(voiceAndRunner);
                return false;
            }

            NetworkProjectConfig.Global.EnableHostMigration = Manager.EnableHostMigration;
            if (Manager.EnableHostMigration)
                NetworkProjectConfig.Global.HostMigrationSnapshotInterval = 5;

            Photon.Realtime.PhotonAppSettings.Instance.AppSettings.AppIdFusion = Manager.FusionAppId;
            Photon.Realtime.PhotonAppSettings.Instance.AppSettings.AppIdVoice = Manager.VoiceAppId;
            Photon.Realtime.PhotonAppSettings.Instance.AppSettings.FixedRegion = Manager.Region ?? string.Empty;

            Manager.Runner = runner;
            Manager.Runner.ProvideInput = true;
            Manager.VoiceClient = string.IsNullOrWhiteSpace(Manager.VoiceAppId)
                ? null
                : voiceAndRunner.GetComponent<FusionVoiceClient>();

            Debug.Log("FusionVR runner created.", voiceAndRunner);

            if (joinDefaultRoom)
                _ = JoinRandomRoom(Manager.DefaultQueue, Manager.DefaultRoomLimit);

            return true;
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
            if (Manager == null || Manager.Runner == null)
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
            if (Manager == null)
                return false;

            if (Manager.Runner == null && !Connect(false))
                return false;

            if (Manager.Runner.IsRunning)
            {
                Debug.LogWarning("FusionVR is already inside a session.");
                return false;
            }

            Dictionary<string, SessionProperty> roomProperties = new Dictionary<string, SessionProperty>
            {
                ["version"] = Application.version
            };

            if (!string.IsNullOrWhiteSpace(queue))
                roomProperties["queue"] = queue;

            NetworkSceneManagerDefault sceneManager =
                Manager.Runner.GetComponent<NetworkSceneManagerDefault>() ??
                Manager.Runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

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

            Manager.Runner.ProvideInput = true;
            StartGameResult result = await Manager.Runner.StartGame(args);

            if (!result.Ok)
            {
                Debug.LogError($"FusionVR failed to start session: {result.ShutdownReason}");
                return false;
            }

            Debug.Log(
                string.IsNullOrWhiteSpace(sessionName)
                    ? $"FusionVR joined public queue '{queue}' as {Manager.Runner.GameMode}."
                    : $"FusionVR joined room '{sessionName}' as {Manager.Runner.GameMode}."
            );

            return true;
        }

        public static void LeaveRoom()
        {
            _ = LeaveRoomAsync();
        }

        public static async Task LeaveRoomAsync()
        {
            if (Manager == null || Manager.Runner == null)
                return;

            NetworkRunner runner = Manager.Runner;

            if (runner.IsRunning)
                await runner.Shutdown(shutdownReason: ShutdownReason.Ok);
            else
            {
                Manager.Runner = null;
                Manager.VoiceClient = null;
                Destroy(runner.gameObject);
            }
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
