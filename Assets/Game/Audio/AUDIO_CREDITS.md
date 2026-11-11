# Audio credits

Every clip in `Assets/Game/Audio/Resources/` comes from OpenGameArt.org. All of them are CC0 (public domain): no attribution is required, but the authors are credited here anyway. `GameAudio` loads every clip in `Resources/SFX/<CueName>/` for that cue and picks one at random. To change a sound, drop clips into the folder (or assign them in the GameAudio inspector, which takes priority).

| Folder (cue) | Files | Source pack | Author | License |
|---|---|---|---|---|
| Fart, FartLoop, FartCharged, FartEmpty (`gastric_*`) | Cut from one 21 s recording into 11 clips (normalised, 8 ms fade-in, 60 ms fade-out) | [Gastric Distress](https://opengameart.org/content/gastric-distress) | LFA | CC0 |
| FartEmpty (`bubble_02`), Splash, RiverLoop | splash_01/02/04/05/07, bubble_02, loop_water_02 | [40 CC0 water / splash / slime SFX](https://opengameart.org/content/40-cc0-water-splash-slime-sfx) | rubberduck | CC0 |
| RaftCollision, RaftCollisionHeavy, WoodCreak | wood_hit_01-05, wood_slam_01-03, wood_squeak_01/02, wood_cracking_01 | [100 CC0 metal and wood SFX](https://opengameart.org/content/100-cc0-metal-and-wood-sfx) | rubberduck | CC0 |
| RaftCollisionHeavy | bfh1_wood_hit_01 | [75 CC0 breaking / falling / hit SFX](https://opengameart.org/content/75-cc0-breaking-falling-hit-sfx) | rubberduck | CC0 |
| PendulumWhoosh | swish-1/4/7 | [Swishes sound pack](https://opengameart.org/content/swishes-sound-pack) | artisticdude | CC0 |
| PendulumWhoosh | whoosh2_0 | [Air whoosh](https://opengameart.org/content/air-whoosh) | pyranostudios | CC0 |
| Checkpoint, Wipeout | jingles_STEEL01, jingles_PIZZA07 | [85 short music jingles](https://opengameart.org/content/85-short-music-jingles) | Kenney | CC0 |
| Finish | WinSteelDrums | [Win jingle](https://opengameart.org/content/win-jingle) | Fupi | CC0 |
| Respawn, Countdown, CountdownGo, UIClick, UIBack, UIOpen | maximize_006, select_003, confirmation_002, click_001, drop_002, back_002, open_002 | [Interface Sounds](https://opengameart.org/content/interface-sounds) | Kenney | CC0 |
| UIHover | rollover2 | [51 UI sound effects](https://opengameart.org/content/51-ui-sound-effects-buttons-switches-and-clicks) | Kenney | CC0 |
| Music/Gameplay_TropicalLoop | Tropical.wav | [Tropical loop](https://opengameart.org/content/tropical-loop) | wipics | CC0 |
| Music/Menu_UkuleleIsland | ukele.mp3 | [Happy ukulele island surfing theme](https://opengameart.org/content/happy-ukelele-island-surfing-theme) | Tarush Singhal | CC0 |

Import settings come from `Assets/Game/Scripts/Editor/UiAudioImport.cs`. Music and the river loop stream as Vorbis. SFX are forced to mono and decompressed on load.

The clips were picked by name and length; nobody has listened to them yet. If one sounds wrong, swap it with another file from the same pack.
