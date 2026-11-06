# Stage2 落下プレス機

`Assets/Prefabs/Environment/Stage2/FallingPress/` の `FallingPress.prefab`（プレス機のみ）と `PressBallStand.prefab`（鉄球マーク付き専用台座）は独立したPrefab。停止は鉄球または台座との実Collider接触で決まり、`Ball Stand` の割り当ては任意。保存済みStage2では x=88, y=1.23808 に1組を配置している。

## 設計・使い方

- ルートを床に置く。回転0、Scale1で使用する。動くのは `MovingHead` のKinematic Rigidbody2Dだけ。
- 台座は側面・上面ともPlayerに当たる。鉄球/プレス用ColliderはLayer2に維持し、Player用の同寸ColliderをDefault Layer0に追加して既存の接地判定にも対応する。プレス本体の側面・上面は通常どおりPlayerに当たる。下降で挟む直前だけ、そのPlayerとの実体衝突を解除して押し込みを防ぐ。上面には接地用のPlayer専用Collider（Default Layer0）を持つ。ProjectSettingsは変更しない。
- 下降位置は `Collider2D.Cast` で判定し、床・鉄球・台座のうち先に当たった位置で止める。床上の鉄球、台座だけ、台座上の鉄球のいずれにも衝突し、Playerでは停止しない。床は `Floor Layers` で指定し、既定はDefault / Walls。床レイヤーは本体Colliderにも自動で追加する。
- 台座高1.9+鉄球直径1.7+接触余白0.04で標準停止高は約3.64。鉄球が外れたら下降を再開し、台座に接触した位置で止まる。台座と鉄球のどちらもない場合は最下点0.04まで下降する。安全表示の基準3.5、開始位相、上下の基準点は維持。
- 鉄球を床へ押し込まないよう、下降するプレス側の移動を接触直前に制限する。鉄球の質量・速度・重力・Stateや、鉄球/Playerと台座・床の衝突には書き込まない。
- Player判定は移動とは独立した移動区間のOverlapBoxで行う。頭上と足元の支持面に挟む場合だけ、Playerとこのプレス本体/上面の衝突を一時解除し、既存 `PlayerHealth.TakeDamage` を使用する。解除してもダメージ検出は継続でき、プレスは最下点まで下降する。無敵・死亡・復帰は既存HP処理に任せる。
- 衝突の解除はColliderの組み合わせ単位。形状が離れたら元の設定に戻し、無効化時にも後始末する。他のプレスとの衝突や、停止済みの狭い隙間への新たな侵入防止は維持する。PlayerのCollider・速度・重力・位置は変更しない。
- 挟まれたPlayerは `PressPlayerSquash` が足元を支点にSpriteの頂点だけを縦圧縮・横伸びさせる。PlayerルートとColliderは変形しない。既存のライティング・点滅を維持し、離脱・死亡・復帰・無効化で元の表示に戻す。
- 被挟み時の描画高さは鉄球Colliderの直径に合わせる。鉄球を取得できない場合は従来の30%を使用する。Inspectorの最小高さは維持する。物理衝突・被弾条件・保持/復元時間は維持。
- プレス・台座は独立配置できる。Inspectorの `Ball Stand` は配置確認用で、停止に専用所有権や1対1の割り当ては必要ない。複製した未接続のプレスも、接触した鉄球または台座で止まる。
- 台座の上面に鉄球が実接触すると、左右の石が赤から水色に光る。鉄球が外れると赤に戻る。元の石のドット絵をマスク化した表示なので、金属や鉄球マークの色は変えない。チェックマーク・発光枠・上部バーは撤去した。セット時の小さな金属SEは維持。
- 鋼板とシリンダーの色をStage2の緑青系に調整し、警告灯は見分けられる明るさを保つ。プレス停止時は、安全な通路高なら緑、低すぎる場合はアンバーを表示する。衝突時には鉄球と同じ火花・火種・フラッシュの処理をPrefab内で使用する。火花本数は `Impact Spark Count` で調整でき、0で演出を無効化する。SE・CameraShake2D・下降前の警告点滅は維持。
- `Synchronize` を有効にしたプレスは共通のゲーム時間で周期を合わせ、台座や鉄球で途中停止した場合も他のプレスと同じタイミングで上昇を始める。同じ周期値とStart Phaseを使うことで同期し、異なるStart Phaseを指定すれば時差を付けられる。無効にすると従来の個別周期で動く。

