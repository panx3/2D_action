# ステージセレクトの仕上げ

演出と画質の改良。既存の入力・進行条件・非同期ロード・ステージ開始ゲートを使用する。

## 変更内容とファイル

- `Assets/Scenes/StageSelectScene.unity`：背景、筐体、印刷ラベル、床反射、光、粒子、開放メッセージの配置と参照を保存。
- `Assets/Script/UI/StageSelectController.cs`：結晶の集結・蓄光・亀裂・散開・余韻、挿入とCRT接近の緩急、ガラスの響きを含む合成音。
- `Assets/Script/UI/StageSelectPreview.cs`：静止したラベル用の画像、選択切り替え時の映像クロスフェード、反射用出力、奥行きぼけと信号復帰。生成したRenderTexture/Materialは終了時に破棄。
- `Assets/Script/UI/StageSelectLighting.cs`：選択中の画面に連動する反射光、開放時の背景減光と光環、一部の前景破片のぼけ。
- `Assets/StageSelectGlass.shader`、`Assets/StageSelectSurface.shader`、`Assets/StageSelectAtmosphere.shader`：CRTの光にじみ、ガラス、筐体の縁の光、反射の粗さ、ビネット、印刷面と光の走査。
- `Assets/Art/StageSelect/Cinematic/`：室内背景、8種類の透明な結晶破片、専用Materialと各`.meta`。生成方法と最終プロンプトは同フォルダーの`ArtDirection.md`。
- `Assets/Editor/StageSelectCinematicFinish.cs`：既存Hierarchyに仕上げを適用する補助処理。対象Sceneが未保存なら適用を中断する。
- `Assets/Script/UI/StageSelectCrystalFragment.cs`：完成画像を80個の接合できる多角形に切り分けるUIメッシュ。
- `Assets/Editor/StageSelectFlowProbe.cs`：反射・ラベル接続、接合破片80個、散開と余韻の観測を既存の通し検証に追加。

## 背景・接地感の追加修正

背景が明るく細かすぎたため、専用`PixelRoom` Materialで320×180相当の点サンプリング、32段階/チャンネルの減色、彩度72%、linear露出0.32を適用。元の生成画像は保持した。

反射が本体に隠れ、ゲーム機画像の透明な余白から強く減衰していたため、反射の始点・配置・減衰を修正。CRT反射は中心(0,-252)、サイズ920×150。ゲーム機反射は中心(0,-355)、サイズ660×80。ブラウン管とゲーム機の接地点に、外側まで広がる暗い影を配置した。

ゲーム機の右後部からCRT右下へつながる、影と細い縁の光を持つドット状のAVケーブルを追加。装飾はすべてクリックを遮らない。`StageSelectCinematicFinish.Apply(true)`はこの修正だけを適用し、既存の演出調整値を保持する。

ステージセレクト内の`GameBgmController.initialState`を`None`に変更。タイトル・ステージのBGM設定と、セレクトの操作・開放・挿入SEは維持する。

変更対象は`StageSelectAtmosphere.shader`、`StageSelectCinematicFinish.cs`、`StageSelectScene.unity`、専用Materialと各`.meta`、本記録。

追加修正の検証：C#・シェーダー正常。Play Modeで通常/未開放表示、影・反射・ケーブルの表示、BGM用AudioSourceの非再生を確認し、関連する新規Console Error/Exceptionは0件。検証用設定を復元し、元のTitleSceneへdirty=falseで復帰。遷移・開放の通し再検証とBuildは今回未実施。

## 有効なInspector値

ブラウン管筐体の質感調整：`StageSelectSurface.shader`の筐体描画だけを変更し、陰影・縁の反射を3pxの画素単位へ統一。細かなランダムノイズを少数の色むらと規則的なディザへ置き換え、側面の通気スリットと角のネジを追加した。ガラスとの境界形状・表示映像・Inspector値は維持。Scene/Prefab/Materialの変更・保存は不要。この小規模調整ではシェーダーコンパイルと関連Consoleのみ確認し、Play Modeでの最終外観確認は未実施。

破片集結の調整：破片は80個。最終的なカセット型結晶の画像を、内部の頂点を少しずらした8列×5行の格子から80個の三角形に切り分ける。到着位置と向きを固定し、同じ模様・画素グリッドのまま隙間なく接合する。ランダムな輪郭を大きく重ねる方式による、外周の突き出し・深いくぼみを解消。0.55秒の発光演出のピークで破片を隠し、従来のカセット型結晶の1枚絵を表示する。その後に亀裂・破砕を再生。四角い塊の仮画像は使用しない。背景・BGM・その他の演出値は保持する。

`foregroundShardScale`=1.2、`assembledShardScale`=1.0。破片は完成画像の実際の領域に対応する大きさで、従来の大幅な拡大を不要にした。`assemblyPositions`は生成処理が保存する80個の到着位置（Inspector非表示）。`FittedCrystalPieces.mat`は完成画像と同じ192×112の画素グリッド。元の結晶PNGやImporterは変更しない。

