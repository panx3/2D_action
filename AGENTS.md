# AGENTS.md

## 適用範囲

- このファイルはリポジトリ全体に適用する。
- 作業開始時に、対象パスまでの間により深い `AGENTS.md` がないか確認する。存在する場合は、より対象に近いルールを優先する。
- 本プロジェクトは Unity 6（`6000.4.2f1`）の2Dアクションゲーム「鉄球少女」。入力は Input System を使用する。

## 現在のプロジェクト構成

- Build Settings の本番導線は `Assets/Scenes/TitleScene.unity`（index 0）から `Assets/Scenes/CompletScene.unity`（index 1）。
- `CompletScene` の主要構成は次のとおり。
  - `Main Camera`
  - `Test_Stage/Gimmicks`：壁、スイッチ、扉、チェックポイント、リスポーン、磁力、ゴール、足場、トラップ、敵など
  - `__ImportedFromSampleScene`：`Player`、`morningstar`、`ChainLine`、`PlayerHUD`
  - `BackgroundRoot`、`StageAudio`、`Grid`
  - `PauseCanvas`、`GoalCanvas`、`CrystalAcquiredUI`、`CRT_Filter`、`EventSystem`
- ゲーム用Prefabは主に `Assets/Prefabs/Enemies`、`Assets/Prefabs/Gimmicks`、`Assets/Prefabs/UI` にある。Player系Prefabは `Assets/Player.prefab` などルート直下にもあるため、名前だけで正本を決めず、Scene上のインスタンスとPrefab接続元・overrideを確認する。
- ランタイムコードは主に `Assets/Script`、Editor用のセットアップ・検証・Play Mode補助コードは `Assets/Editor` にある。asmdefはなく、通常は `Assembly-CSharp` / `Assembly-CSharp-Editor` としてコンパイルされる。
- `Assets/_Recovery` と `Assets/Scenes/_MergeReference` は復旧・比較用。明示的な依頼がない限り、本番Sceneとして編集・置換しない。
- `Library`、`Temp`、`Logs`、`obj`、生成された `.csproj` / `.slnx` は成果物ではない。依頼なしに編集・追加・削除しない。

## 変更前の必須調査

1. `git status --short` と対象ファイルのdiffを確認し、既存の未コミット変更を把握する。ユーザーの変更を上書き・巻き戻し・整形しない。
2. Unity MCPで現在開いているScene、dirty状態、対象GameObjectのHierarchyとComponentを確認する。調査目的だけでSceneを保存しない。
3. 関連するScene、Prefab、Scriptをすべて特定する。PrefabはAsset本体だけでなく、Sceneインスタンスのoverrideと参照先も確認する。
4. `rg` やUnity MCPのAsset検索で型名・SerializeField・イベント購読・Scene遷移・呼び出し元を追い、既存の責務所有者を決める。
5. 不具合対応では、再現条件とUnity Consoleの既存Error/Exceptionを先に記録する。原因不明のまま推測で変更を重ねない。仮説ごとに関連箇所を読み、最小の確認で原因を絞る。

## 実装ルール

- 既存仕様、操作感、物理挙動、入力割り当て、演出タイミング、音量、Scene遷移を依頼なく変更しない。特にPlayer移動、ジャンプ、鉄球の投擲・回収・Hook/Swing、鎖長、当たり判定は感触に直結する。
- 必要以上の大規模リファクタリング、命名変更、ファイル移動、全体整形をしない。目的を満たす最小のdiffにする。
- 既存コード、イベント、Controller、Utility、Prefabを再利用し、似た役割のManagerや状態管理、AudioSource、入力処理を重複実装しない。
- 新しい責務を追加する前に、下の所有関係を確認する。既存の所有者を拡張できる場合はそこへ統合する。
- 公開・SerializeFieldの名前や型を変更するとScene/Prefab参照が壊れ得る。変更が必要なら参照範囲を調査し、移行方法を用意する。
- Tag、Layer、Physics 2D設定、Input Actions、Animator parameter、Scene名を文字列で使う箇所を変更する場合は、Project Settingsと利用箇所を横断確認する。
- `Time.timeScale` はPause、Goal、タイトル遷移、ゴール演出、鉄球のヒットストップで共有される。変更前後で競合、解除漏れ、Retry/Title復帰後の値を確認する。

## Unity Assetの安全な編集

