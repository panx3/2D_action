using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HP画像のピンク枠部分だけを、右端から暗い矩形で1枠ずつ隠すUI。
/// ハート部分はマスク対象外（barStartX 以降のみ）。
/// </summary>
[DisallowMultipleComponent]
public class SegmentHpBarUI : MonoBehaviour
{
    public const int SegmentMaskCount = 14;

    [System.Serializable]
    public struct HpSegmentMaskRect
    {
        [Tooltip("画像左下原点・ピクセル")]
        public float xMin;
        public float xMax;
        public float yMin;
        public float yMax;
    }

    [Header("参照")]
    [SerializeField, Tooltip("未設定ならシーン内を検索")]
    private PlayerHealth _playerHealth;
    [SerializeField, Tooltip("HP画像の Image。未設定かつ Sprite がある場合は実行時に生成")]
    private Image _hpBarImage;
    [SerializeField, Tooltip("自動生成時に使うスプライト（tekkyu_hp_extracted_transparent）")]
    private Sprite _hpBarSprite;

    [Header("画像ピクセルサイズ")]
    [SerializeField] private Vector2 _imagePixelSize = new Vector2(340f, 59f);

    [Header("ピンクHPバー領域（画像左下原点・ピクセル）")]
    [SerializeField] private float _barStartX = 60f;
    [SerializeField] private float _barStartY = 16f;
    [SerializeField] private float _barWidth = 260f;
    [SerializeField] private float _barHeight = 26f;
    [SerializeField, Tooltip("バー左端の枠内余白（セグメント枠数）。tekkyu_hp は先頭1枠が空白")]
    private float _barLeadingGapSegments = 1f;

    [Header("マスク配置（14個）")]
    [SerializeField, Tooltip("Mask_Right00=右端 … Mask_Right13=左端。画像左下原点のピクセル矩形")]
    private HpSegmentMaskRect[] _segmentMaskRects = new HpSegmentMaskRect[SegmentMaskCount];

    [Header("マスク見た目")]
    [SerializeField] private Color _maskColor = new Color(0.08f, 0.08f, 0.12f, 0.92f);

    [Header("自動生成")]
    [SerializeField] private bool _autoCreateCanvasIfMissing = true;
    [SerializeField] private Vector2 _screenPadding = new Vector2(143f, -113f);

    [Header("表示サイズ")]
    [SerializeField, Tooltip("ON のとき Play 開始時に HpBarImage の RectTransform を下の値で上書きする")]
    private bool _applyDisplayLayoutOnPlay = false;
    [SerializeField, Tooltip("HP1枠あたり約36pxになるよう tekkyu_hp スプライト基準で調整")]
    private Vector2 _displaySize = new Vector2(583.66f, 92.098f);
    [SerializeField, Tooltip("左上アンカー時の余白（x: 右方向、y: 下方向）")]
    private Vector2 _displayPadding = new Vector2(143f, -113f);
    [SerializeField, Tooltip("ON のとき Play 開始時に SegmentMaskLayer を _segmentMaskRects から再生成する")]
    private bool _rebuildMasksOnPlay = false;

    private Image[] _segmentMasks;
    private bool _masksBuilt;
    private bool _loggedPlayerHealthResolve;
    private static Sprite _whiteSprite;

    private void Reset()
    {
        EnsureMaskRectCapacity();
        SyncSegmentMaskRectsFromBar();
    }

    private void OnValidate()
    {
        // 配列サイズだけ整える。Sync や GameObject 操作は Inspector 描画中に行わない。
        ResizeMaskRectArrayOnly();
    }

    private void Awake()
    {
        ResolvePlayerHealth();
        EnsureHpBarImage();
        if (_applyDisplayLayoutOnPlay)
            ApplyDisplayLayout();
        EnsureMaskRectCapacity();
    }

    private void Start()
    {
        ResolvePlayerHealth();
        Canvas.ForceUpdateCanvases();
        EnsureSegmentMasksReady();
        BindHealthEvents();
        RefreshFromHealth();
    }

    private void OnEnable()
    {
        ResolvePlayerHealth();
        BindHealthEvents();
        RefreshFromHealth();
    }

    private void OnDisable()
    {
        UnbindHealthEvents();
    }

