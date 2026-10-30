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

Controller：Gather .55秒 / Crack .34秒 / Burst .38秒、切り替え .24秒、挿入 .52秒、起動 .26秒、ズーム 1秒、ゲームへの接続 .34秒。Float Height 6、周期 3.4秒、挿入先 `(2, -220)`、挿入縮小 .46（差込口の幅に対応）、Impact Shake 5。選択入力は既存 `UI/Navigate` / `Submit` / `Cancel` の複製を所有し、共有アクションを無効化しません。長押しは .34秒後から .18秒間隔、切り替え予約は最後の1件だけです。決定後の連打・戻る入力は受け付けません。

Preview：Softness .55、Scanlines .13、Vignette .22、Noise .015。背景各層に Sprite、Tint、Drift を設定できます。

SE：既存の CheckpointActivate、ひびがはいる音、砕ける音を使用。切り替え・挿入・CRT起動・開始不可はオリジナルの短い合成音で、Controller の各 Clip に本素材を設定すれば差し替わります。ゲーム機・テレビ・カセットは、この画面用のオリジナル仮アートです。

## 検証

`StageSelectFlowProbe.Run(outputDirectory)` は既存の編集Sceneを保存せず `playModeStartScene` から起動します。タイトル、登場、左スティック/十字キー/左右キー、未実装の開始拒否、連打、切り替え中の戻る、挿入、ズーム、Stage1 の開始演出/BGMと後処理を一度検証します。結果は `StageSelectFlowProbe.Result()`。既存の TitleLoadingTransitionPlayModeTest はドットワイプ自体を確認するため、Play Mode のインスタンスだけ接続先を Stage1 にして検証します。保存済みタイトルの導線は変更しません。

演出・SE音量・パッドでの最終操作感はユーザーの Play Mode 評価で調整します。
