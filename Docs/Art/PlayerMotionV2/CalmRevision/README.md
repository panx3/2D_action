# 頭の揺れ・実際の体格・動作のつながりの改修

2026-10-31 / `codex/player-motion-unification`。この版は78コマ。コマを増やす方向ではなく、元の走りの静かな姿勢と、一度落ち着いたら保つ動きへ見直した。

## 変更したこと

- **ぶら下がりはノータッチ。** PNG・取り込み `.meta` のSHA-256が改修前と一致。既存の再生データ・表示処理も維持。IdleのPNG・Clipも同様に一致し、鎖登りも今回の再制作対象から除外した。
- 走りは元の `run_test_run3-export.png` の6ポーズを再利用。静かな前傾姿勢に戻し、追加していた3pxの上下動を廃止。1周0.85秒と入力・移動性能は維持。
- 頭の寸法・頭の基準位置・首より下・足の接地を別々に校正。武器の先端、髪の広がり、曲げた膝でフレーム全体の大きさや原点が変わらないようにした。頭の基準はIdle先頭の頭蓋高約31pxに近い32px。セルの大きさを揃えるだけの処理にはしていない。
- チャージは胴体の回転と足のポーズを継承し、顔の向きと目線を安定させた。チャージ走りは静かな前傾6ポーズへ再制作。脚の歩調を鉄球の720度/秒の回転位相から分離し、地上の歩調で再生する。前の握りは低い胸元から口元へ上げた。
- 降下は髪を上へ激しく開く絵をやめ、柔らかく浮いて残る形へ変更。2コマを0.18秒かけて収束させ、以後は最後の形を保つ。深く沈む着地絵は使用せず、頭の上下は小さな収束にした。
- 上投げは顔を上げて上を見る。斜め上・斜め下・下も投擲方向を見るように修正。横投げは元のポーズを使い、全方向の地上／空中で見た目の寸法を校正した。
- 投げた後に張力の角度で違う頭・投げ方向のポーズへ細かく切り替わる処理を廃止。発射ポーズを最後まで優先し、投擲後の空中張力では同じ投げ方向の静かなフォロースルーを保持する。
- 地上の踏ん張りは2コマを繰り返さず、一度収束して保持。張力表示に0.12秒の余韻を持たせ、小さなForceの有無で点滅させない。**追加Forceや張力計算の変更ではない。**

## 比較資料

左から **Idle / 修正前 / 修正後**、同じ32 PPU相当・2倍表示。

- [走り比較動画](compare-run.mp4)
- [チャージ比較動画](compare-charge.mp4)
- [チャージ走り比較動画](compare-charge-run.mp4)
- [降下比較動画](compare-fall.mp4)
- [チャージの時系列比較画像](charge-filmstrip.png)
- [現在の各Spriteの再生・コマ送り](../Implementation/preview.html)

比較動画はSpriteの再生資料で、鉄球の物理やゲームの状態遷移を再現したものではない。降下の修正後は同じ絵へ収束して保つため、後半は大きく動かないのが意図した状態。

**実行を進めて撮影したゲーム内画像:** [Stage2走行](../Implementation/Stage2-run.png) / [降下](../Implementation/Stage2-fall.png) / [着地](../Implementation/Stage2-land.png) / [静止チャージ](../Implementation/Stage2-calm-charge.png) / [走りチャージ](../Implementation/Stage2-calm-charge-run.png) / [空中チャージ](../Implementation/Stage2-calm-charge-air.png) / [上投げ](../Implementation/Stage2-calm-throw-up.png) / [投げた後の通常復帰](../Implementation/Stage2-calm-after-throw.png) / [Stage1降下](../Implementation/Stage1-fall.png)。

## 原因と寸法合わせ

前版はコマごとの頭の角度・寸法・配置が違い、Skirtの暗い領域とシルエット全体の下端で原点を求めていた。武器や脚の位置によって基準が変わり、連続再生では揺れが大きくなっていた。さらに、短い降下・張力ループ、投擲中の張力による割り込み、チャージの回転位相に結び付いた脚の再生が落ち着かない見え方を増やしていた。

[calibration.json](../CalmSources/calibration.json) に原画ごとの頭蓋の基準点・寸法、姿勢ごとの基準位置、接地の指定を保存。肌・髪の領域から得た候補をそのまま無条件には使わず、上向きの顔・手が顔の近くにあるコマ等は確認して校正した。Pythonスクリプトは画素を読み取る検査専用で、絵を編集・書き出さない。ゲーム用SpriteはUnity Editorの書き出し・Sprite Editor Data Provider経由で更新している。

