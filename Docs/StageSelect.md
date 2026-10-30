# ステージセレクト

導線は `TitleScene → StageSelectScene → Stage1`。タイトルの既存トランジションとステージの既存開始演出・BGM・ステージ名表示を使います。

## Scene とデータ

- `Assets/Scenes/StageSelectScene.unity`：`Main Camera`、`StageSelectPresentation`（Canvas、Controller、Preview、SE）、`EventSystem`、`StageSelectAudio`。
- `Assets/Data/StageSelect/SkyCapital.asset`：蒼天の浮遊王都、接続先 **Stage1**、開始可能。
- `Assets/Data/StageSelect/MachineCity.asset`：紅蓮の機巧都市、接続先 Stage2、開始無効。作成時点の StageSelect ブランチには保存済み Stage2 Scene がないため、選択とプレビューのみ有効です。
- `Assets/Art/StageSelect/`：オリジナルのピクセル素材、専用ドットフォント。各 Image の Sprite と Controller の Default Cartridge を差し替え可能です。

既存のカセットを複製し、Controller の **Stages** 配列へ追加するとステージを増やせます。Stage Name、Title Sprite、Cartridge Sprite/Tint、Accent、Scene Name、Playable、Unavailable Message、背景3層を設定します。Title Sprite が未設定なら Stage Name を表示します。番号は自動付加しません。Playable が有効でも Build の Scene リストに接続先がなければ開始しません。

Stage2 を統合した後は、その Scene を Build の Scene リストへ登録し、MachineCity の Playable を有効にしてください。背景2枚は `origin/main` から元のパス・meta/GUIDのまま再利用しており、仮の機巧都市背景ではありません。Stage2 本体やギミック一式の別ブランチ統合はこの変更には含みません。

## 描画と遷移

Preview は既存 Sprite を 640×360 の RenderTexture に合成し、ゆっくりドリフトさせます。既存 `CRTFilter.shader` のぼかし/CRTパスをこの小さな映像だけに適用します。前景は Overlay Canvas、Point フィルターのピクセル素材です。StageSelect に全画面 CRTFilterController は置きません。

決定後は非同期ロードを先行し、挿入→電源→CRTズームを再生します。ズームが完了してもロードが終わらない場合は、CRT映像を全画面のまま維持します。Canvas は一時的に DontDestroyOnLoad で残り、読み込まれたゲーム画面へ短く重ね合わせて消えます。Gameplay Camera と CRT 設定は変更しません。`TitleStageTransition.IsTransitioning` の共通ゲートで既存の PlayerStartIntro/BGM/カーソルを待機させ、終了時にゲート・Time.timeScale・UI・RenderTexture・入力の複製を解放します。

## Inspector の主な調整値

Controller：解放時だけGather 1.65秒 / Crack .48秒 / Burst .45秒、切り替え .24秒、整列 .16秒、挿入 .42秒、起動 .38秒、ズーム 1秒、ゲームへの接続 .34秒。Float Height 6、周期 3.4秒、挿入先 `(0, -119)`、挿入縮小 .78（差込口の幅に対応）、Impact Shake 5。選択入力は既存 `UI/Navigate` / `Submit` / `Cancel` の複製を所有し、共有アクションを無効化しません。長押しは .34秒後から .18秒間隔、切り替え予約は最後の1件だけです。決定後の連打・戻る入力は受け付けません。

Preview：960×600、Softness 1.2、Scanlines .32、Scanline Count 240、RGB Separation .9px、Vignette .26、Noise .012、Brightness .9、Curvature .18、Glass Reflection .24。背景各層に Sprite、Tint、Drift を設定できます。

SE：既存の CheckpointActivate、ひびがはいる音、砕ける音を使用。切り替え・挿入・CRT起動・開始不可はオリジナルの短い合成音で、Controller の各 Clip に本素材を設定すれば差し替わります。ゲーム機・テレビ・カセットは、この画面用のオリジナル仮アートです。

## 暗いレトロ空間のビジュアル

