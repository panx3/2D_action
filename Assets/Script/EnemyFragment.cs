using System.Collections;
using UnityEngine;

/// <summary>
/// Enemy専用の演出破片。見た目だけを担当し、Colliderは持たない。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D))]
public sealed class EnemyFragment : MonoBehaviour
{
    [SerializeField] private Sprite[] fragmentSprites;
    [SerializeField, Min(0f)] private float fadeDuration = 0.4f;

    private SpriteRenderer _spriteRenderer;
    private Coroutine _lifetimeRoutine;

    public int FragmentSpriteCount => CountValidSprites();
    public Sprite CurrentSprite => _spriteRenderer != null ? _spriteRenderer.sprite : null;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
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
