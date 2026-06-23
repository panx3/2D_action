using UnityEngine;

/// <summary>
/// 画面遷移・ゴール・チェックポイント等が Player 本体だけに反応するための共通判定。
/// </summary>
public static class PlayerColliderUtility
{
    public const string PlayerTag = "Player";

    public static bool IsPlayerBody(Collider2D other)
    {
        if (other == null || !other.CompareTag(PlayerTag))
            return false;

        return other.GetComponentInParent<Player>() != null;
    }

    public static bool IsPlayerBody(Collision2D collision)
    {
        return collision != null && IsPlayerBody(collision.collider);
    }
}