移動中の破片には、同じ192×112グリッドの1画素幅で暗い紫の階段状アウトラインを付ける。上・左側はやや明るい紫にして縁の陰影を出し、滑らかな切り抜きに見えない画素単位の輪郭にする。各破片の移動進捗92～100%で輪郭を消し、接合時は元の正確な三角形に戻すため、完成後に暗い接合線や隙間が残らない。その後は既存の発光・1枚絵への切替・亀裂・破砕を使う。

輪郭用の画素は同じ色の横列をまとめたメッシュとして初回にキャッシュし、追加MaterialやGameObjectは作らない。変更は`StageSelectCrystalFragment.cs`と`StageSelectController.cs`。Inspector項目追加、Scene/Prefab/Materialの変更・保存なし。80個の配置・倍率・演出時間・既存調整値を維持する。

アウトライン追加後の確認：C#コンパイル成功。開放演出を1回再生し、移動中の80個の破片で輪郭を確認、接合後は全80個で輪郭・画素段差がなく元の三角形へ戻ること、1枚絵への切替・亀裂・開放完了を確認。1破片の最大頂点数は観測時316。関連Console Error/Exceptionは0件。本番進行データ不変、検証用進行状態・検証前のフォントAssetを復元。元のTitleSceneはdirty=falseのまま、Scene/Prefabは保存していない。ステージ遷移全体の再検証・Build・実機での最終外観評価は今回未実施。既知の不具合なし。

集結中は`UnlockLight`とfusion lightの強さを0に固定し、中央の発光・光輪を表示しない。全破片の到着後、画像切替の発光を0から立ち上げる。集結・発光・破砕の演出時間は維持する。ぼかし用Materialを割り当てる前景破片を連続したグループにまとめ、Material交互切替による描画バッチ増加を抑える。

横長化・集結中の光の削除は`StageSelectController.cs`と本記録のみの変更で、Scene/Prefabの追加保存なし。開放部分を検証用データで再生し、集結途中の中央発光・光輪のalpha=0、完成シルエット、カセット型結晶への切替、開放完了を確認。元のTitleSceneをdirty=falseで維持し、デバッグ用の保存状態も復元した。ステージ遷移の通し再検証とBuildはこの調整では未実施。

起動・ズーム時に重ねていた`PinkJewel/Flash.png`のひし形発光を削除。ステージ移行開始時に透明化し、ゲーム開始まで再表示しない。結晶の画像切替・破砕時の発光は維持する。

密度・起動光の修正後もコンパイルと通し検証に合格（Error/Exception/Warningは0件）。集結後の密度を画面確認し、起動からステージ表示までひし形画像のalphaが0であることを確認した。CRT上側の余白50px、四角い仮画像なしを維持する。

同時にCRT上端の重なりを修正。ガラス上端と外装上端の余白が12pxで、内側と外側のベベルが重なっていた。画面下端を維持して余白を50pxへ広げ、画面を(0,107)・684×400、外装Materialのglass offsetを(0,17)へ同期した。外装本体は(0,90)・780×534を維持。`ApplyCrystalAssembly(true)`でこの2点のみを適用できる。

今回の変更対象は`StageSelectController.cs`、`StageSelectCinematicFinish.cs`、`StageSelectFlowProbe.cs`、`StageSelectScene.unity`、`TheatreHousing.mat`。演出テンポの最終評価とBuildは未実施。

固定位置への集結と上端修正の検証：C#・GPUシェーダー・実シーンの通し検証に合格。破片のみの集結状態と修正後のCRT上端を画面確認し、開放記録・中断後再生・ステージ開始・操作/BGM復帰を確認。検証中のError/Exception/Warningは0件。元のTitleSceneとdirty状態を維持した。

| 項目 | 値 |
| --- | --- |
| 集結 / 発光・画像切替 / 亀裂 / 散開 / 余韻 | 2.50 / 0.55 / 0.68 / 0.78 / 1.55 秒 |
| 選択切り替え | 0.42 秒 |
| 整列 / 挿入 / 起動 / CRT接近 / ステージ表示 | 0.25 / 0.50 / 0.48 / 1.45 / 0.65 秒 |
| カセット浮遊 | 高さ3、周期4.8秒 |
| CRT softness / scanlines / RGB separation | 0.65 / 0.17 / 0.55 |
| CRT brightness / glass reflection / phosphor bloom | 1.02 / 0.21 / 0.28 |
| CRT spill / floor light / mote opacity | 0.16 / 0.12 / 0.56 |

時間はunscaled秒。非同期ロード待ち時間は別。ぼけは選択画面内の合成処理で、ゲームプレイのカメラやポストプロセス設定を変更しない。新しいUI参照はController・Preview・Lightingに接続済み。既存の挿入位置・倍率、入力アセット、ステージデータ、開放条件、既存クリップの参照は維持した。

