using UnityEngine;
using UnityEngine.InputSystem;

public class DoorTestInput : MonoBehaviour
{
    [SerializeField] private GimmickDoor targetDoor;

    private void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.oKey.wasPressedThisFrame)
        {
            targetDoor.Open();
        }

        if (Keyboard.current.cKey.wasPressedThisFrame)
        {
            targetDoor.Close();
        }

        if (Keyboard.current.tKey.wasPressedThisFrame)
        {
            targetDoor.Toggle();
        }
    }
}