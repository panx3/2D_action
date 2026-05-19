using UnityEngine;

public class GimmickDoor : MonoBehaviour
{
    [Header("Door State")]
    [SerializeField] private bool startsOpen = false;

    [Header("Visual Settings")]
    [SerializeField] private Color closedColor = Color.red;
    [SerializeField] private Color openColor = Color.gray;

    private Collider2D doorCollider;
    private SpriteRenderer spriteRenderer;
    private bool isOpen;

    private void Awake()
    {
        doorCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (startsOpen)
        {
            Open();
        }
        else
        {
            Close();
        }
    }

    public void Open()
    {
        isOpen = true;

        if (doorCollider != null)
        {
            doorCollider.enabled = false;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = openColor;
        }

        Debug.Log("GimmickDoor OPEN");
    }

    public void Close()
    {
        isOpen = false;

        if (doorCollider != null)
        {
            doorCollider.enabled = true;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = closedColor;
        }

        Debug.Log("GimmickDoor CLOSE");
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
}