# 石造SFゲートと一度式床スイッチ

生成済みの `Gate_ClosedOpen_EyeLevel.png` と `PressureSwitch_RaisedPressed.png` を使用。
モニター付きの HitSwitch は変更していない。

## 実装と設定

- `Assets/Prefabs/Gimmicks/GimmickDoor.prefab` は既存の GimmickDoor を継続使用。
  - Door_Back: 描画順 -2、Door_Barrier: 1、Player: 2、Door_Front: 8。
    キャラを青い膜より前に描き、青く透けてめり込んで見えないようにした。
  - 石枠は同じ絵を前後に分割。開閉でフェードするのは抽出した青い膜だけ。
  - 開くと膜の BoxCollider2D が即座に無効になり、0.28秒で膜が消える。
  - 柱の Collider は追加していない。開いた空間に不可視の壁は残らない。
  - 遮断判定は膜の中央の厚さ0.12 world units。Player本体の半径を含め、中心線の手前約0.56で止まる。
  - 薄い壁への高速接近でDiscreteのすり抜けを再現したため、Player.AwakeでContinuousを設定。
  - 標準の root scale (2,4) のままで、外枠は約 6.34 × 6.17 world units。
    旧前景枠の約 2.8 × 4.68 に対して大きくした。下端の位置は維持。
- `Assets/Prefabs/Gimmicks/WeightSwitch.prefab` は既存の WeightSwitch を継続使用。
  - morningstar タグの最初の接触で ON に固定し、検知 Collider を停止。
  - 台座は固定し、キャップが0.16秒で沈み、ピストンが縮む。最後は押し込み後の絵に切り替える。
  - 再接触、複数 Collider の同時接触、disable/enable で再発火しない。
  - Pressed イベントは一度だけ発火し、既存の振動フィードバック連携を維持。
    C# Pressedは押した瞬間、UnityEvent onPressedは扉にカメラが到着した時に発火する。
  - 旧 onReleased のシリアライズは互換性のため保持するが、呼び出さない。
  - 押し込み距離 0.4875 sprite units。標準幅では約0.775 world units。
  - 素材の幅は約3.05 world units。未作動時は約1.5、作動後は約0.7の見た目の高さ。
  - 状態はシーン内で継続。シーン再読み込み・リトライで初期状態になる。
  - 台座Visualと可動PressureCapにPlayer専用の非Trigger BoxCollider2Dを追加。
    既存のplayerレイヤー（3）のみ衝突、他のレイヤーは除外、layerOverridePriority=1。
    Defaultレイヤーは維持し、Floorタグで接地として扱う。プロジェクト全体のLayer設定は変更しない。
    台座はローカルサイズ(1.78,0.40)・offset(0,0.20)、キャップは(1.58,0.36)・offset(0,0.75)。
  - キャップの当たり判定は押し込みと一緒に沈み、押し込み後の絵に切り替わっても残る。
    中断時も必ずpressTravel分の最終位置に合わせる。
  - プレイヤーは乗れるが作動しない。rootの検知Triggerは従来通りmorningstarタグだけでONになる。
    鉄球はPlayer専用の固体判定に遮られない。
- 音は `Assets/Audio/SFX/Imported/StoneSwitchPress.mp3`。
  - ユーザー提供MP3をそのままコピー（SHA256一致、約1.18秒）。音量0.9。
  - OneShotAudioUtility を使用し、ONへの一度の遷移で再生する。
- 開扉音は `Assets/Audio/SFX/Imported/StoneGateOpen.mp3`。
  - 提供された `turning_pages-sci-fi-power-down-blip-608638.mp3` を変更せずコピー。約0.432秒、音量0.9。
  - GimmickDoorが閉→開へ切り替わり、膜が消え始める瞬間に一度だけOneShotAudioUtilityで再生。
    スイッチのカメラ演出では扉への到着後に鳴る。初期Open・開状態への重複Open・Closeでは鳴らない。
  - 時間停止中も音は再生される。DecompressOnLoad / PCM / Preload設定。
  - 開扉音追加後はコンパイル・Prefab/両保存Sceneへの割り当て・Consoleを確認。
    新しい音の実機再生・聴感は未確認。以前の46項目は演出追加時の結果で、今回再実行していない。
- 画像8枚は `Assets/Art/Gimmicks/StoneGate/`。Point、非圧縮、MipMapなし、100 PPU。
  - 生成シートから4分の1へNearestで切り出し。背景の薄い光を除いて輪郭を保つ。
  - すべて同じ原点で分割し、開閉で石枠がずれないようにした。
  - 元のrootの配置・scaleは維持。子の縦倍率でrootの非等方scaleを相殺し、ドットを引き伸ばさない。
  - 既存シーンの古い子Transformオーバーライドも、Awakeで再整列する。

## スイッチから扉へのカメラ演出

1. 接触時に音と振動を開始し、ONを確定。物理時間を0にする。
2. 押し込みを実時間で進め、0.12秒の間を置く。
3. CameraFollowのイベント所有権を取得し、1.6秒で扉の全体中心へ移動。
4. 到着後0.18秒待って、既存のonPressed/Open接続を一度だけ発火。
5. 開扉を0.70秒見せる。膜は時間停止中でも実時間0.28秒で消える。
6. 0.40秒で押した瞬間のプレイヤー構図へ戻り、入力・時間・通常追従を再開する。

