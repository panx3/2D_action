# ピンクジュエル収集

CompletSceneの任意収集アイテム。取得順にかかわらず、配置番号1〜3がHUDとリザルトの同じ枠に対応する。数字・取得通知・操作停止・カメラ移動は追加していない。

## 素材

- 本体: `Assets/Art/PinkJewel/PinkJewel.png`。ユーザー提供PNGをバイト単位でそのままコピー。SHA256: `89DCD41479D243D429FA34C0E4F2D91AEC3EA3DCC3BD6C9C2102F4F54EE7A91B`。
- 実使用UI: `Assets/Art/PinkJewel/JewelUI.png`。ユーザー提供の `DeUI用crystal取得HUD素材t.png` を無加工でコピーし、左のピンクを取得済み、右の白を未取得としてSprite Editorで分割。どちらも18×29pxで、HUDとリザルトで同じSpriteを使用する。
- UI表示サイズは54×87（元画像の3倍）。HUDのアイコン間は12 UI px（左端固定で右へ拡張）、リザルトは6 UI px。以前生成したHUD／リザルト画像は参照しない。
- 生成原本: `Assets/Art/PinkJewel/Source/`。VFXは引き続き使用する。初版で生成したHUD／リザルト原本も保管されている。プロンプトは [PinkJewelImagePrompts.md](PinkJewelImagePrompts.md)。
- 実使用VFX: `RadianceStars.png`, `RadianceDiamonds.png`（各4コマ）、`RadianceGlint.png`, `Ring.png`, `Halo.png`, `Flash.png`。生成アトラスをUnity側で分割・縮小。すべて別レイヤーで、本体画像への焼き込みはない。
- 取得SE: `Assets/Audio/SFX/Items/コインGET.mp3`。ユーザー提供MP3を無加工コピー。取得確定後の1回だけ、既存 `OneShotAudioUtility` / `WorldImpactSfx` から2D再生する。本体非表示で音は途切れない。
- SpriteはPoint／非圧縮／Mip Mapなし／Clamp。地面の影だけは既存回復アイテムの `Assets/Generated/Tapioca/GroundShadow.png` を共用する。

## PrefabとScene配置

- `Assets/Prefabs/Items/PinkJewel.prefab`: 複製し、PinkJewelPickupのSlotを1〜3に設定する。番号は重複させない。
- `Assets/Prefabs/Items/PinkJewelPickupVfx.prefab`: 取得本体から独立して再生・消滅するVFX。
- `Assets/Prefabs/UI/PinkJewelHUD.prefab`: 既存HPバーのFrame配下。左下アンカー、左上Pivot、位置(24,-50)、幅174・高さ87。HPバー下で左揃えにし、既存HPバーの移動とCanvasScalerを継承する。
- `Assets/Prefabs/UI/PinkJewelResult.prefab`: GoalMenuの `CRTPresentation/PhosphorUI/SafeArea` 配下。中央アンカー、位置(0,62)、幅174・高さ87。既存のCRT表示と非スケール時間を利用する。「CRYSTAL ACQUIRED」の副題を外し、収集結果は3つの記号だけで表示する。
- Sceneの `PinkJewels` が収集状態を所有する。子の3つは同一Prefabのインスタンス。

| 配置番号 | 初期World位置 | 意図 |
|---|---|---|
| 1 | (18, 1.7, 0) | 序盤の跳躍で狙う |
| 2 | (68, 3.2, 0) | MagnetPoint付近の空中ルート |
| 3 | (168, 13.1, 0) | JumpPad後の上昇区間 |

配置は初期案。実際の遊びやすさ・探索性はユーザーがPlay Modeで調整する。

## Inspector初期値

| 対象 | パラメータ | 初期値 |
|---|---|---|
| PinkJewelPickup | Slot | Prefab=1、Scene=1/2/3 |
| 同上 | Float Amplitude / Period | 0.125 world units（32PPUで4px）/ 1.8秒 |
| 同上 | Glow Intensity / Particle Rate | 0.4 / 合計8個毎秒 |
| 同上 | Reflection Period | 2.6秒を基準にランダム化 |
| 同上 | Shine Period / Duration / Intensity | 3.2秒 / 0.65秒 / 0.85 |
| 同上 | Sound Volume | 0.7（既存WorldImpactSfxの音量を継承） |
| Visual | Local Scale | (0.85, 0.85, 0.85)。本体と待機VFXを15％縮小、取得判定は維持 |
| 同上 | Shadow Opacity / Probe Distance | 0.52 / 8 units |
| 同上 | Pickup Duration / Burst Count | 1.0秒 / 28個 |
| 同上 | Burst Speed / Flash Intensity | 2.5 / 0.85 |
| 同上 | BoxCollider2D（Trigger） | 幅1.55、高さ2.25 |
| PinkJewelIconRow | Highlight Duration / Scale | 0.36秒 / 1.12倍 |

粒子の色・大きさ・寿命・放出形状、光のSpriteや色は各PrefabのParticleSystem／SpriteRendererでも変更できる。既存のHP・回復・入力・鉄球物理・クリア画面の設定値は変更していない。

## 取得状態と進行の連携

