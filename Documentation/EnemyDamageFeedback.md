# 敵のダメージ可視化

ビームサソリに適用済みの、`EnemyHealth` を読む共通表示Componentです。
他の敵のPrefabには追加していません。Player・鉄球・鎖・入力の処理も変更していません。

## 構成

- `EnemyHealth`: 従来どおりHP・被弾受付・ノックバック・撃破破片を所有。
- `EnemyDamageFeedback`: 確定した `HitConfirmed` だけを受けて、ゲージ・フラッシュ・VFXを表示。
- `EnemyMiniHealthGauge`: 生成画像の枠・背景・ピンクの残量を重ねる一本のSpriteバー。左端を固定し、HP比率に応じて右端が減る。Canvasや入力判定は不要。
- `EnemySpriteEffect`: 生成したスプライトシートを単発／間欠ループで再生。単発は再生後に自身を破棄。
- `EnemyHealthEditor`: 既存Inspector項目を残して、読み取り専用のCurrent HPを追加。

ゲージは敵と同じSceneに属する独立オブジェクトなので、SpriteのflipXやRootの負スケールで反転しません。
ヒット／撃破VFXも独立しており、本体のDestroy後に再生を続けます。
瀕死VFXは生存中だけ位置を追従し、死亡・Disable・ResetHealth時に停止します。

## ビームサソリの設定

| 項目 | 値 |
|---|---|
| EnemyHealth / Max Hp | 3 |
| Max Damage Per Hit | 1 |
| Hit Invincible Duration | 0.25秒 |
| 既存の点滅フラッシュ時間 | 0秒（新しい白フラッシュを使用） |
| Gauge Display Time | 1.5秒、確定被弾でリセット |
| Gauge Offset | `(0, 2.95, 0)`、Root位置基準 |
| Gauge Bar / Fill Size | `(1.6, 0.2)` / `(1.48, 0.12)`、HP数に関わらず一本のバー |
| Flash Color / Duration | 白 / 0.1秒 |
| Critical HP Threshold | 1 |
| Critical / Defeat Offset | `(0, 1.1, 0)` |
| Hit Offset | `(0, 0, 0)`、ImpactPoint基準 |
| Critical Prefab Scale | `(1.6, 1.6, 1)`（火花・煙を拡大） |

鉄球は速度・溜めによって1より大きいDamageを渡すため、サソリだけ1回の確定被弾の減少量を1に制限しています。
`Max Damage Per Hit=0` は従来どおりのダメージです。他の敵の既存挙動は維持します。
サソリは `3 → 2 → 1 → 0` の3回で撃破されます。0.25秒の受信側無敵中の接触では表示も再発生しません。
巡回幅・射程・射撃頻度・ビーム打ち消し・接触ダメージなし・専用SEは前回の設定を維持しています。

## 生成素材

画像生成には内蔵 `image_gen` を使用しました。4つの最終透過PNGをそのままプロジェクトへコピーしています。

| PNG（Assets/Art/EnemyDamageFeedback） | 内容 | 切り出し | PPU |
|---|---|---|---|
| GaugeAtlas.png | 飾りを省いた横長の枠、PlayerのHPバーに寄せたピンクの残量、暗いグレーの背景 | 横3セルの各部品の透過余白を除いた矩形 | 100 |
| HitAtlas.png | 白・赤・オレンジの火花 | 横4コマ | 512 |
| CriticalAtlas.png | 小さな電気火花4コマ＋灰色の煙4コマ | 横4×縦2、上段火花・下段煙 | 512 |
| DefeatAtlas.png | 赤ピンクの破壊光、細かな金属片、広がってほどける薄い煙 | 横3×縦2、6コマ | 256 |

すべてPointフィルタ、圧縮なし、MipMapなし、透過を使用。
SpriteRect・Sprite ID・名前の変更はUnityのSprite Data Provider経由で行い、`.meta` の手編集はしていません。
VFXは共通のセル寸法と中央Pivotを使って再生します。元画像の縦横が割り切れない箇所はピクセル境界へ切り出しています。
実際に使用した4種のプロンプトと撃破素材の修正プロンプトは [EnemyDamageArtPrompts.md](EnemyDamageArtPrompts.md) に記録しています。

## Prefabと差し替え

`Assets/Prefabs/Enemies/DamageFeedback/`:

