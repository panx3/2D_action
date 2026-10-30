# Stage2 地面タイル拡張

既存の錆び地面9枚を残し、70枚を追加した素材集。ステージ本体の配置や操作コードは変更しない。

## 使うパレット

Tile Palette で `Stage2_MachineCity` を選び、Stage2 の既存 `Grid/Ground1` または `Ground2` に描く。

上から順に **錆び地面 / 鋼鉄の鉄板 / 耐火レンガ / 金属格子**。地面3系統は左に基本の3×3、右下に内角4種、その上に坂の組タイルを配置した。既存9枚のパレット位置は変更していない。

| パレット領域（セル座標） | 内容 |
|---|---|
| x=0..2, y=0..2 | 既存の錆び地面9枚 |
| x=6..11, y=0..2 | 錆び地面の内角・坂 |
| x=0..2 と 6..11, y=-7..-5 | 鉄板の基本・内角・坂 |
| x=0..2 と 6..11, y=-14..-12 | レンガの基本・内角・坂 |
| x=0..3, y=-20 | 格子の左端・中央・右端・単独足場 |

## 坂の描き方

- 各材質の右側に、左から **45度右上がり / 45度右下がり / 緩い右上がり / 緩い右下がり** が並ぶ。
- 45度は **縦2セル×横1セル**、緩坂は **縦2セル×横2セル** の範囲をまとめて選択して描く。1枚だけでは坂が完成しない。
- どちらも低い側から高い側へ1セル（2 world units）上がる。低い側は同じ行の平地、高い側は1行上の平地に接続する。
- 上段の透明部分を含めて組タイルとして使い、坂の上から別の埋めタイルを重ねない。坂の下は同じ材質の Fill で埋められる。
- 内角は `InnerTopLeft / InnerTopRight / InnerBottomLeft / InnerBottomRight`。床と壁、天井と壁が内側で接続する場所に使う。
- 格子の中央は繰り返し配置でき、単独足場は両端付き。通常の固い足場で、下からのすり抜けや落下操作はない。

## 配置見本

`Assets/Prefabs/Environment/Stage2/MachineCityTerrainSamples.prefab` を空の検証用シーンへ配置すると、3材質それぞれの平地・左右の45度坂・緩坂・内角・格子を確認できる。見本には Floor タグ、静的 Rigidbody2D、TilemapCollider2D、CompositeCollider2D、既存の GroundTileSeamBlender を継承している。生成用・検証用のランタイムスクリプトは不要。

本番 Stage2 には見本を自動配置していない。見本Prefabを本番へ置く場合は、既存地形と重ならない位置で使用する。

## 規格

- 全スプライト42×42px、42 PPU、中心Pivot。矩形Gridは `(2, 2, 0)`、Tile Transformは `(2, 2, 1)`。
- Point、圧縮なし、MipMapなし。外周・格子の透過を保持。
- Collider TypeはSprite。Custom Physics Shapeを保存し、画素の凹凸とは独立した直線の接地面を使う。
- 平地はタイル上端から16px下、sprite-local y=5pxで接触。側端の透明余白4px、底端8pxも既存規格を継承。
- 坂の接地線は低端26px / 高端68px（組タイル下端からの高さ）。42px境界で分割した輪郭をCompositeで結合する。
- 格子の衝突範囲はタイル下端から18..26px。模様の穴は衝突の穴にはならない。

追加画像は `Assets/image_/Stage/Stage2/`、Tileは `Assets/Tiles/Stage2/{RustExtensions,Steel,Brick,Grate}/`。生成元は `Source/`、生成条件は `TerrainGenerationPrompts.md` に保存。

## 検証記録（2026-10-30）

- 全79枚のSprite参照・42px寸法・42 PPU・倍率・Custom Physics Shapeと、6画像の取り込み設定を確認。
- 見本の平地・45度坂・緩坂の接続部・格子を1,251地点でRaycast確認。設計した接地線との最大誤差は0.000031 world units。
- 実際のPlayerと鉄球、Input Systemの仮想Gamepadを使用し、3材質それぞれで左右移動・平地ジャンプ着地・緩坂上りと鉄球牽引・緩坂下り・格子上ジャンプ着地の計18ケースをPlay Mode確認。検証中の追加Error/Exceptionは0件。物理コントローラーと最終的な操作感は未確認。
- 本番Stage2は保存せず、元の未保存状態、Ground1=420セル / Ground2=8セル、Playerの編集時配置を保持。検証用オブジェクトと入力デバイスはPlay終了時に破棄。
