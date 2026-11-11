# Fart Online: web app, phones and tablets

Written for: developers who build, deploy or change the web and mobile versions of Fart Online.

## Building and deploying the web app

- **Build:** menu *Tools > Fart Online > Build Web App (WebGL)* (`Scripts/Editor/WebBuilder.cs`). The output goes to `D:\FOBuild\Web`, about 34 MB with gzip. The build switches the editor to the WebGL platform; switch back in *File > Build Profiles* to build desktop players.
- **Deploying:** upload the *contents* of `D:\FOBuild\Web` to any static host over https, such as itch.io (HTML5 zip), GitHub Pages or Netlify. Gzip uses a decompression fallback, so no server configuration is needed (a server that sends `Content-Encoding: gzip` loads faster). Never upload `D:\FOBuild\Web_BurstDebugInformation_DoNotShip`.
- **Local test:** `cd D:\FOBuild\Web` then `python -m http.server 18765`, and open http://127.0.0.1:18765. Opening `index.html` straight from disk does not work.
- **Installable web app (PWA):** the template `Assets/WebGLTemplates/FartOnline` provides:
  - `manifest.webmanifest`: fullscreen, any orientation, brown theme, icons in `TemplateData/icons` rendered from the game's UI by `WebBuilder.RenderIcons()`;
  - `ServiceWorker.js`: caches the build for offline play;
  - a full-window responsive canvas;
  - a Fart Online loading screen (Lilita One);
  - no page scrolling, pinch zoom or long-press menus over the game;
  - the device pixel ratio capped at 1.5 on mobiles and 2 on desktops.
- **Configure only:** *Tools > Fart Online > Web > Configure WebGL Settings + Icons* sets the template, compression and data caching, the Mobile pipeline settings, the post-processing data copy and the icons.

## Rendering on WebGL (lessons learned)

- **Quality level:** the web player runs the **Mobile** quality level (`PlatformBoot` forces it): Forward renderer, render scale 0.85 with Linear upscaling, depth and opaque textures on (the water needs both), no MSAA, 35 m shadows.
- **Additional-light shadows are off on both pipeline assets.** The scene only has the sun. On WebGL, URP's placeholder additional-light shadow map is bound to a shadow sampler, WebGL rejects the draw ("Mismatch between texture format and sampler type"), and every lit custom-shader object (canyon, raft, pigs, trees) became invisible.
- **Post-processing:** URP disables all of it when any shader in the renderer's PostProcessData is unsupported. The FSR (EASU) shader isn't supported on WebGL, so `Mobile_Renderer` uses `Assets/Settings/PostProcessData_Web.asset`, a copy with EASU swapped for the final-post shader. FSR is never selected.
- **Diagnosing:** `gldiag.js` in the session scratchpad wraps the WebGL draw calls and lists the samplers of any program whose draw fails. A failing program that binds a `sampler2DShadow` points at shadow settings.

## Online play in the browser

The game's network transport is **WebSockets** (`NetworkSessionManager.useWebSockets`), so a browser can **join** a game hosted by the desktop app. Desktop players use the same transport, so web and desktop players share a raft.
- **Browsers can't host.** Unity Transport can't listen for connections in WebGL, so the web menu shows the room code / address box and **Join** only.
- The address box is pre-filled with the host name of the page. If the PC that hosts the game also serves the web app, players on the same Wi-Fi just press Join.
- **Plain `http://` pages only.** An `https://` page may only open `wss://` (TLS) connections, and the desktop host doesn't serve TLS. To play online from the browser, serve the web app over http on your LAN (for example `python -m http.server` in `D:\FOBuild\Web`). Internet play would need a TLS proxy or Unity Relay (the project would need to be linked to Unity Gaming Services).
- **Automated test:** `index.html?fo-net=join&fo-addr=B9S-AAAB&fo-secs=60` makes the page join as a scripted bot (`NetAutoTest`). It logs to the browser console.

## Responsive layout (`GameplayHUD.UpdateResponsive`)

- **Canvas scaling:**
  - Landscape uses a 1920x1080 reference and portrait 1080x1920.
  - The scaler matches whichever side limits the screen, so the whole reference area always fits on 19.5:9 phones, 4:3 tablets and desktops.
  - The layout re-runs whenever the window size, orientation or touch mode changes.
- **Portrait:**
  - The logo and tagline move to the top and the menu board to the bottom.
  - The player cards and radar move to the top corners, shrunk.
  - Tips appear under the top bar.
  - The chase camera widens its field of view (up to 80°) and pulls back on tall screens (`RaftChaseCamera.portraitFieldOfView`).
