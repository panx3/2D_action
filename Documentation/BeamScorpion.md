# ビームサソリ

## 配置

`Assets/Prefabs/Enemies/BeamScorpion.prefab` をSceneへドラッグしてください。
Root原点が足元です。平らな地面の上面より約0.06だけ上に置けば、開始時に着地します。
既存の `EnemyPlacementTools` はサソリを対象にしていないため、配置には使用しません。
本番の `CompletScene` は元から未保存だったため、今回の配置・保存は行っていません。
Prefab内の参照、Layer、AudioSource、Animatorは設定済みです。

## 動作と既存システム

- ゆっくり左右巡回。胴体ColliderのCastで接地・壁を、予測移動先からの下向きRaycastで足場端を確認します。
- 前方7以内にPlayerが見える場合、巡回時間が3秒を超えると停止して攻撃します。追尾せず、予備動作開始時の向きを保持します。
- 予備動作0.65秒 → ビーム0.55秒 → 硬直0.8秒 → 巡回。次の攻撃には巡回時間3秒が必要です。
- `EnemyHealth` がHP1、Morning Starの `IMorningStarHitReceiver`、撃破破片、被弾SE、ヒットストップ、復帰を担当します。
- ビーム／本体接触は `PlayerColliderUtility.IsPlayerBody` を通し、`PlayerHealth.TakeDamage` を使用します。既存の無敵・死亡・復帰中は追加ダメージを与えません。
- 本体接触とビームは同じ0.7秒のダメージ間隔を共有します。撃破／無効化時にビーム・チャージ・移動・接触攻撃を停止します。
- `GroundProjectedShadow`、既存の影画像、敵の物理Material、`OneShotAudioUtility` を再利用しています。

## スプライトとAnimator

既存画像の画素・切り出し範囲・GUID・Sprite IDはそのままです。

- `Assets/image_/ビームサソリ_wolk.png`: 登録済み4コマ、100 PPU、5コマ/秒。
- `Assets/image_/ビームサソリ_attack.png`: 登録済み6コマ、50 PPU。
- 両方Pointフィルタ、圧縮なし、MipMapなし。Pivotを各コマの下中央に揃えています。
- 元画像の大きさの差をPPUで補正し、歩行と攻撃の表示サイズを合わせています。
- `Visual` 子のAnimatorはSprite差し替えだけを行い、Root・Colliderを変形しません。
- 攻撃0/1コマを構え、2/3コマを発射、4/5コマを戻しに使用。発射口は各コマのレンズ中心に追従して左右反転します。

## ビームとVFX

最大長8、太さ0.14、継続0.55秒、色RGBA `(1, 0.08, 0.32, 1)`、ダメージ1。
固定方向のRaycastで最も近いPlayer本体または非Triggerの環境Colliderまで伸びます。
壁が先なら壁で停止します。判定は中心線で、外周光は演出です。

`Assets/Prefabs/Enemies/BeamScorpionVFX/` の6つのPrefabをControllerから差し替えできます。

| Prefab | 用途 |
|---|---|
| Charge | 予備動作中のしっぽ先端の収束光 |
| Muzzle | 発射開始時の短いフラッシュ |
| Beam | 外周光・主線・白寄りのコア・上下の細い副線 |
| PlayerHit | Playerとの交点の火花 |
| EnvironmentHit | 壁／地面との交点の火花。命中中は0.1秒ごとに更新 |
| Afterglow | 発射終了後0.14秒の残光 |

仮素材は `Assets/Generated/BeamScorpion/BeamPixel.png` の1px白画像です。
各Prefabの子SpriteRendererを画像へ差し替えできます。Beamは `BeamScorpionBeamVfx`、
フラッシュ／火花は `BeamScorpionBurstVfx` のComponentと参照を維持してください。
色はControllerの `Beam Color` から伝播し、判定処理はVFXに含めていません。

## Inspector

| 設定 | 初期値 |
|---|---|
| Move Speed / Start Facing Right | 0.8 / OFF（左向き） |
| Turn Delay / Probe Distance | 0.15秒 / 0.12 |
| Attack Interval / Attack Cooldown | 3秒 / 0.8秒 |
| Windup Time / Detection Range | 0.65秒 / 7 |
| Beam Duration / Length / Thickness | 0.55秒 / 8 / 0.14 |
| Beam Color | 赤ピンク `(1, 0.08, 0.32, 1)` |
| Beam Damage / Contact Damage | 1 / 1 |
| Damage Interval / Knockback | 0.7秒 / 5 |
| Ground Layer / Obstacle Layer | Default + Walls |
| Player Layer | player |
| Beam SE Volume | 0.55 |

`Beam Origin`、Visual Renderer／Animator、攻撃コマと発射口オフセット、Audio Source、
Beam Fire SE／Beam Hit SE、6種のVFX PrefabもInspectorから調整できます。
GroundCheck／EdgeCheck／WallCheckはPrefab内の確認用マーカーです。安全判定には実際の胴体Colliderの境界を使用します。
既存の他の敵・Player・本番Sceneの調整値は変更していません。

## 仮SE

- 発射: `Assets/Audio/SFX/tekkyu_launch.wav`
- 着弾: `Assets/Audio/SFX/Imported/打撃6.mp3`（発射より抑えた音量、連続再生を制限）
- AudioSourceは2D、Play On Awakeなし、Loopなし。現在のWorldImpactSfxと同じMixer Groupに接続します。
- 専用音への差し替えはControllerのSEスロットで行います。

## 作成・変更ファイル

- Runtime: `Assets/Script/BeamScorpionController.cs`、`BeamScorpionBeamVfx.cs`、`BeamScorpionBurstVfx.cs`。
- Editor: `Assets/Editor/BeamScorpionSetup.cs`、`BeamScorpionPlayModeChecks.cs`。
- 本体Prefab: `Assets/Prefabs/Enemies/BeamScorpion.prefab`。
- VFX: `Assets/Prefabs/Enemies/BeamScorpionVFX/` 内の上記6Prefab。
- Animator: `Assets/Animation/BeamScorpion/BeamScorpion.controller`、`Patrol.anim`、`Windup.anim`、`Firing.anim`、`Recovery.anim`。
- 仮画像: `Assets/Generated/BeamScorpion/BeamPixel.png`。
- 上記新規アセットの `.meta` とフォルダ `.meta`。
- 既存2画像の `.meta`（Importer設定とPivotのみ）。
- 本書 `Documentation/BeamScorpion.md`。

`Tools > Beam Scorpion > Create Prefab` は新規作成専用です。既存Prefabの調整値を上書きしません。
`Validate Prefab` で参照、HP、Importer、死亡時の無効化設定を検証できます。

## 検証

- Unity 6000.4.2f1でコンパイル成功。
- `Tools > Beam Scorpion > Run Play Mode Checks`: **PASS (24), runtimeErrors=0**。
- 3体の巡回、壁／足場端での反転、歩行コマ、攻撃中停止、攻撃状態遷移、左右のビーム・発射口・Player着弾VFX・発射SE呼び出しを確認。
- PlayerのHP減少と無敵、本体接触、壁遮蔽、環境着弾VFX、1回のMorning Star攻撃による撃破、撃破後の攻撃停止、ResetHealth復帰を確認。
- 長さ・太さ・色の変更反映も確認。
- 一時Preview Sceneで発射ポーズとビーム表示を確認。本番Sceneの未保存状態は維持。
- 本番ステージへの配置後の難易度、ゲームパッド実操作、最終的な見た目・効果音の聴感はユーザー確認が必要です。

新たな必須設定はありません。残る作業は本番への配置と、必要に応じた演出・SE・難易度の調整です。
