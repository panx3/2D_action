using System.Collections;
using UnityEngine;

/// <summary>
/// HP0による死亡リスポーンを管理する。
/// 最後に通過したCheckpoint位置へPlayerを戻し、死亡時のみHPを全回復する。
/// RespawnZoneなどのギミック即時復帰は GimmickRespawnController が担当する。
/// </summary>
[DisallowMultipleComponent]
public class DeathRespawnManager : MonoBehaviour
{
    [Header("スポーン地点")]
    [SerializeField, Tooltip("ゲーム開始時のスポーン位置。未通過のチェックポイントが無い場合はここに戻る。")]
    private Transform _defaultSpawnPoint;

    [Header("参照")]
    [SerializeField, Tooltip("リスポーン対象の Player。")]
    private Player _player;
    [SerializeField, Tooltip("リスポーン対象の PlayerHealth。OnDead を購読する。")]
    private PlayerHealth _playerHealth;
    [SerializeField, Tooltip("落下復帰と共通のPlayer／MorningStar／Chain初期化を行うController。")]
    private GimmickRespawnController _gimmickRespawnController;
    [SerializeField, Tooltip("GimmickRespawnControllerが無い場合のCamera即時復帰先。")]
    private CameraFollow _cameraFollow;

    [Header("リスポーン設定")]
    [SerializeField, Tooltip("死亡から復活までの待機時間（秒）。")]
    private float _delaySeconds = 1.5f;

    private Vector2 _lastCheckpoint;
    private bool _hasCheckpoint;
    private Coroutine _respawnRoutine;

    private void Awake()
    {
        ResolvePlayerReferences();

        if (_gimmickRespawnController == null)
            _gimmickRespawnController = FindAnyObjectByType<GimmickRespawnController>(FindObjectsInactive.Exclude);

        if (_cameraFollow == null)
            _cameraFollow = FindAnyObjectByType<CameraFollow>(FindObjectsInactive.Exclude);

        if (_defaultSpawnPoint != null)
        {
            _lastCheckpoint = _defaultSpawnPoint.position;
            _hasCheckpoint = true;
        }
    }

    private void ResolvePlayerReferences()
    {
        if (_player == null)
        {
            _player = FindAnyObjectByType<Player>(FindObjectsInactive.Exclude);
            if (_player != null)
                Debug.LogWarning("[DeathRespawnManager] Player was auto-found. Assign it in Inspector to avoid wrong references.", this);
            else
                Debug.LogWarning("[DeathRespawnManager] Player is not assigned and could not be found.", this);
        }

        if (_playerHealth == null)
        {
            _playerHealth = FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Exclude);
            if (_playerHealth != null)
                Debug.LogWarning("[DeathRespawnManager] PlayerHealth was auto-found. Assign it in Inspector to avoid wrong references.", this);
            else
                Debug.LogWarning("[DeathRespawnManager] PlayerHealth is not assigned and could not be found.", this);
        }
    }

    private void OnEnable()
    {
        if (_playerHealth != null)
            _playerHealth.OnDead += HandlePlayerDead;
    }

    private void OnDisable()
    {
        if (_playerHealth != null)
            _playerHealth.OnDead -= HandlePlayerDead;
    }

    /// <summary>
    /// チェックポイント通過時に呼ぶ。以降の死亡で同位置から復活する。
    /// </summary>
    public void RegisterCheckpoint(Vector2 position)
    {
        _lastCheckpoint = position;
        _hasCheckpoint = true;
    }

    /// <summary>
    /// リスポーン処理を手動でトリガー（OnDead を介さず呼ぶ場合）。
    /// </summary>
    public void Respawn()
    {
        if (_respawnRoutine != null) return;
        _respawnRoutine = StartCoroutine(RespawnRoutine());
    }

    private void HandlePlayerDead()
    {
        Respawn();
    }

    private IEnumerator RespawnRoutine()
    {
        if (_delaySeconds > 0f)
            yield return new WaitForSeconds(_delaySeconds);

        Vector2 target = _hasCheckpoint
            ? _lastCheckpoint
            : (_defaultSpawnPoint != null ? (Vector2)_defaultSpawnPoint.position : (Vector2)transform.position);

        if (_gimmickRespawnController != null)
        {
            _gimmickRespawnController.RespawnAt(target, true);
        }
        else if (_player != null)
        {
            Rigidbody2D rb = _player.Rigidbody2D;
            if (rb != null)
            {
                rb.position = target;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            else
            {
                _player.transform.position = target;
            }
        }

        if (_gimmickRespawnController == null && _playerHealth != null)
        {
            // HP0 で死亡したリスポーン＝リトライ（満タン復帰）。HP が残っている場合のみ HP 維持。
            if (_playerHealth.CurrentHp <= 0)
                _playerHealth.ResetToFullHp();
            else
                _playerHealth.ReviveKeepCurrentHp();
        }

        // 通常経路ではGimmickRespawnController側がReset完了後にSnapする。
        if (_gimmickRespawnController == null && _cameraFollow != null)
            _cameraFollow.SnapToTarget();

        _respawnRoutine = null;
    }
}
