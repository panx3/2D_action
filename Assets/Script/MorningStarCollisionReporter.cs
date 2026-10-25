using UnityEngine;

/// <summary>
/// 鉄球本体の衝突を MorningStarLauncher へ転送する。
/// Launcher が Player 側にある構成向け。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class MorningStarCollisionReporter : MonoBehaviour
{
    private MorningStarLauncher _launcher;
    private MorningStarImpactSparks _impactSparks;

    public MorningStarLauncher Launcher => _launcher;

    public void Initialize(MorningStarLauncher launcher)
    {
        _launcher = launcher;
        _impactSparks = GetComponent<MorningStarImpactSparks>();
        if (_impactSparks != null)
            _impactSparks.Initialize(launcher);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (_launcher == null)
            return;
        if (_impactSparks != null)
            _impactSparks.TryPlay(collision, _launcher);
        var rollingBall = collision.collider != null
            ? collision.collider.GetComponentInParent<RollingIronBall>() : null;
        if (rollingBall != null)
            rollingBall.TryPlayMorningStarImpact(collision, _launcher);
        _launcher.OnMorningStarCollision(collision);
    }
}
