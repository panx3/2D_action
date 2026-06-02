using System.Collections;
using UnityEngine;

public class CameraShake2D : MonoBehaviour
{
    private Vector3 _originalLocalPosition;
    private Coroutine _shakeRoutine;

    private void Awake()
    {
        _originalLocalPosition = transform.localPosition;
    }

    public void Shake(float duration, float strength)
    {
        if (!isActiveAndEnabled)
            return;

        if (_shakeRoutine != null)
            StopCoroutine(_shakeRoutine);

        _shakeRoutine = StartCoroutine(ShakeRoutine(duration, strength));
    }

    private IEnumerator ShakeRoutine(float duration, float strength)
    {
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            Vector2 offset = Random.insideUnitCircle * strength;
            transform.localPosition = _originalLocalPosition + new Vector3(offset.x, offset.y, 0f);
            yield return null;
        }

        transform.localPosition = _originalLocalPosition;
        _shakeRoutine = null;
    }
}
