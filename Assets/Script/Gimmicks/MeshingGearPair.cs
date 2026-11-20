using UnityEngine;

/// <summary>Drives both gears from a single angle so the tooth phase stays synchronized.</summary>
[DisallowMultipleComponent]
public sealed class MeshingGearPair : MonoBehaviour
{
    [SerializeField] private GearPlatform gearA;
    [SerializeField] private GearPlatform gearB;
    [SerializeField, Min(0f), Tooltip("歯車 A の回転速度（度/秒）。歯車 B はピッチ半径比に応じて逆回転します。")]
    private float degreesPerSecond = 24f;
    [SerializeField, Tooltip("歯車 A を反時計回りに回します。歯車 B の向きも連動して反転します。")]
    private bool counterclockwise = true;

    private double driveAngle;

    private void FixedUpdate()
    {
        if (gearA == null || gearB == null || gearA == gearB)
            return;

        float speed = counterclockwise ? degreesPerSecond : -degreesPerSecond;
        float ratio = gearA.WorldPitchRadius / gearB.WorldPitchRadius;
        driveAngle += (double)speed * Time.fixedDeltaTime;
        gearA.RotateTo(driveAngle);
        gearB.RotateTo(-driveAngle * ratio);
    }

    private void OnDisable()
    {
        if (gearA != null)
            gearA.StopRotation();
        if (gearB != null)
            gearB.StopRotation();
    }
}