    [ContextMenu("Recalculate Segment Mask Rects From Bar")]
    public void RecalculateSegmentMaskRectsFromBar()
    {
        EnsureMaskRectCapacity();
        SyncSegmentMaskRectsFromBar();
    }

    /// <summary>Editor の Apply ボタンから SegmentMaskLayer を再構築する。</summary>
    public void RebuildSegmentMaskLayer()
    {
        EnsureMaskRectCapacity();
        RebuildSegmentMasksInternal(allowInEditMode: true);
    }

    /// <summary>Editor から HpBarImage の RectTransform をスクリプト値で上書きする。</summary>
    public void ApplyDisplayLayoutFromScript()
    {
        ApplyDisplayLayout();
    }

    private void EnsureSegmentMasksReady()
    {
        if (_hpBarImage == null)
            EnsureHpBarImage();
        if (_hpBarImage == null)
            return;

        if (!_rebuildMasksOnPlay && TryBindExistingSegmentMasks())
            return;

        RebuildSegmentMasksIfReady();
    }

    private bool TryBindExistingSegmentMasks()
    {
        if (_hpBarImage == null)
            return false;

        Transform layer = _hpBarImage.transform.Find("SegmentMaskLayer");
        if (layer == null)
            return false;

        var masks = new Image[SegmentMaskCount];
        for (int i = 0; i < SegmentMaskCount; i++)
        {
            Transform maskTransform = layer.Find($"Mask_Right{i:00}");
            if (maskTransform == null)
                return false;

            Image image = maskTransform.GetComponent<Image>();
            if (image == null)
                return false;

            masks[i] = image;
        }

        _segmentMasks = masks;
        _masksBuilt = true;
        return true;
    }

    [ContextMenu("Rebuild Segment Mask Layer")]
    private void RebuildSegmentMaskLayerMenu()
    {
        RebuildSegmentMaskLayer();
    }

    private void EnsureMaskRectCapacity()
    {
        ResizeMaskRectArrayOnly();
        if (IsMaskRectArrayEmpty())
            SyncSegmentMaskRectsFromBar();
    }

    private void ResizeMaskRectArrayOnly()
    {
        if (_segmentMaskRects != null && _segmentMaskRects.Length == SegmentMaskCount)
            return;

        var previous = _segmentMaskRects;
        _segmentMaskRects = new HpSegmentMaskRect[SegmentMaskCount];
        if (previous == null)
            return;

        int copyCount = Mathf.Min(previous.Length, SegmentMaskCount);
        for (int i = 0; i < copyCount; i++)
            _segmentMaskRects[i] = previous[i];
    }

    private bool IsMaskRectArrayEmpty()
    {
        if (_segmentMaskRects == null || _segmentMaskRects.Length != SegmentMaskCount)
            return true;

        for (int i = 0; i < _segmentMaskRects.Length; i++)
        {
            HpSegmentMaskRect rect = _segmentMaskRects[i];
            if (rect.xMax > rect.xMin && rect.yMax > rect.yMin)
                return false;
        }

        return true;
    }

    private void SyncSegmentMaskRectsFromBar()
    {
        float slotCount = SegmentMaskCount + Mathf.Max(0f, _barLeadingGapSegments);
        float slotWidth = _barWidth / slotCount;
        float yMin = _barStartY;
        float yMax = _barStartY + _barHeight;

        for (int i = 0; i < SegmentMaskCount; i++)
        {
            int segmentIndexFromLeft = SegmentMaskCount - 1 - i;
            float xMin = _barStartX + (_barLeadingGapSegments + segmentIndexFromLeft) * slotWidth;
            _segmentMaskRects[i] = new HpSegmentMaskRect
            {
                xMin = xMin,
                xMax = xMin + slotWidth,
                yMin = yMin,
                yMax = yMax
            };
        }
    }

    private void ResolvePlayerHealth()
    {
        if (_playerHealth != null)
            return;

        if (!Application.isPlaying)
            return;

        _playerHealth = FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Exclude);
        if (_loggedPlayerHealthResolve)
            return;

