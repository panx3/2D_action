# CRT pause / result and original goal celebration

ポーズ・クリア表示は共通CRT画面。ゴール時は従来のロゴ演出を再生し、ワンテンポ置いてCRTクリア画面へ移る。

## Ownership

- `PauseMenuController`: existing `Player/Pause` input, pause time ownership, continue/retry/controls/title.
- `GoalMenuController`: existing `GoalPoint.ShowGoal` destination, result priority, stopped gameplay/input handoff, next/retry/title.
- `CrystalAcquiredUI`: existing `Play(Action)` entry point, immediate gameplay input lock, eased stop, original animated celebration logo / flash / sparkles, configured post-logo beat, then result callback.
- `Player` / `MorningStarLauncher`: goal input gate for movement, jump, weapon input and buffered commands; pre-existing combat hit stop is cancelled before the goal acquires time.
- `GameBgmController`: pause-only low-pass tone (2200 Hz, 0.18-second unscaled fade); normal tone is restored on resume and immediately on Goal BGM.
- `CRTOverlayController`: shared frozen backdrop, reversible signal acquisition, unscaled animation, pointer distortion mapping.
- `CRTMenuItem` / `CRTMenuNavigation`: selection treatment and wraparound navigation; disabled next-stage action is skipped.
- `CRTFilterController` / `CRTFilter.shader`: one final camera effect for background and text. Ordinary gameplay Inspector values are retained.

The overlay requests the next natural pre-CRT camera frame while its presentation is hidden. This preserves PixelPerfectCamera's viewport/upscale path. A raw snapshot and a softened/dimmed snapshot are retained until dismissal. The softened layer fades over the raw frame while CRT curvature, mask, RGB offset, scanlines and vignette blend from the ordinary gameplay profile. Text remains sharp before the final common CRT pass. There is no continuous snapshot allocation during gameplay.

Only one overlay can own the camera. Acquisition blocks pause, then hides before invoking the existing goal callback. Result retains the pause block. Runtime textures/materials are released on dismissal or scene exit. Scene transitions restore `Time.timeScale` to 1; normal resume restores the prior scale.

## Prefabs / Inspector

The existing `PauseMenu`, `GoalMenu`, `CrystalAcquiredUI` prefab GUIDs and root flow-component fileIDs are preserved. Old stone frames, moss, chain decoration, button frames, Japanese placeholder labels, fourteen-star result layer were replaced. The original CONGRATULATION image presentation is retained before the CRT result.

New reusable profile on the PauseMenu and GoalMenu prefab roots's `CRTOverlayController`:

| Parameter | Effective value |
|---|---:|
| Acquisition Duration (entry / resume) | 0.22 seconds |
| Screen Scale | 1.16 |
| Transition Jitter | 2.4 render pixels |
| Transition Jitter Duration | 0.16 seconds (three diminishing bursts) |
| Curvature | 0.22 |
| Scanlines | 0.58 |
| Noise | 0.045 |
| RGB Offset Pixels | 1.2 |
| Vignette | 0.62 |
| Glass Reflection | 0.13 |
| Background Blur | 3 |
| Background Brightness | 0.28 |

`CrystalAcquiredUI`: logo delay 0.08 / pop 0.34 / hold 1.25 / fade 0.24 seconds; final scale 1.12. The user-adjusted post-logo beat is currently 0.6 seconds. `Goal Stop Duration` is 0.24 seconds: gameplay time eases from its current scale to zero with a cubic decay, then remains stopped through the logo, beat and result. UI animation uses unscaled time throughout. The former `slowMotionDuration` value migrates to `goalStopDuration`; the former temporary slow-motion scale is superseded by a complete stop.

`CompletScene`: PauseCanvas sorting 700, GoalCanvas 710, goal celebration canvas 650. Goal and acquisition Inspector references point to the scene's existing PauseMenuController. Gameplay HUD stays below these screens.

`GoalMenuController.nextSceneName` remains empty as before. CONTINUE is dimmed/non-interactable; RETRY receives initial focus. Set an available build scene name to enable CONTINUE. Title remains `TitleScene`; retry reloads the active scene.

Silkscreen is imported as a static point-filtered TMP bitmap font. The font's OFL notice is included in `Assets/Fonts/Silkscreen-OFL.txt`. No separate CRT Material asset is required: the camera material is private runtime state; the TMP material/atlas are font subassets.

## Reuse / regeneration

