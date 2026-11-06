# Stage2 巨大通電門

承認された無骨な黒鉄の門を、Stage2専用 `S2_GrandElectricDoor` Prefab Variantへ組み込み。左右非対称のモーター・変圧器・厚板・大きなボルトを中心とし、電気は黄色～白で表示する。緑の装飾は追加していない。

## 現在の設定

| 項目 | 設定 |
|---|---|
| 基部 / Scale | (8.5, 1.24) / (1, 1, 1) |
| 絵の外形 | 約13.5 × 17 units（門全体432 × 544px、32 PPU） |
| 通路Collider | 幅6 × 高さ12、Offset (0, 6) |
| 左右の扉板 | 各3 × 12、移動量は片側3.25 |
| 電源成立 | 既存0.8秒、接触猶予0.12秒を保持 |
| カメラ移動 | 0.5秒、門＋主人公を含むBoundsと余白1から倍率を計算 |
| ロック解除 | 0.55秒 |
| 扉の収納 | 1.8秒、両端の速度・加速度が0になる補間 |
| 全開確認 / 復帰 | 0.2秒 / 0.45秒 |
| 一度開いた後 | 全開を保持。演出は再発火しない |

当初の約12×16から、承認原画の柱・機械を無理なく残す13.5×17へ拡張した。Colliderは当初指定の6×12。扉板とロックは透過形状を使用するSpriteMaskで柱の奥へ収納され、全開になるまで通路Colliderを維持する。枠の振動・排気は表示専用で、通路Collider自体は動かさない。

蓄電表示3本が順に満ち、受電端子から駆動部へ電流の光が移動する。解除音、収納中の低い駆動音、停止衝撃音と短い排気を分離した。受電音ElectricActivateは既存の受電側が所有し、駆動はStoneGateOpen、解除はStoneSwitchPress、停止は既存の金属衝撃音を使用。原画内の電灯・黄色い配管は専用マスクで通電時だけ明るくする。

## 制御と互換性

- ElectricReceiverCableが通電条件を、ElectricDoorが扉の進捗とColliderを所有。読み取り専用ChargeProgressを表示に公開。
- ElectricActivationSequenceだけが演出を管理。CameraFollowの既存イベント所有権を使用し、所有者だけが位置と倍率を変更する。
- 演出中はゲーム時間を止め、扉と表示を非スケール時間で進める。カメラを借りられない場合は通常時間で開く。
- Playerが感電状態を外部解除するとElectricStunEndedで即座に演出を返却し、リスポーンの描画・入力・時間の所有権へ渡す。
- 死亡・落下復帰は、分解演出が通常時間で動くため、門が止めた時間を返す。実際のPause / Goal / Game Overが止めている場合は時間を再開しない。
- 大型演出はStage2専用のopt-in。共有ElectricDoor / ElectricReceiverCable PrefabおよびCompletSceneのアセットは変更しない。
- Stage2の受電端子・TeslaCoil・地形・収集物は維持。閉鎖範囲内のWrenchCrabだけX=16へ移動し、Yを保持。

## 制作元

画像は組み込みimage_genで新規制作・編集。承認デザインは `ApprovedRuggedSource.png`、固定枠は `FrameSource.png`、可動部は `PartsSource.png`。使用したプロンプトは同じフォルダの `*Prompt.txt` に保存。

ゲーム用画像は `Assets/Art/ElectricGimmick/GrandGate/`。Point / MipMapなし / 無圧縮 / 32 PPU。原画の透過を維持し、固定枠・前景ヘッダー・前景柱・左右扉・ロック・ローター・受電表示へ分離した。開口部の透過からSpriteMaskを作成。発光マスクは黄色を起点に、隣接する白熱ハイライトを含めて作成し、待機時に白い光だけが残らないようにした。

`Gate-*.png` は実際のSprite / Shader / SpriteMaskを使った状態別プレビュー。`Stage2-*.png` はPlay Modeの実画面。検証記録は `Validation.txt`。

## 保存と確認範囲

改修前のStage2配置を最初に保存し、`906d241` に記録した。完成したStage2とPrefab Variantを保存。既存の別作業 `StageSelectJapanese.asset` は保持する。mainへのマージはしない。

通し攻略、実コントローラーの操作感、最終的な迫力と音の評価はユーザー確認に残す。
## 変更ファイル

- `Assets/Scenes/Stage2.unity`
- `Assets/Prefabs/ElectricGimmick/S2_GrandElectricDoor.prefab` と `.meta`
- `Assets/Art/ElectricGimmick/GrandGate/` の画像・発光マスク・Materialと各 `.meta`
- `Assets/GrandGatePower.shader` と `.meta`
- `Assets/Script/Gimmicks/Electric/GrandElectricDoorVisuals.cs` と `.meta`
- `Assets/Script/Gimmicks/Electric/ElectricDoor.cs`
- `Assets/Script/Gimmicks/Electric/ElectricReceiverCable.cs`
- `Assets/Script/Gimmicks/Electric/ElectricActivationSequence.cs`
- `Assets/Script/CameraFollow.cs`
- `Assets/Script/Player.ElectricStun.cs`
- 本フォルダの原画・プロンプト・画面・検証記録。`*.cs.txt` は検証用ソースの記録で、Unityへインポートしない。