- `MiniHealthGauge.prefab`: `Frame Sprite` / `Lit Sprite` / `Empty Sprite` を交換可能。HP2・4・5以上も同じ全幅の一本バーで表示。全幅1.6。
- `Hit.prefab`: 4コマ、14fps、約0.29秒。
- `Critical.prefab`: 火花12fps＋0.65秒休止、煙8fps＋0.9秒休止。小さく間欠表示。
- `Defeat.prefab`: 6コマ、12fps、0.5秒。最後の0.18秒で薄く消える。

敵ごとに `EnemyDamageFeedback` のGauge／Hit／Critical／Defeatの参照を交換できます。
各VFXは `EnemySpriteEffect.Frames` の画像配列、速度、休止、開始遅延、移動を調整できます。
ゲージの全体寸法・内部寸法は `EnemyMiniHealthGauge` で調整します。

## 全敵の破片調整

`EnemyHealth` 共通の `Fragment Scatter Scale=1.15` で、既存の生成範囲とImpulseを15%広げます。
`Fragment Direction Spread=1.1`（従来0.8）で命中方向への偏りを少し緩めます。
敵ごとの破片数・寿命・Prefab・基礎設定は保持しています。既存Prefabを個別に保存し直す必要はありません。
`FragmentBurst2D.Spawn` の省略時は従来の0.8なので、壊せる壁の飛散は変更しません。

## 白フラッシュ

`Assets/EnemyHitFlash.shader` と `Assets/Materials/EnemyHitFlash.mat` を使います。
Body Renderersそれぞれの子にアルファ形状だけを描く白いSpriteを重ね、同じスプライト・反転・描画順へ同期します。
元のSpriteRendererの色・Materialを変更しないため、共有Material経由で他の敵が白くなる事故を避けています。
複数Rendererを持つ敵も配列で指定可能です。影・ビーム・ゲージをBody Renderersへ含めないでください。

## 他の敵へ適用

1. `EnemyHealth` を持つ敵に `EnemyDamageFeedback` を追加。
2. `Body Renderers` に本体のRendererだけを指定。未指定時は `Visual` 子から取得。
3. Flash Materialと上記4つのPrefabを指定。
4. 最大HP、ゲージの頭上位置、エフェクトの補正位置を敵に合わせて設定。
5. HP1の敵では必要に応じてGauge EnabledとCritical Effect EnabledをOFFにする。

Gauge／Flash／Hit／Critical／Defeatには個別の有効スイッチがあります。
瀕死演出は「最大HPより減っており、現在HPが閾値以下」の生存中だけ表示します。
復帰時は従来どおり `EnemyHealth.ResetHealth()` を呼ぶと、表示もリセットされます。
`disableOnDeath` に表示Componentを追加する必要はありません。

## 変更ファイル

- 新規Runtime: `EnemyDamageFeedback.cs`、`EnemyMiniHealthGauge.cs`、`EnemySpriteEffect.cs`。
- 変更Runtime: `EnemyHealth.cs`（任意のダメージ上限、復帰完了通知、敵共通の飛散設定）、`FragmentBurst2D.cs`（方向の散り幅引数、省略時は従来値）。
- 新規Editor: `EnemyDamageFeedbackSetup.cs`、`EnemyDamageFeedbackChecks.cs`、`EnemyHealthEditor.cs`。
- 既存Editor: `BeamScorpionSetup.cs`、`BeamScorpionPlayModeChecks.cs`（HP3との整合）。
- `BeamScorpion.prefab`、上記4つの共通Prefab、4枚の生成PNG、Flash shader／Materialと新規アセットの`.meta`。
- 本書と画像生成プロンプト、ビームサソリの実装メモ。

## 確認

Unity 6000.4.2f1でコンパイル成功。一本バーへの変更と煙のフェードを含むダメージ表示チェックは **PASS (29), runtimeErrors=0**。
HP3への変更後も、巡回・ビーム・鉄球打ち消しの既存チェックは **PASS (38), runtimeErrors=0**。

`Tools > Enemies > Damage Feedback > Run Play Mode Checks` で、3回被弾、タイマー延長、複数Rendererのフラッシュ、Material非干渉、HP2/4/5、瀕死ループ、撃破後の独立再生、復帰を確認します。
本番Sceneは保存せず、Play Modeだけの一時Sceneで検証します。
最終的な見た目・大きさ・ゲームパッドでの手応えはユーザー確認が必要です。
