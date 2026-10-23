# モーニングスター娘のスタート演出

`Assets/Scenes/CompletScene.unity` に設定済みです。直接再生、TitleSceneのSTART経由の両方で動作します。

左上の画面外 → 放物線ジャンプ → 着地 → 右へ歩行 → 停止 → 操作解放。
停止・操作解放から2秒待機した後、右上にステージ名Spriteを5秒表示します。待機中・表示中も通常操作できます。
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
| Morning Star Offset | (-2, -1.15) | 演出中だけの鉄球の相対位置 |

登場から操作解放までは約1.4秒（ロード・ポーズ中を除く）。最終X座標は元の開始位置と同じ-16.1です。

- Jump Start Point / Landing Point / Final Stop PointにTransformを指定すると、その座標を優先します。開始時に座標を取得するため、経路用Transformは演出中に追従しません。
- 座標は足元ではなく**Playerルート**です。Align To GroundがONの場合、Landing / FinalのYは床に合わせます。床LayerはDefault / Wallsです。
- 開始区間は障害物のない床を使ってください。演出は決めた経路を移動し、壁との衝突による経路変更は行いません。
- Playerを別の位置へ移した場合はLanding Position、またはPoint参照も調整してください。
- Camera Followは演出中だけ現在位置で停止し、完了後に既存の追従を再開します。
- 別Sceneに追加する場合はPlayerにPlayerStartIntroを追加し、Camera Followと着地点を設定してください。

## ステージ名Sprite（ステージ1〜3共通）

`Assets/Prefabs/UI/StageTitleUI.prefab` を各ステージのSceneに一つ配置してください。
CompletSceneでは既存CanvasをこのPrefabへ移行済みです。ステージ2・3のSceneは作成していません。

Prefabルートの **Stage Title UI** で調整します。

| Inspectorフィールド | 現在値 | 用途 |
|---|---|---|
| Start Intro | CompletSceneのPlayer | 完了通知元。未設定なら同じScene内から自動取得 |
| Stage Sprite | ステージ名sprite_0 | ステージごとに割り当てるSprite |
| Base Size | (630, 141) | 拡大前の基準サイズ |
| Size Multiplier | 1.5 | 基準サイズに対する表示倍率。拡大は累積しない |
| Slide Distance | 48 | 上から登場・上へ退出する距離（基準解像度でのpx） |
| Show Delay After Intro | 2秒 | 開始演出完了通知からの待機時間。Total Durationには含めない |
| Fade In Duration | 0.4秒 | 下降スライドとフェードインの時間 |
| Fade Out Start | 4秒 | 表示開始から透明にし始めるまで |
| Total Duration | 5秒 | フェードアウト完了までの合計時間 |

用意済みの`Assets/image_/ステージ名sprite.png`は分割済みのため、そのまま使用しています。

| Sprite | 使用先 |
|---|---|
| ステージ名sprite_0 | ステージ1「蒼天の浮遊王都」 |
| ステージ名sprite_1 | ステージ2「紅蓮の機巧都市」 |
| ステージ名sprite_2 | ステージ3「月影の水晶古城」 |

- 将来Sceneを分けても、Prefabを配置してStage SpriteとStart Introを指定するだけで利用できます。参照未指定時の探索は同じScene内に限定します。
- 子の **StageTitle > RectTransform** は右上アンカー・右上Pivot、余白48、表示サイズ945×211.5（基準630×141の1.5倍、1920×1080基準）。Imageは縦横比を維持します。狭い解像度では画面内へ収まる倍率に制限します。
- 0〜0.4秒で定位置より上から下降しながらフェードイン、0.4〜4秒で定位置・透明度1を維持、4〜5秒で上昇しながらフェードアウトします。移動はanchoredPositionを使い、移動と透明度をEaseOut Cubicで補間します。上端を越えるスライド量は制限し、完全に消えたらImageを非表示にします。
- このCanvasだけScreen Space - Overlayを維持します。既存CRTが有効な場合も、カメラ追従や揺れによるずれが出ない画面固定配置です。他のCanvas・カメラ・CRTの設定は変更しません。
- ポーズ中は表示時間も停止します。UIはraycastを遮らず、入力・物理・カメラ・ゴールの制御を持ちません。
- ポーズ中は表示前の待機時間も停止します。待機前に一度だけの再生予約を行うため、待機中の重複通知でもコルーチンは増えません。
- 子の **StageTitle > Shadow** で影を調整できます。初期値はEffect Color=黒・Alpha 0.3、Effect Distance=(2, -2)、Use Graphic Alpha=ON。既存Imageと同じCanvasGroupのフェード・RectTransformのスライドへ追従します。
- `PlayerStartIntro.Completed`を操作解放後に受け取り、`ShowOnce()`で一度だけ再生します。チェックポイント復帰・重複通知・再有効化では再表示せず、Sceneの再ロードで再表示します。
- 画像が未設定なら警告を出して表示を省略します。代替画像やCanvasの自動生成は行いません。

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

検証内容: 5種類の既存アニメーション、着地イベント1回、登場中の操作禁止、右上Sprite表示とフェード、表示中の移動／ジャンプ／右スティック発射、リスポーン、Rest物理値復帰、重複通知と再有効化での再表示防止。
最終的な登場速度・歩数・重量感は実際のゲームパッドで確認してください。
