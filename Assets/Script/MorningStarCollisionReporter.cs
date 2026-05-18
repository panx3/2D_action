using UnityEngine;

/// <summary>
/// 鉄球本体の衝突を MorningStarLauncher へ転送する。
/// Launcher が Player 側にある構成向け。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class MorningStarCollisionReporter : MonoBehaviour
{
    private MorningStarLauncher _launcher;

    public void Initialize(MorningStarLauncher launcher)
    {
        _launcher = launcher;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (_launcher == null)
            return;
        _launcher.OnMorningStarCollision(collision);
    }
}
