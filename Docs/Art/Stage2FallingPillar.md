# Stage2 倒れる石柱

画像生成には imagegen の組み込みツールを使用。添付の柱のシルエット、段のある基部、色味を参照した。

- 生成元 `Assets/Art/Gimmicks/FallingPillarBridge/StoneColumnStages_Source.png`
- Unity 用素材は `FallingPillarBridgeSetup` で切り出し、24 PPU と Point フィルタを設定。
- 立っている高さは約14.88。折れ口は地面から約2.33。長い部分は約12.43。
- 回転支点は折れ口の右端（ローカル座標 `(1.0417, -0.0833)`）。着地直前に外側へ約0.744移動し、根元との重なりを抑える。
- 立った柱・残る台座・倒れた柱それぞれの輪郭に合わせた PolygonCollider2D を使用。

## 生成プロンプト

Convert the attached reference to a production 2D action game pixel art sprite sheet. Retain the reference's specific silhouette and design: long weathered warm gray stone cylinder, shallow solid round cap, subdued bronze band, and a substantial round stepped foundation with small restrained fasteners. Match the supplied shapes closely. The stepped circular foundation is very important and remains identical in all four stages. Thick dark outline, hard crisp square pixels and 16-bit game shading, no smooth/painted blending. Original game pixel density about 70 pixels wide including base and 310 high, nearest-neighbor upscale. Match the image's warm brown/gray stone material. Closed solid stone, no pipe opening. Flat side-on orthographic 2D view with shallow ellipses. Four identical aligned pillars in four equally spaced 384px-wide columns on 1536x1024 transparent canvas. Keep complete bounds within each column; top around y35, bottom around y995. Stage 1 undamaged. Stage 2 short dark fissure above the stepped foundation, around y850. Stage 3 branching lateral crack at the same low height, not starting at ground. Stage 4 same upper pillar completely broken off with a rugged zigzag solid stone cross section, and the short base and broken stump remaining BELOW in the same column. Give stage 4 an actual transparent horizontal separator from y836 to y850, upper broken edge around y802-835, lower broken edge around y851-885; base bottom at y995. Maintain exact dimensions and design across all four damage states. No captions, labels, floor, loose debris outside silhouettes, cast shadows or background. Truly transparent RGBA.
