# 画面演出システム

CompletSceneに接続済み。`ScreenVisuals`のInspectorでマスターON/OFFと、参照しているProfileの全設定を編集できる。実行中にも反映される。機能追加・調整であり、Player、鉄球、入力、CameraFollow、Time.timeScaleの本番処理は変更していない。

## 調査と描画構成

- Unity 6000.4.2f1、Built-in Render Pipeline、StandaloneWindows64、既存品質Ultra。
- URP、Renderer Asset、Volume、Cinemachineを使う構成ではない。既存のCamera＋PixelPerfectCameraを維持。
- 背景は`ParallaxBackgroundLayer`の3層。既存の青白い空気遠近マテリアル、透明部分、Point Filterを保持。
- Sorting Layerはmorningstar / Default / chain。GameObject Layerは既存設定を保持し、新設していない。
- HUD/メニューは元々Screen Space Overlay。既存CRTが実行時にCameraへ変換するため、新ビネットは独立Canvas（order 50）に描画し、HP（100）/ステージ名（400）/メニュー（650以上）より下へ配置。UIへの今回のぼかし・霧・局所光の適用はない。既存CRTの見た目は維持。
- Scene固有の管理オブジェクト。DontDestroyOnLoadや追加カメラを使わない。新システム専用の全画面RenderTextureもない。

## 機能

|機能|実装|
|---|---|
|ビネット|uGUI上の数学的グラデーション。色、強度、滑らかさ、丸み、縦横比対応。UIの背面。|
|背景/周辺ぼかし|背景Shader内の中心＋上下左右5サンプル。背景層ごとの倍率と画面周辺の滑らかな係数を合成。Player/敵/地形/UIはサンプリング対象外。|
|空気遠近・霧|既存の霞に距離係数による色補正を追加。霧はワールド座標の連続的な波形から計算し、タイル境界で模様を切らない。背景だけに薄く適用。|
|環境粒子|画像生成した4種の透過ドット素材、ワールド空間シミュレーション、寿命の始終でフェード、低速のランダム速度/ノイズ。最大96個。カメラが大きく移動した場合は古い粒子をクリア。|
|2Dライティング|Built-in用のSprite/Tilemap表面Shader。環境光と最大8個の局所光を合成。GameObject Layerマスク、半径、減衰、弱い揺らぎを設定可能。影生成や全画面Bloomは追加しない。|
|パララックス|既存のCover/鏡像ループを維持。層ごとのScroll/Depth/Tint/Contrast/Saturation/Blur/Fog/LightingをProfileから設定。スケール再計算結果が変わった場合だけTransformを書き込む。カメラ再生成時も視差の位相を保持。|

局所光は`ScreenLocalLight2D`を任意のオブジェクトへ追加して配置する。既存GoalPointのクリスタルに1灯を接続済み。`Emission Source`のSprite非表示/透明化に合わせて光も消える。磁石には光源を追加していない。互換性を確認した`Sprites/Default`のSprite/Tilemapだけを実行時マテリアルへ差し替え、他の独自Shaderは保持する。背景は既存専用Shaderに拡張している。

## 操作と初期値

`ScreenVisuals`を選択。`Visuals Enabled`で元画面と比較できる。`Stage settings (shared profile)`以下に設定を展開する。Profileの編集は同じアセットを参照するSceneにも影響するため、ステージを増やす場合は複製して割り当てる。

|設定|SkyRuins初期値|
|---|---|
|品質|High|
|ビネット|強度0.15、滑らかさ0.65、丸み0.65|
|背景ぼかし|強度0.18、半径1テクセル|
|周辺ぼかし|強度0.12、開始範囲0.6|
|距離色補正|0.12、青白色(0.70, 0.83, 0.96)|
|霧|不透明度0.045、速度(0.025, 0.006)、変化0.55|
|粒子|毎秒5、速度0.12、不透明度0.24、可視サイズ目安0.09|
|環境光|色(0.96, 0.98, 1.0)、強さ1.0|
|クリスタル局所光|強度0.25、半径4.5、減衰1.7、揺らぎ0.035|
|背景Scroll|元のFar=0、Mid=0.14、Front=0.24を保持。OverrideをONにした層だけ変更。|
|背景Depth|Far=1、Mid=0.65、Front=0.25|
|背景Blur倍率|Far=1、Mid=0.45、Front=0.12|

全演出/コンポーネントをOFFにすると、自分が差し替えたマテリアル参照だけを元に戻し、追加粒子/光/ビネットを無効化する。元アセットとInspector調整値には書き込まない。既存の背景空気遠近やCRTは元の演出なので残る。

Profile未指定でも安全な既定値で動作する。未登録の背景層は既存の絵を優先する。`SkyRuins`をCompletSceneへ接続し、将来の別ステージ用に`MoonlitRuins`と`WarmRuins`を用意した。TitleSceneは専用の既存演出を維持し、本システムを接続していない。

