# プレイヤーモーション実装記録

以下は初回82コマ組み込み時の記録。**現在は[揺れ・サイズ・つながりの改修](../CalmRevision/README.md)を適用した78コマ版**。ぶら下がりは初回から変更していない。

ブランチ: `codex/player-motion-unification`（main `3257ea5` から分岐）。2026-10-31。

[ゲーム用Spriteの再生・コマ送り・Idle比較](preview.html)

Idleの画像・アニメーションを基準として残し、ほかの動作を82コマへ展開して組み込んだ。機能追加・表示調整であり、移動性能を調整する変更ではない。既存のAnimator・Clip・MorningStarLauncher・鎖プールを利用している。

## 素材と見た目

| 動作 | コマ数 | 内容 |
|---|---:|---|
| 引きずり走り | 8 | 前傾・後ろの柄、脚の踏み替え、髪と服の遅れ |
| ジャンプ | 8 | 上昇2・頂点1・降下2・着地3。降下でツインテールと服を大きく広げる |
| チャージ | 24 | 静止・走行・空中それぞれ8。鉄球の回転位相で選択 |
| 発射 | 30 | 5角度×3コマ×地上／空中。左右反転で8方向 |
| 回収・張力 | 6 | 回収2・地上踏ん張り2・空中で引かれる姿勢2 |
| 吊り下がり | 6 | ぶら下がり2・中央の鎖登り4 |

生成原画は `../ProductionSources/`、ゲーム用は `Assets/Art/Player/MotionV2/`。built-in image_genを使用し、プロンプトと採否は [production-prompts.json](../production-prompts.json)、代表案の履歴は [prompts.json](../prompts.json) に保存した。

原画からキャラクターを切り出し、原寸のドット密度へ最近傍で整理。Idleの先頭コマに実在する上位96色へ統一した。セルは192×192、32 PPU、Point、非圧縮、MipMapなし、NPOT縮小なし。シートは最大768×384なので2048上限による縮小は発生しない。足元は各セルの上端から159pxを基準とし、空中姿勢と走行の上下動にだけ明示した差をつける。

## 実装と接続点

- `PlayerMotionSet` にSprite、コマ時間、柄の鎖接続点、チャージ用の別の握りをまとめる。Sceneは共有アセットを直接参照し、既存Prefabからの生成時はAwakeでResourcesから一度だけ読み込む。
- 通常移動は既存5本のClipのSprite曲線を更新。GUID、Animatorパラメーター、既存のTransform曲線と保持時間は維持。
- 特殊表示は既存Launcherのpartialへ統合。優先順位は登り→吊り下がり→チャージ→回収→投擲後の空中張力→投擲→停止中の踏ん張り→頂点→Animator。通常ジャンプ中に後ろの鉄球から引かれても、降下絵が投擲後の姿勢へ化けないようにしている。
- Spriteの表示用接続点はコマごとに変更。**既存HandAnchor／通常の物理支点の値は変更していない。** 鎖登りは高い方の握りを既存の上向き物理アンカーへ合わせ、一本の中央の鎖を握る。
- 発射角の45度選択はSpriteだけ。実際の照準・発射方向は連続値をそのまま使う。
- 張力表示は既存Force適用結果 `LastAppliedPlayerTension` の読み取りのみ。Forceを追加していない。
- チャージは後ろの手で柄、前の手で鉄球側の鎖を握る。前の握りの位置を回転軸として、既存の鉄球Rigidbodyを既存のFixedUpdateから動かす。追加の鉄球画像／物理オブジェクトはない。
- チャージ経路は柄先端→たるむ鎖→前の握り→鉄球。リンク表示と補助LineRendererで同じ14点の経路を使う。奥側の球・鎖・白いエフェクトをキャラクターの背後へ回す。
- Sprite辞書、描画経路List、鎖プールを再利用。毎フレームのResources読み込みや、この変更によるフレームごとの新規オブジェクト生成は行わない。

追加Inspector参照: `MorningStarLauncher.playerMotionSet`。
共有アセット: `Assets/Resources/PlayerMotion/PlayerMotionV2.asset`。

| チャージ専用の追加値 | 実効値 |
|---|---:|
| `chargeOrbitRadius` | 1.25 world units |
| `chargeEntryTime` | 0.12 sec |
| `chargeSlackDepth` | 最大0.9 world units |

既存 `spinRadius=1` は保存値を変更せず、共有モーションセットを使用するチャージ中だけ専用半径を使う。既存回転速度720度/秒、最大チャージ1.2秒、攻撃倍率、解除後の共通Rest設定を維持。

## 保持したゲーム設定

