using System.Collections;
using UnityEngine;

public class GimmickDoor : MonoBehaviour
{
    [Header("Door State")]
    [SerializeField] private bool startsOpen = false;

    [Header("Visual Settings")]
    [SerializeField] private SpriteRenderer backRenderer;
    [SerializeField] private SpriteRenderer barrierRenderer;
    [SerializeField] private SpriteRenderer frontRenderer;
    [SerializeField] private Sprite closedSprite;
    [SerializeField] private Sprite openSprite;
    [SerializeField] private Color closedColor = Color.white;
    [SerializeField] private Color openColor = Color.white;
    [SerializeField, Min(0.01f)] private float transitionDuration = 0.22f;

    private Collider2D doorCollider;
    private bool isOpen;
    private bool initialized;
    private Coroutine transitionRoutine;

    public bool IsOpen => isOpen;
    public bool TransitionHadIntermediateFrame { get; private set; }

    private void Awake()
    {
        doorCollider = GetComponent<Collider2D>();
        ResolveRenderers();
        isOpen = startsOpen;
        ApplyStateImmediate();
        initialized = true;
    }

    public void Open()
    {
        SetOpenState(true);
    }

    public void Close()
    {
        SetOpenState(false);
    }

    public void Toggle()
    {
        if (isOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    private void SetOpenState(bool open)
    {
        ResolveRenderers();
        bool changed = !initialized || isOpen != open;
        isOpen = open;

        if (doorCollider != null)
            doorCollider.enabled = !isOpen;

        if (!changed)
            return;

        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);
        TransitionHadIntermediateFrame = false;
        transitionRoutine = StartCoroutine(AnimateBarrier(isOpen ? 0f : 1f));

        Debug.Log(isOpen ? "GimmickDoor OPEN" : "GimmickDoor CLOSE");
    }

    private IEnumerator AnimateBarrier(float targetAlpha)
    {
        if (barrierRenderer == null)
            yield break;

        float startAlpha = barrierRenderer.color.a;
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, transitionDuration);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            float alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            if (alpha > 0.001f && alpha < 0.999f)
                TransitionHadIntermediateFrame = true;
            SetBarrierAlpha(alpha);
            yield return null;
        }

        SetBarrierAlpha(targetAlpha);
        transitionRoutine = null;
    }

    private void ResolveRenderers()
    {
        if (backRenderer == null)
            backRenderer = transform.Find("Door_Back")?.GetComponent<SpriteRenderer>();
        if (barrierRenderer == null)
            barrierRenderer = transform.Find("Door_Barrier")?.GetComponent<SpriteRenderer>();
        if (frontRenderer == null)
            frontRenderer = transform.Find("Door_Front")?.GetComponent<SpriteRenderer>();
    }

    private void ApplyStateImmediate()
    {
        if (doorCollider != null)
            doorCollider.enabled = !isOpen;

        if (backRenderer != null)
        {
            if (openSprite != null)
                backRenderer.sprite = openSprite;
            backRenderer.color = openColor;
        }

        if (barrierRenderer != null)
        {
            if (closedSprite != null)
                barrierRenderer.sprite = closedSprite;
            barrierRenderer.color = closedColor;
            SetBarrierAlpha(isOpen ? 0f : 1f);
        }
    }

    private void SetBarrierAlpha(float alpha)
    {
        if (barrierRenderer == null)
            return;

        Color color = barrierRenderer.color;
        color.a = Mathf.Clamp01(alpha);
        barrierRenderer.color = color;
    }

    private void OnDisable()
    {
        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);
        transitionRoutine = null;
    }
}
