# ゲーム内コントローラー振動

## 調査と構成

- Unity 6000.4.2f1、Input System 1.19.0。既存PlayerInputはInvoke Unity Eventsと `InputSystem_Actions` を使用し、Player／MorningStarLauncherは `Gamepad.current`、Keyboard／Mouseも参照している。入力方式・Action・ボタン割り当ては変更しない。
- `PlayerHealth.OnDamaged / OnDead`、`PauseMenuController.PauseStateChanged` を再利用。鉄球は実際に速度を設定する `FirePendingThrow` と、敵が実ダメージ確定後に呼ぶ `PlayEnemyHitStop` に汎用通知だけを追加する。重量スイッチはON確定時の通知を追加。
- `GamepadRumbleManager` がモーター出力、設定、優先順位、時間、停止を所有する。`GameplayRumbleFeedback` は上記イベントと既存 `ChargeProgress` をManagerへ渡す。HP・物理・入力・SE・VFXを所有／変更しない。
- 本番設定は `Assets/Resources/Feedback/GamepadRumbleManager.prefab`。プレイ開始時にResourcesから1体を自動生成し、DontDestroyOnLoadで保持する。Scene変更時に現在のPlayer／Launcher／Pause／WeightSwitchへ接続し直す。Scene編集・参照の手動設定は不要。Prefabがない場合も既定値で生成し、未登録パターンは何もしない。

## Inspector初期値

`Rumble Enabled=true`、`Master Intensity=1`。PrefabのGamepadRumbleManagerで調整する。

| Patterns / Event | Low Frequency Intensity | High Frequency Intensity | Duration | Priority |
|---|---:|---:|---:|---:|
| PlayerDamage | 0.6 | 0.8 | 0.20秒 | 80 |
| EnemyHit | 0.25 | 0.45 | 0.08秒 | 50 |
| NormalThrow | 0.15 | 0.25 | 0.06秒 | 30 |
| FullChargeThrow | 0.5 | 0.7 | 0.15秒 | 60 |
| WeightSwitch | 0.2 | 0.35 | 0.10秒 | 40 |
| PlayerDeath | 0.8→0 | 0.9→0 | 0.50秒 | 100 |
| Charge | 上限0.12 | 上限0.20 | 終了通知まで（Durationは使用しない） | 10 |

各パターンの `Intensity Curve` は経過割合0〜1に対する強度。通常は一定1、死亡は1→0の直線。`Charge Rumble Curve` はLauncherの実際の `ChargeProgress` に対し0.15→1のEaseInOutで、開始時は0.018／0.03、最大でも0.12／0.20になる。独自のチャージ時計は作っていない。

## 競合と停止

- 単一リクエスト方式。高優先度は低優先度を破棄し、低優先度は拒否。同優先度は新しい要求に置換。古い要求を待ち行列へ保存／再開しない。
- チャージは開始時に1回だけ登録し、実値が変わったときだけ既存ハンドルを更新。解除・キャンセル・投擲で停止する。古いハンドルの停止通知が新しい強攻撃や被弾を止めることはない。中断後は次のチャージ開始まで再登録しない。
- 時間はunscaledTimeで計測し、命中ヒットストップ中も自然に終了する。ゲームのPauseイベントは即座に破棄・停止する。死亡はGame Over画面への遷移中も最大0.50秒の減衰を完了して停止し、それ以後の通常イベントを拒否する。
- Sceneロード／アンロード／切り替え、フォーカス喪失、アプリPause、機能OFF、Master=0、Manager無効化、コントローラー切断／変更、アプリ終了、Editor Play Mode終了／アセンブリ再読み込みで停止する。
- 出力は最大30Hzへ集約し、同じ強度を毎フレーム送らない。USB HIDのビジーによる停止コマンド脱落対策として、停止ゼロを0.2秒だけ再送する。出力例外はゲームへ伝播させず、そのデバイスの新規要求を再接続まで拒否する。
- 出力先は既存操作と同じ `Gamepad.current`。未接続時は無処理。接続前の要求や、前のコントローラーの振動は接続後へ持ち越さない。

## DualSenseと拡張

Input System 1.19.0の `DualSenseGamepadHID.SetMotorSpeeds` はUSB用通常振動を実装している。独自HID送信、ドライバー、外部ライブラリ、Input System更新は不要。アダプティブトリガー、音声駆動の高度なハプティクス、LED演出は追加しない。Bluetoothは今回の対象外で、このバージョンの実装にもBluetooth出力に関する制約がある。

公式資料: [Gamepad Support](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.19/manual/Gamepad.html)、[DualSenseGamepadHID](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.19/api/UnityEngine.InputSystem.DualShock.DualSenseGamepadHID.html)。USB側の対応とUnityの出力実装があっても、実際の振動はデバイス・接続・ドライバー環境による。今回のEditorではDualSense HIDが認識されていた。

将来のギミックは `GamepadRumbleManager.PlayEvent(...)` または `GamepadRumbleManager.Instance?.Play(pattern)` を呼ぶ。例えばInspectorで `[SerializeField] private GamepadRumblePattern bossImpact = new GamepadRumblePattern(0.5f, 0.4f, 0.3f, 70);` と定義すれば、既存パターンの列挙を増やさずに独自演出を追加できる。動的生成したWeightSwitchへ既存のブリッジを使う場合はScene接続時の探索対象外になるため、生成側でManagerを呼ぶか接続を追加する。

## 変更ファイル

- 新規: `Assets/Script/GamepadRumbleManager.cs`, `Assets/Script/GameplayRumbleFeedback.cs`, `Assets/Editor/GamepadRumbleSetup.cs` と各.meta。
- 通知追加のみ: `Assets/Script/MorningStarLauncher.cs`, `Assets/Script/Gimmicks/WeightSwitch.cs`。
- 新規本番Prefab: `Assets/Resources/Feedback/GamepadRumbleManager.prefab` とフォルダー／Prefabの.meta。
- Scene／既存Prefab／Input Actions／Project Settings／Packagesの変更なし。既存の未コミット入力・カーソル・鉄球作業は保持し、今回のcommitへ含めない。

## 検証

2026-10-23: コンパイル成功。既存CompletSceneのPlay Modeで、一時的な仮想Gamepad出力を用いて以下を確認した。確認用コードはOS一時フォルダーのみで、プロジェクトへ専用テスト機能・UI・ボタン・Sceneを追加していない。

- 強度・Masterの反映と0〜1制限、時間切れ、優先順位、古い要求／ハンドルの再開防止、OFF／ON、Manager無効化、フォーカス／アプリPause、未登録設定、非対応出力例外、切断／未接続／再接続。
- 実際のFirePendingThrowの通常／最大チャージ通知、実ChargeProgressの開始／半分／最大／キャンセル、EnemyHealthが受理した命中とヒットストップ中の出力、既存PauseMenuの開閉、WeightSwitchのOFF→ON、PlayerHealthの被ダメージと死亡。
- 実際のGame Over表示（timeScale=0）中の死亡カーブ中間値0.4／0.45と終了時0／0。中間値と期限はEditor更新間隔に左右されないようManagerの開始時刻を一時的に設定してサンプルした。
- 実際のTitleSceneロードで前の出力が0になり、同じManagerが維持されること。終了後、元のCompletSceneと既存のdirty状態を復元。

新規ゲームコード由来のConsole Error／Exceptionなし。検証用コマンドでは再接続の503／504や更新タイミングに依存する判定失敗が発生したため、残項目を個別に再確認した。DualSense HIDの認識は確認済みだが、実機モーターの振動・強さ・USBドライバー経由の実出力は未確認。実機の体感確認は通常プレイで行う。