## 負荷

- High: 最大8灯、通常の背景ぼかし、環境粒子。
- Medium: 最大4灯、ぼかしの混合量と粒子発生量を半減。
- Low: 背景/周辺ぼかしの追加サンプルを停止、最大2灯、粒子発生量20%。
- ぼかしOFF時は追加のテクスチャサンプリングをスキップ。粒子/光源OFFも個別に選択可能。
- マテリアルは元マテリアル＋Layer単位で共有。背景の複製タイルも同じマテリアルを使う。
- 背景3層は最大5テクスチャサンプル、追加ビネットは1枚。カメラ追加、GrabPass、毎フレームのTexture生成を行わない。
- 新規Rendererの検出は再利用Listで2秒間隔。必要なら`Refresh spawned renderer bindings`で即時反映。既存フィードバックの一時的なマテリアル変更を上書きしない。
- 生成画像は105,522 bytes。インポート上限512、Point、非圧縮、Mipなし。追加の常駐RenderTextureは0。

## 生成画像

`Assets/Art/ScreenVisuals/RuinsDustAtlas.png`。既存の浮遊遺跡2枚を画像生成へ参照入力し、青/薄紫/象牙/淡金の小さなドット片を透過2×2アトラスとして生成した。元画像は上書きしていない。数学的なビネット・霧・光には画像素材を追加していない。

## 変更ファイル

- 修正: `Assets/BackgroundAerialPerspective.shader`, `Assets/Script/ParallaxBackgroundLayer.cs`, `Assets/Scenes/CompletScene.unity`（ScreenVisualsとGoalPointへの光源追加のみをコミット対象にする）。
- 新規Runtime: `Assets/Script/ScreenVisualController.cs`, `ScreenVisualProfile.cs`, `ScreenLocalLight2D.cs`。
- 新規Shader: `Assets/ScreenVisualSprite.shader`, `ScreenVisualVignette.shader`, `ScreenVisualParticles.shader`, `ScreenVisualLighting.cginc`。
- 新規Editor: `Assets/Editor/ScreenVisualSetup.cs`, `ScreenVisualChecks.cs`。
- 新規設定/素材: `Assets/Art/ScreenVisuals/{SkyRuins,MoonlitRuins,WarmRuins}.asset`, `RuinsDust.mat`, `RuinsDustAtlas.png`。
- 上記新規Unityアセットとフォルダに対応する`.meta`。既存GUIDを保持。Prefab/Packages/ProjectSettingsの変更なし。

## 検証

`Tools > Tekkyu > Verify Screen Visuals`で統合確認を再実行できる。保存済みCompletSceneから開始し、テスト用Profile複製と一時オブジェクトだけを操作する。確認後にEdit Modeへ戻す。結果と比較画像は`%TEMP%/tekkyu-screen-visuals/validation/`へ保存する。

確認範囲は個別/全体ON/OFF、元マテリアル参照復元、粒子の発生・消去、High/Medium/Low、光源マスク、画素の実測による環境光/局所光、3タイルの連続配置、正負の大きなカメラ移動、UI順序、Point Filter、カメラ再生成、16:9/4:3/縦長の背景カバー、TitleSceneへの遷移とゲームScene再読み込み。比較画像も目視確認する。

FPSは同一の停止画面でOFF/ONを各120フレーム測定する。Editor全体のGC値はMCP/Editorも含み、このコンポーネントだけの割当量を表さない。GPU時間・overdrawの専用計測、実機ビルド、ゲームパッドによる全ステージ通しプレイ、実ウィンドウのサイズ変更、動作中の主観的なちらつき/最終ゲームフィールは未検証。最終的な見え方と操作感はユーザーのPlay Mode確認が必要。

2026-10-21の最終実行はPASS、Runtime Error/Exception=0、C#/Shaderコンパイルエラーなし。停止画面の測定はOFF 332.8fps / 中央値2.90ms / p95 3.75ms / DrawCall平均19、ON（粒子38個）290.0fps / 中央値3.31ms / p95 4.37ms / DrawCall平均21。Editor全体のGCはOFF平均9,918 / ON平均9,599 bytes/frameであり、本システム単独のGC測定値ではない。短時間のEditor測定なので実機性能を保証する値ではない。

独立したSprite描画の画素値でも光を確認した。輝度は基準0.251、局所光ON 0.310、対象Layer除外0.251、環境光75%で0.216。背景/キャラクター/UIのON/OFF比較画像を確認した。配置の隙間についてはタイルBoundsの一致を自動検証しており、移動中の見え方の最終評価は未実施。

Scene内の作業開始前の未保存編集は一時フォルダへ退避して保存内容に維持した。その差分は今回のコミットへ含めない。
