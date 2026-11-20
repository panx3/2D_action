using UnityEngine;

/// <summary>描画専用の遠景を、カメラの演出ズーム中も通常時と同じ画面比率で表示する。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
[DefaultExecutionOrder(1200)]
public sealed class BackgroundScreenSize : MonoBehaviour
{
    [SerializeField, Tooltip("未指定の場合は Main Camera を使用します。")]
    private Camera targetCamera;

    private Camera activeCamera;
    private CameraFollow cameraFollow;
    private Vector3 authoredScale;
    private float initialSize;

    private void OnEnable()
    {
        authoredScale = transform.localScale;
        activeCamera = null;
        cameraFollow = null;
    }

    private void LateUpdate()
    {
        if (activeCamera == null || !activeCamera.isActiveAndEnabled)
        {
            activeCamera = targetCamera != null && targetCamera.isActiveAndEnabled ? targetCamera : Camera.main;
            if (activeCamera == null) return;

            cameraFollow = activeCamera.GetComponent<CameraFollow>();
            initialSize = activeCamera.orthographicSize;
        }

        if (!activeCamera.orthographic) return;

        float referenceSize = cameraFollow != null && cameraFollow.isActiveAndEnabled
            ? cameraFollow.BaseOrthographicSize
            : initialSize;
        if (referenceSize <= 0f) return;

        float zoom = activeCamera.orthographicSize / referenceSize;
        transform.localScale = new Vector3(authoredScale.x * zoom, authoredScale.y * zoom, authoredScale.z);
    }

    private void OnDisable()
    {
        transform.localScale = authoredScale;
    }
}