2026-11-06: 台座のみ・床上の鉄球・台座上の鉄球での停止、Player被挟みと鉄球直径に合わせた描画高さ、既存の射出/回収/スイングをPlay Modeで確認。コンパイル成功、新規Error/Exceptionなし。Scene/Prefabの保存データとInspector値は変更なし。

2026-11-06 追記: 共通周期の2台（片方が台座で停止する場合を含む）、火花7個と0個、床・台座・鉄球での停止、Player被挟み、鉄球の射出/回収/SwingをPlay Modeで確認。Stage2のComposite Colliderも下降スイープで検出した。Prefabは保存済み。Prefab更新前の確認時点でStage2に未保存の変更があり、Sceneは保存していない。検証中にPlay Modeでの再コンパイルが重なった際、既存の `Enemy.OnEnable` で例外が1件発生したが、通常のPlay Mode再実行ではエラー/例外0件。

## Inspector初期値

| 項目 | 値 |
|---|---:|
| Width / Body Height | 7 / 2 |
| Travel Distance | 6 |
| Descent / Ascent Speed | 12 / 2.5 |
| Top Wait / Bottom Wait | 2 / 1.8秒 |
| Warning Time | 0.7秒 |
| Start Phase | 0（0–1のサイクル比率） |
| Synchronize | 有効（同じ周期・位相で同期） |
| Impact Spark Count | 24（0で無効） |
| Required Player Clearance（安全表示の基準） | 3.5 |
| Contact Skin | 0.04 |
| Floor Layers | Default / Walls |
| 鋼板・シリンダー色 | RGBA (0.68, 0.82, 0.75, 1) |
| Damage / Knockback | 1 / 横7・縦0 |
| 台座高さ / 支持Collider幅 | 1.9 / 3.7 |
| Surface Tolerance（上面接触の識別） | 0.12 |
| セット表示 | 左右の石：未セットは赤、セット時は水色 |
| Minimum Squash Height | 0.15（描画高さ比率） |
| Maximum Squash Width | 1.18（最大描画幅比率） |
| Squash Hold / Recovery | 0.12 / 0.18秒 |

旧 `Ball Stop Enabled`、`Placement Half Width`、`Maximum Vertical Speed` は廃止。接触した鉄球をすり抜ける設定や、台座中央だけを成功範囲にする設定は持たない。

幅を大きくする場合は既存の鎖の長さとの関係を確認する。横幅7は基本鎖長5に合わせた設定。台座は実体化したため、台座を通過するには乗り越える操作と、その高さに応じたプレスの配置が必要。既存Player/鉄球のInspector調整値は変更していない。

## 素材

imagegen組み込みツールで新規生成した画像を切り出し、最近傍縮小と二値Alpha化のみ行った。元画像はCodex生成先に保持。使用Spriteはすべてこのフォルダに実体を保存している。

- `PressBody.png`: 252×82、36 PPU、下面中央Pivot。
- `BallStand.png`: 148×67、35.26 PPU、足元中央Pivot。鉄球マークを絵に組み込んでいる。
- `Piston.png`: 22×72、24 PPU、中央Pivot。
- `LightPixel.png`: 1×1、1 PPU、警告灯/火花用。
- `StandStones.png` / `StandStoneGlow.png`: 148×67、35.26 PPU。既存画像の左右の石だけを切り出したグレースケールと、控えめなドット状の光。専用Shaderで赤/水色を維持する。
- Point / Uncompressed / MipMapなし / 透過PNG。
- `PressImpact.wav` / `BallClang.wav`: この機械用に数式合成した22,050HzモノラルPCM。既存SEは上書きしない。

生成プロンプト（組み込みimagegen、透過背景指定）:

> Create production 2D side-view pixel-art game sprites on a transparent canvas, no text, no labels, no scenery. Three precisely separated objects arranged vertically with generous transparent gaps: TOP a wide horizontal falling industrial hydraulic press head, rectangular width-to-height ratio 4:1, pale icy blue steel plates shaded navy, cyan central light strip, rivets and bronze bolts, thick dark outline, bottom edge yellow/black diagonal hazard stripes, perfectly flat horizontal crushing underside, front orthographic view. No attached rods on the press head. MIDDLE a low sturdy iron-and-stone pedestal, width-to-height 2:1, flat top recessed slightly in center to seat a spiked iron ball, icy steel with navy shadows, large high-contrast pale square front plaque depicting a dark spiked iron ball pictogram, small cyan indicator lamps. BOTTOM a single narrow vertical telescoping hydraulic piston/support mechanism, silver blue steel with dark collars. Industrial ancient factory matching warm dark rusty stone backgrounds. Crisp authentic 16-bit hand-pixeled style, limited palette, visible individual square pixels, no blur, no antialiasing, no perspective. Fully visible isolated objects, no balls, characters, ground shadows or background. These are separate game assets, designed to be sliced apart. Use most canvas width for the top press.

