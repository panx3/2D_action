# プレイヤーモーションの再調整

Idleとぶら下がりを基準・維持対象として、走り、降下、下方向の投げ、空中の斜め上投げを見直した。新規のゲーム用シートは `Assets/Art/Player/MotionV2/*Refined.png`。元のシートは残している。原画と生成指示は `Sources/`、`prompts.json` にある。

- 走りは既存の前傾・柄を後ろに持つ姿勢と0.85秒／6コマの周期を維持し、輪郭・顔・服を整えた。
- 降下は2コマで髪が空中に残る輪郭へ変更し、繰り返しは行わない。
- 下・斜め下投げの頭、首、肩、胴と脚の比率、および空中斜め上投げの見かけの大きさをIdleと比較して修正した。
- 立ち・空中チャージは既存の絵から0、1、3、7コマを選び、それぞれ2位相分使用する。鉄球の回転方向・角速度・軌道設定、チャージ走りの歩調は維持する。
- 投げの絵が終了する約0.18秒後は、物理用の手の保持が続いていてもAnimatorの通常姿勢へ戻る。投擲後に張力だけを理由として踏ん張り・引っ張られ姿勢を強制しない。

`review-*.png` と `compare-*.mp4` はIdleと変更前後の原寸比比較。動画はSpriteの周期を示すもので、物理シミュレーションではない。`Stage1-*.png`、`Stage2-*.png` は実際のシーンのカメラ画像（1600×900）。

今回の主な変更ファイル：

- `Assets/Art/Player/MotionV2/` の `RunRefined.png`、`FallRefined.png`、`ThrowDownRefined.png`、`ThrowDownRightRefined.png`、`ThrowUpRightAirRefined.png` と各 `.meta`。
- `Assets/Resources/PlayerMotion/PlayerMotionV2.asset`、`Assets/Animation/Walk.anim`、`Assets/Animation/JumpFall.anim`。
- `Assets/Script/MorningStarLauncher.Motion.cs`、`Assets/Script/MorningStarLauncher.cs`。
- `Assets/Editor/PlayerMotionV2Setup.cs`、`PlayerMotionV2Setup.Calm.cs`、`PlayerMotionV2Setup.Refinement.cs`、`PlayerMotionV2Validation.cs`、`PlayerMotionV2PlayExercise.cs` と新規ファイルの `.meta`。
- このフォルダーの原画、校正値、比較画像・動画、実行記録。Scene／Prefabは今回保存していない。

## 鎖の接続点

新しい絵で自動検出が柄の反対側や髪を拾った5コマは、表示用の接続点を柄先に指定した。値は右向きSprite原点からのUnityローカル座標で、物理Jointの接続点は変更していない。

| 対象 | X | Y |
| --- | ---: | ---: |
| 降下 0 | -1.5625 | -1.125 |
| 降下 1 | -1.46875 | -1.125 |
| 空中斜め上投げ 0 | -0.46875 | 1.625 |
| 空中斜め上投げ 1 | 2.21875 | 2.15625 |
| 空中斜め下投げ 0 | -1.875 | 1.5625 |

投げ終了後、Idleを表示している間は既存の通常時の柄の位置に鎖を描画する。物理用の保持時間と位置はそのまま。`*-anchors.png` の水色は表示上の鎖の接続点を示す。

## 検証

- 32 PPU、Point、非圧縮、MipMapなし、テクスチャの縮小防止と参照をUnity上で確認。
- 停止中のPlay Mode検証で、8方向×地上／空中の絵選択、投げ終了後の通常姿勢、吊り下がり・鎖登りの参照、チャージ解除・発射・Respawn時の物理設定復帰を確認。
- Stage1とStage2で仮想ゲームパッドによる走り・ジャンプ・降下・着地、チャージ・チャージ走り・空中チャージ・上投げを確認。Stage2では下投げ、空中斜め下・斜め上投げも確認した。床に即座に当たる下投げは早期に通常状態へ復帰した。
- Unityのコンパイル完了後、関連Console Error／Exceptionなし。Idle、ぶら下がり画像とmeta、既存の別作業アセットが検査前後で同一のままであることをハッシュで確認。
- 8方向全てについて実機コントローラー操作は行っていない。動きの好みや操作感の最終判断は実際のプレイで確認する。
