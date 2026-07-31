using System;
using System.Collections.Generic;

using UnityEngine;

using Fusion;
using Fusion.Sockets;

using Fusion.VR.Player;

namespace Fusion.VR.Networking
{
    public class FusionVRRunner : MonoBehaviour, INetworkRunnerCallbacks
    {
        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"FusionVR: player joined ({player})");

            if (!runner.IsRunning)
                return;

            bool shouldSpawnPlayer = runner.GameMode switch
            {
                GameMode.Shared => player == runner.LocalPlayer,
                GameMode.Host => runner.IsServer,
                GameMode.Server => runner.IsServer,
                GameMode.Client => runner.IsServer,
                GameMode.AutoHostOrClient => runner.IsServer,
                GameMode.Single => runner.IsServer,
                _ => false
            };

            if (!shouldSpawnPlayer)
                return;

            NetworkObject networkedPlayer = runner.Spawn(
                FusionVRManager.Manager.NetworkedPlayerPrefab,
                Vector3.zero,
                Quaternion.identity,
                player
            );

            runner.SetPlayerObject(player, networkedPlayer);
            FusionVRManager.Manager.playerCache[player] = networkedPlayer;

            Debug.Log($"FusionVR: spawned player object for {player}");
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (!FusionVRManager.Manager.playerCache.TryGetValue(player, out NetworkObject networkedPlayer))
                return;

            if (networkedPlayer != null && networkedPlayer.HasStateAuthority)
                runner.Despawn(networkedPlayer);

            FusionVRManager.Manager.playerCache.Remove(player);
        }

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            FusionVRManager manager = FusionVRManager.Manager;

            if (manager == null || manager.Head == null || manager.LeftHand == null || manager.RightHand == null)
                return;

            FusionVRNetworkedPlayerData data = new FusionVRNetworkedPlayerData
            {
                headPosition = manager.Head.position,
                headRotation = manager.Head.rotation,
                leftHandPosition = manager.LeftHand.position,
                leftHandRotation = manager.LeftHand.rotation,
                rightHandPosition = manager.RightHand.position,
                rightHandRotation = manager.RightHand.rotation
            };

            // Fusion input must be supplied every tick, even when the poses have not changed.
            input.Set(data);
        }

        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            Debug.Log($"FusionVR: runner shutdown ({shutdownReason})");

            if (FusionVRManager.Manager != null && FusionVRManager.Manager.Runner == runner)
            {
                FusionVRManager.Manager.Runner = null;
                FusionVRManager.Manager.VoiceClient = null;
                FusionVRManager.Manager.playerCache.Clear();
            }

            Destroy(gameObject);
        }

        public void OnConnectedToServer(NetworkRunner runner) { }

        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            Debug.LogWarning($"FusionVR: disconnected from server ({reason})");
        }

        public void OnConnectRequest(
            NetworkRunner runner,
            NetworkRunnerCallbackArgs.ConnectRequest request,
            byte[] token)
        {
        }

        public void OnConnectFailed(
            NetworkRunner runner,
            NetAddress remoteAddress,
            NetConnectFailedReason reason)
        {
            Debug.LogError($"FusionVR: connection failed ({reason})");
        }

        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }

        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }

        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }

        public async void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
        {
            await runner.Shutdown(shutdownReason: ShutdownReason.HostMigration);

            if (!FusionVRManager.Connect(false))
            {
                Debug.LogError("FusionVR: failed to create a runner for host migration");
                return;
            }

            StartGameResult result = await FusionVRManager.Manager.Runner.StartGame(new StartGameArgs
            {
                HostMigrationToken = hostMigrationToken,
                HostMigrationResume = Resume
            });

            if (!result.Ok)
                Debug.LogError($"FusionVR: host migration failed ({result.ShutdownReason})");
        }

        private void Resume(NetworkRunner runner)
        {
            Debug.Log("FusionVR: host migration resumed");
            Debug.LogWarning("FusionVR: complete host-migration object restoration is not implemented yet");
            FusionVRManager.OnHostMigrationResume?.Invoke(runner);
        }

        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

        public void OnReliableDataReceived(
            NetworkRunner runner,
            PlayerRef player,
            ReliableKey key,
            ReadOnlySpan<byte> data)
        {
        }

        public void OnReliableDataProgress(
            NetworkRunner runner,
            PlayerRef player,
            ReliableKey key,
            float progress)
        {
        }

        public void OnSceneLoadDone(NetworkRunner runner) { }

        public void OnSceneLoadStart(NetworkRunner runner) { }
    }
}