## 検証コマンド

Unityメニュー `Tools/Falling Press/Validate Saved Assets` と `Run Play Mode Checks`。後者はStage2の保存データに触れず、遠方の仮設床で実Player/鉄球と仮想Gamepadを使用し、終了後は編集モードに戻る。結果は `FallingPressPlayModeChecks.Result()` で取得できる。最終的な操作感、音量、コース全体の難易度は実機Gamepadで評価する。

以前のPlayer接触時の修正（鉄球・台座の停止条件は上記の現行仕様を参照）:

- 原因は `LimitPlayerDescent` が移動先をPlayer頭上に制限し、`PlayerBlocked` を下待機状態として扱っていたこと。Player用の停止位置制限を廃止し、ダメージ判定を独立させた。`StandBlocked` / `PlayerBlocked` の旧enum値は互換性のため残すが使用しない。
- 現在のぺちゃんこ演出スクリプト・Shader・保持/復元時間は変更しない。呼び出す接触面は最初の被挟み位置に固定し、下降するプレス下面に合わせてさらに潰れたり、演出が延長されたりしないようにする。
- Inspector・Prefabデザイン・Scene配置は維持。検証対象は、Player被弾後の最下点到達、床へのめり込み防止、描画高さ30%、台座上の鉄球だけの支持、取り外し後の再下降、複数台・周期・既存射出/回収/Dragging/Swing。
- 実Playerの衝突/被挟み7ケースと連続回帰確認がPASS。被挟み後の最下点0.04到達、描画高さ30%、既存無敵時間、未セット鉄球の保護、台座上の鉄球による停止/取り外し、離脱後の衝突復元、複数台、射出→Recall→Dragging、Magnet/Swingを確認。コンパイル成功、新規Error/Exceptionは0。保存済みPrefab/Sprite/Script参照は正常、Stage2は未保存変更なし。動画ファイルは取得できず、動画との直接比較・実機Gamepadの通し評価は未実施。
- 使用API: [Physics2D.IgnoreCollision](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Physics2D.IgnoreCollision.html)、[Collider2D.Distance](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Collider2D.Distance.html)、[Rigidbody2D.GetContacts](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Rigidbody2D.GetContacts.html)。

2026-10-30の変更と検証:

- 台座にPlayer用の実Colliderを追加。側面で停止し、上面に着地・接地できることを実Playerで確認。
- セット表示を左右の石の赤→水色→赤に変更。実行中の描画も確認。
- 変更前の再現ではPlayerの足元が床下約0.965に押し込まれた。プレスの物理押し込みを除き、描画だけの圧縮へ変更した後は、継続接触中も足元は床上約0.01を維持した。
- 縦圧縮・横伸び、PlayerルートScale/Collider寸法の維持、既存HPダメージ、離脱後のMaterial/描画順/形状の復元を確認。Shaderの描画をCameraで確認。
- 既存の鉄球・台座での停止、台座なしの鉄球での停止、鉄球の側面衝突、右スティック射出→Recall→Dragging、Magnet/Swing遷移も再検証してPASS。
- コンパイル成功。最終検証中の新規Error/Exceptionは0。Prefab2種を保存し、Scene上の未保存の配置調整は保持した。
- 実機Gamepadでの最終操作感、圧縮量・表示の好み、配置を含めた通しプレイはユーザー評価に委ねる。

参照したUnity 6000.4の物理API: [Collider2D.Cast](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Collider2D.Cast.html)、[Collider2D.GetContacts](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Collider2D.GetContacts.html)、[Rigidbody2D.useFullKinematicContacts](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Rigidbody2D-useFullKinematicContacts.html)。使用システムは既存と同じRigidbody2D / Collider2D。動的な鉄球にプレスを押し込まないため、Kinematicの移動を実形状のスイープ結果までに制限する。静的台座との自動衝突応答がない点もこのスイープで補う。
