using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
  [Header("References")]
  [SerializeField] private Transform cameraTransform;
  [SerializeField] private DialogueUI dialogueUI;
  [Tooltip("ไม่บังคับ: Animator ของโมเดลตัวละคร")]
  [SerializeField] private Animator animator;
  [Tooltip("ชื่อ Float Parameter ใน Animator ปล่อยว่างถ้าไม่ใช้")]
  [SerializeField] private string speedParameter = "Speed";

  [Header("Movement")]
  [SerializeField, Min(0f)] private float walkSpeed = 3f;
  [SerializeField, Min(0f)] private float runSpeed = 6f;
  [SerializeField, Min(0f)] private float rotationSmoothTime = 0.1f;
  [SerializeField, Min(0f)] private float jumpHeight = 1.2f;
  [SerializeField] private float gravity = -20f;

  [Header("Camera Orbit")]
  [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.6f, 0f);
  [SerializeField, Min(0.5f)] private float distance = 4f;
  [SerializeField, Min(0.5f)] private float minDistance = 1.5f;
  [SerializeField, Min(0.5f)] private float maxDistance = 7f;
  [SerializeField, Min(0f)] private float zoomStep = 0.5f;
  [SerializeField, Min(0f)] private float mouseSensitivity = 0.15f;
  [SerializeField, Range(-89f, 0f)] private float minPitch = -30f;
  [SerializeField, Range(0f, 89f)] private float maxPitch = 70f;

  [Header("Camera Collision")]
  [SerializeField] private LayerMask cameraCollisionMask = ~0;
  [SerializeField, Min(0.05f)] private float cameraRadius = 0.25f;

  private CharacterController controller;
  private float yaw;
  private float pitch = 15f;
  private float verticalVelocity;
  private float turnVelocity;
  private int speedHash;
  private bool hasSpeedParam;

  private bool DialogueIsOpen => dialogueUI != null && dialogueUI.IsOpen;

  private void Awake()
  {
    controller = GetComponent<CharacterController>();

    if (cameraTransform == null && Camera.main != null)
      cameraTransform = Camera.main.transform;

    if (cameraTransform == null || dialogueUI == null)
    {
      Debug.LogError("Assign Camera Transform and Dialogue UI.", this);
      enabled = false;
      return;
    }

    if (cameraTransform.IsChildOf(transform))
      Debug.LogWarning("กล้องไม่ควรเป็นลูกของ Player ในโหมด Third Person", this);

    if (animator != null && !string.IsNullOrEmpty(speedParameter))
    {
      speedHash = Animator.StringToHash(speedParameter);
      foreach (var p in animator.parameters)
        if (p.nameHash == speedHash && p.type == AnimatorControllerParameterType.Float)
          hasSpeedParam = true;
    }
  }

  private void Start()
  {
    yaw = transform.eulerAngles.y;
    distance = Mathf.Clamp(distance, minDistance, maxDistance);

    if (!DialogueIsOpen)
      LockCursor();
  }

  private void Update()
  {
    bool canControl = !DialogueIsOpen && Cursor.lockState == CursorLockMode.Locked;

    if (canControl)
      UpdateLookAndZoom();

    UpdateMovement(canControl);
  }

  private void LateUpdate()
  {
    // กล้องตามตัวละครเสมอ แม้ระหว่างคุยกับ NPC
    UpdateCamera();
  }

  private void UpdateLookAndZoom()
  {
    Mouse mouse = Mouse.current;
    if (mouse == null) return;

    Vector2 delta = mouse.delta.ReadValue();
    yaw += delta.x * mouseSensitivity;
    pitch -= delta.y * mouseSensitivity;
    pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

    float scroll = mouse.scroll.ReadValue().y;
    if (Mathf.Abs(scroll) > 0.01f)
    {
      distance -= Mathf.Sign(scroll) * zoomStep;
      distance = Mathf.Clamp(distance, minDistance, maxDistance);
    }
  }

  private void UpdateMovement(bool canControl)
  {
    if (!controller.enabled) return;

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

      if (keyboard.leftShiftKey.isPressed) speed = runSpeed;
      jumpPressed = keyboard.spaceKey.wasPressedThisFrame;
    }

    // ทิศเดินอ้างอิงจากมุมกล้อง (แนวราบ)
    Quaternion camYaw = Quaternion.Euler(0f, yaw, 0f);
    Vector3 moveDir = camYaw * new Vector3(input.x, 0f, input.y);

    if (moveDir.sqrMagnitude > 0.0001f)
    {
      float targetAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
      float angle = Mathf.SmoothDampAngle(
          transform.eulerAngles.y, targetAngle, ref turnVelocity, rotationSmoothTime);
      transform.rotation = Quaternion.Euler(0f, angle, 0f);
    }

    if (controller.isGrounded && verticalVelocity < 0f)
      verticalVelocity = -2f;

    float down = -Mathf.Max(0.01f, Mathf.Abs(gravity));

    if (canControl && controller.isGrounded && jumpPressed)
      verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * down);

    verticalVelocity += down * Time.deltaTime;

    Vector3 velocity = moveDir * speed;
    velocity.y = verticalVelocity;

    CollisionFlags flags = controller.Move(velocity * Time.deltaTime);
    if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
      verticalVelocity = 0f;

    if (hasSpeedParam)
      animator.SetFloat(speedHash, moveDir.magnitude * speed, 0.1f, Time.deltaTime);
  }

  private void UpdateCamera()
  {
    Vector3 pivot = transform.position + targetOffset;
    Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
    Vector3 back = rotation * Vector3.back;

    float camDistance = distance;

    // กันกล้องทะลุกำแพง
    if (Physics.SphereCast(pivot, cameraRadius, back, out RaycastHit hit,
            distance, cameraCollisionMask, QueryTriggerInteraction.Ignore))
    {
      camDistance = Mathf.Max(0.2f, hit.distance);
    }

    cameraTransform.SetPositionAndRotation(pivot + back * camDistance, rotation);
  }

  private void LockCursor()
  {
    Cursor.lockState = CursorLockMode.Locked;
    Cursor.visible = false;
  }

  private void OnApplicationFocus(bool hasFocus)
  {
    if (hasFocus && enabled && !DialogueIsOpen)
      LockCursor();
  }
}