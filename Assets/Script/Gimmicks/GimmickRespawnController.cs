using UnityEngine;

/// <summary>
/// ギミック復帰地点とPlayer／鉄球の共通復元処理を管理する。
/// 奈落復帰ではHPペナルティを適用する。
/// HP0死亡時の演出は DeathRespawnManager、リトライ選択はGameOverMenuControllerが担当する。
/// </summary>
public class GimmickRespawnController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private Rigidbody2D playerRigidbody;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private MorningStarLauncher morningStarLauncher;
    [SerializeField] private CameraFollow cameraFollow;
    [SerializeField] private Transform initialRespawnPoint;

    [Header("Optional Morning Star Reset")]
    [SerializeField] private Transform morningstar;
    [SerializeField] private Rigidbody2D morningstarRigidbody;
    [SerializeField] private Vector2 morningstarOffset = new Vector2(-1.5f, 0f);

    private Vector3 currentRespawnPosition;
    private DeathRespawnManager deathRespawnManager;

    public Vector3 CurrentRespawnPosition => currentRespawnPosition;

    private void Awake()
    {
        if (player == null)
        {
            Player resolvedPlayer = FindAnyObjectByType<Player>(FindObjectsInactive.Exclude);
            if (resolvedPlayer != null)
                player = resolvedPlayer.transform;
        }

        if (playerRigidbody == null && player != null)
            playerRigidbody = player.GetComponent<Rigidbody2D>();

        if (playerHealth == null && player != null)
            playerHealth = player.GetComponent<PlayerHealth>();

        if (morningStarLauncher == null && player != null)
            morningStarLauncher = player.GetComponent<MorningStarLauncher>();

        if (cameraFollow == null)
            cameraFollow = FindAnyObjectByType<CameraFollow>(FindObjectsInactive.Exclude);

        if (initialRespawnPoint != null)
        {
            currentRespawnPosition = initialRespawnPoint.position;
        }
        else if (player != null)
        {
            currentRespawnPosition = player.position;
        }
    }

    public void SetRespawnPoint(Vector3 position)
    {
        currentRespawnPosition = position;
        Debug.Log("Checkpoint Updated");
    }

    public void Respawn()
    {
        if (deathRespawnManager == null)
            deathRespawnManager = FindAnyObjectByType<DeathRespawnManager>();
        if (deathRespawnManager != null && deathRespawnManager.isActiveAndEnabled)
        {
            deathRespawnManager.RespawnFromFall(currentRespawnPosition);
            return;
        }
        if (playerHealth != null) playerHealth.ApplyFallRespawnPenalty();
        if (playerHealth != null && playerHealth.IsDead)
        {
            GameOverMenuController.ShowForDeath();
            return;
        }
        RespawnAt(currentRespawnPosition, false);
    }

    /// <summary>
    /// 死亡・落下のどちらからも同じ位置／MorningStar初期化処理を使用する。
    /// </summary>
    public void RespawnAt(Vector3 position, bool restoreFullHealth, bool snapCamera = true)
    {
        if (player == null)
            return;

        currentRespawnPosition = position;

        // チェックポイントまたは初期地点へ戻す
        player.position = position;

        if (playerRigidbody != null)
        {
            playerRigidbody.position = position;
            playerRigidbody.linearVelocity = Vector2.zero;
            playerRigidbody.angularVelocity = 0f;
        }

        // 奈落復帰は減少後のHPを維持する。全回復は明示的な呼び出しに限る。
        if (playerHealth != null)
        {
            if (restoreFullHealth || playerHealth.CurrentHp <= 0)
                playerHealth.ResetToFullHp();
            else
                playerHealth.ReviveKeepCurrentHp();
        }

        if (morningStarLauncher != null)
        {
            morningStarLauncher.ResetForRespawn();
        }
        else if (morningstar != null)
        {
            morningstar.position =
                position + (Vector3)morningstarOffset;
        }

        if (morningStarLauncher == null && morningstarRigidbody != null)
        {
            morningstarRigidbody.linearVelocity = Vector2.zero;
            morningstarRigidbody.angularVelocity = 0f;
        }

        // Player位置・速度、MorningStar／Chainを直した後にカメラを即座に復帰する。
        // Snap内でLook Aheadと残留CameraShakeも0へ戻る。
        if (snapCamera && cameraFollow != null)
            cameraFollow.SnapToTarget();

        Debug.Log("Player, MorningStar and Chain respawned safely");
    }
}
