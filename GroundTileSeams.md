# Stage1 地面の内部境界補正

対象は `Stage1 > Grid > Ground1 / Ground2`。PNG、Tileアセット、配置、Colliderには変更を加えない。

## 調整

各地面の `GroundTileSeamBlender` で調整する。

| Inspector | 初期値 | 意味 |
| --- | --- | --- |
| Enable Blending | ON | OFFで元のピクセル表示に戻る |
| Blend Strength | 0.4 | 境界色の補正量 |
| Blend Width | 2 | 境界の片側の幅。元画像のピクセル単位、1〜3 |
| Pixel Preservation | 0.8 | 色の補正量を離散化し、細かすぎる中間色を抑える度合い |
| Dither Strength | 0.25 | 固定4×4パターンのディザ量。時間による変化なし |

タイルの追加・削除は変更通知から自動更新。Inspector変更も再生成する。
Sprite自体・Grid構造を変更した場合はコンポーネントのコンテキストメニュー `Rebuild Seam Cache` を実行する。
新しい地面Tilemapには `Tools > Stage1 > Set Up Ground Seam Blending` を実行する。
この操作は、名前がGroundで始まり、地面ドット絵を含み、TilemapCollider2Dを持つTilemapを検出する。既存の調整値と独自マテリアルは維持する。シーンの保存は手動。

## 方式

元Spriteを一時的にGPUから読み出し、隣接する辺の小さな領域の色差を測る。
模様そのものを平均化するのではなく、小さな色のオフセットを境界帯に適用する。
元の明暗差、元のアルファ、元の描画メッシュを保持する。透明部分と外周1pxは補正しない。
四隅は複数辺の補正を正規化し、補正が重なって強くなることを避ける。

補正色アトラスとセル参照テクスチャはメモリ上だけに生成し、Pointフィルタ・ミップなしで使用する。
既存の描画パスに組み込み、フルスクリーン処理や追加の描画パスは使わない。
ScreenVisualControllerが照明用マテリアルに切り替えた場合も同じ補正を使用する。
毎フレームのタイル走査はなく、変更通知をまとめてキャッシュを再生成する。

変更検知は Unity の [tilemapPositionsChanged](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/tilemaps/tilemap/tilemappositionschanged) と tilemapTileChanged、描画データの受け渡しは [Renderer.SetPropertyBlock](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/renderer/setpropertyblock) を使用。
変更通知の個別セル列には依存せず、対象Tilemapのキャッシュをまとめて更新する。

## 制約

- 同じTilemapの上下左右の接続が対象。別Tilemapとの接続・重なりを横断した補正は行わない。
- 対象はXYの矩形Gridで、セルを満たす静的Tile、軸方向の拡大・反転。斜面、回転、オフセット、アニメーション、パック済みSprite、個別色付きセルなどは元の描画を維持する。
- 複雑な石模様や繰り返し構造を復元するものではなく、内部境界の色の急変を控えめに軽減する。
- キャッシュは起動時と編集時に生成。実行中に大量のタイルを連続破壊する用途では、部分更新などの拡張が必要。
- 今回の地面2枚は計620タイル、GPUキャッシュ約7.3MiB。Editorでの生成は約120ms + 23ms。実機での性能保証値ではない。

## 確認

`Tools > Stage1 > Check Ground Seam Blending` は一時的なPreview Sceneで検証する。
水平・垂直・2×2角、外周・透明度・内部の保護、OFF/コンポーネント無効化、同一結果、追加削除、整数カメラ移動・ズームを画像比較する。
Stage1の実素材でもON/OFF画像を出力する。保存先はConsoleに表示する。ゲーム本体の配置・Collider・カメラは変更しない。

最終的な見た目、非整数倍率での表示、実際のゲームパッド操作感の判断はユーザー確認とする。

Play Modeでも照明用Shaderとの共存とON/OFFを確認。約2万フレームの間、各Tilemapの生成回数は1回のままであることを確認した。
終了・再開時のテクスチャ解放も確認し、修正後の新規Error/Exceptionは0件。
フルステージ走行や実機GPUの負荷測定は未実施。

## 変更ファイル

- `Assets/Script/GroundTileSeamBlender.cs`：生成・キャッシュ・調整・変更検知
- `Assets/GroundTileSeam.shader`、`Assets/GroundTileSeam.cginc`、`Assets/GroundTileSeam.mat`：元の描画内での境界限定補正
- `Assets/ScreenVisualSprite.shader`、`Assets/Script/ScreenVisualController.cs`：既存照明との連携
- `Assets/Editor/GroundTileSeamSetup.cs`、`Assets/Editor/GroundTileSeamChecks.cs`：設定と検証
- `Assets/Scenes/Stage1.unity`：地面2枚のコンポーネント・マテリアル参照
- 新規Unityファイルの各 `.meta`、この説明書

PNG、Tileアセット、Prefab、その他のScene、Packages、ProjectSettingsは変更していない。
