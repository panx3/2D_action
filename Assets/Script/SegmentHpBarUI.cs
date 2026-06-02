using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HP画像のピンク枠部分だけを、右端から暗い矩形で1枠ずつ隠すUI。
/// ハート部分はマスク対象外（barStartX 以降のみ）。
/// </summary>
[DisallowMultipleComponent]
public class SegmentHpBarUI : MonoBehaviour
{
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
    [SerializeField, Min(1)] private int _segmentCount = 14;

    [Header("マスク見た目")]
    [SerializeField] private Color _maskColor = new Color(0.08f, 0.08f, 0.12f, 0.92f);

    [Header("自動生成")]
    [SerializeField] private bool _autoCreateCanvasIfMissing = true;
    [SerializeField] private Vector2 _screenPadding = new Vector2(8f, -8f);

    [Header("表示サイズ")]
    [SerializeField] private Vector2 _displaySize = new Vector2(200f, 34.7f);
    [SerializeField] private Vector2 _displayPadding = new Vector2(12f, -12f);

    private Image[] _segmentMasks;
    private bool _masksBuilt;
    private static Sprite _whiteSprite;

    private void Awake()
    {
        ResolvePlayerHealth();
        EnsureHpBarImage();
        ApplyDisplayLayout();
    }

    private void Start()
    {
        ResolvePlayerHealth();
        BuildSegmentMasks();
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

    private void ResolvePlayerHealth()
    {
        if (_playerHealth != null)
            return;

        _playerHealth = FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Exclude);
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

        int segmentsToUse = Mathf.Min(_segmentCount, maxHp);
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

    private void BuildSegmentMasks()
    {
        if (_hpBarImage == null || _masksBuilt)
            return;

        var existing = _hpBarImage.transform.Find("SegmentMaskLayer");
        if (existing != null)
            Destroy(existing.gameObject);

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
        float segmentWidth = _barWidth / _segmentCount;
        _segmentMasks = new Image[_segmentCount];

        for (int i = 0; i < _segmentCount; i++)
        {
            // 右端から i 番目（i=0 が一番右）
            int segmentIndexFromLeft = _segmentCount - 1 - i;
            float x0 = _barStartX + segmentIndexFromLeft * segmentWidth;
            float x1 = x0 + segmentWidth;
            float y0 = _barStartY;
            float y1 = _barStartY + _barHeight;

            var maskGo = new GameObject($"Mask_Right{i:00}", typeof(RectTransform), typeof(Image));
            maskGo.transform.SetParent(layerGo.transform, false);

            var rt = maskGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0 / _imagePixelSize.x, y0 / _imagePixelSize.y);
            rt.anchorMax = new Vector2(x1 / _imagePixelSize.x, y1 / _imagePixelSize.y);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

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

    private static Sprite GetWhiteSprite()
    {
        if (_whiteSprite != null)
            return _whiteSprite;

        Texture2D tex = Texture2D.whiteTexture;
        _whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
        return _whiteSprite;
    }
}