2026-10-30の参考画像に合わせ、部屋・棚・壁飾り・大きな見出しを取り除いた構成へ更新しました。`StageSelectVisualUpgrade` は既存Hierarchyの参照・ボタン・Controllerを保ったまま表示を変更します。新しいSceneをBuilderから作る場合も同じビジュアルを適用します。

`DarkBackdrop` は暗い藍色のグラデーションと微細なドット模様。`BackgroundCRT` と `Television` は独立したままで、現在のステージ背景を表示します。CRTは中心 `(0, 94)`、サイズ `768×432`。カセットは `(0, 16)`、ゲーム機は `(0, -208)`。生成素材の透明な余白は原本のまま保持し、RectTransformと別のラベル位置で調整しています。ゲーム機とカセットはPointインポートと専用Surface Materialのピクセル格子で描き、ラベルは既存のステージ名Spriteを使用します。カセットへの番号の追加はありません。

`Assets/Art/StageSelect/DarkRetro/` の Console/Cartridge は、参考画像をスタイルと照明の基準にしてbuilt-in ImageGenで生成した独立した透過素材です。参考画像全体の貼り付けや切り抜き背景は使っていません。原稿・生成プロンプトは同フォルダーの `GenerationPrompts.md`。テレビフレームと矢印は専用の小さなピクセル素材です。旧仮素材も残しており、Spriteを差し替えられます。

Built-in Render Pipelineを維持しています。Overlay Canvas上で `StageSelectSurface.shader` と独立した発光Imageを重ね、実際の3Dライトや全画面Bloomを追加していません。青いCRT光、カセット・結晶のピンク光、床面に相当する控えめな反射、ゲーム機直下の接地影、10個の小さな結晶光を別々に管理します。`StageSelectLighting` の CRT Spill `.13`、Pink Spill `.12`、Floor Reflection `.07`、Mote Opacity `.65`、Breath Period `4.5秒` が調整箇所です。これらは描画のみを変更し、入力、Stage1の接続、ステージ開始演出、Gameplay Camera、ゲーム側CRTには書き込みません。

新しい専用素材は差し替え可能な制作アートです。機巧都市カセットのTintだけは、新しい灰色筐体と色を揃えるため白へ更新しています。他のステージデータ、SE、演出時間は保持しています。

## 検証

`StageSelectFlowProbe.Run(outputDirectory)` は既存の編集Sceneを保存せず `playModeStartScene` から起動します。タイトル、登場、左スティック/十字キー/左右キー、未実装の開始拒否、連打、切り替え中の戻る、挿入、ズーム、Stage1 の開始演出/BGMと後処理を一度検証します。結果は `StageSelectFlowProbe.Result()`。既存の TitleLoadingTransitionPlayModeTest はドットワイプ自体を確認するため、Play Mode のインスタンスだけ接続先を Stage1 にして検証します。保存済みタイトルの導線は変更しません。

演出・SE音量・パッドでの最終操作感はユーザーの Play Mode 評価で調整します。

## ゴール破片による解放と湾曲CRTへの改修

通常の入場では結晶演出を再生せず、Stage1カセットを最初から表示します。Stage2の解放条件は **Stage1のゴール結晶破壊だけ** です。3個の収集用ピンク結晶（PinkJewelCollection）とは無関係です。設定上はゴール破壊時に得た破片が、次のカセットになるという扱いです。

`GoalPoint.ReachGoal` の確定地点で `StageSelectProgress.RecordClear` を呼び、`IronBallGirl.StageSelect.Clear.Stage1` に保存します。Stage2データの Unlock After Scene は `Stage1`。初回セレクト入場でそのカセットを優先表示し、専用の新しい破片が集合→生の結晶塊→亀裂→破砕→カセット出現を再生します。完了時だけ `IronBallGirl.StageSelect.Reveal.Stage2` を保存するため、中断すると次回再開し、完了後は繰り返しません。未解放のStage2を選ぶとカセットの代わりに結晶塊と解放条件が表示されます。

