# Stage2 Emerald Airship

人影を除いたエメラルド飛行船をStage2のBackgroundRootへ1隻配置。真横の構図、銅・ブロンズの船室と緑の気嚢を使用。生成はBuilt-in image_gen、最終プロンプトはFinalPrompt.txt、生成元はEmeraldAirship-FinalSource.png。

- Sprite: Assets/image_/Stage/Stage2/Verdigris/EmeraldAirship.png。320×160、32 PPU、Point、無圧縮、MipMapなし、透過。
- Prefab: Assets/Prefabs/Environment/Stage2/Verdigris/S2_EmeraldAirship.prefab。
- Material: Assets/Art/Stage2Pilot/EmeraldAirship.mat。既存Background Aerial Perspectiveを使用。Saturation=0.86、暖色の霞=0.14、距離フェード=0.07、5タップぼかしStrength=0.6 / Radius=1.5px。緑を残しながら、遠景らしく輪郭・コントラストを和らげる。2Dの遠景専用ぼかしであり、カメラ全体への被写界深度の追加ではない。既存CRTは1回のみ。
- Scene配置: (-4, 6.2, 7)、Scale 0.9、表示9×4.5 unit。Sorting Default:-950、空(-1000)と工場(-900)の間。
- 既存ForegroundElement / ParallaxLayer2Dを再利用。Horizontal Multiplier=0.04、Vertical Multiplier=0.02、Opacity=1、遮蔽フェードなし。カメラ追従率0.96 / 0.98。ランタイムC#追加・変更なし。
- カメラが(100,6)移動すると飛行船は(96,5.88)移動し、画面内では(-4,-0.12)だけ流れる。逆方向も復元することをPlay Mode確認。ステージ全体で長く見える一隻として配置し、複製ループはしない。
- Collider / Rigidbodyなし。地面、操作、敵AI、ギミック変更なし。SceneとPrefab保存済み。ユーザーのStageSelectJapanese.asset変更は保持。

Play ModeのGameビューを確認。検証中の敵停止・ステージ名Canvas非表示は保存していない。通し攻略と実機での最終的な見え方は未確認。

最終確認: コンパイル失敗なし、シェーダー対応、直近15分のConsole Error / Exceptionなし。Stage2は保存済み・dirtyなし。検証終了後にPlay Mode停止、Pause解除、Time.timeScale=1へ復元。最終の遠景表現はStage2-Airship-GameView.pngを参照。
