# Stage2 落下プレス機

`Assets/Prefabs/Environment/Stage2/FallingPress/FallingPress.prefab` を1つ置けば動作する。鉄球マーク入りの `PressBallStand.prefab` が中央にネストされている。Stage2 の `Stage2FallingPress/FallingPress`（床位置 x=88, y=1.23808）に1組配置済み。

## 設計・使い方

- ルートを床に置く。回転0、Scale1で使用する。動くのは `MovingHead` のKinematic Rigidbody2Dだけ。
- 台座は奥側の鉄球専用設備。Playerは手前を走るため台座との衝突を除外する。台座のColliderは鉄球Layer7のみ、プレス本体のColliderはPlayer Layer3のみ。ProjectSettingsや既存のLayer設定は変更しない。
- 鉄球は `MorningStarLauncher.BallBody` から識別。台座中央±0.6、底面が支持面±0.12、上下速度1.2以下、Dragging/Dropping/Thrown のときだけ有効。回収・充填・Swing・登場・復帰中は支点にしない。
- プレスは鉄球の上端+0.04で停止し、床から3.5以上のクリアランスを確認する。台座高1.9+実鉄球直径1.7で、標準停止高は約3.64。実PlayerのCollider高3.3を確保する。
- 支点が外れると下降再開。支点が残っていれば下待機後に上昇し、次サイクルでも停止する。鉄球の質量・速度・重力・Stateへの書き込みはない。台座以外の鉄球は停止させないため、プレスの表示と重なる場合がある。
- 既存 `PlayerHealth.TakeDamage(1, knockback)` を使用。移動区間全体のOverlapBoxで高速下降も検知し、無敵・死亡・復帰は既存HP処理に任せる。
- 各プレスは1つの台座を所有する。Prefabを複製すれば内部参照も独立。別置きの `PressBallStand` を使う場合は、元の子台座を無効化してControllerの `Ball Stand` に別台座を割り当てる。
- 鉄球停止時はシアンの表示、火花、金属SE。通常着地は衝撃SE、火花、既存CameraShake2Dによる小さな振動。警告は赤/黄の点滅。

## Inspector初期値

| 項目 | 値 |
|---|---:|
| Width / Body Height | 7 / 2 |
| Travel Distance | 6 |
| Descent / Ascent Speed | 12 / 2.5 |
| Top Wait / Bottom Wait | 2 / 1.8秒 |
| Warning Time | 0.7秒 |
| Start Phase | 0（0–1のサイクル比率） |
| Ball Stop Enabled | true |
| Required Player Clearance | 3.5 |
| Contact Skin | 0.04 |
| Damage / Knockback | 1 / 横7・縦0 |
| 台座高さ / 支持Collider幅 | 1.9 / 3.7 |
| Placement Half Width | 0.6 |
| Surface Tolerance | 0.12 |
| Maximum Vertical Speed | 1.2 |

幅を大きくする場合は既存の鎖の長さとの関係を確認する。横幅7は基本鎖長5のまま中央の鉄球を残して通過できる設定。既存Player/鉄球のInspector調整値は変更していない。

## 素材

imagegen組み込みツールで新規生成した画像を切り出し、最近傍縮小と二値Alpha化のみ行った。元画像はCodex生成先に保持。使用Spriteはすべてこのフォルダに実体を保存している。

- `PressBody.png`: 252×82、36 PPU、下面中央Pivot。
- `BallStand.png`: 148×67、35.26 PPU、足元中央Pivot。鉄球マークを絵に組み込んでいる。
- `Piston.png`: 22×72、24 PPU、中央Pivot。
- `LightPixel.png`: 1×1、1 PPU、警告灯/火花用。
- Point / Uncompressed / MipMapなし / 透過PNG。
- `PressImpact.wav` / `BallClang.wav`: この機械用に数式合成した22,050HzモノラルPCM。既存SEは上書きしない。

生成プロンプト（組み込みimagegen、透過背景指定）:

> Create production 2D side-view pixel-art game sprites on a transparent canvas, no text, no labels, no scenery. Three precisely separated objects arranged vertically with generous transparent gaps: TOP a wide horizontal falling industrial hydraulic press head, rectangular width-to-height ratio 4:1, pale icy blue steel plates shaded navy, cyan central light strip, rivets and bronze bolts, thick dark outline, bottom edge yellow/black diagonal hazard stripes, perfectly flat horizontal crushing underside, front orthographic view. No attached rods on the press head. MIDDLE a low sturdy iron-and-stone pedestal, width-to-height 2:1, flat top recessed slightly in center to seat a spiked iron ball, icy steel with navy shadows, large high-contrast pale square front plaque depicting a dark spiked iron ball pictogram, small cyan indicator lamps. BOTTOM a single narrow vertical telescoping hydraulic piston/support mechanism, silver blue steel with dark collars. Industrial ancient factory matching warm dark rusty stone backgrounds. Crisp authentic 16-bit hand-pixeled style, limited palette, visible individual square pixels, no blur, no antialiasing, no perspective. Fully visible isolated objects, no balls, characters, ground shadows or background. These are separate game assets, designed to be sliced apart. Use most canvas width for the top press.

## 検証コマンド

Unityメニュー `Tools/Falling Press/Validate Saved Assets` と `Run Play Mode Checks`。後者はStage2の保存データに触れず、遠方の仮設床で実Player/鉄球と仮想Gamepadを使用し、終了後は編集モードに戻る。結果は `FallingPressPlayModeChecks.Result()` で取得できる。最終的な操作感、音量、コース全体の難易度は実機Gamepadで評価する。

2026-10-30の確認結果:

- コンパイル成功。Prefab内部参照、欠損Script、Colliderの除外Layer、PNGのImport設定を検査済み。
- Play Mode: 警告、無支点の全下降と上昇、2組の独立動作、専用台座の鉄球で停止、3.5以上のクリアランス、鉄球が潰れないこと、Rest物理設定の維持、台座の共有拒否を確認。
- 実Playerの左→右への安全な走行、鉄球を外した際の下降再開、既存HP/無敵処理による接触ダメージを確認。テストは待ち時間だけ短縮し、安全横断時は下待機4秒として高さと衝突を検証している。本番Prefabの下待機は1.8秒。
- 仮想Gamepadの右スティック射出→Recall→Dragging、既存Magnet/Swing遷移、中心から外れた鉄球の拒否、停止機能OFF時の全下降を確認。
- 検証中の新規Error/Exceptionは0。終了後にStage2を開き直し、SceneはDirtyなし。新規Broken PPtrなし。作業前に存在したSceneRootsの欠損参照はUnityのScene保存によって実在するTransform参照に正規化された。
- 保存Sceneのカメラ描画でプレス、シリンダー、警告帯、台座の鉄球マークを目視確認。Player/鉄球/鎖/Inputの既存スクリプト、既存調整値、敵・地形・通電装置・ゴールは変更していない。
- 実機Gamepadでの最終操作感、音量、コース通しプレイは未実施。未解決の実装不具合はなし。

参照したUnity 6000.4の物理API: [Rigidbody2D.MovePosition](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Rigidbody2D.MovePosition.html)、[Physics2D.OverlapBox](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Physics2D.OverlapBox.html)、[Collider2D.excludeLayers](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Collider2D-excludeLayers.html)。使用システムは既存と同じRigidbody2D / Collider2D。