- **Boards:** every board (pause, settings, how to play, results, game over, lobby) shrinks to fit the screen. `UiPop.SetBase` keeps that scale through the pop-in animation.
- **Web and mobile extras:** the Quit button becomes Fullscreen (there's also a Fullscreen button in Settings). Mobile platforms run at 60 fps.

## Touch controls (`Input/TouchInput.cs`)

- **Buttons:**
  - Bottom-left: ◀ ▶ hold to walk around the rim.
  - Bottom-right: a big **FART** button (tap for a puff, hold for a stream), **CHARGE** (hold, then release for a big blast) and **BRAKE**.
- **Who they control:** the buttons feed the first local player (`TouchInput.Owner`) exactly like keys, so they work in solo, with the AI crew, in local co-op (as player 1) and online.
- **Multi-touch:** each button tracks its own finger, so you can walk and fart at the same time.
- **When they show:** Settings > *Touch buttons*: **Auto** (mobile platforms, or as soon as a touchscreen is used), **Always on**, or **Off**. The choice is saved in PlayerPrefs (`fo.touch`).

## Tests

- **Layout:** *Tools > Fart Online > Tests > Run Responsive Probe* renders the menu, HUD, pause, settings, how to play and game over in:
  - phone portrait (1080x2340);
  - phone landscape (2340x1080);
  - tablet portrait (1536x2048);
  - tablet landscape (2048x1536);
  - desktop (1920x1080).

  The images go to `ArtSource/Game/Previews/Responsive/`.
- **Browser:** puppeteer-core with the installed Chrome loads the real web build at 1280x720 and as an emulated iPhone (touch, mobile user agent). It taps Play Solo, holds FART, then rotates to landscape.
  - The checks are: zero failing WebGL draws and no console errors.
  - The scripts are in the session scratchpad (`webtest/test.js`, `gldiag.js`). They aren't part of the project.

## Android build, landscape lock and phone performance (2026-11-07)

- **Build:** *Tools > Fart Online > Build Android APK* writes `D:\FOBuild\Android\FartOnline.apk` (IL2CPP, ARM64, Vulkan + GLES3). An incremental build takes about 7 minutes. Install it with `adb install -r D:\FOBuild\Android\FartOnline.apk`.
- **Landscape only:** the game is played sideways on phones.
  - `AndroidBuilder.Configure` allows landscape left and right only, with auto-rotation.
  - `PlatformBoot` sets the same rules at runtime.
  - The HUD uses its landscape layout and respects the camera cutout. On a 2400x1080 phone the safe area starts at x = 107.
- **Test phone:** OnePlus CPH2467, Snapdragon 695 / Adreno 619, 2400x1080 at 120 Hz.
- **Results:** gameplay went from **45 fps (17.4 ms GPU) to a steady 60 fps (13.9 ms GPU)** at render scale 0.65.
- **What was slow:** the game was GPU bound. The on-device diagnostic switched one feature off at a time to find the costs:
  1. The canyon and water shaders' mobile paths still computed value noise with hash functions: 4 to 8 hashes per pixel, and 80 per canyon vertex. They now sample `_FONoiseTex` instead, a 128x128 tiling RGBA noise texture made at startup (`GameFlow/MobileNoise.cs`, bound by `PlatformBoot`). The water's vertex swell is now two sine waves.
  2. The four crew pigs (30.8k triangles and 19.7k skinned vertices each) cost about 5 ms of GPU time and 3 ms of main thread. *Tools > Fart Online > Build Mobile Pig Mesh* (`Editor/PigMobileMeshBuilder.cs`) builds a lighter mesh, `Assets/Characters/Pig/Models/Pig_Mesh_Mobile.asset`: Unity's Mesh LOD generator simplifies each submesh, then only the vertices a chosen LOD uses are kept. The result has 6.9k vertices and 9.2k triangles, and keeps the same bones, bind poses, all 23 blend shapes, UVs and vertex colours. `Players/MobileMeshSwap.cs`, on PF_Pig's mesh, switches to it on the Mobile quality level.
- **Adaptive resolution (`GameFlow/AdaptiveResolution.cs`):**
  - It starts at about a 720p internal resolution and stays between scale 0.5 and 0.8. The UI always draws at native resolution.
  - Two seconds with more than 8% missed frames lowers the scale by 0.05. Eight clean seconds with GPU headroom raise it by 0.05.
  - A raise followed by a drop within 12 s lowers the ceiling, so the scale doesn't oscillate.
- **Frame pacing:** Android optimized frame pacing (Swappy) is on, vSync count is 0, and the target is 60 fps.
- **Device tools (adb):**
  - `adb logcat -s Unity | grep FartPerf`: every 5 s, fps, worst frame, 1% low, CPU and GPU ms, render scale, screen and safe area, and run state.
  - (Removed 2026-11-11: the `fo_auto` hands-free AI run and `DeviceAutoPlay.cs`. Only real players can play; measure performance by playing by hand.)
  - Add `--es fo_diag 1` for `DeviceDiag`. Once the run is under way it holds the scale at 0.65 and measures fps, GPU ms and main-thread ms with one thing switched off at a time: shadows, pigs, water, canyon, particles, decor, hazards, UI, render scale and the world camera. Lines are tagged `[FartDiag]`.
  - The `ClassNotFoundException ... AssetPackManager` line at startup is Unity checking for Play Asset Delivery. It's harmless.
