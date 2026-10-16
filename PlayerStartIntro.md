# モーニングスター娘のスタート演出

`Assets/Scenes/CompletScene.unity` に設定済みです。直接再生、TitleSceneのSTART経由の両方で動作します。

左上の画面外 → 放物線ジャンプ → 着地 → 右へ歩行 → 停止 → STAGE 1 → 操作解放。
既存のJumpUp / JumpFall / JumpLand / Walk / Idle、着地音、足音を使用します。
土煙エフェクトは追加していません。

## 調整

CompletSceneの **Player > Player Start Intro** を調整してください。

| Inspectorフィールド | 現在値 | 用途 |
|---|---|---|
| Play On Start | ON | OFFなら開始演出を省略 |
| Jump Start Offset | (-12, 7) | Scene上のPlayer初期位置からの開始オフセット |
| Landing Position | (-17.5, 1.29) | 着地点のPlayerルート座標。Align To GroundがYを床に合わせる |
| Walk Distance | 1.4 | 着地後に右へ進む距離 |
| Intro Jump Duration | 0.85秒 | ジャンプ登場時間 |
| Jump Arc Height | 2.5 | 開始点と着地点を結ぶ直線に加える放物線の高さ |
| Walk Speed | 3 | 演出中の歩行速度（units/秒） |
| Stage Title Delay | 0.12秒 | 停止からタイトル表示まで |
| Stage Title Duration | 0.8秒 | タイトル表示時間 |
| Stage Title | STAGE 1 | 表示テキスト |
| Morning Star Offset | (-2, -1.15) | 演出中だけの鉄球の相対位置 |

合計約2.3秒（ロード・ポーズ中を除く）。最終X座標は元の開始位置と同じ-16.1です。

- Jump Start Point / Landing Point / Final Stop PointにTransformを指定すると、その座標を優先します。開始時に座標を取得するため、経路用Transformは演出中に追従しません。
- 座標は足元ではなく**Playerルート**です。Align To GroundがONの場合、Landing / FinalのYは床に合わせます。床LayerはDefault / Wallsです。
- 開始区間は障害物のない床を使ってください。演出は決めた経路を移動し、壁との衝突による経路変更は行いません。
- Playerを別の位置へ移した場合はLanding Position、またはPoint参照も調整してください。
- Camera Followは演出中だけ現在位置で停止し、完了後に既存の追従を再開します。
- StageIntroCanvas > TitleBand > StageTitleで文字の見た目を調整できます。CanvasGroupは通常alpha=0です。
- UI参照が未設定の場合は、実行時に簡易CanvasとTMP文字を生成します。
- 別Sceneに追加する場合はPlayerにPlayerStartIntroを追加し、Camera Followと着地点を設定してください。

## 制御・保守

- Playerの`SetInputEnabled(bool)`で入力を遮断し、演出中は通常のFixedUpdateを停止します。入力は蓄積しません。
- 鉄球は既存の`TransitionToState()`を通したStartIntro状態を使い、鎖の制約と物理シミュレーションを停止します。
- 終了時は既存の`ResetForRespawn()`経路でRestへ戻します。通常プレイの移動・ジャンプ・鉄球の調整値は変更しません。
- コンポーネントを演出途中で無効化した場合も、停止位置へ移動して物理・カメラ・元の入力許可状態を復元します。
- リスポーン時には演出を再生しません。Sceneの再ロード時には再生します。
- 再セットアップはUnityメニュー **鉄球少女 > Player > スタート演出を設定**。CompletSceneを保存します。既存の演出調整値は維持します。

## 確認

Editor検証用の`PlayerStartIntroPlayModeTest.Run()`と`Run(true)`で、直接開始／タイトル経由を確認できます。
検証は仮想ゲームパッドを使い、終了時に取り除いてPlay Modeを終了します。通常のゲーム開始時には実行されません。

確認済み: 5種類の既存アニメーション、着地イベント1回、操作禁止、タイトル表示、演出時間、操作復帰後の移動／ジャンプ／右スティック発射、リスポーン、Rest物理値復帰。
最終的な登場速度・歩数・重量感は実際のゲームパッドで確認してください。