        _loggedPlayerHealthResolve = true;
        if (_playerHealth != null)
            Debug.LogWarning("[SegmentHpBarUI] PlayerHealth was auto-found. Assign it in Inspector to avoid wrong references.", this);
        else
            Debug.LogWarning("[SegmentHpBarUI] PlayerHealth is not assigned and could not be found.", this);
    }

    private void BindHealthEvents()
    {
        if (_playerHealth == null)
            return;

        _playerHealth.OnHealthChanged -= HandleHealthChanged;
        _playerHealth.OnHealthChanged += HandleHealthChanged;
    }

    private void UnbindHealthEvents()
    {
        if (_playerHealth == null)
            return;

        _playerHealth.OnHealthChanged -= HandleHealthChanged;
    }

    private void HandleHealthChanged(int currentHp, int maxHp)
    {
        SetHp(currentHp, maxHp);
    }

    private void RefreshFromHealth()
    {
        if (_playerHealth == null) return;
        SetHp(_playerHealth.CurrentHp, _playerHealth.MaxHp);
    }

    /// <summary>現在HPに合わせて右端からマスクを表示する。</summary>
    public void SetHp(int currentHp, int maxHp)
    {
        if (!_masksBuilt || _segmentMasks == null)
            return;

        maxHp = Mathf.Max(1, maxHp);
        currentHp = Mathf.Clamp(currentHp, 0, maxHp);

        int segmentsToUse = Mathf.Min(SegmentMaskCount, maxHp);
        int lostSegments = Mathf.Clamp(maxHp - currentHp, 0, segmentsToUse);

        for (int i = 0; i < _segmentMasks.Length; i++)
        {
            if (_segmentMasks[i] == null) continue;
            _segmentMasks[i].gameObject.SetActive(i < segmentsToUse);
            // i=0 が右端。ダメージ分だけ右から暗くする。
            _segmentMasks[i].enabled = i < lostSegments;
        }
    }

    private void ApplyDisplayLayout()
    {
        if (_hpBarImage == null)
            return;

        RectTransform rect = _hpBarImage.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = _displayPadding;
        rect.sizeDelta = _displaySize;
    }

    private void EnsureHpBarImage()
    {
        if (_hpBarImage != null)
            return;

        Image[] images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i].gameObject.name == "HpBarImage")
            {
                _hpBarImage = images[i];
                break;
            }
        }

        if (_hpBarImage != null)
            return;

        if (!_autoCreateCanvasIfMissing || _hpBarSprite == null)
            return;

        var canvasGo = new GameObject("HpCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(640f, 360f);
        scaler.matchWidthOrHeight = 0.5f;

        var imageGo = new GameObject("HpBarImage", typeof(RectTransform), typeof(Image));
        imageGo.transform.SetParent(canvasGo.transform, false);

        _hpBarImage = imageGo.GetComponent<Image>();
        _hpBarImage.sprite = _hpBarSprite;
        _hpBarImage.preserveAspect = true;
        _hpBarImage.raycastTarget = false;

        var rect = imageGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = _screenPadding;
        rect.sizeDelta = _imagePixelSize;
    }

    private void RebuildSegmentMasksIfReady()
    {
        RebuildSegmentMasksInternal(allowInEditMode: false);
    }

    private void RebuildSegmentMasksInternal(bool allowInEditMode)
    {
        if (_hpBarImage == null)
            EnsureHpBarImage();
        if (_hpBarImage == null)
            return;

        if (!Application.isPlaying && !allowInEditMode)
            return;

        _masksBuilt = false;
        BuildSegmentMasks(allowInEditMode);
        if (Application.isPlaying)
            RefreshFromHealth();
    }

    private void BuildSegmentMasks(bool allowInEditMode)
    {
        if (_hpBarImage == null || _masksBuilt)
            return;

        if (!Application.isPlaying && !allowInEditMode)
            return;

        EnsureMaskRectCapacity();

        var existing = _hpBarImage.transform.Find("SegmentMaskLayer");
        if (existing != null)
            DestroyMaskHierarchy(existing.gameObject, allowInEditMode);

        var layerGo = new GameObject("SegmentMaskLayer", typeof(RectTransform));
        layerGo.transform.SetParent(_hpBarImage.transform, false);
        layerGo.transform.SetAsLastSibling();

        var layerRect = layerGo.GetComponent<RectTransform>();
        layerRect.anchorMin = Vector2.zero;
        layerRect.anchorMax = Vector2.one;
        layerRect.pivot = new Vector2(0.5f, 0.5f);
        layerRect.anchoredPosition = Vector2.zero;
        layerRect.sizeDelta = Vector2.zero;

        Sprite maskSprite = GetWhiteSprite();
        _segmentMasks = new Image[SegmentMaskCount];

        Vector2 rectSize = _hpBarImage.rectTransform.rect.size;
        if (rectSize.x <= 0f || rectSize.y <= 0f)
            rectSize = _displaySize;

        GetSpriteContentRect(rectSize, out Vector2 contentMin, out Vector2 contentSize);

        for (int i = 0; i < SegmentMaskCount; i++)
        {
            HpSegmentMaskRect spec = _segmentMaskRects[i];

            var maskGo = new GameObject($"Mask_Right{i:00}", typeof(RectTransform), typeof(Image));
            maskGo.transform.SetParent(layerGo.transform, false);

            var rt = maskGo.GetComponent<RectTransform>();
            ApplyMaskRect(rt, spec, contentMin, contentSize, rectSize);

            var img = maskGo.GetComponent<Image>();
            img.sprite = maskSprite;
            img.type = Image.Type.Simple;
            img.color = _maskColor;
            img.raycastTarget = false;
            img.enabled = false;

            _segmentMasks[i] = img;
        }

        _masksBuilt = true;
    }

    private void ApplyMaskRect(
        RectTransform rt,
        HpSegmentMaskRect spec,
        Vector2 contentMin,
        Vector2 contentSize,
        Vector2 rectSize)
    {
        rt.anchorMin = SpritePixelToNormalizedAnchor(spec.xMin, spec.yMin, contentMin, contentSize, rectSize);
        rt.anchorMax = SpritePixelToNormalizedAnchor(spec.xMax, spec.yMax, contentMin, contentSize, rectSize);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// preserveAspect 時に Image が実際に描画するスプライト領域（RectTransform ローカル座標）。
    /// </summary>
    private void GetSpriteContentRect(Vector2 rectSize, out Vector2 contentMin, out Vector2 contentSize)
    {
        float srcW = _imagePixelSize.x;
        float srcH = _imagePixelSize.y;

        if (!_hpBarImage.preserveAspect || srcW <= 0f || srcH <= 0f || rectSize.x <= 0f || rectSize.y <= 0f)
        {
            contentMin = Vector2.zero;
            contentSize = rectSize;
            return;
        }

        float srcRatio = srcW / srcH;
        float rectRatio = rectSize.x / rectSize.y;

        if (srcRatio > rectRatio)
        {
            contentSize = new Vector2(rectSize.x, rectSize.x / srcRatio);
            contentMin = new Vector2(0f, (rectSize.y - contentSize.y) * 0.5f);
        }
        else
        {
            contentSize = new Vector2(rectSize.y * srcRatio, rectSize.y);
            contentMin = new Vector2((rectSize.x - contentSize.x) * 0.5f, 0f);
        }
    }

    private Vector2 SpritePixelToNormalizedAnchor(
        float spriteX,
        float spriteY,
        Vector2 contentMin,
        Vector2 contentSize,
        Vector2 rectSize)
    {
        float localX = contentMin.x + (spriteX / _imagePixelSize.x) * contentSize.x;
        float localY = contentMin.y + (spriteY / _imagePixelSize.y) * contentSize.y;

        return new Vector2(
            rectSize.x > 0f ? localX / rectSize.x : 0f,
            rectSize.y > 0f ? localY / rectSize.y : 0f);
    }

    private static Sprite GetWhiteSprite()
    {
        if (_whiteSprite != null)
            return _whiteSprite;

        Texture2D tex = Texture2D.whiteTexture;
        _whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
        return _whiteSprite;
    }

    private void DestroyMaskHierarchy(Object obj, bool allowInEditMode)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
        {
            Destroy(obj);
            return;
        }

        if (!allowInEditMode)
            return;

#if UNITY_EDITOR
        UnityEditor.Undo.DestroyObjectImmediate(obj);
#endif
    }
}
