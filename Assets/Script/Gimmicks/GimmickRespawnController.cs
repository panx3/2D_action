using UnityEngine;

/// <summary>
/// RespawnZoneや場外落下など、ギミックによる即時位置復帰を管理する。
/// 復帰時はHPを全回復する。
/// HP0死亡時のリスポーンは DeathRespawnManager が担当する。
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
        RespawnAt(currentRespawnPosition, true);
    }

    /// <summary>
    /// 死亡・落下のどちらからも同じ位置／MorningStar初期化処理を使用する。
    /// </summary>
    public void RespawnAt(Vector3 position, bool restoreFullHealth)
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

        // 既存の落下復帰・死亡復帰は満タン、明示的なHP維持復帰にも対応する。
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
        if (cameraFollow != null)
            cameraFollow.SnapToTarget();

        Debug.Log("Player, MorningStar and Chain respawned safely");
    }
}
