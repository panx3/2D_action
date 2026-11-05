# Tapioca drink motion

Generated with the built-in imagegen tool on 2026-11-05. Source: `Drink.png` beside this file. References: `Assets/image_/立ち絵(sprite).png` and `Assets/image_/Items/TapiocaMilkTea.png`.

Run `PlayerMotionV2Setup.BuildDrink()` outside Play Mode to import only these six frames. It aligns the boot baseline to Idle, samples into 192 × 192 cells at 32 PPU, and uses the existing character palette plus the cup palette. The runtime samples the sequence across each pickup's effective drinking duration; the existing default is 0.9926531 seconds. This setup does not save scenes, prefabs, or other motion clips.

## Integration and settings

Feature addition: grounded drinking temporarily owns Player constraints and input, with invulnerability independent of the damage timer. Airborne pickups wait without protection. Multiple pickups drink sequentially. Death, respawn, goal presentation and disable paths release ownership and invalidate delayed recovery. Existing heal amount (4), audio, pickup VFX and adjusted Inspector values are preserved. The shared motion set adds `drink`: six frames, 6.044408 FPS, no loop; runtime playback follows each pickup's effective duration.

Changed runtime scripts:

- `Assets/Script/Items/TapiocaHealingPickup.cs`
- `Assets/Script/Player.ItemDrink.cs` and its new `.meta`
- `Assets/Script/Player.cs`
- `Assets/Script/PlayerHealth.cs`
- `Assets/Script/Player.ElectricStun.cs`
- `Assets/Script/Player.EnemyHit.cs`
- `Assets/Script/PlayerMotionSet.cs`
- `Assets/Script/MorningStarLauncher.cs`
- `Assets/Script/MorningStarLauncher.Electric.cs`
- `Assets/Script/MorningStarLauncher.Motion.cs`
- `Assets/Script/ChainConstraint2D.cs`

Changed or added assets/tooling:

- `Assets/Resources/PlayerMotion/PlayerMotionV2.asset`
- `Assets/Art/Player/MotionV2/Drink.png` and its new `.meta`
- `Assets/Editor/PlayerMotionV2Setup.Drink.cs` and its new `.meta`
- The source image and this provenance document in `Docs/Art/PlayerMotionV2/ProductionSources/`.

## Verification (2026-11-05)

Unity compilation succeeded. Stage2 Play Mode probes passed 52 assertions covering grounded/airborne pickup, both facing directions, delayed/full-HP healing, damage protection, pause, sequential items, movement/jump restoration, charge cancellation and neutral input, source/player disabling, goal interruption, actual fall respawn and death, independent damage immunity, flying-ball physics, obstructed rope safety correction, recall and matching Rest physics. The probes and their objects were temporary; no validation helpers were installed in the runtime scene.

No related Console Error or Exception was found. Unity MCP's skill-file generator separately logged failures writing `.agents/skills/assets-copy/SKILL.md` during reloads. No Scene or Prefab was modified or saved, and Stage2 remained clean after leaving Play Mode. Final subjective game-feel evaluation and physical-controller evaluation remain for the user; other scenes and a full player build were not tested.

## Generation prompt

Create a production pixel art sprite animation sheet for the EXACT girl from reference 1 drinking the EXACT bubble tea cup from reference 2. Transparent background. Exactly SIX full-body frames in a regular 3-column by 2-row grid, read left to right then top to bottom. No text, labels, grid lines, shadows, effects, scenery, extra cups, or extra characters. Each equal square cell contains one same-sized girl, feet planted at identical baseline, head at identical height, body centered at same x. Keep girl's original pink twin ponytails, tiny top ahoge, black sleeveless overall dress, pale cream jacket, pink ribbons, brown ankle boots, petite proportions and sharp low resolution pixel clusters and palette. Same 3/4 right-facing angle as reference. Keep pink weapon handle in the relaxed trailing hand at exactly its reference resting angle; that hand remains lowered across ALL frames. Opposite free arm holds a small bubble tea cup with blue straw in front of upper chest and face. The cup must be realistically small, approximately 1/3 of face height. Frames: 1 cup at chest, begins raising; 2 cup near mouth, straw touching lips; 3 drinking through straw, tiny closed-eye cheek swallow; 4 drinking, eyes softly closed; 5 second tiny swallowing pose; 6 satisfied eyes closed and lowers cup to chest. Each pose subtle with zero body bouncing, no walking. All six have identical face shape, anatomy, clothing, legs, boots, body size, pink-hair outline. This is native game pixel art, no soft gradients, no blur, no smooth vector look, no large illustration. Sheet landscape, 3 by 2 square cells with generous equal transparent padding in every cell, entire hair and all feet fully visible.