## 確認方法と範囲

### 解放デバッグとゴール手前への移動

メニュー`鉄球少女 > Stage Select > 解放デバッグ`を開く。

- 「ステージ2を解放する」のチェックは、通常のPlay中も操作できる。セレクトではSceneを読み直さず即時更新する。演出途中の変更も反映でき、連打した場合は最後の指定を採用する。
- 画面遷移中は要求を保持し、安全になった直後に反映する。現在の保存状態と反映待ちの理由をウィンドウに表示し、定期的に更新する。
- 「ステージ1のゴール手前へテレポート」は、Stage1を読み込み、開始演出が終わったらゴール手前の静的な床を検索する。既存の`GimmickRespawnController.RespawnAt`を使い、全回復したPlayer・鉄球・鎖・カメラを復元する。通常のチェックポイントは維持する。Stage1の通常Play中からも使える。
- クリアから開放まで試すときは、未解放にしてからゴール手前へ移動し、ゴール結晶を壊す。
- 本番の進行データは変更しない。Play停止時には元の保存領域と開始Scene設定を戻す。

不具合の原因は、通常Playや遷移中の要求を早期returnで捨てていたこと、チェックごとにSceneを読み直していたこと、表示が継続更新されていなかったこと。`StageSelectDebugWindow`が要求を保持し、`StageSelectController.RefreshDebugSelection`が既存の表示・状態を復元して反映する構成へ変更した。追加のゲーム用UIや保存システムは作っていない。

検証済み：通常Playからの利用、同一Controllerでの切り替え、解放演出途中のチェック解除、連打時の最終指定、実際のタイトル遷移中の要求保持、ゴール手前での接地・入力・Rest物理・カメラ・通常チェックポイント、ゴールのクリアから開放完了、本番保存キーの不変。検証中のError/Exception/Warningは0件。

Edit ModeのボタンからStage1を直接起動してゴール手前へ移動する経路も確認済み。停止後は保存領域とplayModeStartSceneが元へ戻り、元のTitleSceneはdirty=falseを維持した。検証前から変更されていたフォントAssetは、検証前の内容をそのまま保持した。Buildと実機での主観的な操作感の最終確認は未実施。

デバッグ改修の変更対象：`StageSelectDebugWindow.cs`、`StageSelectController.cs`（Editor用の更新API）、本記録。再コンパイル時にEditorWindowのstatic constructorからSessionStateを読むと例外になるため、保存領域の復元を`InitializeOnLoadMethod`へ移した。

輪郭・破片サイズ修正の変更対象：`StageSelectController.cs`、`StageSelectCrystalFragment.cs`と`.meta`、`StageSelectLighting.cs`、`StageSelectCinematicFinish.cs`、`StageSelectFlowProbe.cs`、`StageSelectScene.unity`、`Cinematic/FittedCrystalPieces.mat`と`.meta`、本記録。変更した設定は破片80個、前景倍率1.2、到着倍率1.0、非表示の接合位置・多角形データ。既存の背景・CRT・音・タイミング調整は保持した。

輪郭修正後の確認：C#コンパイル成功、開放演出を1回再生し、80個の三角形の面積合計が元画像全域と一致、全破片の到着位置・角度・倍率が一致、集結中の発光0、1枚絵への切替・亀裂・開放完了を確認。再生中のConsole Error/Exceptionは0件。本番進行データ不変、検証用進行状態と検証前のフォントAssetを復元。StageSelectSceneのみ保存し、元のTitleSceneはdirty=falseのままEdit Modeへ復帰。今回の修正ではステージ遷移全体の再検証とBuildは未実施。最終的な見た目・テンポはユーザー確認待ち。その他の既知の問題なし。

2026-10-30の最終版：C#コンパイル、CRT・筐体・反射/光環シェーダー検証、実シーンの通し検証に合格。検証実行中のError/Exception/Warningは0件。元のTitleSceneとdirty状態を維持してEdit Modeへ復帰した。

既存の`StageSelectFlowProbe`でTitle→Select→Stage1→クリア→Select→Stage2を検証する。検証用の保存領域を使い、本番進行データを変更しない。途中キャンセルと開放演出の再生、完了記録の維持、入力・BGM・Time.timeScale・永続オブジェクトの後処理も対象。

通常の本番データに触れず開放演出を見る場合は、既存メニュー`鉄球少女 > Stage Select > 解放デバッグ`の「ステージ1をクリアしたことにする（解放演出を再生）」を使用できる。

実機のコントローラーでの主観的な操作感・音量バランス、スタンドアロンBuildや他GPUでの確認は未実施。変更したSceneはStageSelectSceneのみ。Prefab・ゲームプレイScene・Packages・Project Settingsの変更はない。
