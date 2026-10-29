# Stage 2 / 灼熱の機械都市 — 素材試作

試験ブランチ: `NewStagePilot`。

Stage1 の実アセット `Assets/image_/Stage/地面ドット絵.png`、`Assets/Tiles/Comp`、シーン内 `Grid/Ground1` / `Ground2` を基準にした追加素材。既存の素材・タイル・シーン・操作処理は変更しない。

## 地面

- `Assets/image_/Stage/Stage2/MachineCityGround.png`: 126 × 126 px、42 × 42 px の 3 × 3 スプライト。
- `Assets/Tiles/Stage2/MachineCityGround_*.asset`: 上段 左端 / 上面 / 右端、中段 左壁 / 埋め / 右壁、下段 左下 / 底 / 右下。
- `Assets/Tiles/Stage2/Stage2_MachineCity.prefab`: Tile Palette。Stage1 と同じ矩形グリッド、セル `(2, 2, 0)`、42 PPU、各 Tile の Transform `(2, 2, 1)`。
- `Assets/Prefabs/Environment/Stage2/MachineCityGroundGrid.prefab`: 既存 Ground1 / Ground2 の構成を複製した小さな配置例。Floor タグ、静的 Rigidbody2D、TilemapCollider2D と CompositeCollider2D、既存の GroundTileSeamBlender を継承。

上面の奥行きは浅くする。足が乗る面と、その手前にある明るい縁を区別する。左右は崖として切り、下端は島型に細くせず四角くする。Stage1 と同じく上段には水平な接触線を設定。この画像ではタイル上端から16px、sprite-local y = 5px。側端は透明余白4px、底端は8pxを除いた矩形。Tile の Collider Type は Sprite。Stage1 自体の接触線や設定は変更しない。

## 装飾・背景

`Assets/Prefabs/Environment/Stage2/` に炉、配管、歯車、工業ランプ、コンテナ、蒸気口を用意。すべて装飾用で、Collider・ダメージ・回転・蒸気アニメーションは付けない。足元 Pivot、21 PPU、Order in Layer = 1。標準 Scale = 1.5、コンテナは 1.1。

`ForegroundPipes.prefab` は既存 ForegroundElement / ParallaxLayer2D を使用。WorldFixed、Order = 30、Opacity = 0.85、Player と重なると薄くなる。衝突判定なし。

`MachineCityBackground.prefab` は空と都市の 2 層。既存 ParallaxBackgroundLayer を使用し、横視差 = 0 / 0.14、縦視差倍率 = 0.25、左右ミラーループを有効化。描画順 -1000 / -900。カメラ未指定なので実行時 Camera.main を取得する。

すべて Point、圧縮なし、Mip Map なし。背景は 100 PPU。生成元 PNG は `Source/` に保存し、取り込み時は最近傍縮小、均等なタイル分割、装飾の透明余白除去だけを行う。

## 確認用シーン

`Assets/Scenes/Stage2_MaterialPreview.unity` は見た目とサイズ比較用。PlayerScaleReference は Stage1 の現在の Sprite と Scale を使用した静止表示で、Player 本体・入力・ゲーム進行は持たない。このシーン自体は Build Settings へ登録しない。

## 操作可能な Stage2

`Assets/Scenes/Stage2.unity` を開いて Play。Stage1 のシーンを複製して内部参照を維持し、Player・Morning Star・鎖・追従カメラ・HP・ポーズ・CRT・BGM・登場演出をそのまま使用する。操作や物理のスクリプトと既存 Prefab は変更していない。

新しい地面の平地・2段の段差・ジャンプで越える6単位の穴・上段足場・ゴール区間を配置。装飾6種類を計13個、前景配管を4個、視差背景2層を配置した。途中にチェックポイント2か所、落下復帰領域、Magnet、宝石3個と、終点に既存のGoalPointを配置。装飾は衝突しない。

スタート位置は `(-8, 3.298095, 0)`。最初の地面の接触高さは `1.238095`。カメラ・移動・ジャンプ・鉄球の設定は Stage1 から継承し、参照・配置と登場演出の着地座標だけを Stage2 に合わせた。ステージ名は既存の `ステージ名sprite_1`（紅蓮の機巧都市）を使用する。

`MachineCityVisuals.asset` は WarmRuins を複製した Stage2 専用設定。花びらを無効にし、暖色の霞と控えめな粒子を設定。元の演出 Profile は変更しない。

キャラクター・チェックポイント・ゴール・宝石を背景になじませるため、同じ Profile の Lighting / Ambient Color を `(1, 0.83, 0.70, 1)`、Ambient Intensity を `1` に設定。さらに World Tone Color `(1, 0.65, 0.35, 1)`・World Tone Strength `0.22`・World Saturation `0.92` を設定し、結晶のピンクも残しながらアンバーに寄せる。既存の ScreenVisualController と Sprite Shader が描画時だけ適用する。元 PNG・SpriteRenderer の色・アニメーション・UI の色は変更しない。新項目は既定で強さ `0`・彩度 `1` のため他の Profile は従来表示を維持する。元の白寄りの環境光は `(1, 0.97, 0.94, 1)`。

背景マテリアルも Stage2 専用の `MachineCitySkyAtmosphere.mat` と `MachineCityDistrictAtmosphere.mat` を使用。霞の色は `(0.68, 0.37, 0.28)`、強さは空 `0.03`・都市 `0.08`。Stage1 の青い霞や共有マテリアルを上書きしない。

ポーズ・死亡メニューから Stage2 にリトライできるよう、Build Settings の既存シーン順序を維持して Stage2 を末尾に追加した。Title の START は引き続き Stage1 に進む。素材確認用シーンは操作可能な Stage2 と別に保持する。

通常の配置は Tile Palette を選び Ground1 / Ground2 に描く。装飾 Prefab は足元を地面の接触線に合わせて配置する。

生成方式と最終プロンプトは `GenerationPrompts.md` を参照。

## 確認

Editor で9枚の寸法・PPU・Physics Shape、Palette の GridPalette sub-asset、装飾と前景に Collider がないことを確認。平地の継ぎ目を含む56地点の下向き Raycast で接触面 y = 1.238095 を確認。Player の静止 Sprite の足先を同じ高さに配置し、Camera で描画して見た目を確認した。コンパイルエラーなし。

操作可能な Stage2 は Play Mode に入り、Input System の仮想 Gamepad で登場完了、移動・追従カメラ、South のジャンプ・着地、右スティック射出、East の回収、Start のポーズ、East の再開を確認。実配置のチェックポイント接触、落下時の HP -1 とチェックポイント復帰、ポーズからの Stage2 リトライと HP・チェックポイントのリセットも確認した。検証中の Error / Exception は0件。

物理コントローラーでの操作感、コース全体の難易度、ゴール破壊までの通しプレイはユーザー確認が必要。蒸気・歯車は静止素材。
