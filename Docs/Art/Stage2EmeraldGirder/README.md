# Stage2 Emerald Girder Terrain

緑に酸化したような古い鉄骨を主役にした、当たり判定つきの床・壁タイル。H鋼の縁、鋲留めの接合板、斜め補強材を残し、裏板は完全透過。四角いマス単位で横・縦へ組み合わせる通常Tile方式。

## Assets / usage

- Atlas: `Assets/image_/Stage/Stage2/MachineCityEmeraldGirder.png`。114×152、各38×38、38 PPU、Point、MipMapなし、無圧縮、FullRect、alpha transparency。
- Tiles: `Assets/Tiles/Stage2/EmeraldGirder/`。TopLeft / Top / TopRight / Left / Fill / Right / BottomLeft / Bottom / BottomRight / FillCross / FillJoint の11枚。
- Palette: 既存 `Assets/Tiles/Stage2/Stage2_MachineCity.prefab` の X=6～8、Y=0～-3。既存パレットの配置とGridPalette設定は保持。
- Sample: `Assets/Prefabs/Environment/Stage2/S2_EmeraldGirderSamples.prefab`。横長の床、縦長の壁、銅との境界見本。既存見本Prefabは未変更。
- Grid 2×2、Tile transform (2,2,1)、Floorタグ、Layer0、Static Rigidbody2D、TilemapCollider2D Merge + CompositeCollider2D、Sorting -1、既存GroundTileSeamBlenderを使用。

## Stage2 placement / collision

`Ground1` の X=-8～-1・Y=-6～0（56セル）、X=19～24・Y=-6～2（54セル）の計110セルだけ対応するTileへ置換。中央差分は (-6,-2)、(-3,-4)、(21,-2)、(22,0) の4セル。

各Spriteのピボット・カスタム物理形状、Tileのmatrix/color/flags/colliderTypeを既存MachineCityGroundから継承。透過の穴は背景を見せる表現であり、通り抜け用の穴ではない。既存の地形外形・接地位置・通行条件を保つため、穴の中も既存地面と同じ当たり判定がある。

既存地面の画像・Prefab・地形配置・操作・敵・鉄球・CRTの設定は変更していない。新規ランタイムスクリプトなし。ユーザーのStageSelectJapanese.asset変更はバイト単位で保持。Scene保存時にUnityが参照数0の旧埋め込みTile/Spriteを整理したが、既存の有効セル・コライダーには変更なし。

## Art provenance

Built-in image_gen 使用。`GenerationPrompt.txt` と `TransparencyPrompt.txt` を参照。
`LayoutReference.png` は既存タイルを切り出した寸法用テンプレート、`StructureSource.png` は承認された鉄骨案、`GeneratedSource.png` は裏板を抜いた最終生成原画。最終原画を技術的に切り出し・縮小し、既存の接地ラインに合わせて38pxのAtlasへ格納した。穴はalpha=0、鉄骨はalpha=255。質感は緑の本体と暖色のボルトを残す。

## Validation

- 11枚のimport設定・ピボット・物理形状・Tile設定を既存と照合して一致。
- 置換前後で521セルの占有位置、matrix/color/flagsを比較。110セルだけ新Tileへ変更。Ground1/Ground2のCompositeCollider2Dのパス・頂点は0.00001 world unit精度で一致。
- Play Modeの独立PhysicsScene2Dで実タイル配置を使ったBoxプローブを確認。着地、3セル分の継ぎ目通過（高さ変動0）、右側の緑の壁への衝突が成功。これはPlayerコントローラーの操作感テストではない。
- 見本の接続と透過をレンダー確認、Stage2 Gameビューを確認。コンパイル失敗なし。画像は `TileSamples.png` / `Stage2-Start-GameView.png`。
- 撮影中の敵停止、開始演出スキップ、タイトルCanvas非表示はPlay Mode限定で保存していない。終了後はPlay/Pause停止、Time.timeScale=1。Scene/Prefab保存済み、Scene dirtyなし。
- 通し攻略、実機入力と最終的な見え方・操作感の評価は未実施。mainへはマージしない。

最終再検証: 保存済みSceneのPlay Mode往復後も全地形データが一致。修正・組み込み後の直近3分のConsole Error / Exceptionは0件。制作中の一時的なMCP実行コードの型名エラーは修正済みで、プロジェクトのコンパイル失敗・ランタイムエラーはない。
