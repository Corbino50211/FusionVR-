![FusionVRLogoNoBackSmall](https://github.com/fchb1239/FusionVR/assets/29258204/48221303-cec0-47b9-bc0e-d129bba3dbcc)

# FusionVR — Fusion 2 Community Fork

A lightweight Unity VR networking framework built on Photon Fusion.

This repository is a fork of the original FusionVR project by **fchb1239**. The `fusion2-port` branch is being updated for Photon Fusion 2 and Unity 6, with a focus on host-authoritative co-op VR games.

> **Status:** active development. The Fusion 2 port has not completed its first full Unity compile and two-instance networking test yet. Keep production projects on a pinned commit until a tested release is tagged.

## Current target

- Unity 6
- Photon Fusion 2.1
- `GameMode.AutoHostOrClient`
- Six players by default
- Networked head and hands
- Public queues and private room codes
- Player names, colours, and cosmetics
- Optional Photon Voice integration

## Major Fusion 2 changes

The port currently includes:

- Fusion 2 `INetworkRunnerCallbacks` signatures
- Fusion 2 network-property change callbacks
- Host-authoritative player spawning
- `SetPlayerObject` registration
- VR pose input supplied every network tick
- Fusion 2 `NetworkTransform` teleport calls
- Fresh runner creation after shutdown or failed connection
- Duplicate session-operation protection
- Automatic runner callback registration
- Optional Voice detection without a hard compile dependency

## Installation

Read [FUSION2_SETUP.md](FUSION2_SETUP.md) before importing the framework.

The important order is:

1. Import Photon Fusion 2.1.
2. Confirm Fusion compiles by itself.
3. Import this fork.
4. Run the networking-only test.
5. Install the matching Photon Voice integration afterward, if needed.

Do not import an older standalone Photon Realtime package over Fusion 2.1.

## Basic use

```csharp
using Fusion.VR;
```

### Connect and join the default queue

```csharp
FusionVRManager.Connect();
```

### Join a public queue

```csharp
bool joined = await FusionVRManager.JoinRandomRoom("Warehouse", 6);
```

Players only match with sessions using the same application version and queue property.

### Join a private room

```csharp
bool joined = await FusionVRManager.JoinPrivateRoom("48291", 6);
```

### Leave

```csharp
await FusionVRManager.LeaveRoomAsync();
```

### Player settings

```csharp
FusionVRManager.SetUsername("Worker");
FusionVRManager.SetColour(Color.blue);
```

### Cosmetics

```csharp
FusionVRManager.SetCosmetics("Head", "HardHat");
```

Or set multiple slots:

```csharp
Dictionary<string, string> cosmetics = new Dictionary<string, string>
{
    ["Head"] = "HardHat",
    ["Face"] = "SafetyGlasses"
};

FusionVRManager.SetCosmetics(cosmetics);
```

Cosmetic slot names are limited to 16 characters and cosmetic object names are limited to 32 characters by the current networked types.

## Required scene references

The scene must contain exactly one `FusionVRManager` with:

- Fusion App ID
- Local XR head transform
- Local left-hand transform
- Local right-hand transform
- Runner prefab
- Registered networked player prefab

The runner prefab requires `NetworkRunner`. `FusionVRRunner` is recommended on the prefab and is added automatically at runtime if missing.

Photon Voice components are optional. When a Voice App ID is assigned but no `FusionVoiceClient` is found, FusionVR warns and continues with networking only.

## First test goal

Before adding locomotion or grabbing, verify that:

1. One instance starts as host.
2. A second instance joins as client.
3. Both players see synchronized head and hand poses.
4. Either player can leave.
5. A fresh runner can reconnect without restarting the application.

## Roadmap

- [x] Initial Fusion 2 API port
- [x] Optional Voice dependency
- [x] Runner lifecycle cleanup
- [ ] Clean Unity 6 compile
- [ ] Two-instance head/hand synchronization test
- [ ] Joystick locomotion
- [ ] Snap and smooth turning
- [ ] Networked one-hand grabbing
- [ ] Two-player heavy-object carrying
- [ ] Host-authoritative physics objects
- [ ] Tested tagged release

## Contributing

Keep Fusion 2 work on feature branches and open pull requests into `main`. Include the Unity version, Fusion version, and exact Console errors when reporting a bug.

## Credit and license

Original FusionVR framework by **fchb1239**.

This fork retains the original [MIT License](LICENSE) and copyright notice.
