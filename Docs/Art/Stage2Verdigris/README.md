# Stage2 — 夕焼けの廃工場と緑青

既存の夕焼け・銅製機械を保ち、場所ごとに錆の密度が異なる設備と中景を追加した美術調整。Runtime C#、入力、敵AI、鉄球、鎖、ギミック、カメラ設定は変更していない。

## 配置と方針

- `Assets/Scenes/Stage2.unity` に新規Prefabインスタンス19個を実配置。`Stage2VerdigrisArt` 配下に小型設備、残骸、中景機械、冷却設備を整理。
- 既存の配管2個、炉1個、ランプ1個を緑青バリエーションへ変更。銅色の炉・ランプ・配管も残した。
- 中盤の冷却槽は錆の多い専用設備。明るいエメラルド／ターコイズを広く使い、黒褐色の酸化面との境界は不均一な色むらとして描写。剥がれた塗装のような穴や明るい輪郭線は避けた。
- 銅色主体の電気扉区間、緑青が多い整備設備区間、暖色を残すゴール側で偏りを作る。約20%という色の目安を各画面へ均等に割り当てない。
- **地面は元の状態を維持。** 地面テクスチャ・Tileアセットの改変はなく、試作した錆タイルと関連画像は削除済み。今後地面に錆を追加する場合は、当たり判定のない装飾Prefabを重ねる方式とする。
- 既存の空・工場背景画像・Material・パララックスは維持。追加した中景4組と冷却ランドマーク2個は、近い工場設備としてワールド固定。連続する背景面を作らないため、ループ端の隙間は生じない。

## 追加テクスチャ

場所: `Assets/image_/Stage/Stage2/Verdigris/`

| 画像 | サイズ | PPU | 用途 |
|---|---:|---:|---|
| PipeCluster_Verdigris.png | 84×70 | 21 | 既存配管の緑青バリエーション |
| Furnace_Verdigris.png | 82×71 | 21 | 炉の外装。炎は元の画素を保持 |
| IndustrialLamp_Verdigris.png | 34×77 | 21 | ランプの緑青バリエーション |
| PressureGauge.png | 19×21 | 21 | 既存素材から再利用した圧力計 |
| PipeElbow.png | 18×38 | 21 | 再利用した小型配管 |
| ValveWheel.png | 23×23 | 21 | 再利用したバルブ |
| GearSalvage.png | 34×43 | 21 | 再利用した歯車の残骸 |
| MachineCityCoolingReservoir.png | 256×192 | 28 | 新規の大型冷却槽 |
| MachineCityCoolingTower.png | 54×128 | 28 | 新規冷却設備の側塔を使った中景 |

すべて **Point / 圧縮なし / MipMapなし / Clamp / NPOTリサイズなし**。透過を保持。既存装飾は元のピクセル寸法を維持し、緑青を少数の色へ整理。新規設備も画像自体をネイティブ解像度へ縮小してから取り込んだ。Prefab化だけでドット絵になるという扱いはしていない。

## 追加Prefab

場所: `Assets/Prefabs/Environment/Stage2/Verdigris/`

- `S2_PatinaPipeCluster` / `S2_PatinaFurnace` / `S2_PatinaIndustrialLamp`
- `S2_ServiceBoiler` / `S2_WallServicePipes` / `S2_PressureValve`
- `S2_ElbowExhaust` / `S2_FloorSalvage` / `S2_SmallSteamVent`
- `S2_MidgroundMachinery`
- `S2_OxidizedCoolingReservoir` / `S2_DistantCoolingTower`

12種ともCollider・Rigidbody・ゲーム用スクリプトなし。`Default` Sorting Layer、通常の小物はOrder 0〜1、中景機械は-601〜-599、冷却ランドマークは-720 / -710。既存の前景配管の透過処理とユーザー調整済み配置は維持。

追加オブジェクトは合計36 SpriteRenderer。新しい粒子、回転スクリプト、追加CRTは導入していない。小型蒸気口は既存の半透明の蒸気画像を再利用。

## 検証

- Unity AssetDatabaseの取り込みと動的C#実行に成功。ゲーム用C#変更なし、Unityコンパイルエラーなし。
- 最終確認期間のConsole Error / Exceptionはともに0件。
- 変更前後の既存ゲーム用コンポーネント284個のシリアライズ値が一致。
- 521セルのタイル寸法・PPU・Transform・Physics Shapeが一致。676地点の床Raycast結果も一致。
- 地面は元のTile参照へ復元。Stage1、共有の元画像、ProjectSettings、Packagesの変更なし。
- 追加PrefabのSprite参照・Sorting・衝突なしを検査。CRTは既存の1個。
- Play Modeの画面確認は、静止した美術レイアウト検査のため一時的に敵AIと物理時間を停止し、カメラを各地点へ移動して実施。これらの検査操作は保存していない。
- 通し攻略、実機コントローラー操作、最終的な色の密度・見た目・操作感の評価は未実施。最終評価はユーザーのPlay Mode確認に委ねる。
- Stage2と新規Prefabは保存済み。検査終了時はEdit Mode、未保存変更なし、`Time.timeScale=1`。

画面記録: [冷却槽区間](Stage2-CoolingReservoir-GameView.png) / [整備設備区間](Stage2-ServiceDistrict-GameView.png) / [開始地点](Stage2-Start-GameView.png)。最終的な美術評価用であり、通し攻略の記録ではない。

## 画像生成と材質資料

Built-in `image_gen`を使用。採用した生成元は`Source/`、プロンプトと取り込み方針は[GenerationPrompts.json](GenerationPrompts.json)。写真資料は色と材質の観察に使用し、ゲーム画像へ直接貼り付けていない。

銅の腐食の青緑・赤褐色・暗色の層と、安定した緑青／局所的な腐食の違いは、[Canadian Conservation Institute: Identifying Archaeological Metal](https://www.canada.ca/en/conservation-institute/services/conservation-preservation-publications/canadian-conservation-institute-notes/identifying-archaeological-metal.html)と[Caring for metal objects](https://www.canada.ca/en/conservation-institute/services/preventive-conservation/guidelines-collections/metal-objects.html)を参照。実際の腐食の説明を基に、ゲームで読み取れる粗い色面へ美術的に整理している。
