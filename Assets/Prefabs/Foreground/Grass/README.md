# 前景の低い草

採用した foreground-grass-v2.png を6種類に分割。番号は透過余白を除く幅×高さの面積が大きい順です（横幅だけの順ではありません）。色と透過は元画像を維持しています。

| 番号 | Prefab名 | 形 | 幅×高さ (world units) |
|---|---|---|---|
| 01 | ForegroundGrass_01_FlowerFan | 花付きの扇形 | 5.130 × 2.490 |
| 02 | ForegroundGrass_02_LongGrassPatch | 横長の草むら | 6.130 × 1.870 |
| 03 | ForegroundGrass_03_FlowerTuft | 花付きの草むら | 4.970 × 2.190 |
| 04 | ForegroundGrass_04_WideTuft | 幅広の草むら | 5.830 × 1.800 |
| 05 | ForegroundGrass_05_LeaningPetalTuft | 横に流れる花びら付きの草 | 4.600 × 1.660 |
| 06 | ForegroundGrass_06_SmallPetalTuft | 小さな花びら付きの草 | 4.420 × 1.590 |

Sceneへドラッグして下中央の原点を地面に合わせてください。Scaleは(4, 4, 1)が初期値です。初版から縦横を4倍に拡大しています。

- PNG: Assets/Art/Environment/ForegroundGrass/
- 400 PPU、Point、圧縮なし、Mipmapなし、下中央Pivot。
- ForegroundElementを使用。Default / Order 30、白Tint、不透明度1、WorldFixed、重なり時のフェードOFF。
- 地面上でずれない固定配置。Collider / Rigidbodyなし。
- Sceneへの配置・保存は行っていません。最終的な大きさと足元の重なりは配置先で確認してください。
