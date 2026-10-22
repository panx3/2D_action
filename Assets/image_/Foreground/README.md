# カメラ直前の巨大な前景

小型のステージ装飾ではなく、上下が画面外へ続く巨大な柱です。明るい遺跡の青紫の石材、緑のツタ、小さなピンクの花に合わせています。旧小型PNG・Prefab・3か所の配置は撤去済みです。

## 素材・Prefab

- `NearIvyPier.png`: 125 × 300 px、巨大なツタ付き角柱。
- `NearBrokenGateway.png`: 148 × 300 px、崩れたアーチの肩を残した柱。
- Spriteは100 PPU（既存の背景装飾と同じ）、Point、圧縮なし、Mipmapなし、中央Pivot。透過余白を除去した後、最近傍で縮小しています。
- `Assets/Prefabs/Foreground/NearForegroundElement.prefab`が共通の基底Prefab。素材別Variantは`NearIvyPier.prefab`と`NearBrokenGateway.prefab`。
- XY Scaleは10.666667。高さ32、幅約13.33 / 15.79 world units。通常カメラの高さ12.375に対し約2.6画面分。
- `NearForegroundSoft.mat`は`IronBallGirl/ForegroundSoftSprite`を使用。透明色のRGBが縁へ滲まないpremultiplied alphaの小さな9点フィルター。画面全体のぼかし処理は追加していません。

## Inspectorの既定値

| 項目 | 値 |
|---|---|
| Sorting Layer / Order | Default / 30 |
| Tint | (0.86, 0.88, 0.94, 1) |
| Opacity | 1 |
| Movement | Parallax |
| Camera | 未指定ならMain Camera |
| Horizontal / Vertical Multiplier | 1.25 / 1 |
| Fade When Player Covered | ON |
| Covered Opacity / Fade Duration | 0.6 / 0.15秒 |
| Material: Near Camera Softness | 0.85テクセル。0でぼかしなし |

画面上の移動倍率1がワールド固定、1.25が地形より25%速い前景です。ワールド変位は`cameraDelta * (1 - multiplier)`。既存の`ParallaxLayer2D`がCameraFollow後に移動し、`ForegroundElement`がその後で重なり時の透明度を更新します。XY倍率は0～2、固定表示はWorldFixedを選択します。直接`ParallaxLayer2D`を編集せず`ForegroundElement`から設定してください。

薄くする判定はPlayerの既存ColliderのBoundsを読むだけです。前景自体にCollider/Rigidbodyはありません。Playerを持たないSceneでは通常の不透明度を維持します。

現在のPlayerはOrder 2、鎖8、鉄球9、UI100以上です。CRTはUIをScreenSpaceCameraに切り替えるため、UIを越えるSorting Layerを新設せず、Default内のOrder 30を前景用に使用しています。

## CompletScene

`ForegroundRoot`配下に2つのPrefabインスタンスを配置しています。

| 名前 | 編集時ワールド座標 | 表示区間 |
|---|---|---|
| FG_CloseIvyPier | (-4, 1.5, 0) | 序盤。画面手前を大きく横切る |
| FG_CloseBrokenGateway | (182, 2.5, 0) | 後半の上部通路付近。左右反転 |

足場に接地させていません。パララックスのため、表示時の座標は編集時の座標と異なります。既存の地形・敵・ギミック・カメラ・背景3層は変更していません。

## Stage2

素材別Prefabを別Sceneへ配置するだけで使用できます。Scene固有の参照、自動生成、DontDestroyOnLoadはありません。ParallaxにはMainCameraタグのCameraか明示指定したCameraを使います。Sceneの通常オブジェクトとしてアンロード時に破棄されます。

## 画像生成

Built-in image_genで2種類を新規生成。元の装飾画像を色調とピクセル表現の参照に使用し、旧小型画像は使用していません。生成プロンプトは以下です。

### NearIvyPier

Create a NEW EXTREME CLOSE-UP foreground asset for this bright fantasy pixel-art platform game, editing from the supplied art references. This is NOT a small prop and NOT a little column standing on a platform. It is a COLOSSAL very thick ancient stone architectural pillar immediately in front of the camera, meant to occupy about 45 percent of a widescreen game view and extend far BEYOND BOTH the top and bottom edges of that view. Make a single broad monolithic architectural pier, width-to-height about 0.42, gently leaning 6 degrees; the top and bottom of the shaft are cut off by the image canvas as if the column continues indefinitely. No capital, no complete base, no floor, no little pile of rubble. Bold massive blocky silhouette with substantial opaque stone area. Front face 70 percent wide, narrow right shadow face. Wide large rectangular lavender-grey stone blocks, broad vertical carved recessed bands, large sparse cracks and chipped outer corners, thick very dark blue-purple shadows; pale warm grey/pink-lavender light facets matching the game's masonry, but foreground overall slightly darker. Mossy green ivy clusters and a handful of tiny subdued pink flowers cling to one edge; no metal, no factory, no sci-fi, no glowing elements. Crucial: draw HUGE CLOSE stone surfaces with broad simple pixel clusters, not a miniature decorative object photographed at a distance. Orthographic 2D side view, no isometric floor. Coarse hand-clustered pixel art on a logical grid about 128 wide by 300 tall, limited cohesive palette, no smooth painting, no fine noisy detail, no antialias, no baked blur. Transparent alpha everywhere outside the enormous pillar's stair-stepped silhouette. Subject fills the canvas; real transparent PNG, no checkerboard. The in-game camera will display only the middle section of this huge asset.

### NearBrokenGateway

Create a second NEW EXTREME CLOSE foreground architectural asset matching these pixel-art references: a gigantic broken stone gateway PIER with a fractured arch shoulder branching out toward the right at the top. It must read as architecture RIGHT BESIDE THE CAMERA, designed to block a large part of a 2D platform game screen, not a small floor decoration. One enormous tall solid pier on the LEFT 65 percent of the image, with a massive broken curved arch shoulder reaching into the upper right and open transparent space below that shoulder on the right. The shaft continues beyond BOTH the top and bottom image boundaries: crop it at top and bottom, no complete capital, no pedestal or floor. Wide, bold structural silhouette width-to-height about 0.52. Thick warm lavender-grey stone blocks with broad slate blue/indigo shadow faces, heavy carved horizontal seam bands, large chipped corners and fractures, chunky moss and green ivy with occasional tiny subdued pink flowers along the silhouette. Match the cheerful sky-ruins game's stone and vegetation palette, slightly darker foreground tones, no iron, no industrial or sci-fi shapes, no bright banner or glow. Orthographic side view, coarse hand-clustered pixel art with large readable pixel shapes, logical resolution approximately 150x300px, limited palette, no noisy tiny texture, no smooth painting, no antialias or baked blur. Real transparent PNG outside the solid silhouette and in the arch opening, no checkerboard or background. Fill the frame with huge CLOSE stone surfaces. The in-game view will show only the middle of this asset, with the near pillar extending far offscreen.
