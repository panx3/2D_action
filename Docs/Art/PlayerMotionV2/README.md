# プレイヤーモーション統一・実装済み

作業ブランチ: `codex/player-motion-unification`  
分岐元: main `3257ea5`  
状態: 頭の揺れ・実際の体格・動作のつながりを改修した78コマ版。未コミット・未push。

[最新の改修・比較動画・検証結果](CalmRevision/README.md) / [ゲーム用Spriteの再生・Idle比較](Implementation/preview.html)

Idleと承認済みのぶら下がりを維持。元の6コマの静かな前傾走りを基礎に戻し、頭の位置と寸法を校正した。降下は髪が柔らかく残る一度きりの収束、投げた後は一定方向のフォロースルーとし、細かな張力の変化で頭を振らない。上投げは顔も上を向く。

原画をそのまま拡大表示せず、Idleから採った96色とゲーム用のドット密度へ統一した。通常移動・発射・鉄球の物理設定は保持。チャージだけは、柄を持つ手と鉄球側の鎖を握る手を分け、後者を回転軸に既存の鉄球を動かす。

制作物:

- ゲーム用シート: `Assets/Art/Player/MotionV2/`
- 共有モーション参照・接続点: `Assets/Resources/PlayerMotion/PlayerMotionV2.asset`
- 最新の採用原画・校正: [CalmSources](CalmSources/)
- built-in image_gen制作プロンプト: [最新の編集](CalmRevision/prompts.json) / [初回制作](production-prompts.json)
- ゲーム内画像・検証記録: [最新改修](CalmRevision/README.md) / [初回組み込み](Implementation/README.md)

Stage1・Stage2の入力による走行／上昇／降下／着地、方向別Sprite、通常発射速度、チャージ位相・中断・Respawn・状態復帰を確認。コンパイル完了、関連Console Error／Exceptionなし。実機Gamepadでの最終的な動き心地と通しプレイはユーザー確認とする。

以前の代表案は [review.html](review.html)、当時の説明は [representative-review.md](representative-review.md) に保存している。これらは制作途中の資料で、現在のゲーム用シートとは異なる。
