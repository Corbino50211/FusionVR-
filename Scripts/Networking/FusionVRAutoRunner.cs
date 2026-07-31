using Fusion;
using UnityEngine;

namespace Fusion.VR.Networking
{
    /// <summary>
    /// Creates a lightweight runner template when the manager has no runner prefab assigned.
    /// This keeps the package usable without depending on the missing Fusion 1 VoiceAndRunner prefab.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FusionVRManager))]
    public sealed class FusionVRAutoRunner : MonoBehaviour
    {
        private GameObject generatedTemplate;

        private void Awake()
        {
            FusionVRManager manager = GetComponent<FusionVRManager>();

            if (manager == null || manager.VoiceAndRunner != null)
                return;

            generatedTemplate = new GameObject("FusionVR Runner Template");
            generatedTemplate.hideFlags = HideFlags.HideInHierarchy;
            generatedTemplate.transform.SetParent(transform, false);
            generatedTemplate.AddComponent<NetworkRunner>();
            generatedTemplate.AddComponent<FusionVRRunner>();

            manager.VoiceAndRunner = generatedTemplate;
        }

        private void OnDestroy()
        {
            if (generatedTemplate != null)
                Destroy(generatedTemplate);
        }
    }
}
