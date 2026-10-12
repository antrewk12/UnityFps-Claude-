using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third-person orbit camera: stays behind the target, turns with it, and tilts up/down with the mouse.
/// If no target is assigned, it finds the GameObject tagged "Player".
/// Also runs in Edit mode so the camera stays behind the player while you arrange the scene.
/// </summary>
[ExecuteAlways]
public class CameraFollow : MonoBehaviour
{
    private const string PlayerTag = "Player";

    [SerializeField] private Transform target;

    [Header("Framing")]
    [Tooltip("Distance from the look point to the camera.")]
    [SerializeField, Min(0.5f)] private float distance = 6f;
    [Tooltip("Height above the target's pivot that the camera looks at (roughly head height).")]
    [SerializeField] private float lookAtHeight = 1.2f;
    [Tooltip("Sideways shift of the look point; positive = over the right shoulder.")]
    [SerializeField] private float shoulderOffset = 0f;

    [Header("Mouse Tilt")]
    [Tooltip("Degrees of tilt per pixel of vertical mouse movement.")]
    [SerializeField, Min(0f)] private float pitchSensitivity = 0.15f;
    [SerializeField] private bool invertY = false;
    [SerializeField] private float startPitch = 15f;
    [SerializeField, Range(-89f, 0f)] private float minPitch = -20f;
    [SerializeField, Range(0f, 89f)] private float maxPitch = 65f;

    [Header("Smoothing")]
    [Tooltip("How quickly the camera swings behind the target when it turns. 0 = locked.")]
    [SerializeField, Min(0f)] private float rotationSmoothTime = 0.03f;
    [Tooltip("How quickly the camera catches up when the target moves. 0 = locked.")]
    [SerializeField, Min(0f)] private float positionSmoothTime = 0.05f;

    [Header("Collision")]
    [Tooltip("Pull the camera in when terrain or obstacles block the view of the target.")]
    [SerializeField] private bool avoidObstacles = true;
    [SerializeField] private LayerMask obstacleLayers = ~0;
    [SerializeField, Min(0f)] private float collisionRadius = 0.3f;

    private PlayerMovement targetMovement;
    private float currentYaw;
    private float yawVelocity;
    private float pitch;
    private Vector3 smoothedPivot;
    private Vector3 pivotVelocity;

    private void Awake()
    {
        if (target == null)
        {
            GameObject player = GameObject.FindWithTag(PlayerTag);
            if (player != null)
            {
                target = player.transform;
            }
        }
        if (target != null)
        {
            targetMovement = target.GetComponent<PlayerMovement>();
        }
        pitch = startPitch;
    }

    private void Start()
    {
        // Snap into place on the first frame instead of gliding in from the scene position.
        if (target != null)
        {
            currentYaw = target.eulerAngles.y;
            smoothedPivot = target.position;
            ApplyCameraTransform();
        }
    }

    // LateUpdate runs after the target has moved and rotated this frame, which avoids jitter.
    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        // In Edit mode there is no mouse look or smoothing: just snap behind the target.
        if (!Application.isPlaying)
        {
            pitch = startPitch;
            currentYaw = target.eulerAngles.y;
            smoothedPivot = target.position;
            ApplyCameraTransform();
            return;
        }

        UpdatePitch();

        // Smooth the angle and the position separately so the camera orbits at a constant distance
        // instead of cutting across the circle when the target turns.
        currentYaw = Mathf.SmoothDampAngle(currentYaw, target.eulerAngles.y, ref yawVelocity, rotationSmoothTime);
        smoothedPivot = Vector3.SmoothDamp(smoothedPivot, target.position, ref pivotVelocity, positionSmoothTime);

        ApplyCameraTransform();
    }

    private void UpdatePitch()
    {
        Mouse mouse = Mouse.current;
        // Follow the player's mouse-look state so tilting stops after Esc, same as turning.
        if (mouse == null || (targetMovement != null && !targetMovement.IsMouseLookActive))
        {
            return;
        }

        // Moving the mouse up should look up, which means lowering the camera (smaller pitch).
        float delta = mouse.delta.ReadValue().y * pitchSensitivity;
        pitch += invertY ? delta : -delta;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    private void ApplyCameraTransform()
    {
        Quaternion orbit = Quaternion.Euler(pitch, currentYaw, 0f);
        Vector3 lookPoint = smoothedPivot + Vector3.up * lookAtHeight + orbit * Vector3.right * shoulderOffset;
        Vector3 desiredPosition = lookPoint + orbit * new Vector3(0f, 0f, -distance);

        if (avoidObstacles)
        {
            desiredPosition = PullInFromObstacles(lookPoint, desiredPosition);
        }

        transform.position = desiredPosition;
        transform.rotation = Quaternion.LookRotation(lookPoint - desiredPosition, Vector3.up);
    }

    private Vector3 PullInFromObstacles(Vector3 lookPoint, Vector3 desiredPosition)
    {
        Vector3 toCamera = desiredPosition - lookPoint;
        float length = toCamera.magnitude;
        if (length < 0.001f)
        {
            return desiredPosition;
        }

        Vector3 direction = toCamera / length;
        if (Physics.SphereCast(lookPoint, collisionRadius, direction, out RaycastHit hit, length, obstacleLayers, QueryTriggerInteraction.Ignore)
            && hit.transform != target && !hit.transform.IsChildOf(target))
        {
            return lookPoint + direction * hit.distance;
        }

        return desiredPosition;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        targetMovement = target != null ? target.GetComponent<PlayerMovement>() : null;
    }
}
