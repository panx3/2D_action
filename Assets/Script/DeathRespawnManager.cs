using System.Collections;
using UnityEngine;

/// <summary>落下はHPを減らして復帰し、HP0死亡は演出後にゲームオーバー画面へ進む。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DeathRespawnFeedback))]
public class DeathRespawnManager : MonoBehaviour
{
    [Header("スポーン地点")]
    [SerializeField] private Transform _defaultSpawnPoint;

    [Header("参照")]
    [SerializeField] private Player _player;
    [SerializeField] private PlayerHealth _playerHealth;
    [SerializeField] private GimmickRespawnController _gimmickRespawnController;
    [SerializeField] private CameraFollow _cameraFollow;
    [SerializeField] private DeathRespawnFeedback _feedback;

    [Header("電子リスポーン")]
    [SerializeField, Range(0.15f, 0.3f), Tooltip("電子分解からカメラスライドまでの秒数。旧死亡待機とは独立")]
    private float _dissolveDuration = 0.24f;
    [SerializeField, Range(0.15f, 0.3f)] private float _rebuildDuration = 0.22f;
    [SerializeField, Range(0.4f, 0.8f), Tooltip("カメラ移動のこの進行度から再構築を重ねる")]
    private float _rebuildStartProgress = 0.58f;

    private Vector2 _lastCheckpoint;
    private bool _hasCheckpoint;
    private Coroutine _respawnRoutine;
    private MorningStarLauncher _launcher;
    private Rigidbody2D _body;
    private bool _inputWasEnabled;
    private bool _bodyWasSimulated;
    private Vector3 _respawnPosition;

    public bool IsRespawning { get; private set; }

    private void Awake()
    {
        if (_player == null) _player = FindAnyObjectByType<Player>();
        if (_playerHealth == null && _player != null) _playerHealth = _player.GetComponent<PlayerHealth>();
        if (_gimmickRespawnController == null) _gimmickRespawnController = FindAnyObjectByType<GimmickRespawnController>();
        if (_cameraFollow == null) _cameraFollow = FindAnyObjectByType<CameraFollow>();
        if (_feedback == null) _feedback = GetComponent<DeathRespawnFeedback>();
        if (_player != null)
        {
            _body = _player.GetComponent<Rigidbody2D>();
            _launcher = _player.GetComponent<MorningStarLauncher>();
        }
        if (_defaultSpawnPoint != null) RegisterCheckpoint(_defaultSpawnPoint.position);
    }

    private void OnEnable()
    {
        if (_playerHealth != null) _playerHealth.OnDead += HandlePlayerDead;
    }

    private void OnDisable()
    {
        if (_playerHealth != null) _playerHealth.OnDead -= HandlePlayerDead;
        if (_respawnRoutine != null) StopCoroutine(_respawnRoutine);
        _respawnRoutine = null;
        // 無効化・シーン終了時にも借りた入力／描画／物理を返す。
        if (IsRespawning)
        {
            if (_playerHealth == null || !_playerHealth.IsDead) RestoreAtDestination();
            FinishRespawn();
            if (_feedback != null) _feedback.Clear();
        }
    }

    public void RegisterCheckpoint(Vector2 position)
    {
        _lastCheckpoint = position;
        _hasCheckpoint = true;
    }

    public void Respawn()
    {
        Vector3 target = _hasCheckpoint ? (Vector3)_lastCheckpoint
            : _gimmickRespawnController != null ? _gimmickRespawnController.CurrentRespawnPosition
            : _defaultSpawnPoint != null ? _defaultSpawnPoint.position : transform.position;
        BeginRespawn(target, false);
    }

    public void RespawnFromFall(Vector3 position) => BeginRespawn(position, true);

    private void HandlePlayerDead() => Respawn();

    private void BeginRespawn(Vector3 position, bool falling)
    {
        if (IsRespawning || !isActiveAndEnabled || _player == null) return;
        var intro = _player.GetComponent<PlayerStartIntro>();
        if (intro != null && intro.IsPlaying) intro.enabled = false;
        IsRespawning = true;
        _respawnPosition = position;
        _inputWasEnabled = _player.InputEnabled;
        _bodyWasSimulated = _body != null && _body.simulated;
        _player.SetInputEnabled(false);
        _player.SetStartIntroControl(true);
        _player.SetStartIntroMotion(true, Vector2.zero);
        if (_playerHealth != null) _playerHealth.BeginRespawnProtection();
        if (falling && _playerHealth != null) _playerHealth.ApplyFallRespawnPenalty();
        if (_cameraFollow != null) _cameraFollow.HoldForRespawn();
        if (_feedback != null) _feedback.PlayDeath(_player, _launcher, falling, _dissolveDuration);
        if (_launcher != null) _launcher.BeginRespawnPresentation();
        if (_body != null)
        {
            _body.linearVelocity = Vector2.zero;
            _body.angularVelocity = 0f;
            _body.simulated = false;
        }
        _respawnRoutine = StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        float elapsed = 0f;
        while (elapsed < _dissolveDuration)
        {
            yield return null;
            elapsed += Time.deltaTime;
        }
        if (_playerHealth != null && _playerHealth.IsDead)
        {
            // 選択画面でも入力・物理を止めたままにし、旧チェックポイントへは復元しない。
            _respawnRoutine = null;
            GameOverMenuController.ShowForDeath();
            yield break;
        }
        // カメラは死亡位置に保持し、不可視のPlayerと装備だけ先に復元する。
        RestoreAtDestination();
        if (_cameraFollow != null) _cameraFollow.BeginRespawnTravel(_respawnPosition);
        while (_cameraFollow != null && _cameraFollow.IsRespawnTravelling
            && _cameraFollow.RespawnTravelProgress < _rebuildStartProgress)
            yield return null;
        if (_feedback != null) _feedback.PlayRebuild(_rebuildDuration);
        elapsed = 0f;
        while (elapsed < _rebuildDuration || (_cameraFollow != null && _cameraFollow.IsRespawnTravelling))
        {
            yield return null;
            elapsed += Time.deltaTime;
        }
        FinishRespawn();
        _respawnRoutine = null;
    }

    private void RestoreAtDestination()
    {
        if (_player == null) return;
        if (_gimmickRespawnController != null)
            _gimmickRespawnController.RespawnAt(_respawnPosition, false, false);
        else
        {
            _player.transform.position = _respawnPosition;
            if (_body != null) _body.position = _respawnPosition;
            if (_playerHealth != null) _playerHealth.ReviveKeepCurrentHp();
            if (_launcher != null) _launcher.ResetForRespawn();
        }
        Physics2D.SyncTransforms();
    }

    private void FinishRespawn()
    {
        if (_feedback != null) _feedback.EndPresentation();
        if (_launcher != null) _launcher.EndRespawnPresentation();
        if (_body != null)
        {
            _body.linearVelocity = Vector2.zero;
            _body.angularVelocity = 0f;
            _body.simulated = _bodyWasSimulated;
        }
        Physics2D.SyncTransforms();
        if (_playerHealth != null) _playerHealth.EndRespawnProtection();
        if (_player != null)
        {
            _player.SetStartIntroControl(false);
            _player.SetInputEnabled(_inputWasEnabled);
        }
        if (_cameraFollow != null) _cameraFollow.EndRespawnTravel();
        IsRespawning = false;
    }
}