Duplicate one of the overlay prefabs, assign its message/buttons, and keep scene flow in its owning controller. Call `Show()` / `HideAnimated(onHidden)` for reversible presentation; gate submissions with `IsReady`. `IsClosing` blocks repeat input. `Hide()` cancels pending completion and cleans up immediately for scene changes, disable, or another screen taking priority. `Show()` returns false if another presentation owns the camera. Keep presentation under a root Canvas included by CRTFilterController, with the backdrop below `PhosphorUI`.

Use `Tools/鉄球少女/UI/Rebuild CRT Overlays` with a saved CompletScene open. This rebuilds visuals and reconnects scene references. It preserves existing root flow fields and CRT profile values. Pause/result installers delegate their UI generation to this builder. GoalCelebrationLogoSetup owns the original logo presentation; it is not rebuilt as a CRT menu. Individual prefab rebuild methods are also available.

The curved-screen mouse mapping matches the final shader. CRT menu graphics deliberately do not receive the ordinary unwarped pointer raycast; Input System UI navigation/submit remains active for gamepad/keyboard. The backdrop blocks pointer interaction with gameplay.

## Validation / acceptance

- Unity C# and shader compilation and serialized-reference validation.
- Synthetic Input System gamepad Start/B/navigation, curved mouse click, duplicate request rejection, acquisition/result handoff, disabled-next-stage skipping, actual retry and title scene loads.
- Targeted three-hit GoalPoint path, goal BGM, frozen backdrop pixel stability, and advancing unscaled CRT material clock at timeScale zero.
- Game View captures reviewed at 1920x1080.
- Existing EnemyGoalTransitionPlayModeTest was updated for the new UI but stops before its goal phase at `Checkpoint monument base sprite missing`. Checkpoint assets were not changed for this UI task.

Physical gamepad/keyboard feel, final artistic acceptance, non-16:9 displays, target-build/GPU performance and a configured next-stage transition remain for user verification. No gameplay physics or input mappings were retuned.

## Changed files

Runtime: `Assets/Script/GameBgmController.cs`, `Assets/Script/PauseMenuController.cs`, `Assets/Script/GoalMenuController.cs`, `Assets/Script/CRTFilterController.cs`, `Assets/Script/UI/CrystalAcquiredUI.cs`, `Assets/CRTFilter.shader`.

Added: `Assets/Script/UI/CRTOverlayController.cs`, `CRTMenuItem.cs`, `CRTMenuNavigation.cs`; `Assets/Editor/CRTOverlayInstaller.cs`; Silkscreen `.ttf`, OFL notice and static TMP font asset; their `.meta` files; this document. No new Prefab or standalone Material/Shader asset was necessary.

Rebuilt: `Assets/Prefabs/UI/PauseMenu.prefab`, `GoalMenu.prefab`, `CrystalAcquiredUI.prefab`.

Scene: `Assets/Scenes/CompletScene.unity` (overlay order and pause-owner references only).

Editor generation compatibility: `Assets/Editor/RecoveryAndUiIntegrator.cs`, `GoalEnemyTransitionSetup.cs`, `GoalCelebrationCheckpointSetup.cs`, `GoalCelebrationLogoSetup.cs`. Pause/result generation methods delegate to the common CRT builder; logo generation uses the original builder; unrelated gameplay installers retain their existing behavior.

Updated UI assertions: `Assets/Editor/EnemyGoalTransitionPlayModeTest.cs`.

This is a requested UI redesign (Adjustment only). Existing gameplay CRT values, Input Actions, physics, next/title scene settings and prefab GUIDs were preserved. Targeted runtime checks passed. Final visual approval and physical-controller evaluation remain with the user.

## Seamless transition and goal polish

The full-screen static, extra RGB kick and large tearing band were removed. Entry retains the actual play image, then blends into the pause treatment over 0.22 seconds. Jitter shifts by up to 2.4 render pixels in three diminishing bursts over 0.16 seconds, refreshing at 60 Hz. Fine two-row chatter (40 percent) and wider twelve-row bands (60 percent) make the horizontal breakup visible without adding brightness. Menu content fades in during the same motion. Resume reverses the same curve, mask, blur/dim and content fade, with the same brief row jitter. Gameplay remains frozen until dismissal finishes; pause BGM unmuffles alongside the exit. Closing during entry reverses from the current progress. The approved final CRT size (1.16), curve, palette, blur/dim profile and menu flow remain intact.

