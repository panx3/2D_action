using System.Collections;
using UnityEngine;

/// <summary>
/// 敵共通の物理破片。地形だけに衝突し、Sprite選択・回転・寿命を担当する。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
public sealed class EnemyFragment : MonoBehaviour
{
    [SerializeField] private Sprite[] fragmentSprites;
    [SerializeField, Min(0f)] private float fadeDuration = 0.4f;

    [Header("Fragment Physics")]
    [SerializeField, Tooltip("地面・壁のみを指定。Player / Enemy / WallFragmentは含めない")]
    private LayerMask terrainLayers = (1 << 0) | (1 << 6);
    [SerializeField, Min(0f), Tooltip("生成時の角速度を±この値から選ぶ（度/秒）")]
    private float maxAngularSpeed = 180f;
    [SerializeField, Tooltip("選ばれたSpriteのサイズ・PivotにBoxCollider2Dを合わせる")]
    private bool fitColliderToSprite = true;

    private SpriteRenderer _spriteRenderer;
    private BoxCollider2D _collider;
    private Rigidbody2D _body;
    private Coroutine _lifetimeRoutine;

    public int FragmentSpriteCount => CountValidSprites();
    public Sprite CurrentSprite => _spriteRenderer != null ? _spriteRenderer.sprite : null;

    private void Awake()
    {
        ConfigurePhysics();
    }

    /// <summary>Prefab設定時と実行時で同じ設定を使う。壁破片の設定や衝突表は変更しない。</summary>
    [ContextMenu("Configure Fragment Physics")]
    public void ConfigurePhysics()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _collider = GetComponent<BoxCollider2D>();
        _body = GetComponent<Rigidbody2D>();

        int fragmentLayer = LayerMask.NameToLayer("WallFragment");
        if (fragmentLayer >= 0)
            gameObject.layer = fragmentLayer;

        if (_body != null)
            _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        if (_collider != null)
        {
            _collider.isTrigger = false;
            // Collider側だけで接触先を限定し、既存のLayer Collision Matrixは維持する。
            _collider.includeLayers = terrainLayers;
            _collider.excludeLayers = ~terrainLayers.value;
            _collider.layerOverridePriority = 1;
            FitCollider();
        }
    }

    private void FitCollider()
    {
        if (!fitColliderToSprite || _collider == null || _spriteRenderer == null || _spriteRenderer.sprite == null)
            return;

        Bounds bounds = _spriteRenderer.sprite.bounds;
        Vector2 center = bounds.center;
        if (_spriteRenderer.flipX) center.x = -center.x;
        if (_spriteRenderer.flipY) center.y = -center.y;
        _collider.offset = center;
        _collider.size = new Vector2(Mathf.Max(0.01f, bounds.size.x), Mathf.Max(0.01f, bounds.size.y));
    }

    /// <summary>
    /// 最初の一巡は配列順に選び、同時生成された破片の重複を抑える。
    /// 配列数を超えた分だけランダム選択へ切り替える。
    /// </summary>
    public void Initialize(int spawnIndex, float lifeTime)
    {
        if (_spriteRenderer == null)
            _spriteRenderer = GetComponent<SpriteRenderer>();

        int validCount = CountValidSprites();
        if (_spriteRenderer != null && validCount > 0)
        {
            int validIndex = spawnIndex < validCount
                ? Mathf.Max(0, spawnIndex)
                : Random.Range(0, validCount);
            _spriteRenderer.sprite = GetValidSprite(validIndex);
            Color color = _spriteRenderer.color;
            color.a = 1f;
            _spriteRenderer.color = color;
        }

        FitCollider();
        if (_body != null)
        {
            float speed = Mathf.Max(0f, maxAngularSpeed);
            _body.angularVelocity = Random.Range(-speed, speed);
        }

        if (_lifetimeRoutine != null)
            StopCoroutine(_lifetimeRoutine);
        _lifetimeRoutine = StartCoroutine(FadeAndExpire(Mathf.Max(0f, lifeTime)));
    }

    private IEnumerator FadeAndExpire(float lifeTime)
    {
        float fadeTime = Mathf.Min(fadeDuration, lifeTime);
        float visibleTime = Mathf.Max(0f, lifeTime - fadeTime);
        if (visibleTime > 0f)
            yield return new WaitForSeconds(visibleTime);

        if (_spriteRenderer != null && fadeTime > 0f)
        {
            Color startColor = _spriteRenderer.color;
            float elapsed = 0f;
            while (elapsed < fadeTime)
            {
                elapsed += Time.deltaTime;
                Color color = startColor;
                color.a = Mathf.Clamp01(1f - elapsed / fadeTime);
                _spriteRenderer.color = color;
                yield return null;
            }
        }

        Destroy(gameObject);
    }

    private int CountValidSprites()
    {
        if (fragmentSprites == null)
            return 0;

        int count = 0;
        foreach (Sprite sprite in fragmentSprites)
        {
            if (sprite != null)
                count++;
        }

        return count;
    }

    private Sprite GetValidSprite(int validIndex)
    {
        if (fragmentSprites == null)
            return null;

        int current = 0;
        foreach (Sprite sprite in fragmentSprites)
        {
            if (sprite == null)
                continue;
            if (current == validIndex)
                return sprite;
            current++;
        }

        return null;
    }
}
