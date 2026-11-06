# Stage2 — 銅工場とエメラルドの冷却水

夕焼けの銅工場を巡る冷却水を、ステージ2の青緑の主題とする。水を蓄える貯水槽、ガラス越しに水が見える循環装置、熱を逃がす放熱器、緑青の付いた導管を、一つの工業設備群として配置した。

## 今回の修正

- 小さく切り出した配管・歯車、接続が曖昧な小型設備、旧中景の組み合わせなど18組をSceneから撤去。旧Prefab8個と未使用の切り抜きテクスチャ5枚も削除。
- ガラス冷却装置4台、放熱器3台、貯水槽2基を実配置。2か所の既存装飾を大型の放熱設備に置き換えた。設置する地面の高さと、左右の脚が同じ平面に乗ることを確認。
- 青緑は、透明なエメラルドの水、ターコイズの陶製放熱部品、銅の緑青で材質を分けた。錆の小斑点だけで色を増やす方式をやめた。
- 背景の大型タンク・ドーム・水平配管にも青緑を追加。元画像の透過と輪郭、非編集領域の画素を維持し、Stage2の背景だけに適用。
- 地面・足場・Tile・当たり判定は変更しない。地面へ錆を追加する場合は、今後も衝突のない独立した装飾Prefabを重ねる方式とする。

## 現在のアセット

画像: Assets/image_/Stage/Stage2/Verdigris/

| 画像 | ネイティブ寸法 | PPU |
|---|---:|---:|
| EmeraldGlassCondenser.png | 144×144 | 21 |
| TurquoiseRadiatorBank.png | 144×112 | 21 |
| EmeraldCoolingCistern.png | 168×112 | 24 |
| MachineCityEmeraldSkyline.png | 2172×724 | 100 |
| MachineCityCoolingReservoir.png | 256×192 | 28 |
| PipeCluster_Verdigris.png | 84×70 | 21 |
| Furnace_Verdigris.png | 82×71 | 21 |
| IndustrialLamp_Verdigris.png | 34×77 | 21 |

すべてPoint、無圧縮、MipMapなし。新設備は元画像自体をゲーム用の解像度に整えている。

Prefab: Assets/Prefabs/Environment/Stage2/Verdigris/

- S2_EmeraldGlassCondenser / S2_TurquoiseRadiatorBank / S2_EmeraldCoolingCistern
- S2_OxidizedCoolingReservoir
- S2_PatinaPipeCluster / S2_PatinaFurnace / S2_PatinaIndustrialLamp

7種類ともSpriteRendererのみの装飾。Collider、Rigidbody、動作スクリプトなし。追加ルートは新設備9台と既存の大型冷却槽1基で合計10 SpriteRenderer。前回の36 Rendererから整理した。通常設備の描画順はDefault:0、貯水槽は-450、大型の緑青冷却槽は-720。人物・敵・鉄球・ギミックの手前には出さない。

## 色を保つマテリアル

- Assets/Art/Stage2Pilot/CoolingWaterSurface.mat: Built-in Unlit/Transparent。新設備3種の色を保持し、暖色のWorld Toneによる水・ガラスの濁りを避ける。元絵には銅の夕焼け反射を描き込んである。既存CRTは通常どおり最後に1回適用。
- Assets/Art/Stage2Pilot/MachineCityEmeraldDistrictAtmosphere.mat: 既存の背景用Materialを複製。Saturation=1、AtmosphereStrength=0.025。既存の空・元背景・共有Material・Profileは変更しない。

既存のパララックス、ループ、カメラ設定は維持。近い設備はワールド固定。

## 検証と保存

既存ゲーム用コンポーネント284個のシリアライズ値、521セルのタイル形状・PPU・Transform、676地点の床Raycast結果は変更前と一致。Stage1、Runtime C#、入力、鉄球、鎖、AI、ギミック、Packages、ProjectSettingsは変更なし。

Play Modeでは静止レイアウトを見るため、一時的に敵AI・物理時間を停止し、カメラを移動して確認した。画像記録では開始時のステージ名表示を一時的に隠している。これらの検査操作は保存しない。通し攻略、実機コントローラー、最終的な見た目の評価はユーザー確認が必要。

最終確認: Unityコンパイルエラーなし、Console Error / Exception 0件。Prefab参照・インポート・153個のSceneアセットGUID・設備の足元27地点を検査済み。Scene / Prefab / Materialは保存済み。検査終了時はEdit Mode、Scene未保存変更なし、Time.timeScale=1。

画面記録: [開始地点](Stage2-Start-GameView.png) / [放熱設備](Stage2-Radiator-GameView.png) / [終盤の冷却水槽](Stage2-CoolingWater-GameView.png)。

画像生成にはBuilt-in image_genを使用。今回の採用プロンプトは[CoolingWaterPrompts.json](CoolingWaterPrompts.json)、以前から残している緑青設備の生成記録は[GenerationPrompts.json](GenerationPrompts.json)。採用元PNGはSource/。新背景は生成元を全面置換せず、緑の材質領域のみ元画像へ反映し、元の透過を完全に保持。

銅の腐食材質の資料は[Canadian Conservation Institute](https://www.canada.ca/en/conservation-institute/services/preventive-conservation/guidelines-collections/metal-objects.html)を参照。今回の水・ガラス・陶製部品は、緑青とは別の青緑要素として設計している。