Goal logo: starts at 0.08 seconds; larger final size 1.12; pop 0.34 seconds with a small 12-percent overshoot and 3.5-percent stretch; readable hold 1.25 seconds; fade 0.24 seconds. The hit flash is capped at 0.38 opacity and clears in 0.075 seconds. Pixel sparkles burst near the logo's scale peak, stop rotating after 0.34 seconds, then fade into a restrained afterglow. The original image, slow motion (0.18 / 0.24 seconds) and Goal BGM are preserved; the post-logo beat is now 1.1 seconds.

Updated: CRTOverlayController, CRTFilterController, CRTFilter.shader, CrystalAcquiredUI, CRTOverlayInstaller, GoalCelebrationLogoSetup, PauseMenu / GoalMenu / CrystalAcquiredUI prefabs, and this document. No image file, Scene, physics, input or BGM setting was changed by this refinement. No new asset file is required.

Goal-polish validation passed previously: the original larger logo settles without continuous wobble; sparkle rotation stops after the burst; the 0.45-second post-logo beat and Goal BGM are preserved. That refinement saved three prefabs without editing or saving Scene files.

Latest tempo/resume revision: CRTOverlayController, PauseMenuController, CRTFilter.shader, PauseMenu / GoalMenu prefabs, and this document. Unity C# / shader compilation and serialized-reference checks passed. Targeted Play Mode checks passed for continuous unscaled entry/exit, brief jitter settling, frozen gameplay until exit completion, repeat-input rejection, early reversal, non-default prior time-scale restoration, snapshot release, disable during exit, goal takeover without a stale resume callback, result retry and pause retry. No runtime Console Error/Exception during the run. Two prefabs saved; no Scene files edited or saved; the clean original TitleScene editor setup was restored. Final transition tempo / jitter feel and physical-controller acceptance remain with the user.

Latest horizontal-jitter / goal-beat adjustment: transition strength 0.65 -> 2.4 render pixels, duration 0.12 -> 0.16 seconds, adding twelve-row horizontal band offsets to the fine chatter. Post-logo delay 0.45 -> 1.1 seconds; the logo sequence itself remains as approved. Changed CRTOverlayController, CRTFilter.shader, CrystalAcquiredUI, GoalCelebrationLogoSetup, the matching goal-test wait (3.6 seconds), PauseMenu / GoalMenu / CrystalAcquiredUI prefabs, and this document. Three prefabs saved through Unity MCP. This is parameter / shader tuning only; transition ownership and scene flow code were not changed. Initial C# / shader compilation and saved profile checks passed. After the test-wait adjustment, final assembly reload / Console verification could not complete: Unity logged failures saving its CanvasesSaveData.asset preferences and MCP calls timed out. No Scene files were edited or saved; TitleScene was clean when the prefabs were saved. Final visual feel is left to the user's Play Mode check; the earlier lifecycle checks were not repeated.

## Goal break: input lock and eased stop

The final crystal hit calls the existing celebration entry point in the same frame. Player movement, jump callbacks, keyboard/gamepad polling and weapon input are blocked immediately; pending jump/fire commands are cleared. Existing ball/player physics and Morning Star state transitions are retained while global time falls as `startingScale * (1 - progress)^3` over `Goal Stop Duration` (0.24 unscaled seconds). Earlier combat hit stop is cancelled before this acquisition, and new combat hit stop cannot override the goal time.

The clock stays at zero through the logo and post-logo beat (the user's current 0.6-second setting was preserved). Logo, sparkles, CRT and Goal BGM continue independently. The result takes over the frozen game and input without a frame of resumed gameplay. Cancelling the celebration before handoff restores the prior input/time state and drops its callback; result teardown/retry releases the goal input gate.

Changed: `Assets/Script/Player.cs`, `Assets/Script/MorningStarLauncher.cs`, `Assets/Script/UI/CrystalAcquiredUI.cs`, `Assets/Script/GoalMenuController.cs`, `Assets/Editor/GoalCelebrationLogoSetup.cs`, `Assets/Prefabs/UI/CrystalAcquiredUI.prefab`, this document. `FormerlySerializedAs` preserves the former duration value as `goalStopDuration`; obsolete `slowMotionScale` is replaced by a full stop. No new Script, Material, Shader or metadata asset was required. The prefab was saved through Unity MCP; Scene files were not edited or saved.

C# compilation and targeted Play Mode checks passed: first two hits retain normal control; final break locks held input and queued actions immediately; time decreases through intermediate speeds to zero; player/ball/game clock stay frozen; UI and Goal BGM advance; the configured beat and CRT handoff retain the freeze; retry restores controls/time; disable during celebration restores the original 0.65 time scale without a late callback; ordinary pause/resume still works. No runtime Console Error/Exception during this run. Final deceleration feel remains for the user's evaluation.
