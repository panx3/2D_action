# バネガエル

`Assets/Prefabs/Enemies/SpringFrog.prefab` を足場に配置してください。初期方向は右で、Playerの位置には反応しません。Sceneへの自動配置は行いません。

## 初期設定

| Inspector | 値 |
| --- | --- |
| Jump Force | 3.2（上向きImpulse） |
| Move Speed | 1.5 units/s |
| Jump Interval | 着地後0.12秒 |
| Initial Direction | Right |
| Ground Layer | Default / Walls（既存レンチカニと同じmask 65） |
| Ground Check Distance | 0.08 |
| Edge Check Depth | 0.18 |
| Wall Check Distance | 0.08 |
| Animation Speed | 12（踏切・着地コマ/秒） |
| Contact Damage | 1 |
| Contact Knockback / Cooldown | 5 / 0.7秒 |
| Max HP | 1（EnemyHealth） |
| Audio Source | 各個体の専用AudioSource |
| Jump SE | TrampolineBounce.wav |
| Jump SE Volume | 0.65 |

Rigidbody2Dは既存レンチカニのDynamic / Mass 1 / Gravity Scale 1 / 回転固定 / Continuousを継承します。現在の重力では高さ約0.52 units、水平移動約0.98 unitsの小ジャンプです。RootのScaleは1、Visualは4倍で、元画像の1ドットを均等に拡大します。

GroundCheckは足元、EdgeCheckとWallCheckは右向きの位置を編集します。左向き時にはコードが判定位置を反転します。地形ColliderはGround Layerの対象である必要があります。

## 動作と既存システムとの接続

- 接地後の間隔を経て、水平速度と上向きImpulseを一度だけ与えます。下降は重力で処理し、空中で方向転換しません。空中の壁接触では水平移動を止め、着地後に次の安全な方向を選びます。
- 踏切前に壁、進路の足場、ジャンプの予測移動距離を確認します。両方向とも危険な狭い足場では垂直に跳ね、安全を優先します。移動床や実行中の地形消滅までは予測しません。
- 既存のバネガエル画像の6個のSpriteを左から順に使用します。踏切・上昇・頂点・下降・着地を実際の物理状態に合わせてSpriteRendererで切り替えます。Animatorは不要です。Transformの上下アニメーションや変形、素材の再生成は行いません。
- 身体のCollisionEnter/StayからPlayerColliderUtilityでPlayerを判別し、PlayerHealth.TakeDamageを呼びます。既存の無敵・被ダメージ演出・リスポーン保護を利用します。
- EnemyHealthが既存のIMorningStarHitReceiverを提供します。HP1のため有効な攻撃1回で死亡します。disableOnDeathでSpringFrogを停止し、既存処理でCollider・Rigidbody・表示を無効化します。命中音とEnemyFragmentの飛散も既存処理を利用します。
- ジャンプ開始時だけ各個体のAudioSource.PlayOneShotを呼びます。Jump SEとJump SE VolumeはPrefabまたは各Instanceで差し替え可能です。Play On AwakeとLoopは無効です。

## 検証

`Tools > Spring Frog > Validate Prefab` で参照を確認できます。`Run Play Mode Validation` は一時Scene内の3体で左右移動、反復ジャンプ、壁・崖の折り返し、6コマ、AudioSource再生を確認し、身体接触によるダメージ・無敵時間・有効な攻撃1回での死亡も検証します。一時Sceneは保存しません。結果はConsoleの `[SpringFrogTest]`、または `SpringFrogPlayModeTest.GetResult()` で確認できます。

実ステージの配置、実際の右スティック攻撃、聴感上の音量とジャンプの手触りは手動Play Mode確認が必要です。

2026-10-22、Unity 6000.4.2f1でコンパイル成功。上記Play Mode検証はPASS（3体、8秒、検証中のError/Exception 0）。検証終了後はTitleSceneに戻り、Sceneのdirty変更なしを確認しました。Player側、既存敵Prefab、Scene、Project Settingsは変更していません。

検証は既存の作業中変更を保持した状態で行いました。既存のステージ済み`TapiocaHeal.cs`削除は本変更のcommit対象外です。HEADに残る同スクリプト55行目は存在しない`SegmentHpBarUI.PlayHealFlash`を参照するため、その削除を含めずにクリーンチェックアウトすると既存のCS1061が発生します。別作業の削除内容は変更していません。

物理APIの確認資料: Unity 6.4 [Rigidbody2D.linearVelocity](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Rigidbody2D-linearVelocity.html)、[PhysicsScene2D.Raycast](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/PhysicsScene2D.Raycast.html)。
