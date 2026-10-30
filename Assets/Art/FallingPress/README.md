# Stage2 落下プレス機

`Assets/Prefabs/Environment/Stage2/FallingPress/` の `FallingPress.prefab`（プレス機のみ）と `PressBallStand.prefab`（鉄球マーク付き専用台座）は独立したPrefab。停止は実Colliderへの衝突で決まり、`Ball Stand` の割り当ては任意。保存済みStage2では x=88, y=1.23808 に1組を配置している。

## 設計・使い方

- ルートを床に置く。回転0、Scale1で使用する。動くのは `MovingHead` のKinematic Rigidbody2Dだけ。
- 台座は奥側の支持設備。Playerは手前を走るため台座との衝突を除外する。台座は鉄球Layer7とプレスLayer2、プレスはPlayer Layer3・鉄球Layer7・台座Layer2との実衝突を有効にする。ProjectSettingsは変更しない。
- プレス下降前に実際のBoxCollider2Dを `Collider2D.Cast` でスイープし、最初に当たった台座または鉄球の直前まで移動する。Kinematicのため静的な台座との接触も有効にする。Playerや敵を停止支点にはしない。
- 鉄球は `MorningStarLauncher.BallBody` で識別する。台座の有無、中央範囲、速度、State、Player用クリアランスで鉄球との衝突を無効化しない。台座だけでも止まり、台座がない場所の鉄球でも止まる。
- 台座上の鉄球なら台座高1.9+直径1.7+接触余白0.04で、標準停止高は約3.64。台座だけなら約1.94、床の鉄球だけなら約1.74でも停止する。クリアランス3.5は安全表示の基準であり、停止の成立条件ではない。
- 支点がなくなると下降を再開し、次に当たる台座・鉄球で再停止する。支点が残っていれば下待機後に上昇する。鉄球の質量・速度・重力・Stateへの書き込みはない。プレス側だけの移動を制御して鉄球を押し潰さない。
- 既存 `PlayerHealth.TakeDamage(1, knockback)` を使用。移動区間全体のOverlapBoxで高速下降も検知し、無敵・死亡・復帰は既存HP処理に任せる。
- プレス・台座は独立配置できる。Inspectorの `Ball Stand` は配置確認用で、停止に専用所有権や1対1の割り当ては必要ない。複製した未接続のプレスも、接触した台座・鉄球で止まる。
- 台座の上面に鉄球が実接触して支えられると、前面の発光枠・上部バーが緑になり、大きなチェックマークと小さな金属SEでセット状態を示す。中央範囲への侵入だけでは点灯しない。鉄球が外れると消灯する。
- プレス停止時は、安全な通路高なら緑、低すぎる場合はアンバーを表示する。衝突時の火花・SE・既存CameraShake2D、下降前の赤/黄点滅は維持。

## Inspector初期値

| 項目 | 値 |
|---|---:|
| Width / Body Height | 7 / 2 |
| Travel Distance | 6 |
| Descent / Ascent Speed | 12 / 2.5 |
| Top Wait / Bottom Wait | 2 / 1.8秒 |
| Warning Time | 0.7秒 |
| Start Phase | 0（0–1のサイクル比率） |
| Required Player Clearance（安全表示の基準） | 3.5 |
| Contact Skin | 0.04 |
| Damage / Knockback | 1 / 横7・縦0 |
| 台座高さ / 支持Collider幅 | 1.9 / 3.7 |
| Surface Tolerance（上面接触の識別） | 0.12 |
| セット表示 | 緑の枠・バー・チェックマーク |

旧 `Ball Stop Enabled`、`Placement Half Width`、`Maximum Vertical Speed` は廃止。接触した鉄球をすり抜ける設定や、台座中央だけを成功範囲にする設定は持たない。

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

2026-10-30の今回の変更:

- 鉄球を除外していた衝突設定と、台座中央・通過高さによる停止ゲートを撤去。実Colliderのスイープと通常の剛体衝突を使用する。
- 空の台座との停止、鉄球を置いたときの停止・表示、安全な走行、鉄球を外したときの下降再開、PlayerダメージをPlay Modeで確認。
- 台座なしの床の鉄球で、Player用クリアランスを満たさなくてもプレスが停止することを確認。移動中の鉄球がプレスの側面に実衝突し、すり抜けないことも確認。
- 既存の右スティック射出→Recall→DraggingとMagnet/Swing遷移、鉄球のRest物理設定維持を確認。
- 自動検証はPASS。コンパイル成功、検証中の新規Error/Exceptionは0。
- 横断テストは下待機4秒で高さ・衝突を検証。本番Prefabの下待機1.8秒と、ユーザー調整値は維持する。
- Scene上の未保存の配置調整は保持し、今回Sceneは保存しない。Prefabは分離状態と既存GUIDを維持し、更新した物理設定・表示を保存する。
- 実機Gamepadでの最終操作感、表示の好み、音量、コース通しプレイはユーザー評価に委ねる。

参照したUnity 6000.4の物理API: [Collider2D.Cast](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Collider2D.Cast.html)、[Collider2D.GetContacts](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Collider2D.GetContacts.html)、[Rigidbody2D.useFullKinematicContacts](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Rigidbody2D-useFullKinematicContacts.html)。使用システムは既存と同じRigidbody2D / Collider2D。動的な鉄球にプレスを押し込まないため、Kinematicの移動を実形状のスイープ結果までに制限する。静的台座との自動衝突応答がない点もこのスイープで補う。
