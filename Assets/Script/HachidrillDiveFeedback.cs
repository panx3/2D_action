using UnityEngine;

/// <summary>急降下・着地衝撃波・掘削・引き抜きのドット絵表示。ダメージや物理は変更しない。</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(100)]
[RequireComponent(typeof(HachidrillAttackController))]
public sealed class HachidrillDiveFeedback : MonoBehaviour
{
    [SerializeField] private Sprite[] diveFrames;
    [SerializeField] private Sprite[] impactFrames;
    [SerializeField] private Sprite[] drillingFrames;
    [SerializeField] private Sprite[] extractionFrames;
    [SerializeField, Min(1f)] private float framesPerSecond = 14f;
    [SerializeField, Min(0.1f), Tooltip("64pxの1コマをワールド単位で表示する幅。原画と同程度のピクセル密度。")]
    private float effectWidth = 2.4f;

    private HachidrillAttackController attack;
    private EnemyHealth health;
    private SpriteRenderer air, ground;

    private void Awake()
    {
        attack = GetComponent<HachidrillAttackController>(); health = GetComponent<EnemyHealth>();
        var original = GetComponent<HachidrillController>().VisualRenderer;
        air = CreateRenderer("DiveSlipstream", original, -1);
        ground = CreateRenderer("DrillGroundEffect", original, 1);
    }

    private SpriteRenderer CreateRenderer(string objectName, SpriteRenderer original, int order)
    {
        var child = new GameObject(objectName); child.layer = gameObject.layer;
        child.transform.SetParent(transform, false);
        var renderer = child.AddComponent<SpriteRenderer>();
        if (original != null)
        {
            renderer.sharedMaterial = original.sharedMaterial;
            renderer.sortingLayerID = original.sortingLayerID;
            renderer.sortingOrder = original.sortingOrder + order;
        }
        renderer.enabled = false;
        return renderer;
    }

    private void LateUpdate()
    {
        if (health.CurrentHp <= 0 || !attack.isActiveAndEnabled) { Hide(); return; }
        air.enabled = ground.enabled = false;
        float age = attack.StateElapsed;
        switch (attack.State)
        {
            case HachidrillAttackController.ActionState.Windup:
                // 最後の固定照準に合わせて短い予告を出す。
                if (age > 0.35f) Show(air, diveFrames, age, true, transform.position,
                    Vector2.SignedAngle(Vector2.down, attack.DiveDirection), 0.65f);
                break;
            case HachidrillAttackController.ActionState.Dive:
                Show(air, diveFrames, age, true, transform.position,
                    Vector2.SignedAngle(Vector2.down, attack.DiveDirection), 1f);
                break;
            case HachidrillAttackController.ActionState.Embedded:
                bool impact = age < Duration(impactFrames);
                Show(ground, impact ? impactFrames : drillingFrames,
                    impact ? age : age - Duration(impactFrames), true, attack.ImpactPoint,
                    Vector2.SignedAngle(Vector2.up, attack.ImpactNormal), 1f);
                break;
            case HachidrillAttackController.ActionState.Extract:
                Show(ground, extractionFrames, age, false, attack.ImpactPoint,
                    Vector2.SignedAngle(Vector2.up, attack.ImpactNormal), 1f);
                break;
        }
    }

    private float Duration(Sprite[] frames) => frames != null ? frames.Length / Mathf.Max(1f, framesPerSecond) : 0f;
    private void Show(SpriteRenderer renderer, Sprite[] frames, float age, bool loop, Vector3 position, float angle, float size)
    {
        if (frames == null || frames.Length == 0) return;
        int frame = Mathf.FloorToInt(age * framesPerSecond);
        if (!loop && frame >= frames.Length) return;
        Sprite sprite = frames[Mathf.Max(0, frame) % frames.Length];
        if (sprite == null) return;
        renderer.sprite = sprite; renderer.enabled = true;
        Transform target = renderer.transform;
        target.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
        float scale = effectWidth * size / Mathf.Max(0.001f, sprite.rect.width / sprite.pixelsPerUnit);
        Vector3 parent = transform.lossyScale;
        target.localScale = new Vector3(scale / Mathf.Max(0.001f, Mathf.Abs(parent.x)),
            scale / Mathf.Max(0.001f, Mathf.Abs(parent.y)), 1f);
    }

    private void Hide() { if (air != null) air.enabled = false; if (ground != null) ground.enabled = false; }
    private void OnDisable() => Hide();
}
