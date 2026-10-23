# 大きなピンクの木

追加機能。既存の足場・苔の柱・ステージ画面を参照し、和風の桜ではなくピンクの葉の木として画像生成した。

- 画像: `Assets/Art/Environment/PinkTree_Large.png`（透過 PNG、元画像は保持）
- Prefab: `Assets/Prefabs/Environment/PinkTree_Large.prefab`
- Material: `Assets/Art/Environment/PinkTreeWind.mat`
- Shader: `Assets/PinkTreeWind.shader`
- 配置: `CompletScene` の開始地点の奥、`(-13.5, -2.7933412, 1.5)`。足場の実際の Collider 表面から根元を少し埋めている。
- 大きさ: 約 `8.53 x 7.90` ユニット。Transform Scale は 1。
- 描画: Default / Order in Layer `-10`。足場・キャラクター・既存ギミックより奥。
- Collider / Rigidbody / ゲームプレイ用スクリプト: なし。

## Import / Inspector

Sprite Single、Full Rect、Point、Clamp、Mip Maps なし、圧縮なし、Max Size 256。実効テクスチャは `256 x 237`。

Unity はインポート時の縮小率に合わせて Sprite の PPU も補正するため、元画像側の Pixels Per Unit は `152.7848`、インポート後の実効 PPU は `30`。地面タイルの実効ドット密度に合わせている。

Pivot は `(0.63, 0.019)`。Shader の非表示 `_PivotX` も `0.63`。個別の透過テクスチャを使い、Sprite Atlas に入れない。

揺れは専用の Sprite Shader だけで行う。共有の時間・入力・カメラ・物理には書き込まない。下側 36% は固定し、上側ほど風の影響を増やす。

- Canopy Sway: `3.2` テクスチャピクセル
- Leaf Rustle: `0.75` テクスチャピクセル
- Wind Speed: `1.05`
- Wind Phase: `0`
- Stationary Trunk Height: `0.36`

横方向の余白を描画時に追加し、端の葉が風で切れないようにする。風の変位はテクスチャの 1 ピクセル単位。透明な輪郭の半透明ノイズは Shader 内で除去する。

## Image generation

Built-in `image_gen` を使用。参照は実際のステージのカメラ画像、`Assets/image_/地面ドット絵.png`、`Assets/image_/Foreground/NearIvyPier.png`。初期の二案は採用せず、プロジェクトには最終案のみを保存した。

最終生成プロンプト:

> Use the attached ACTUAL GAME SCREENSHOT plus its ground tile and ivy pillar as strict visual STYLE REFERENCES. Generate a matching NEW ISOLATED TRANSPARENT ENVIRONMENT SPRITE: ONE LARGE PINK-LEAVED EUROPEAN FANTASY TREE, as if painted by the same pixel artist who drew the ground and ivy pillar. Output ONLY the complete tree on genuine alpha transparency. Do not copy any scenery, characters, ground platform or pillar. The screenshot shows the required game world: cool blue floating castle ruins, crisp dark purple outlined stone tiles, yellow-green moss, warm light and blue-violet shadows. Match the reference sprite rendering: selective dark plum pixel outlines, small stepped edge clusters, angular organic pixel shading with 3-4 tonal bands, warm peach edge highlights, rich blue-violet underside shade, restrained small light accents, no soft rendered gradients. The last two reference images are the most important for brushwork and pixel treatment. The tree should be rendered at the visual complexity of roughly a 192x192 native sprite, enlarged nearest-neighbor, with readable 1-3 pixel leaf clusters. Neither tiny high-resolution dithered details nor huge flat 16-pixel geometric blocks. The tree is a large natural upright oak-like tree with an irregular rounded spreading crown of ordinary broad PINK LEAVES, no flowers. Canopy occupies upper 65 percent, stout gently bent trunk lower 35 percent with 2-3 sturdy branches partially visible through small gaps. Crown is contiguous and natural, softly irregular masses, not four separate candy blobs, not a triangular tree, not a bonsai or ornamental Japanese shape. Pink foliage palette: dusty rosy mauve base, muted warm pink sunlit leaves, limited peach-pink highlights, desaturated blue-purple deep leaf shadows, dark violet selective silhouette outline. Think a pink-tinted counterpart of the mossy vegetation in the reference, not cherry blossom petals. Keep detail density in harmony with the reference tiles. Cool gray-brown trunk with purple shadow and warm gray highlights, a few broad bark facets rather than carved swirls, modest yellow-green moss on the base matching the attached ground. Short low simple roots meet a flat ground plane at the bottom; no massive exposed root web. Full tree side view for a 2D platformer, approximately as wide as tall. Leave clear transparent margins all around and do not crop any branch or root. No dangling flower sprays, no tiny blossom symbols, no cherry blossoms, no Japanese motifs, no blossom particles, no watercolor, no 3D, no blur, no smooth painted edges, no oversaturated candy pink, no text, no sprite sheet. The result must belong beside the attached exact ground and pillar sprites.

## Scene の保存

作業開始時点で開いていた CompletScene は未保存変更あり。元のディスク版と未保存状態を作業用出力先へ別々に退避した。

配置は Unity で作成し、ディスク上の Scene には Unity が生成した木の PrefabInstance と SceneRoots の参照だけを統合した。Scene 保存時に現れる既存スクリプトの追加シリアライズは含めていない。開いている Scene の既存の未保存状態は維持する。

## 検証状態

- Shader の Unity コンパイル成功、Shader エラーなし。配置プレビューで Sprite の描画とドット密度を確認。
- 地面への Raycast で表面 `y=-2.7333412` を取得し、根元を `y=-2.7933412` に修正。
- PNG / Material / Prefab / Scene の参照を確認。木は一本で、Collider / Rigidbody なし。
- Scene のディスク差分は木の追加 58 行のみ。既存のユーザー編集は未保存のまま維持。
- 木のインポート以降の Editor.log にエラー・例外の該当行なし。
- 初回は Unity MCP の応答停止により揺れの表示確認が未完了だった。その後の確認では、風の位相を変えた実描画と Play Mode の時間経過の両方で枝葉の変化を確認。下部の比較ピクセルは変化なし。
- 初期値 `1.3 / 0.45 / 0.85` でも処理は動いていたが、変位が最大 1〜2 ドット程度で分かりにくかった。上記の揺れ幅・速さへ一度調整し、Play Mode で再確認した。
- この調整では Shader の既定値、Material、本文書のみを変更。画像・Prefab・Scene の配置は変更していない。揺れの強さの最終的な好みはユーザーが確認する。
- 調整後の Shader コンパイル成功。Play Mode 終了後の Console Error / Exception はいずれも 0 件（直近 10 分）。実際に再生中の木を 72 フレーム、約 6 秒間撮影し、揺れの確認動画を作成した。
