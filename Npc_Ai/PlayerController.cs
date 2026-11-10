using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
  [Header("References")]
  [SerializeField] private Transform playerCamera;
  [SerializeField] private DialogueUI dialogueUI;

  [Header("Movement")]
  [SerializeField, Min(0f)] private float walkSpeed = 4f;
  [SerializeField, Min(0f)] private float runSpeed = 7f;
  [SerializeField, Min(0f)] private float jumpHeight = 1.2f;
  [SerializeField] private float gravity = -20f;

  [Header("Mouse Look")]
  [SerializeField, Min(0f)] private float mouseSensitivity = 0.1f;
  [SerializeField, Range(1f, 89f)] private float maxLookAngle = 80f;

  private CharacterController controller;
  private float verticalVelocity;
  private float pitch;

  private bool DialogueIsOpen =>
      dialogueUI != null && dialogueUI.IsOpen;

  private void Awake()
  {
    controller = GetComponent<CharacterController>();

    if (playerCamera == null || dialogueUI == null)
    {
      Debug.LogError(
          "Assign Player Camera and Dialogue UI.",
          this);

      enabled = false;
    }
  }

  private void Start()
  {
    pitch = Mathf.DeltaAngle(
        0f, playerCamera.localEulerAngles.x);

    if (!DialogueIsOpen)
    {
      Cursor.lockState = CursorLockMode.Locked;
      Cursor.visible = false;
    }
  }

  private void Update()
  {
    if (!controller.enabled)
      return;

    bool canControl =
        !DialogueIsOpen &&
        Cursor.lockState == CursorLockMode.Locked;

    if (canControl)
      UpdateMouseLook();

    UpdateMovement(canControl);
  }

  private void UpdateMouseLook()
  {
    Mouse mouse = Mouse.current;

    if (mouse == null)
      return;

    Vector2 delta = mouse.delta.ReadValue();

    // Mouse delta is already movement accumulated this frame.
    float yaw = delta.x * mouseSensitivity;
    pitch -= delta.y * mouseSensitivity;
    pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);

    transform.Rotate(Vector3.up, yaw, Space.Self);
    playerCamera.localRotation = Quaternion.Euler(pitch, 0f, 0f);
  }

  private void UpdateMovement(bool canControl)
  {
    Keyboard keyboard = Keyboard.current;
    Vector2 input = Vector2.zero;
    float speed = walkSpeed;
    bool jumpPressed = false;

    if (canControl && keyboard != null)
    {
      if (keyboard.wKey.isPressed) input.y += 1f;
      if (keyboard.sKey.isPressed) input.y -= 1f;
      if (keyboard.dKey.isPressed) input.x += 1f;
      if (keyboard.aKey.isPressed) input.x -= 1f;

      input = Vector2.ClampMagnitude(input, 1f);

      if (keyboard.leftShiftKey.isPressed)
        speed = runSpeed;

      jumpPressed = keyboard.spaceKey.wasPressedThisFrame;
    }

    if (controller.isGrounded && verticalVelocity < 0f)
      verticalVelocity = -2f;

    // Force gravity to remain downward.
    float downwardGravity = -Mathf.Max(0.01f, Mathf.Abs(gravity));

    if (canControl && controller.isGrounded && jumpPressed)
    {
      verticalVelocity = Mathf.Sqrt(
          jumpHeight * -2f * downwardGravity);
    }

    verticalVelocity += downwardGravity * Time.deltaTime;

    Vector3 velocity =
        (transform.right * input.x +
         transform.forward * input.y) * speed;

    velocity.y = verticalVelocity;

    CollisionFlags collisions =
        controller.Move(velocity * Time.deltaTime);

    if ((collisions & CollisionFlags.Above) != 0 &&
        verticalVelocity > 0f)
    {
      verticalVelocity = 0f;
    }
  }

  private void OnApplicationFocus(bool hasFocus)
  {
    if (!hasFocus || !enabled || DialogueIsOpen)
      return;

    Cursor.lockState = CursorLockMode.Locked;
    Cursor.visible = false;
  }
}