Stage1のクリア画面は通常のクリアメニューを使い、NEXTからStageSelectSceneへ進みます（Stage1インスタンスのShow Demo EndingはOFF）。他Sceneや共通Prefabの終了表示は変更していません。Stage2のゲームSceneがこのブランチにない点は従来どおりで、解放済みでもゲーム開始は安全に無効です。

テレビは `Unlock/CurvedTelevision.png` の専用筐体へ更新。スクリーンは `(0,70)`、`706×480` のクラシックな縦横比になり、プレビューの背景合成もその比率で計算します。専用筐体Shaderが黒い窓部分だけを抜き、曲面ガラスの映像・反射・減光を背面から表示します。ゲーム側のCRT Shader/Camera設定は変更しません。常時のピンクのキラキラとカセット周囲のピンクHaloは非表示です。結晶の光・破片は解放演出用です。

カセット中心は `(0,38)`。挿入は Align `.16秒`（幅を `.8` に合わせる）→Slide `.42秒`（一定幅で真下へ加速）→新作の機械的なガシャン音→筐体の短い揺れ→起動→既存のCRTズームです。挿入先は `(0,-210)`。ConsoleBackDeckとConsole前面を分け、前面のSurface Clip Y `.72` で差込口の手前だけを遮蔽します。カセットが筐体全体に隠れて消える旧方式から、差込口の中へ沈む見せ方へ変更しています。

解放演出はGather `.85秒`、Crack `.42秒`、Burst `.4秒`。CrystalMass/CrystalShardはImageGenで新規制作した金属飾りのない専用の透過素材で、旧PinkJewel素材はこの演出に使用しません。Fracturesは新作の亀裂マスク、CartridgeClack.wavは新作の短い合成機械音です。いずれもSprite/AudioClipを差し替えられます。生成記録は `Assets/Art/StageSelect/Unlock/GenerationPrompts.md`。

FlowProbeは実際のStage1ゴール確定コールバックを呼び、収集アイテム0個での解放、中断・再開、再入場での非再生、選択とCRT遷移を検証します。PlayerPrefsは専用のFlowProbe名前空間でのみ読み書き・後片付けし、実際の進行データを変更しません。ステージを端から端まで操作してクリアする検証と最終操作感はユーザー確認です。

## 最新の表示修正（上記の旧ビジュアル記録より優先）

テレビ画像のUV変形・色抜き・粗い格子が縁のギザつきの原因だったため、最終表示では使用を止めました。`SmoothCurvedHousing.mat` とSurface Shaderの連続した丸角曲線で筐体を描き、`StageSelectGlass.shader` が同じ曲率のガラス面・柔らかい背景映像・走査線・反射を表示します。色による窓のくり抜きはありません。CRT表示は `(0,78)` / `720×440`、筐体は `(0,62)` / `840×548`。前景カセットは引き続き独立したドット絵です。

未解放カセットでは **砂嵐のみ・カセットも結晶塊も非表示**。Stage1は最初から解放済み扱いで通常プレビューとカセットを表示します。Stage2はStage1ゴール破壊で解放され、初回演出の結晶殻が割れる瞬間にノイズから機巧都市プレビューへ切り替わります。既に解放済みで本編Sceneが未登録の場合も、プレビューは表示して開始は無効です。

挿入時にカセットのalphaを0にする旧処理は削除しました。幅を合わせ、真下へ滑らせ、上部とラベルが筐体から出た状態でガシャンと止まります。Console前面の遮蔽はカセット下部のみを隠し、CRTズーム中もカセットはその位置に残ります。

結晶殻は `Unlock/JaggedCartridgeCrystal.png` を新作。正面向き・横長・ザクザクした外周で、既存カセットを包める塊です。小さな殻から徐々に成長し、破片が横長の面全体へ吸収され、満ちた後に亀裂と破砕が起こり、下にある既存のカセットとステージ名Spriteが現れます。Growthは1.65秒、Crackは.48秒、Burstは.45秒。192×112のドット格子を素材全体へ統一し、プラスチックや金属飾りは描き込みません。生成記録は `JaggedCrystalPrompt.md`。

