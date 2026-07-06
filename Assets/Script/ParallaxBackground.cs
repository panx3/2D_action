using UnityEngine;

/// <summary>
/// 背景スプライトを縦に積み上げ、層ごとに異なるパララックス係数で追従させる。
/// 横方向は同じ画像を並べてワイドステージでも切れ目が出にくいようにする。
/// </summary>
[DisallowMultipleComponent]
public class ParallaxBackground : MonoBehaviour
{
    [System.Serializable]
    private class LayerConfig
    {
        public Sprite sprite;
        [Range(0f, 1f)] public float parallaxX = 0.2f;
        [Range(0f, 1f)] public float parallaxY = 0.1f;
        public int sortingOrder = -100;
    }

    [Header("Layers (下から上の順)")]
    [SerializeField] private LayerConfig[] _layers;

    [Header("配置")]
    [SerializeField] private Transform _cameraTransform;
    [SerializeField, Tooltip("背景の下端を置くワールドY座標")]
    private float _bottomY = -5f;
    [SerializeField, Tooltip("画面の高さに合わせて全体を伸縮する")]
    private bool _fitToCameraHeight = true;
    [SerializeField, Tooltip("fitToCameraHeight 無効時の表示高さ（ワールド単位）")]
    private float _targetWorldHeight = 10f;
    [SerializeField, Min(1)] private int _horizontalRepeatCount = 3;

    private void Awake()
    {
        if (_cameraTransform == null && Camera.main != null)
            _cameraTransform = Camera.main.transform;

        BuildLayers();
    }

    private void BuildLayers()
    {
        ClearGeneratedChildren();

        if (_layers == null || _layers.Length == 0)
            return;

        float worldHeight = ResolveWorldHeight();
        float naturalHeight = 0f;
        foreach (LayerConfig layer in _layers)
        {
            if (layer?.sprite == null)
                continue;

            naturalHeight += layer.sprite.bounds.size.y;
        }

        if (naturalHeight <= 0.0001f)
            return;

        float heightScale = worldHeight / naturalHeight;
        float currentBottomY = _bottomY;
        int layerIndex = 0;

        foreach (LayerConfig layer in _layers)
        {
            if (layer?.sprite == null)
                continue;

            float layerHeight = layer.sprite.bounds.size.y * heightScale;
            float centerY = currentBottomY + layerHeight * 0.5f;
            float tileWidth = layer.sprite.bounds.size.x;

            GameObject layerRoot = new GameObject($"ParallaxLayer_{layerIndex}");
            layerRoot.transform.SetParent(transform, false);
            layerRoot.transform.localPosition = new Vector3(0f, centerY, 0f);
            layerRoot.transform.localScale = Vector3.one * heightScale;

            ParallaxLayer2D parallax = layerRoot.AddComponent<ParallaxLayer2D>();
            parallax.ParallaxX = layer.parallaxX;
            parallax.ParallaxY = layer.parallaxY;
            parallax.Initialize(_cameraTransform);

            int repeat = Mathf.Max(1, _horizontalRepeatCount);
            float startX = -(repeat - 1) * tileWidth * 0.5f;
            for (int i = 0; i < repeat; i++)
            {
                GameObject tile = new GameObject($"Tile_{i}");
                tile.transform.SetParent(layerRoot.transform, false);
                tile.transform.localPosition = new Vector3(startX + tileWidth * i, 0f, 0f);

                SpriteRenderer renderer = tile.AddComponent<SpriteRenderer>();
                renderer.sprite = layer.sprite;
                renderer.sortingOrder = layer.sortingOrder + layerIndex;
            }

            currentBottomY += layerHeight;
            layerIndex++;
        }
    }

    private float ResolveWorldHeight()
    {
        if (!_fitToCameraHeight || _cameraTransform == null)
            return _targetWorldHeight;

        Camera camera = _cameraTransform.GetComponent<Camera>();
        if (camera == null || !camera.orthographic)
            return _targetWorldHeight;

        return camera.orthographicSize * 2f;
    }

    private void ClearGeneratedChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }
}