既存JumpUpにある0.95倍のルートScale曲線は物理への影響を避けて残し、先頭Spriteの取り込み寸法で見た目の縮小を相殺。32 PPU、Point、非圧縮、MipMapなし、192pxセルと既存Clip GUIDを保持した。

描き直しにはbuilt-in image_genを使用。[編集プロンプトと採否](prompts.json)、[採用原画](../CalmSources/)を保存。横投げ・回収／張力は既存原画を再利用して寸法を合わせた。

## 検証

- Unityコンパイル完了。Play Modeの検証を実行できる状態。最終確認時に関連する新規Runtime Error／Exceptionなし。
- [素材検査](../Implementation/asset-validation.txt): 78コマ、地上／空中8方向、参照・取り込み、降下／踏ん張りの非ループ設定。
- [つながりの回帰検査](continuity-validation.txt): 張力が投擲を途中で上書きしない、張力角を変えても同方向フォロースルーを維持、チャージの脚は回転位相で進まない、降下・踏ん張り・空中張力の収束が再ループしない。
- [状態復帰](../Implementation/runtime-validation.txt)・[チャージ中断](../Implementation/charge-transition-validation.txt): 8位相、地上／空中×左右、回収・解除・Respawn後の共通Rest物理と描画順復帰、中央の鎖の握りを確認。
- [Stage2実入力](../Implementation/Stage2-input-exercise.txt)・[Stage1実入力](../Implementation/Stage1-input-exercise.txt): 仮想Gamepad経由で走行・上昇・降下・着地を確認、すべてTrue。
- [戦闘動作の実入力](live-combat-validation.txt): 左ショルダーの静止チャージ→移動チャージ→ジャンプして空中チャージ→解除→右スティックで上投げ→通常復帰を確認、すべてTrue。[時系列記録](live-combat-trace.csv)も保存。
- [保護対象のハッシュ](protected-validation.txt): ぶら下がりPNG／meta、Idle PNG／Clip一致。
- [最終監査](final-audit.txt): 78枚すべてのセル周囲に透明余白あり。発射速度30、既存半径フィールド1、回転720、最大チャージ1.2秒、発射姿勢の既存保持0.5／0.4秒を確認。Stage2・Play停止・未保存変更なし。

最低限の状態検査に加え、原寸の連続コマ・比較動画からの時系列画像・実ゲーム画像を見直した。Editorの書き出し時に深い着地の伸張を検査で検出したため、その絵を採用せず、浅い着地と小さな収束へ修正してから組み込んだ。

## 変更ファイルと設定

- `Assets/Script/MorningStarLauncher.Motion.cs`: 表示優先順位・張力表示の保持・チャージ走行の選択。
- `Assets/Script/PlayerMotionSet.cs`: チャージ脚の歩調を回転位相から分離。
- `Assets/Editor/PlayerMotionV2Setup.cs`、新規 `PlayerMotionV2Setup.Calm.cs` と `.meta`: 校正した寸法・原点・接続点で書き出し、承認済みの吊り下がり素材を再取り込みしない。
  保存も対象モーションセットと5本のClipに限定し、Play検証で生成された無関係なTMPフォントアトラス等を巻き込まない。
- `Assets/Editor/PlayerMotionV2Validation.cs`: コマ数・非ループ・連続表示の回帰検査。
- `Assets/Art/Player/MotionV2/`: ぶら下がり・登り以外の11 PNGと取り込み情報。Run／ChargeRunは6コマへ変更。
- `Assets/Resources/PlayerMotion/PlayerMotionV2.asset`、既存5本の移動／ジャンプ／投擲Clip、制作・比較資料。

追加のInspector項目はなし。既存 `playerMotionSet` を使用。コマ時間は走り1周0.85秒、チャージ走り約4.588コマ/秒、静止／空中チャージは従来の回転位相、降下は0.18秒で収束、踏ん張りは0.1秒で収束。物理の保持時間を延長してアニメーションを待たせていない。

移動・ジャンプ・通常の発射速度／連続角度・回収・張力・重力・質量・減衰・ダメージの設定は変更していない。チャージ専用の半径1.25、導入0.12秒、たるみ最大0.9、回転速度720度/秒、最大チャージ1.2秒も前版から保持。変更したのはSprite上の接続点と表示の選択で、通常の物理支点の値は保持している。

## 終了状態・残る確認

今回の改修ではScene／Prefabを変更・保存していない。終了時は元のStage2、Play停止、未保存Scene変更なしへ戻した。既存の作業変更は保持し、未コミット・未push。

実機Gamepadでの最終的な好み・操作感、全ステージの長時間通しプレイ、Playerビルドは未実施。最終の動き心地はユーザー確認とする。
