using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mouse-delta turning plus WASD / arrow-key movement relative to the player's facing, with gravity.
/// Uses a CharacterController so the player walks up gentle slopes and is blocked by steep mountains.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 8f;
    [SerializeField] private float gravity = -20f;

    [Header("Jump")]
    [Tooltip("Peak height of a jump in metres.")]
    [SerializeField, Min(0f)] private float jumpHeight = 1.5f;
    [Tooltip("Grace period after walking off a ledge during which a jump still counts.")]
    [SerializeField, Min(0f)] private float coyoteTime = 0.12f;
    [Tooltip("A jump pressed this long before landing still fires on landing.")]
    [SerializeField, Min(0f)] private float jumpBufferTime = 0.12f;

    [Header("Knockback")]
    [Tooltip("How fast a push fades, in metres per second lost every second. Higher = shorter shove.")]
    [SerializeField, Min(0.1f)] private float knockbackDrag = 20f;
    [Tooltip("Stacked hits can't push the player faster than this.")]
    [SerializeField, Min(1f)] private float maxKnockbackSpeed = 18f;

    [Header("Mouse Rotation")]
    [Tooltip("Degrees of turn per pixel of horizontal mouse movement.")]
    [SerializeField, Min(0f)] private float mouseSensitivity = 0.15f;
    [Tooltip("Only turn while the right mouse button is held, leaving the cursor free for placing towers.")]
    [SerializeField] private bool rotateOnlyWhileRightMouseHeld = false;
    [Tooltip("Hide and lock the cursor so the mouse can turn endlessly. Esc releases it, left click grabs it again.")]
    [SerializeField] private bool lockCursor = true;

    [Header("Debug")]
    [Tooltip("Show mouse delta, rotation and cursor state in the top-left corner of the Game view.")]
    [SerializeField] private bool showDebugInfo = false;

    private CharacterController controller;
    private float verticalVelocity;
    // Only an explicit Esc stops mouse turning. The editor also drops the cursor lock whenever the
    // Game view loses focus, and that must not silently disable turning.
    private bool cursorReleasedByPlayer;
    private Vector2 lastMouseDelta;
    private Vector3 knockbackVelocity;
    private float lastGroundedTime = float.NegativeInfinity;
    private float lastJumpPressedTime = float.NegativeInfinity;

    /// <summary>Scales walking speed. Skills lower this while channelling, e.g. the beam.</summary>
    public float SpeedMultiplier { get; set; } = 1f;

    public Vector3 KnockbackVelocity => knockbackVelocity;

    /// <summary>Shoves the player. The push fades out on its own; an upward speed gives a small hop.</summary>
    public void ApplyKnockback(Vector3 velocity, float upwardSpeed = 0f)
    {
        velocity.y = 0f;
        knockbackVelocity = Vector3.ClampMagnitude(knockbackVelocity + velocity, maxKnockbackSpeed);
        if (upwardSpeed > verticalVelocity)
        {
            verticalVelocity = upwardSpeed;
        }
    }

    /// <summary>Stops any push immediately, e.g. when the player respawns.</summary>
    public void ClearKnockback()
    {
        knockbackVelocity = Vector3.zero;
    }

    public bool IsMouseLookActive => !cursorReleasedByPlayer;
    // Frame on which a left click resumed mouse look after Esc, so other scripts can ignore that click.
    public int MouseLookResumedFrame { get; private set; } = -1;
    public bool IsGrounded => controller.isGrounded;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void OnEnable()
    {
        cursorReleasedByPlayer = false;
        SetCursorLocked(lockCursor);
    }

    private void OnDisable()
    {
        SetCursorLocked(false);
    }

    private void Update()
    {
        UpdateCursorLock();
        RotateWithMouse();

        Vector3 moveDirection = GetMoveDirection();

        if (controller.isGrounded)
        {
            lastGroundedTime = Time.time;
            if (verticalVelocity < 0f)
            {
                // Small downward force keeps the controller snapped to the ground on slopes.
                verticalVelocity = -2f;
            }
        }

        TryJump();
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = moveDirection * (moveSpeed * SpeedMultiplier) + knockbackVelocity + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
        knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, knockbackDrag * Time.deltaTime);
    }

    private void TryJump()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
        {
            lastJumpPressedTime = Time.time;
        }

        bool jumpBuffered = Time.time - lastJumpPressedTime <= jumpBufferTime;
        bool canJump = Time.time - lastGroundedTime <= coyoteTime;
        if (!jumpBuffered || !canJump)
        {
            return;
        }

        // v = sqrt(2 * h * g) gives exactly jumpHeight at the peak for the current gravity.
        verticalVelocity = Mathf.Sqrt(2f * jumpHeight * Mathf.Abs(gravity));
        // Consume both so one press gives one jump and the coyote window can't double-jump.
        lastJumpPressedTime = float.NegativeInfinity;
        lastGroundedTime = float.NegativeInfinity;
    }

    private void RotateWithMouse()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        lastMouseDelta = mouse.delta.ReadValue();

        // After Esc the mouse belongs to the UI/editor, not the camera.
        if (cursorReleasedByPlayer)
        {
            return;
        }

        if (rotateOnlyWhileRightMouseHeld && !mouse.rightButton.isPressed)
        {
            return;
        }

        // Mouse delta is already "pixels moved this frame", so it is not scaled by deltaTime.
        float yaw = lastMouseDelta.x * mouseSensitivity;
        transform.Rotate(0f, yaw, 0f, Space.World);
    }

    private void UpdateCursorLock()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            cursorReleasedByPlayer = true;
            SetCursorLocked(false);
            return;
        }

        if (cursorReleasedByPlayer && mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            cursorReleasedByPlayer = false;
            MouseLookResumedFrame = Time.frameCount;
        }

        // Keep re-applying the lock: the editor releases it every time the Game view loses focus.
        if (lockCursor && !cursorReleasedByPlayer && Cursor.lockState != CursorLockMode.Locked)
        {
            SetCursorLocked(true);
        }
    }

    private void OnGUI()
    {
        if (!showDebugInfo)
        {
            return;
        }

        GUI.Label(new Rect(10f, 10f, 600f, 22f), $"Mouse delta: {lastMouseDelta}   Player yaw: {transform.eulerAngles.y:F1}");
        GUI.Label(new Rect(10f, 30f, 600f, 22f), $"Cursor: {Cursor.lockState}   Mouse look active: {IsMouseLookActive}   Mouse device: {(Mouse.current != null)}");
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private Vector3 GetMoveDirection()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return Vector3.zero;
        }

        Vector2 input = Vector2.zero;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;

        if (input == Vector2.zero)
        {
            return Vector3.zero;
        }

        // W/S move along the player's facing, A/D strafe. Normalise so diagonals aren't faster.
        return (transform.forward * input.y + transform.right * input.x).normalized;
    }
}
