using UnityEngine;
using UnityEngine.InputSystem;

public class Player : MonoBehaviour
{
    [SerializeField, Header("移動速度")]
    private float _moveSpeed;

    private Vector2 _inputDirection;
    private Rigidbody2D _rigid;

   // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _rigid = GetComponent<Rigidbody2D>();
    }

    // Update is called once per frame
    void Update()
    {
        _Move();
    }

    private void  _Move(){
        _rigid.linearVelocity = new Vector2(_inputDirection.x * _moveSpeed, _rigid.linearVelocity.y);
    }

    public void _OnMove(InputAction.CallbackContext context){
        _inputDirection = context.ReadValue<Vector2>();
    }
}
