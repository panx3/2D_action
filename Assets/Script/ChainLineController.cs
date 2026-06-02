using UnityEngine;



/// <summary>

/// 鎖の LineRenderer。常に手元—鉄球の2点のみ。最大長で終点をクランプする。

/// </summary>

[RequireComponent(typeof(LineRenderer))]

public class ChainLineController : MonoBehaviour

{

    [Header("Chain Points")]

    [SerializeField] private Transform startPoint;

    [SerializeField] private Transform endPoint;



    [Header("Morning Star（任意）")]

    [SerializeField] private MorningStarLauncher launcher;

    [SerializeField, Tooltip("launcher 未設定時に使う最大紐長")]

    private float maxRopeLength = 4.5f;



    [Header("Visual")]

    [SerializeField, Tooltip("OFF 推奨（ChainLinkVisualController 使用時）。ON 時は lineAlpha で薄く表示")]
    private bool drawLineRenderer;

    [SerializeField, Range(0f, 1f)] private float lineAlpha = 0.2f;

    [SerializeField] private bool useSag;

    [SerializeField] private int sagPointCount = 8;

    [SerializeField] private float sagAmount = 0.25f;



    private LineRenderer _line;

    private Color _defaultStartColor;

    private Color _defaultEndColor;

    private float _defaultStartWidth;

    private float _defaultEndWidth;

    private bool _defaultsCached;

    private bool _hookedVisualActive;

    private Color _configuredNormalColor = Color.white;

    private Color _configuredHookedColor = new Color(1f, 0.9f, 0.5f, 1f);

    private float _configuredHookedWidthMultiplier = 1.25f;

    private bool _visualConfigured;



    private void Awake()

    {

        _line = GetComponent<LineRenderer>();

        _line.positionCount = 2;



        if (launcher == null)

            launcher = FindAnyObjectByType<MorningStarLauncher>();

    }



    public void ConfigureHookVisual(Color normalColor, Color hookedColor, float hookedWidthMultiplier)

    {

        _configuredNormalColor = normalColor;

        _configuredHookedColor = hookedColor;

        _configuredHookedWidthMultiplier = Mathf.Max(1f, hookedWidthMultiplier);

        _visualConfigured = true;

    }



    public void SetHookedVisual(bool hooked)

    {

        _hookedVisualActive = hooked;

        ApplyHookedLineVisual();

    }



    private void LateUpdate()

    {

        if (_line == null)

            return;



        if (!drawLineRenderer)

        {

            _line.enabled = false;

            return;

        }



        if (launcher != null)

        {

            _line.enabled = launcher.IsRopeLineVisible;

            maxRopeLength = launcher.MaxRopeLength;

        }



        if (!_line.enabled)

            return;



        if (startPoint == null || endPoint == null)

        {

            _line.enabled = false;

            return;

        }



        ApplyHookedLineVisual();



        if (!useSag)

            DrawStraightChain();

        else

            DrawSagChain();

    }



    private void ApplyHookedLineVisual()

    {

        if (!_defaultsCached)

        {

            _defaultStartColor = _line.startColor;

            _defaultEndColor = _line.endColor;

            _defaultStartWidth = _line.startWidth;

            _defaultEndWidth = _line.endWidth;

            _defaultsCached = true;

        }



        bool hooked = _hookedVisualActive;

        if (launcher != null && launcher.IsHookedState)

            hooked = true;



        Color normalStart = _visualConfigured ? _configuredNormalColor : _defaultStartColor;

        Color normalEnd = _visualConfigured ? _configuredNormalColor : _defaultEndColor;

        Color hookedStart = _visualConfigured ? _configuredHookedColor : normalStart;

        Color hookedEnd = _visualConfigured ? _configuredHookedColor : normalEnd;

        float mult = _visualConfigured ? _configuredHookedWidthMultiplier : 1.25f;



        Color start = hooked ? hookedStart : normalStart;

        Color end = hooked ? hookedEnd : normalEnd;

        start.a *= lineAlpha;

        end.a *= lineAlpha;

        _line.startColor = start;

        _line.endColor = end;

        float startW = hooked ? _defaultStartWidth * mult : _defaultStartWidth;

        float endW = hooked ? _defaultEndWidth * mult : _defaultEndWidth;

        _line.startWidth = startW;

        _line.endWidth = endW;

    }



    private void DrawStraightChain()

    {

        _line.positionCount = 2;

        Vector3 start = startPoint.position;

        Vector3 end = ClampEndToMaxLength(start, endPoint.position);

        _line.SetPosition(0, start);

        _line.SetPosition(1, end);

    }



    private void DrawSagChain()

    {

        Vector3 start = startPoint.position;

        Vector3 end = ClampEndToMaxLength(start, endPoint.position);

        int count = Mathf.Max(2, sagPointCount);

        _line.positionCount = count;



        for (int i = 0; i < count; i++)

        {

            float t = i / (float)(count - 1);

            Vector3 pos = Vector3.Lerp(start, end, t);

            float sag = Mathf.Sin(t * Mathf.PI) * sagAmount;

            pos.y -= sag;

            _line.SetPosition(i, pos);

        }

    }



    private Vector3 ClampEndToMaxLength(Vector3 start, Vector3 end)

    {

        float maxLen = maxRopeLength;

        if (maxLen <= 0f)

            return end;



        Vector3 off = end - start;

        float sqr = off.sqrMagnitude;

        if (sqr <= maxLen * maxLen || sqr < 1e-10f)

            return end;



        return start + off.normalized * maxLen;

    }



    public void SetPoints(Transform newStart, Transform newEnd)

    {

        startPoint = newStart;

        endPoint = newEnd;

    }



    public void SetLauncher(MorningStarLauncher newLauncher)

    {

        launcher = newLauncher;

    }

}


