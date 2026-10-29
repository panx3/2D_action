using UnityEngine;

public sealed class ElectricDoor : MonoBehaviour
{
    [SerializeField] private Vector2 openDirection = Vector2.up;
    [SerializeField, Min(0f)] private float openDistance = 4.2f;
    [SerializeField, Min(0.01f)] private float speed = 2f;
    [SerializeField] private bool stayOpenOnceActivated = true;
    private Vector3 closedPosition;
    private bool open, openedOnce;
    private void Awake() { closedPosition = transform.localPosition; }
    public void Open() { open = true; openedOnce = true; }
    public void Close() { if (!stayOpenOnceActivated || !openedOnce) open = false; }
    private void Update()
    {
        Vector2 dir = openDirection.sqrMagnitude > 0.0001f ? openDirection.normalized : Vector2.up;
        Vector3 target = closedPosition + (Vector3)(dir * (open ? openDistance : 0f));
        transform.localPosition = Vector3.MoveTowards(transform.localPosition, target, speed * Time.deltaTime);
    }
}

