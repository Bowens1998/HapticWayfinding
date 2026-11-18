# Haptic Wayfinding — VR Direction Cues on a Haptic Vest

**English** | [简体中文](README.zh-CN.md)

A reproduction of the ICIC Lab demo video [Haptic suit for wayfinding](https://youtu.be/SMfNwah_cNI) (Eric Jing Du, Nov 2019, 51 s).
Original description: "Using a haptic suit to guide directions without seeing."

## 1. What it does

The participant wears a VR headset and stands in a glass booth in the middle of a room while a "little person" walks around outside it.
The **vest motors on the matching side vibrate**, telling the participant where the figure is relative to them — without needing to see it.

How the cue is computed:
1. Take the participant's **forward vector** (headset orientation by default), projected onto the floor;
2. Take the vector from the participant to the figure (the figure is treated as a point), also projected onto the floor;
3. The **signed angle** between them is the bearing (0° ahead, +90° right, −90° left, ±180° behind);
4. The bearing is quantised into 8 directions (45° each). Each direction drives the **two adjacent columns** of motors (all 5 motors in a column vibrate together). This mapping was verified in the lab on 2026-11-18:

| Direction | Vibrating columns (wearer's view, counted left → right) |
|---|---|
| Front (0°) | Front panel columns 2 + 3 (middle two) |
| Front-right (+45°) | Front panel columns 3 + 4 |
| Right (+90°) | Front right-most + back right-most |
| Back-right (+135°) | Back panel columns 4 + 3 |
| Back (180°) | Back panel columns 2 + 3 (middle two) |
| Back-left (−135°) | Back panel columns 2 + 1 |
| Left (−90°) | Front left-most + back left-most |
| Front-left (−45°) | Front panel columns 1 + 2 |

Press `M` to switch to single-column (SingleColumn) or interpolated two-column (Interpolated) encoding.

## 2. Hardware

| Item | Model / notes |
|---|---|
| Headset | HTC VIVE Pro (Lighthouse base stations + SteamVR). **The Link Box only has a Mini DisplayPort input** — use a DP / Mini DP cable; HDMI will not work |
| Vest | **bHaptics Tactot** (confirmed in the lab; 20 front + 20 back motors), connected over Bluetooth to bHaptics Player 3.6.13 |
| Software | Unity **2022.3.62f3 LTS** + URP (Universal Render Pipeline) + OpenXR (SteamVR runtime) |
| Haptics driver | Per-motor intensities are streamed straight to bHaptics Player's local WebSocket (`ws://127.0.0.1:15881/v2/feedbacks`) — no bHaptics SDK or App ID needed |

## 3. Scene (realistic version)

A 14 × 14 m lab hall with a 3.2 m ceiling. All textures and models are generated procedurally by editor scripts — **no external assets are used**:

- Polished concrete floor, painted walls, acoustic-tile ceiling with 16 LED panels, baseboards, two structural columns
- Three aluminium-framed windows on the east wall (sky, ground and distant buildings outside), radiators below; sunlight falls through the windows onto the floor
- Wooden door with frame, handle and exit sign on the south wall; fire extinguisher; SteamVR base stations in the corners
- Three workstations on the west wall (wooden desks, monitors, keyboards, office chairs); bookshelf, whiteboard and wall clock on the north wall
- In the middle, a 3 × 3 m aluminium-framed **glass booth** with a VR floor mat, where the participant stands (the small room from the video)
- **The figure**: a life-size (≈1.77 m) dressed mannequin with a procedural walk cycle (leg swing, knee bend, arm swing, body bob). Steps are synchronised with the distance actually travelled, so the feet never slide; when it stops it slowly turns to face the participant

Rendering: URP with HDR + ACES tonemapping, mild bloom and vignette, SSAO (ambient occlusion), 4× MSAA and soft shadows. The LED panel lights and bounce lighting are **pre-baked** (Progressive GPU lightmapper); the sun casts realtime shadows. The moving figure receives baked light through light probes, and a box-projected reflection probe covers the room.

## 4. Repository layout

```
HapticWayfinding/
├── README.md / README.zh-CN.md      # this guide (English / Chinese)
├── CLAUDE.md                        # project notes for Claude Code (read automatically when Claude is opened here)
├── tools/
│   ├── vest_test.py                 # test the vest without Unity (motor order / column directions)
│   └── latency_test.ps1             # test without Unity whether vibration delay builds up over time
└── HapticWayfindingVR/              # Unity project
    └── Assets/HapticWayfinding/
        ├── Scripts/Haptics/         # vest layout & mapping, bHaptics link (diff-only sending), calibration
        ├── Scripts/Guidance/
        │   ├── DirectionalHapticGuide.cs    # ★ core: forward vector vs. figure → angle → two adjacent columns
        │   ├── TargetFigureController.cs    # figure movement: mouse drag / arrow keys / orbit / random jumps / number keys
        │   └── ProceduralWalker.cs          # mannequin walk animation
        ├── Scripts/Rig/             # headset pose, desktop simulator without HMD, visual conditions, experimenter map
        ├── Scripts/UI/              # experimenter HUD, live vest visualiser, direction line
        ├── Scripts/Logging/         # CSV logging (pose, bearing, all 40 motor intensities)
        ├── Editor/
        │   ├── ProjectSetup.cs      # one-click setup: layers / input / OpenXR / build
        │   ├── RenderSetup.cs       # URP, SSAO, post-processing
        │   ├── AssetFactory.cs      # procedural textures, materials, meshes
        │   ├── SceneBuilder.cs      # builds the realistic scene and mannequin, bakes lighting
        │   └── PreviewCapture.cs    # renders preview stills
        ├── Scenes/                  # generated scene + baked lighting data
        ├── Generated/               # generated textures / materials / meshes
        └── Rendering/               # URP pipeline assets, post-processing profile, lighting settings
```

## 5. Deploying on the lab desktop

### 5.0 Required for both options

| Software | Notes |
|---|---|
| **Steam + SteamVR** | Open SteamVR and check that the headset and both base-station icons are green. Then SteamVR → Settings → OpenXR → **"Set SteamVR as OpenXR Runtime"** |
| **bHaptics Player** | Download from the bHaptics website. Power on the vest → pair it in the Player until the Tactot shows as connected |
| VIVE Pro cabling | Link Box: **Mini DisplayPort** (to a DP port on the graphics card — HDMI does not work) + USB 3.0 + power |

> Bluetooth tip: preferably give the vest its own USB Bluetooth adapter on the desktop, and in Device Manager untick "Allow the computer to turn off this device to save power" for the Bluetooth adapter (see section 8).

### Option A: run the prebuilt app (no Unity needed)

1. On this repository's GitHub page → **Releases** (right side) → download the latest `HapticWayfinding-win64.zip`.
2. Unzip it anywhere (preferably a path without non-ASCII characters) and double-click `HapticWayfinding.exe`.
3. The app connects to the SteamVR headset and bHaptics Player automatically; the HUD (top left) should show `HMD tracking` and `Vest: Connected`.
4. Press `C` once for the column test to check the vest's left/right orientation (if wrong, press `F6` / `F7`; saved automatically).

### Option B: open the project in Unity (to modify or rebuild)

1. Install **Unity Hub** and sign in (a free Personal licence is fine) → Installs → install **Unity 2022.3.62f3** with *Windows Build Support* (IL2CPP not required). The version must match exactly, otherwise Unity will offer to upgrade the project.
2. Clone the repository:
   ```bash
   git clone https://github.com/Bowens1998/HapticWayfinding.git
   ```
3. Unity Hub → **Add** → **Add project from disk** → choose the repository's `HapticWayfindingVR` folder → open. The first import compiles packages and shaders and takes about 5–10 minutes.
   - If Unity asks *"enable the new Input System backends?"*, click **No** (the project is already configured to use both input systems).
4. Open `Assets/HapticWayfinding/Scenes/HapticWayfinding.unity` and press **Play**. The scene, baked lighting, URP and OpenXR settings are all in the repository — **no regeneration or re-baking needed**.
5. To build an exe: menu **Haptic Wayfinding → 3. Rebuild Scene + Windows Build** (regenerates the scene and re-bakes; a few minutes). Output: `HapticWayfindingVR/Builds/HapticWayfinding.exe`. To build without re-baking, use Unity's File → Build Settings → Build.
6. Other menu items (rarely needed): **1. Configure Project** (layers / input / OpenXR), **Configure URP Rendering**, **2. Build Demo Scene** (regenerate scene and bake), **2b** (no bake, fast), **Capture Preview Stills** (renders previews to `HapticWayfindingVR/Previews/`).

### Troubleshooting

| Symptom | Fix |
|---|---|
| HUD shows `No HMD - desktop simulator` | SteamVR is not running, or the OpenXR runtime is not SteamVR (see the first row of 5.0). Start SteamVR before launching the app |
| HUD shows `bHaptics Player not reachable` | bHaptics Player is not running. Once it is, the app reconnects automatically |
| HUD says connected but the vest doesn't vibrate | Check the vest shows as connected in the Player; press `F5` to switch the send format and try again |
| Vibration left/right is mirrored | Press `F6` (front panel) / `F7` (back panel); saved automatically |
| Low frame rate / stutter in the headset | On a weaker GPU, open `Assets/HapticWayfinding/Rendering/HW_URP.asset` in Unity and set MSAA to 2x or off, then disable SSAO in `HW_URP_Renderer` |

Without a headset the app falls back to a desktop simulator: W/A/S/D to move, Q/E to turn, hold the right mouse button and drag to look around.

## 6. Lab check-list (in order)

1. **Vest**: power on → open bHaptics Player → confirm the Tactot shows as connected.
2. **Test the vest on its own** (no Unity needed):
   ```
   pip install websocket-client
   python tools/vest_test.py columns   # the 8 columns vibrate in turn; the wearer says where they feel it
   ```
   If left/right is swapped, press `F6` (front) / `F7` (back) in the demo; the setting is saved automatically.
3. **Headset**: SteamVR running, base stations OK, headset and base-station icons green.
4. **Unity**: Play → HUD shows `HMD tracking` and `Vest: Connected` → press `C` for the column test and `K` for the sweep.
5. **Function check**: press number keys 1–8 to put the figure in each direction; the wearer should feel vibration at the matching location, and the active columns should follow when they turn.

## 7. Experimenter keys

| Key | Function |
|---|---|
| 1–8 | Put the figure at 0/45/…/315° relative to the participant's current facing (radius 2.5 m) |
| Arrow keys / left-drag on the map | Move the figure manually (it never enters the glass booth or walks into the furniture along the walls) |
| G | Figure motion: Manual / Orbit / RandomJump |
| M | Encoding: AdjacentPair (default) / SingleColumn / Interpolated / Off |
| N | Distance cue: None / PulseRate (faster pulses when closer) / Intensity (stronger when closer) |
| V | Headset view: FigureVisible / FigureHidden (default, figure invisible) / Blindfold (black) |
| Tab | Desktop layout: headset view + top-down map / map only / headset view only |
| H | Haptics on/off; `+` / `-` master intensity |
| C / K / J / X | Column test / rotating sweep / motor-by-motor test / stop test |
| L | Start / stop CSV logging (saved in `HapticWayfindingVR/Logs/`, never committed to git) |
| F5 / F6 / F7 / F8 | Send format (VestFront+VestBack ↔ Vest 40-point) / flip front L-R / flip back L-R / reset (saved automatically) |
| F1 / F2 | Hide/show help / vest visualiser |

## 8. Known issues and notes

- **Vibration delay builds up over time, or the vest disconnects after a while**: measurements show bHaptics Player itself responds within ~1 ms (frame replacement and turnOff); the bottleneck is the **Bluetooth link** from the Player to the vest. The app now sends only what changed (1 message/s when idle, at most ~16/s while dragging), and the HUD shows msg/s. If problems persist:
  - Device Manager → Bluetooth → the Bluetooth adapter → Power Management → untick "Allow the computer to turn off this device to save power";
  - Disconnect other devices on the same Bluetooth adapter (Bluetooth mouse, keyboard, phone);
  - Most reliable: give the vest its own USB Bluetooth adapter;
  - `tools/latency_test.ps1` tells apart "too many changes" from "the Bluetooth link itself is degrading".
- The row of red / yellow dots in the bottom-left of the original video could not be interpreted and is not reproduced.
- Direction uses the **headset** orientation by default. For torso orientation (cue unchanged when only the head turns), strap a VIVE Tracker to the chest and point `DirectionalHapticGuide.body` at it.
- **Safety**: in Blindfold mode the participant sees nothing — always have a spotter and keep the area clear of obstacles.
