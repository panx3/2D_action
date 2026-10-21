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
|環境粒子|画像生成した4種の透過ドット素材、ワールド空間シミュレーション、寿命の始終でフェード、低速のランダム速度/ノイズ。最大96個。画像セルの透明な余白をUVで除き、可視サイズを画角に合わせる。起動時にPrewarmし、カメラが大きく移動した場合は古い粒子をクリア。|
|2Dライティング|Built-in用のSprite/Tilemap表面Shader。環境光と最大8個の局所光を合成。GameObject Layerマスク、半径、減衰、弱い揺らぎを設定可能。局所光の周囲には専用Shaderの淡い光彩を表示。影生成や全画面Bloomは追加しない。|
|パララックス|既存のCover/鏡像ループを維持。層ごとのScroll/Depth/Tint/Contrast/Saturation/Blur/Fog/LightingをProfileから設定。スケール再計算結果が変わった場合だけTransformを書き込む。カメラ再生成時も視差の位相を保持。|

局所光は`ScreenLocalLight2D`を任意のオブジェクトへ追加して配置する。既存GoalPointのクリスタルに1灯、既存の街灯4本の電球位置に各2灯を接続済み。街灯は既存TilemapのSprite・セル位置・変換行列から位置を求めている。`Emission Source`のSprite非表示/透明化、`Emission Owner`のTilemap描画停止に合わせて光も消える。`Glow Enabled / Intensity / Radius`で光彩を制御する。磁石には光源を追加していない。互換性を確認した`Sprites/Default`のSprite/Tilemapだけを実行時マテリアルへ差し替え、他の独自Shaderは保持する。背景は既存専用Shaderに拡張している。

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
|粒子|毎秒5、速度0.12、不透明度0.48、希望ワールドサイズ0.09。余白除去後の描画面を6〜10画面ピクセルに補正し、中心の絵柄を約3〜6ピクセルに保つ。さらに個体差を付ける。|
|環境光|色(0.96, 0.98, 1.0)、強さ1.0|
|クリスタル局所光|強度0.25、半径4.5、減衰1.7、揺らぎ0.035|
|街灯の各電球|暖色(1, 0.73, 0.34)、強度0.48、半径3.8、光彩強度0.28、光彩半径1.5|
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
- 視認性修正で追加: `Assets/ScreenVisualGlow.shader`と対応`.meta`。
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

## 粒子と光の視認性修正

最初の実装では粒子の生成アトラスの透明余白が大きく、通常画面の可視部分が約1〜2ピクセル・不透明度24%になっていた。また開始地点の街灯へ光を接続しておらず、通常の開始画面では局所光0灯だった。アトラスのアルファ境界を測定して各セルの内容をUV範囲(106/256,82/256)-(162/256,152/256)から描画するよう修正し、SkyRuinsの元の不透明度既定値だけを48%へ更新。粒子数と速度は保持した。既存の街灯へ局所光と数学的な光彩を接続し、起動直後の自然発生・光彩表示も確認対象に追加した。

修正後は53項目PASS、Runtime Error/Exception=0、C#/Shaderコンパイルエラーなし。手動Emit/Simulate前の通常起動画面も保存して確認した。光彩だけをOFF/ONした比較画像でも街灯周囲の暖色の差を確認。今回の縦長Game Viewでの測定はOFF 238.3fps / 中央値4.12ms / DrawCall平均19、ON（粒子49個）218.7fps / 中央値4.36ms / DrawCall平均22。測定条件が前回と異なるため前回値との直接比較はしない。実機とゲームパッド通しプレイ、最終的な見た目の評価は未実施。

## 操作しなくても舞い続けるピンクの花びら

新たに画像生成した`PinkPetalsAtlas.png`の4種類を`PinkPetals.mat`へ接続。既存のタイトル用花びらと遺跡背景を画風の参照にし、切れ込み・斜め・横向き・丸い花びらの異なるシルエットを作成した。元のタイトル用素材は変更していない。ゲーム用インポートは128×128、Point、Mipなし、非圧縮、透明背景。