- カメラ位置の更新はCameraFollow.SetEventPositionから行い、既存の境界とイベント所有権を維持。
- `doorToShow`未指定時は既存のonPressed/GimmickDoor.Open接続から自動取得。無効な接続は除外。
- 演出中はPlayer/MorningStarの専用ギミック入力ロックを使用。Goalの入力ロックとは分離。
- Rigidbodyの速度や鉄球の状態をリセットしない。停止前の時間倍率を保持し、終了時に復帰。
- 既存ヒットストップをキャンセルしてから時間を引き継ぐ。Pauseは禁止し、クリアタイムには加算しない。
- カメラ・Player・扉が使えない時や、別のカメラ演出中は、その演出を奪わず直接開扉する。
- 途中でスイッチが無効になると、接続を確定してPlayer構図と時間を復帰。
  Goalが時間を取得した場合は停止を維持し、Respawnがカメラを取得した場合はそのカメラを奪わない。

## 編集と検証

- 設定の再適用: `鉄球少女/Gimmick/石造ゲートと一度式床スイッチを適用`。
  - GimmickVisualSetup の既存所有範囲を拡張した。Prefabを保存し、開いている対象も更新する。
  - Sceneは自動保存しない。既存の無関係な編集やイベント接続を保持する。
- 足場判定だけの再適用: `鉄球少女/Gimmick/床スイッチのPlayer足場判定を適用`。
  - 足場判定追加時はPrefabと開いているインスタンスの当たり判定・Floorタグだけを更新。
    既存の画像・配置・音量・カメラ演出のInspector値を維持した。
  - Play Modeの独立PhysicsScene2Dで11項目を確認。Playerと同寸のCapsuleが未作動・作動後の
    両方に着地でき、接地レイヤーとして検出されること、PlayerではOFFのまま、鉄球ではONに
    なることと鉄球を遮らないことを検証。結果は `output/switch-player-collision/validation.txt`。
    実際のゲームパッド操作による乗り心地は未確認。
- `StoneGateSwitchPlayModeChecks.Run()` で限定したPlay Mode検証。
  - 独立したPhysicsScene2Dで、実際のPrefabとPlayerと同じ1 × 3.3のCapsuleを使用。
  - 閉状態の中央での遮断、左右50 units/sでの接近、開状態の完全通過、描画順、
    タグ判定、音、押し込み、再接触固定と、実際のPlayer/CameraFollow/時間・入力所有者を使う演出検証。
  - 46項目がPASS。ヒットストップからの引き継ぎ、非1倍時間の復帰、Pause抑止、
    クリアタイム除外、無効化・カメラ競合・Goal/Respawnへの引き継ぎも含む。実行中のError/Exceptionは0。
  - 実際のPlayer入力操作による最終的なゲーム感、ステージ全体の見え方、音量の聴感は未確認。
- コンパイル成功。最終確認時の直近Console Error/Exceptionは0。
- Stage1 / CompletScene をPreviewで読み、保存済みの各Sceneにも新しい絵・音・Open接続が
  Prefab経由で引き継がれていることを確認。
- Stage1には作業前から未保存変更あり。両SceneファイルのSHA256は作業前後で一致。
  Stage1の対象オブジェクトは開いているScene上でも更新済みで、未保存状態を維持。
- 対象外の既存変更、Packages、Project Settingsは変更していない。
- Editorの再読み込み時にUnity MCPのSKILL.md自動生成で共有違反ログが一度発生したが、
  ゲーム検証中は発生せず、最終確認の直近Consoleにも残る新規エラーはなかった。

切り出しスクリプト、復旧用の変更前コピー、検証結果と比較画像は `output/gate-switch-integration/`。
カメラ演出追加時の変更前コピー・最終検証結果・中央で止まる表示の確認画像は `output/gate-switch-reveal/`。

変更ファイルは `Assets/Script/Gimmicks/GimmickDoor.cs`、`WeightSwitch.cs`、
`Assets/Editor/GimmickVisualSetup.cs`、`GimmickVisualSetup.StoneGate.cs`、
`StoneGateSwitchPlayModeChecks.cs`、上記2 Prefab・8 PNG・MP3と新規アセットの各.meta。
追加の演出実装は `Assets/Script/Gimmicks/WeightSwitch.DoorReveal.cs`。
`CameraFollow.cs`、`Player.cs`、`MorningStarLauncher.cs`、`GoalMenuController.cs`の既存所有者を拡張。
演出の回帰チェックは `Assets/Editor/StoneGateSwitchPlayModeChecks.DoorReveal.cs`。

仕様確認にはUnity 6000.4同梱の `Collider2D-isTrigger.html`、`BoxCollider2D-size.html`、
`MonoBehaviour.OnTriggerEnter2D.html`、`AudioImporterSampleSettings-preloadAudioData.html`を参照。
演出時間と高速衝突は同梱の `Time-unscaledDeltaTime.html`、`CollisionDetectionMode2D.Continuous.html`も参照。
Player専用衝突の設定は同梱の `Collider2D-includeLayers.html`、`Collider2D-excludeLayers.html`、
`Collider2D-layerOverridePriority.html`、`Physics2D.IgnoreCollision.html`を参照。