変更前を [baseline.txt](baseline.txt) に記録。Stage2の主要値: 移動Force70、地上Drag8、チャージ移動倍率0.65、空中係数0.1、空中Drag1.5、ジャンプ8、降下重力倍率4、ジャンプカット2、最大落下速度-50、最大鎖長5、張力開始比0.82、張力強度42、張力減衰5、最大張力65、発射速度30。これらのScene値とPlayer.csは変更していない。実際の発射速度上限20も既存の処理を使用。

## 検証

- Unityのコンパイル完了、関連Console Error／Exceptionなし。
- [素材検証](asset-validation.txt): 82コマ・方向別参照・取り込み設定を検査。
- [状態検証](runtime-validation.txt): 8位相のチャージ軸／鎖経路、地上・空中8方向のSprite、吊り下がり、登り4コマの中央握り、回収・解除後の同一Rest物理を確認。
- [チャージ終了検証](charge-transition-validation.txt): 地上／空中×左右、反転した回転軸と半径、奥側描画、Respawn後の物理・描画順・専用経路解除。チャージ→既存の発射前回収→Thrown→Respawnを確認。
- [Stage1入力確認](Stage1-input-exercise.txt)・[Stage2入力確認](Stage2-input-exercise.txt): Unity Input Systemの仮想Gamepadを使い、実際に走行・上昇・降下・着地まで進めて撮影。入力マッピングを変更せずに検査。
- [発射速度検証](throw-velocity-validation.txt): 8方向＋任意角度 `(0.37,0.91)` の発射が既存の速度計算と一致。Sprite選択による弾道の角度スナップなし。
- [継続回転](live-charge.txt): 実Playを50フレーム進め、既存球が専用軸の周囲を動くことを確認。
- Git差分ではScene3つに共有参照を各1行追加しただけ。Idle、Player.cs、Prefab、Packages、ProjectSettingsの変更なし。
- [最終監査](final-audit.txt): 82コマすべてのセル周囲に透明余白があり、髪・武器がセル境界で切れていない。Stage2の未保存変更なし、Play停止、コンパイル完了を確認。

画像: [Stage1降下](Stage1-fall.png) / [Stage2走行](Stage2-run.png) / [上昇](Stage2-rise.png) / [降下](Stage2-fall.png) / [着地](Stage2-land.png) / [チャージ全景](Stage2-charge.png) / [チャージ拡大](Stage2-charge-close.png)。これらは実行を進めたゲームカメラ画像。

[斜め上発射](Stage2-throw-up-right.png) / [ぶら下がり](Stage2-hanging.png) / [中央の鎖登り](Stage2-climb.png) は、接続位置を調べるためPlayを一時停止して状態を用意したゲーム内表示資料。入力からその場面まで通して操作した記録とは区別する。

## 変更ファイル

- 新規Runtime: `Assets/Script/PlayerMotionSet.cs`、`MorningStarLauncher.Motion.cs` と各 `.meta`。
- 更新Runtime: `MorningStarLauncher.cs`、`ChainConstraint2D.cs`、`ChainLinkVisualController.cs`、`ChainLineController.cs`、`MorningStarChargeFeedback.cs`。
- 新規Editor: `Assets/Editor/PlayerMotionV2Setup.cs`、`PlayerMotionV2Validation.cs`、`PlayerMotionV2PlayExercise.cs` と各 `.meta`。
- 更新Editor: `PlayerJumpAnimationSetup.cs`、`GameplayFeatureIntegrator.cs`。
- 更新Clip: `Assets/Animation/Walk.anim`、`JumpUp.anim`、`JumpFall.anim`、`JumpLand.anim`、`MorningStarLaunch.anim`。
- 新規素材: `Assets/Art/Player/MotionV2/` の13 PNGとSprite分割情報を含む `.meta`、共有 `PlayerMotionV2.asset` と `.meta`。
- 更新Scene: `Assets/Scenes/Stage1.unity`、`Stage2.unity`、`CompletScene.unity`（各1行の参照追加）。
- 制作・検証資料: `Docs/Art/PlayerMotionV2/`。

## Sceneと残る確認

Stage1・Stage2・CompletSceneはUnity MCP経由で共有モーション参照を設定・保存済み。ユーザーの調整値は保持し、Prefabの保存変更は行っていない。終了時は元のStage2を開き、Play停止・未保存Scene変更なしに戻した。

実機Gamepadでの最終的な操作感、長時間の通しプレイ、壁際・床際であらゆる組み合わせを使った見た目、CompletSceneの通しプレイ、Playerビルドは未実施。最終的なポーズの好みと動き心地はユーザー確認。作業は未コミット・未push。
