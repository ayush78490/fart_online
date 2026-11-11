# UI art and font credits

The UI skin in `Assets/Game/UI/Resources/UI/` is loaded at runtime by `UiKit` (`Assets/Game/Scripts/UI/UiKit.cs`). The 9-slice borders are set by `Assets/Game/Scripts/Editor/UiAudioImport.cs`.

## Sprites: free versions, NOT cleared for commercial use

| Files | Pack | Author | License |
|---|---|---|---|
| Panel_Main, Pill_Brown, Bar_Back, Bar_Fill, Bar_Border, Slider_Knob, Circle_On/Off, Square_DarkBrown, Btn_Green_*, Btn_Red_*, Badge_Green/Red, Icon_Play/Quit/Settings/Share | "Bubble Bits GUI", free version (`C:\Users\Ayush\Downloads\Free_version`) | RDBI | Free version: "may not be used in commercial products. To obtain commercial usage rights, please purchase the full pack." |
| Btn_Wood, Btn_White, Btn_WoodRed, Btn_WoodSquare, Btn_WoodRound, Card_Wood, Plank_Wide, Note_Paper, Container1, Slot, Slot_Selected, Check_On/Off, Icon_Plus/Close/Heart/Bolt, Arrow_A/B, Cursor | "Cozy UI Pack" DEMO (`C:\Users\Ayush\Downloads\DEMO_Cozy_UI_Pack_doboui`), https://dobo-ui.itch.io/ | dobo_ui | Demo: "may not be used in commercial products. To obtain commercial usage rights, please purchase the full pack." |

**Before any commercial release**, buy the full versions of both packs, or replace these sprites. Every sprite is looked up by file name, so a purchased pack can replace these files one for one.

## Fonts (SIL Open Font License 1.1, commercial use OK)

- `Fonts/LilitaOne-Regular.ttf`: Lilita One by Juan Montoreano (google/fonts). License: `Fonts/OFL_LilitaOne.txt`.
- `Fonts/Fredoka-SemiBold.ttf`: a static wght=600 instance made with fontTools from the variable Fredoka by the Fredoka Project Authors (google/fonts). License: `Fonts/OFL_Fredoka.txt`. The font declares no Reserved Font Name, so the modified instance can keep its name.

## Generated in code

The sunburst behind the menus and the fallback rounded and circle shapes are generated at runtime by `UiKit`.

## 3D obstacles and traps

`Assets/Obstacle Pack/`: "Hyper-Casual Obstacle Pack" 1.0.0 by EnsArts (Unity Asset Store), licensed under the Standard Unity Asset Store EULA, which allows use in games, commercial ones included. Only `Assets/Obstacle Pack` was imported. The package also carries a `Packages/manifest.json` that would overwrite this project's packages, so it was left out on purpose.

The endless river uses these models as obstacles and traps. `EndlessRiver.Fit()` scales each one so it fits inside the footprint the obstacle planner reserved for it:

- ice walls (boulders)
- Bomb (spiked mines)
- barrier (barricade)
- barrier2 (concrete barrier)
- log_trap2 (rope fences)
- Hammer (gate poles)
- obstacle spikewall (sliding wall)
- blade (spinner)
- blade spinner (buzzsaw)
- PressTrap (stamping press)
- obstacle spike (spike trap)

The flamethrower, poison thrower and crossbow models are bank scenery.