`ScreenVisuals > Stage settings > Petals`でON/OFF・数・大きさ・不透明度・風速・落下速度・揺れ・色を設定する。SkyRuinsの初期値はON、28枚、基準18画面ピクセル、不透明度0.85、風速1.15、落下0.35、揺れ0.65。ほかのプリセットは明示的に有効化するまでOFF。既存の霧・ホコリ・ライト設定は保持。

`AmbientPetalField`は既存ScreenVisualControllerに所有される描画補助で、別の管理オブジェクトやカメラを作らない。48枚分の固定配列・1つの動的Meshを初期生成し、毎フレームのオブジェクト/Texture生成なしで動かす。初期状態から画面内へ分散配置し、大小による速度差、ゆっくり変化する風、上下の揺れ、幅の変化による表裏の返り、画面外でのループを組み合わせる。長時間待機しても風の時計を一斉リセットしない。

画角に合わせた画面上の大きさを保ち、通常のカメラ移動には奥行き別の視差を付ける。カメラの大きな瞬間移動/再生成ではフィールドを再配置する。描画順はDefault:-650で、背景/ホコリより手前、地形/主人公/敵/UIより奥。UIや主人公を覆わない。High28枚、Medium20枚、Low11枚。Time.timeScale=0のポーズ中は動きを止める。

追加/変更: `Assets/Script/AmbientPetalField.cs`, `ScreenVisualController.cs`, `ScreenVisualProfile.cs`, `Assets/ScreenVisualPetals.shader`, `Assets/Editor/AmbientPetalSetup.cs`, `ScreenVisualChecks.cs`, `Assets/Art/ScreenVisuals/{PinkPetalsAtlas.png,PinkPetals.mat,SkyRuins.asset}`と新規アセットの`.meta`。CompletSceneには既存管理ComponentへのMaterial参照1個だけを追加。Prefab、タイトルScene、Packages、ProjectSettings、入力/物理/カメラ追従の本番処理は変更しない。

花びら追加後の統合確認は61項目PASS、Runtime Error/Exception=0、C#/花びらShaderコンパイルエラーなし。操作なしでの継続的な変形、通常起動での可視数、個別/マスターOFF、ポーズ中の停止、シーン遷移後の破棄を確認した。実際のゲーム画面もキャプチャして目視確認済み。長時間の実機放置、ゲームパッド通しプレイ、最終的な華やかさの評価は未実施。Editorは保存済みCompletScene/Edit Modeに戻している。

### 背景と前景の2層

ユーザー調整により、SkyRuinsの背景側は基準サイズ18→38px、不透明度0.85→0.95に変更した。28枚・風速・揺れは保持。Inspector上の表示名は`Background Petals`、既存の保存フィールド名`petals`は保持。

`Foreground Petals`を独立して追加し、4枚、基準46px、不透明度0.35、風速1.3、落下0.42、揺れ0.65で有効化した。画像・Materialは背景側と共有。配置の種とカメラ移動への反応を変え、別々の花びらが手前を横切る。前景はDefault:40、背景はDefault:-650。キャラクターの描画順2〜6より前、HP/UIの100以上より後ろ。各層のON/OFF・数・サイズ・透明度・速度は個別に調整できる。マスターOFFとComponent無効化時は両方消える。

開始演出でカメラが広角へ変わっても、花びらの画面内の分布を保つよう補正した。従来は開始時の小さい画角に座標が残り、演出後の広い画角で下半分へ偏って見えることがあった。

変更は`AmbientPetalField.cs`, `ScreenVisualController.cs`, `ScreenVisualProfile.cs`, `SkyRuins.asset`と本書。画像、Shader、Scene、Prefabは今回変更/保存していない。Play Modeで2層の可視性、実際の描画順、前景の透明度上限、個別/マスターON/OFF、Component無効化/再有効化を確認。未変更システムの統合テスト再実行、実機の長時間表示、最終的な演出量の評価は省略。

最終確認は背景15枚・前景3枚が画面内に表示され、背景側の描画面は約25〜46px。開始演出後にも上下へ分散し、画面上部の花びらが見えることを実画面で確認した。コンパイル正常、新規Console Error=0。保存済みCompletScene/Edit Modeへ戻し、作業開始前のScene差分は保持した。