`PinkJewelCollection` が3ビットの挑戦内状態を保持する。PlayerPrefsや永続保存は使用しない。Player本体の既存Collider、PlayerHealth、生存状態、復帰中ではないことを確認する。Player配下の装備Colliderも取得対象から除外する。

- チェックポイントと通常の奈落復帰: 同じScene内でプレイヤーを復元する既存構造なので、収集状態には触れず保持。
- GameOver／クリア／Pauseからのリトライ: 新しいSceneインスタンスのAwakeで全リセット。各進行コントローラーの変更は不要。
- タイトルから新規開始／次ステージ: 同様に全リセット。
- クリア: 既存GoalMenuControllerのクリア時刻確定を参照してスナップショット化。それ以後は取得を拒否し、結果確認中は保持。
- 将来通常復帰をScene再読込へ変更する場合: 再読込の直前に `PinkJewelCollection.PrepareForRespawnReload()` を呼ぶ。その1回の同一ステージ再読込だけ取得状態を引き継ぐ。Retry／Title／Nextからは呼ばない。

## ファイルと確認方法

新規Runtimeは `Assets/Script/Items/PinkJewelCollection.cs`, `PinkJewelPickup.cs`, `PinkJewelPickupVfx.cs` と `Assets/Script/UI/PinkJewelIconRow.cs`。既存Runtimeは変更していない。

セットアップは `Assets/Editor/PinkJewelSetup.cs`。既存の配置や設定を上書きせず、未作成のアセットだけ作成する。明示的なUI再生成は Tools > Tekkyu > Pink Jewels > Rebuild Generated UI Icons。

今回の装飾用セットアップは `Assets/Editor/PinkJewelSetup.Radiance.cs`。Tools > Tekkyu > Pink Jewels > Apply Radiance Artworkで、生成VFX、Visualの85％スケール、表面反射レイヤー、取得SEをPrefabへ反映する。光量・放出量を旧初期値から調整済みの場合はその値を保持する。このメニューは見た目を再設定するため、Prefabで独自に変更した粒子サイズなどは再適用される。配置済みSceneは保存しない。

`Assets/PinkJewelShine.shader` は原画のアルファと明るさをマスクに使い、別SpriteRendererへドット単位の斜め反射を加算する。暗い金属の輪郭を残し、3つの配置が同時点滅しないよう開始位相をずらす。ゲーム時間を使うためポーズ中は浮遊・反射・粒子も停止する。

統合チェックは `Assets/Editor/PinkJewelPlayModeChecks.cs`。Tools > Tekkyu > Pink Jewels > Verify Play Modeで明示実行する。現在のCompletSceneと既存GameOverフローを対象に、接触対象、取得順、重複、VFX独立性、HUD、チェックポイント、通常復帰、結果固定、実際のクリアRetry、通常復帰用Scene再読込、HP0後のRetry、タイトル経由新規開始を確認する。結果とキャプチャはOS一時フォルダーの `TekkyuPinkJewelChecks/`。

最終的な配置の到達性、ゲームパッド実操作での取りやすさ、演出の明るさ・豪華さ、アスペクト比変更時の見た目はユーザー確認項目。

## 今回の検証結果

2026-10-23装飾・SE追加: コンパイル成功。Edit Modeの一時Prefabで85％スケール、元画像参照、取得判定サイズ維持、4コマ粒子、表面反射の表示／待機／deltaTime=0での停止、取得時のVisual非表示・粒子消去を確認。Unityの実描画でも反射の変化を確認した。3つの配置済みインスタンスへ新レイヤーとSE（2.06秒・音量0.7）が伝播し、既存WorldImpactSfxは音量1・Muteなし。新規Console Error／Exceptionなし。原画PNGと指定MP3のコピー元・先のSHA256一致を確認。Prefab2つを保存し、CompletSceneは元からの未保存状態を保持した。プレビューは [PinkJewelRadiancePreview.png](PinkJewelRadiancePreview.png)（表面反射が最も明るい瞬間の単体描画）。Play Modeでの実際の音量・輝き・プレイ感の最終評価は未実施。

2026-10-22初版: コンパイル成功。上記のPlay Mode統合チェックは全項目PASS、実行中のError／Exceptionは0。4つのPrefab、取得用参照、SpriteのPoint／非圧縮／Mip Mapなし、粒子の速度カーブ形式も確認済み。収集処理の初回検証結果は [PinkJewelValidation.txt](PinkJewelValidation.txt)。

同日のUI修正: ユーザー提供素材へ差し替え、HUDを拡大・左揃え・狭い間隔に変更し、クリア画面の副題を除去。コンパイル、Sprite参照、RectTransformを確認済み。Consoleの新規エラーはGame View画像取得に失敗した撮影ツール由来のみで、コード由来のError／Exceptionはなし。収集処理のPlay Mode回帰チェックは再実行せず、最終的な見た目はユーザー確認とする。`PinkJewelResultPreview.png` は初版の旧レイアウトを記録した画像。

収集・回復・HPバーの挙動は変更していない。GoalMenuControllerではクリア時の副題だけを空にする。PrefabとCompletSceneは今回のUI差分のみを反映し、既存の未コミット作業を保持する。

検証後の画像保存時にCドライブの空き容量不足が発生したため、最終プレビューはDドライブの本リポジトリへ保存した。空き容量不足は開発環境側の残確認事項。
