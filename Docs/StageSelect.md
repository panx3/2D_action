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

Controller：解放時だけGather .85秒 / Crack .42秒 / Burst .4秒、切り替え .24秒、整列 .16秒、挿入 .42秒、起動 .26秒、ズーム 1秒、ゲームへの接続 .34秒。Float Height 6、周期 3.4秒、挿入先 `(0, -210)`、挿入縮小 .8（差込口の幅に対応）、Impact Shake 5。選択入力は既存 `UI/Navigate` / `Submit` / `Cancel` の複製を所有し、共有アクションを無効化しません。長押しは .34秒後から .18秒間隔、切り替え予約は最後の1件だけです。決定後の連打・戻る入力は受け付けません。

Preview：Softness .8、Scanlines .16、Vignette .26、Noise .012、Brightness .9、Curvature .085、Glass Reflection .14。背景各層に Sprite、Tint、Drift を設定できます。

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
