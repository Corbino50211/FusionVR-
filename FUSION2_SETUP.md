# FusionVR Fusion 2 Setup

This branch is an in-progress port of the original FusionVR framework from Photon Fusion 1 to Photon Fusion 2.

## Supported target

- Unity 6
- Photon Fusion 2.1
- Host/client topology through `GameMode.AutoHostOrClient`
- Up to 6 players by default
- Photon Voice 2 is optional

## 1. Install Fusion

1. Import Photon Fusion 2.1.
2. When Fusion Hub asks for a mode, choose **Advanced** so Host/Server content is visible.
3. Let Unity finish compiling before importing anything else.
4. Add your Fusion App ID through Photon App Settings or the Fusion Hub.

Do not import an older Photon Realtime package on top of Fusion 2.1.

## 2. Install Voice only after Fusion works

The networking core no longer requires Photon Voice to compile.

To add voice:

1. Confirm Fusion compiles by itself.
2. Open Fusion Hub.
3. Install the Voice integration that matches the installed Fusion/Realtime generation.
4. Add `FusionVoiceClient` to the runner prefab.
5. Enter a Voice App ID on `FusionVRManager`.

If a Voice App ID is present but `FusionVoiceClient` is missing, FusionVR logs a warning and continues without voice.

## 3. Network Project Config

Open:

`Tools > Fusion > Network Project Config`

Recommended prototype settings:

- Input Transfer Mode: **Latest State**
- Default Players: **6**
- Host Migration: **Off** until the basic session is stable

Fusion documents Latest State as useful for VR input because head and hand poses create a relatively large input struct.

The config asset should exist once at:

`Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`

If Fusion Hub reports that this asset already exists, close Unity and remove the duplicate/stale config and matching `.meta`, then reopen Unity so Fusion can regenerate it.

## 4. Runner prefab

The object assigned to `FusionVRManager.VoiceAndRunner` must contain:

- `NetworkRunner`
- `FusionVRRunner`
- Optional `FusionVoiceClient`
- Optional Voice recorder/speaker components

If `FusionVRRunner` is missing, the manager adds it at runtime and logs a warning.

## 5. Player prefab

The networked player prefab must contain:

- `NetworkObject`
- `FusionVRPlayer`
- Head `NetworkTransform`
- Left-hand `NetworkTransform`
- Right-hand `NetworkTransform`

Register/bake the prefab through Fusion before entering Play Mode.

Assign the resulting prefab reference to `FusionVRManager.NetworkedPlayerPrefab`.

## 6. Scene setup

1. Add the active scene to Build Profiles/Build Settings.
2. Place one `FusionVRManager` in the scene.
3. Assign the local XR head, left hand, and right hand transforms.
4. Assign the runner prefab.
5. Assign the networked player prefab.
6. Set the Fusion App ID.
7. Leave the Voice App ID empty until Voice is installed correctly.

## 7. First test

The first successful test is intentionally small:

1. Run one instance as host.
2. Run a second instance as client.
3. Confirm both players join the same session.
4. Confirm head and both hand poses synchronize.
5. Leave and reconnect without restarting the Unity Editor.

Do not add joystick locomotion, grabbing, or enemies until this test passes.

## Common failures

### Multiple assembly definition files in PhotonRealtime

Two incompatible Photon Realtime copies were imported. Remove the conflicting Photon packages and reinstall Fusion first, then its matching Voice integration.

### Runner cannot be reused

Fusion runners are single-use. This fork now destroys failed or shut-down runners and creates a fresh runner for the next connection.

### Voice package is missing

This is no longer fatal. Networking runs without Voice, and `VoiceClient` remains null.

### Player spawns but does not move

Check that:

- the runner has `ProvideInput` enabled;
- `FusionVRRunner` is registered as a callback;
- Head/hand references are assigned;
- the player prefab has the three required `NetworkTransform` components;
- the player prefab is registered with Fusion.

## Current development order

1. Fusion 2 compilation
2. Two-player head/hand synchronization
3. Joystick locomotion
4. Snap and smooth turning
5. Networked grabbing
6. Two-player heavy-object carrying
7. Object damage/value system
8. Enemy and contract systems

## Credit

Original FusionVR framework by fchb1239. This fork retains the original MIT license and copyright notice.