各アニメーションでは1フレームの進行を最大.05秒に制限し、初回ロードやShader準備で止まった直後も成長・挿入の途中を飛ばしません。通常の描画速度では設定秒数で再生し、低い描画速度では見えるフレームを優先します。FlowProbeは未解放時のノイズと前景非表示、実際の着座位置とカセットの不透明度、成長途中の結晶も検査・撮影します。

亀裂は新しい `JaggedFractures.png`（192×112の連続した枝分かれ線）を重ねます。以前の途切れた短い縦線は使いません。専用SpriteとMaterialは同じUnlockフォルダーに置き、ゲームプレイ用の結晶Prefabには変更を加えていません。

## 挿入の重なり・魚眼・手前からの融合（最新調整）

挿入バグの原因は、既存のConsoleBackDeckをカセットの旧Sibling Indexへ移動したときに、その奥側の画像がカセットより前へ残ることでした。順序は **ConsoleBackDeck → Cartridge → Console（手前の差込口）** に明示的に固定しています。差込口のクリップもSpriteの実際のUV範囲で正規化してから判定し、画像の透明余白やトリミングに影響されないようにしました。FlowProbeは選択から挿入中・着座・ズームまで順序を検査します。

CRTはポーズ画面と同じ半径方向のバレル歪みを参考にCurvature `.18` へ強化。角のテクスチャ引き伸ばしを防ぐオーバースキャンを入れ、走査線にも歪んだUVを使います。旧300本/高さ600pxの組合せはサンプリングが同じ位相になり濃淡が消えるため、240本・強度`.32`へ変更。微小なRGBずれ`.9px`も追加しました。前景には適用しません。ProbeはGPU上で校正用の縦線を描き、中心/端で曲がる位置差と、走査線の実際の明暗差を検査します。

解放時は24枚の大きな破片が、**画面下側の手前から中央へ飛び上がり**、**Foreground Shard Scale `2.8` → 遠方サイズ → 融合** と変化します。出発位置は下端の左右へ扇状に分散し、上や横からは飛来しません。1.65秒かけて遅延をずらしながら中央の6列×4行の領域へ入り、吸収された量に応じて正面向きの結晶殻が成長します。各破片の基準サイズは42～72×66～102。白い `CrystalFusionLight` を重ね、融合する瞬間と破砕をつなぎます。**Fusion White Strength `.7`** が光量の調整項目です。常時の飾りではなく解放演出中だけ表示します。ゴール破片を使う設定・Stage1クリアのみの条件・中断再開と一度だけの解放記録は維持しています。

この調整の変更対象は `StageSelectController.cs`、`StageSelectPreview.cs`、`StageSelectGlass.shader`、`StageSelectSurface.shader`、Editorの `StageSelectExperienceUpgrade.cs` / `StageSelectFlowProbe.cs`、保存済み `StageSelectScene.unity`、`SlotFrontLip.mat` / `SmoothCurvedHousing.mat`、新しい `CrystalFusionLight.mat` とその `.meta`、本書です。既存の背景Parallax、カセット、結晶殻、破片、亀裂、SEを再利用し、新規の仮画像はありません。Prefab、ゲームプレイのCRT設定、収集物、入力設定、実際のクリア記録は変更していません。

最終FlowProbeはPASS。下側の大きな破片・白い融合光・結晶成長、挿入中からズームまでの描画順、カセット着座、ロック時の砂嵐、Stage1開始、クリア後の解放・中断再開・再入場を確認しました。GPU描画の校正結果は歪み11.8px、走査線の明暗差0.082。修正後コンパイル成功、検証区間のConsole Error/Exception/Warningは0件。元のTitleSceneと未保存状態を維持しました。物理コントローラーの実機操作、ステージ全編の手動クリア、最終的な演出の好みはユーザー確認です。
