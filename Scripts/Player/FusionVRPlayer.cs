using System.Collections.Generic;

using UnityEngine;

using Fusion;
using Fusion.VR.Cosmetics;

using TMPro;

namespace Fusion.VR.Player
{
    public class FusionVRPlayer : NetworkBehaviour
    {
        public static FusionVRPlayer localPlayer;

        public int PlayerId { get; private set; }

        [Header("Objects")]
        public Transform Head;
        public Transform Body;
        public Transform LeftHand;
        public Transform RightHand;

        [Header("Colour Objects")]
        public List<Renderer> renderers = new List<Renderer>();

        [Header("Networked Transforms")]
        public NetworkTransform HeadTransform;
        public NetworkTransform LeftHandTransform;
        public NetworkTransform RightHandTransform;

        [Header("Cosmetics")]
        public List<PlayerCosmeticSlot> cosmeticSlots = new List<PlayerCosmeticSlot>();

        [Header("Other")]
        public TextMeshPro NameText;
        public bool HideLocalName = true;
        public bool HideLocalPlayer;

        [Header("Runtime")]
        public bool isLocalPlayer;

        [Networked, OnChangedRender(nameof(OnNickNameChanged))]
        public NetworkString<_32> NickName { get; set; }

        [Networked, OnChangedRender(nameof(OnColourChanged))]
        public Color Colour { get; set; }

        [Networked, OnChangedRender(nameof(OnCosmeticsChanged)), Capacity(10)]
        public NetworkDictionary<NetworkString<_16>, NetworkString<_32>> Cosmetics => default;

        public override void Spawned()
        {
            PlayerId = Object.InputAuthority.PlayerId;

            if (Object.HasInputAuthority)
            {
                localPlayer = this;
                isLocalPlayer = true;

                if (NameText != null)
                    NameText.gameObject.SetActive(!HideLocalName);

                SetLocalAvatarVisible(!HideLocalPlayer);
                FusionVRManager.LoadPlayer();
            }

            // OnChangedRender is not called for the object's initial state.
            OnNickNameChanged();
            OnColourChanged();
            OnCosmeticsChanged();
        }

        private void Update()
        {
            if (!Object.HasInputAuthority || FusionVRManager.Manager == null)
                return;

            FusionVRManager manager = FusionVRManager.Manager;

            if (manager.Head != null && HeadTransform != null)
                HeadTransform.transform.SetPositionAndRotation(manager.Head.position, manager.Head.rotation);

            if (manager.LeftHand != null && LeftHandTransform != null)
                LeftHandTransform.transform.SetPositionAndRotation(manager.LeftHand.position, manager.LeftHand.rotation);

            if (manager.RightHand != null && RightHandTransform != null)
                RightHandTransform.transform.SetPositionAndRotation(manager.RightHand.position, manager.RightHand.rotation);
        }

        public override void FixedUpdateNetwork()
        {
            // In Host/Server mode, the state authority consumes the client's input and
            // writes it into the NetworkTransforms. Proxies receive the replicated result.
            if (!Object.HasStateAuthority || Object.HasInputAuthority)
                return;

            if (!GetInput(out FusionVRNetworkedPlayerData data))
                return;

            HeadTransform?.Teleport(data.headPosition, data.headRotation);
            LeftHandTransform?.Teleport(data.leftHandPosition, data.leftHandRotation);
            RightHandTransform?.Teleport(data.rightHandPosition, data.rightHandRotation);
        }

        private void SetLocalAvatarVisible(bool visible)
        {
            if (Head != null)
                Head.gameObject.SetActive(visible);

            if (Body != null)
                Body.gameObject.SetActive(visible);

            if (LeftHand != null)
                LeftHand.gameObject.SetActive(visible);

            if (RightHand != null)
                RightHand.gameObject.SetActive(visible);
        }

        private void OnNickNameChanged()
        {
            string playerName = NickName.ToString();

            if (NameText != null)
                NameText.text = playerName;

            gameObject.name = string.IsNullOrWhiteSpace(playerName)
                ? $"Player ({PlayerId})"
                : $"Player ({playerName})";
        }

        private void OnColourChanged()
        {
            foreach (Renderer targetRenderer in renderers)
            {
                if (targetRenderer != null)
                    targetRenderer.material.color = Colour;
            }
        }

        private void OnCosmeticsChanged()
        {
            foreach (PlayerCosmeticSlot slot in cosmeticSlots)
            {
                if (slot == null)
                    continue;

                string selectedCosmetic = string.Empty;

                foreach (KeyValuePair<NetworkString<_16>, NetworkString<_32>> cosmetic in Cosmetics)
                {
                    if (cosmetic.Key.ToString() == slot.SlotName)
                    {
                        selectedCosmetic = cosmetic.Value.ToString();
                        break;
                    }
                }

                foreach (Transform cosmeticTransform in slot.Slot)
                {
                    if (cosmeticTransform == null)
                        continue;

                    GameObject cosmeticObject = cosmeticTransform.gameObject;
                    cosmeticObject.SetActive(cosmeticObject.name == selectedCosmetic);

                    if (cosmeticTransform.GetComponentInChildren<Collider>(true) != null)
                    {
                        Debug.LogWarning(
                            $"It is not recommended to have a collider on cosmetic '{cosmeticObject.name}'.",
                            cosmeticObject
                        );
                    }
                }
            }
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPCSetNickName(string name, RpcInfo info = default)
        {
            NickName = name;
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPCSetColour(Color colour, RpcInfo info = default)
        {
            Colour = colour;
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPCSetCosmetics(CosmeticSlot[] cosmetics, RpcInfo info = default)
        {
            Cosmetics.Clear();

            foreach (CosmeticSlot cosmetic in cosmetics)
            {
                if (Cosmetics.Count >= Cosmetics.Capacity)
                    break;

                Cosmetics.Set(cosmetic.SlotName, cosmetic.CosmeticName);
            }
        }
    }
}