- Unity Editor上のScene、Prefab、GameObject、Component、ScriptableObject変更には、可能な限りUnity MCPを使用する。現在のEditor状態とAssetDatabaseに同期した方法を優先する。
- Unity MCPに接続できる状態では `.unity`、`.prefab`、`.asset` のYAMLを手編集しない。
- Unity MCPが利用できない場合は、接続不良・Safe Mode・コンパイルエラーを先に切り分ける。やむを得ず直接編集する場合は理由を報告し、GUID/fileIDと既存diffを保全できる最小変更に限る。
- Assetの移動・改名・削除では対応する `.meta` とGUIDを維持し、参照切れを確認する。OSのファイル操作よりUnity MCP / AssetDatabase経由を優先する。
- `Assets/Editor` の `*Setup`、`*Integrator`、`*Builder` はSceneやPrefabを保存・再生成し得る。内容と変更対象を読まずに実行しない。特に `TitleSceneBuilder` や各種Integratorを単なる検証として実行しない。
- Scene/Prefabを触る前後でdirty状態と `git diff` を確認し、対象外の自動保存・再import差分を持ち込まない。

## 主要な責務マップ

| 領域 | 主なScript / Asset | 変更時の注意 |
|---|---|---|
| Player移動・ジャンプ・向き・SFX | `Player.cs`、`PlayerHealth.cs`、`Assets/Player.prefab` | Input System、Animator、接地判定、鉄球との連携を一緒に確認する |
| 鉄球・鎖 | `MorningStarLauncher.cs`、`ChainConstraint2D.cs`、`ChainLineController.cs`、`ChainLinkVisualController.cs` | 物理状態と見た目を分離したまま、既存state machineを再利用する |
| ダメージ・死亡・復帰 | `PlayerHealth.cs`、`DeathRespawnManager.cs`、`GimmickRespawnController.cs`、`Checkpoint.cs` | HP0死亡とギミック復帰の所有範囲を混同しない |
| 敵・破壊 | `Enemy.cs`、`EnemyHealth.cs`、`TekkyuEnemy.prefab`、`BreakableWall.cs` | `MorningStarHitContext` / `IMorningStarHitReceiver` の既存経路を使う |
| ゴール | `GoalPoint.cs`、`CrystalAcquiredUI.cs`、`GoalMenuController.cs` | 最終ヒット、BGM切替、演出、Pause抑止、メニュー表示の順序を維持する |
| Pause・Scene遷移 | `PauseMenuController.cs`、`GoalMenuController.cs`、`TitleScreenController.cs`、`TitleStageTransition.cs` | UI選択状態、InputSystem UI、`timeScale`、Retry/Title遷移を回帰確認する |
| BGM・効果音 | `GameBgmController.cs`、`OneShotAudioUtility.cs`、`StageAudio`、Player配下のSFX用AudioSource | BGMの単一所有と既存SFXを維持し、二重再生を作らない |
| Camera・画面演出 | `CameraFollow.cs`、`CameraShake2D.cs`、`CRTFilterController.cs`、背景系Script | 追従、揺れ、Pixel/CRT表示、UI表示を別々に確認する |
| ギミック | `Assets/Script/Gimmicks`、`Assets/Prefabs/Gimmicks` | Scene上の参照、Tag/Layer、Respawn時の初期化を確認する |

## 検証ルール

- 実装後はUnityのimport/compile完了を待ち、Unity Consoleの新しい `Error` と `Exception` を確認する。既存ログと今回発生したログを時刻・内容で区別する。
- コンパイルエラーが1件でも残る状態を完了扱いにしない。Unity MCP自身の認証・接続ログと、ゲームコード／Asset由来のエラーは分けて報告する。
- C#の補助確認には、必要に応じて `dotnet build Assembly-CSharp-Editor.csproj --no-restore` を使う。ただし生成プロジェクトのビルド成功だけでUnity上の動作確認を代替しない。`2D_action.slnx` は環境によって `MSB4068` になるため、検証経路として固定しない。
- 関連する `Assets/Editor/*PlayModeTest.cs`、Validator、Setupがある場合は、先に内容を確認する。変更を伴うSetupと、読み取り中心の検証を区別して対象に合うものだけ実行する。
- 可能ならPlay Modeで実際の操作経路を確認する。最低限、変更箇所に応じて次を選ぶ。
  - Player：移動、左右反転、ジャンプ、着地、死亡、リスポーン
  - 鉄球：投擲、回収、地面衝突、Hook/Swing、磁力、敵・壁へのヒット
  - UI/Flow：Pause開閉、操作説明、Goal演出、Retry、Title復帰
  - BGM：Title → Stage → Goal → Title、およびRetry後の重複再生なし
- Play Mode確認後はPlayを停止し、`timeScale`、Scene dirty、Console、`git status` を再確認する。
- 最後に対象diffを読み直し、Scene/Prefab/Scriptと `.meta` 以外も含め、依頼外の変更がないことを確認する。

## 完了報告

- 変更したファイルと変更理由を列挙する。
- 実行した検証、Unity ConsoleのError/Exception、Play Mode実施有無と結果を簡潔に報告する。
- 未確認事項や既存問題があれば完了と混同せず明記する。
- コンパイルエラー、参照切れ、再現中の重大不具合を残したまま「完了」としない。
