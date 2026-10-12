using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's casting hub. It owns what every skill shares: where the crosshair is pointing (a ray from the
/// centre of the screen), the fire point, the ultimate's charge meter, and whether a channelled skill is running.
/// Each skill is its own <see cref="PlayerSkill"/> component on the same object.
/// </summary>
// Runs before the skills so they always read this frame's aim.
[DefaultExecutionOrder(-50)]
public class PlayerSkills : MonoBehaviour
{
    [Header("Aiming")]
    [Tooltip("Camera whose centre is the aim point. Falls back to the main camera.")]
    [SerializeField] private Camera aimCamera;
    [Tooltip("Marker moved to wherever the crosshair points. Created automatically if empty.")]
    [SerializeField] private Transform aimTarget;
    [Tooltip("Where projectiles and the beam start. Falls back to in front of the player's chest if empty.")]
    [SerializeField] private Transform firePoint;
    [SerializeField, Min(1f)] private float maxAimDistance = 100f;
    [SerializeField] private LayerMask aimLayers = ~0;
    [Tooltip("If the aim point is closer than this to the fire point, shoot along the camera's view instead.")]
    [SerializeField, Min(0f)] private float minAimDistance = 1.5f;

    [Header("Ultimate")]
    [SerializeField, Min(1f)] private float ultimateChargeMax = 100f;
    [Tooltip("Current charge. Set it to the maximum in the Inspector to test the ultimate straight away.")]
    [SerializeField, Min(0f)] private float ultimateCharge;

    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];
    private readonly List<PlayerSkill> skillList = new List<PlayerSkill>();
    private PlayerMovement movement;
    private Ray aimRay;

    /// <summary>World position the crosshair is pointing at.</summary>
    public Vector3 AimPoint { get; private set; }
    /// <summary>True when the crosshair is over something that can take damage.</summary>
    public bool AimingAtEnemy { get; private set; }
    public Transform FirePoint => firePoint;
    public Transform AimTarget => aimTarget;

    public IReadOnlyList<PlayerSkill> Skills => skillList;
    public PlayerMovement Movement => movement != null ? movement : (movement = GetComponent<PlayerMovement>());

    /// <summary>True while a channelled skill (the beam) is running. Other skills can't be cast meanwhile.</summary>
    public bool IsChanneling { get; set; }

    public float UltimateCharge01 => ultimateChargeMax > 0f ? Mathf.Clamp01(ultimateCharge / ultimateChargeMax) : 1f;
    public bool UltimateReady => ultimateCharge >= ultimateChargeMax;

    /// <summary>The click that re-grabs the cursor after Esc, or any input while the cursor is released, shouldn't cast.</summary>
    public bool InputBlocked =>
        Movement != null && (!Movement.IsMouseLookActive || Movement.MouseLookResumedFrame == Time.frameCount);

    private void Awake()
    {
        if (aimCamera == null)
        {
            aimCamera = Camera.main;
        }

        if (aimTarget == null)
        {
            // Kept in the scene root, not under the player, so it doesn't move and turn with the player.
            aimTarget = new GameObject("AimTarget").transform;
        }

        GetComponents(skillList);
    }

    private void Update()
    {
        UpdateAim();
    }

    // ---------- Ultimate charge ----------

    public void AddUltimateCharge(float amount)
    {
        ultimateCharge = Mathf.Clamp(ultimateCharge + amount, 0f, ultimateChargeMax);
    }

    /// <summary>Empties the meter if it's full. Returns false if the ultimate wasn't ready.</summary>
    public bool ConsumeUltimate()
    {
        if (!UltimateReady)
        {
            return false;
        }

        ultimateCharge = 0f;
        return true;
    }

    // ---------- Aiming ----------

    public Vector3 GetFireOrigin()
    {
        return firePoint != null
            ? firePoint.position
            : transform.position + transform.up * 0.4f + transform.forward * 0.9f;
    }

    /// <summary>Direction from the given origin toward whatever the crosshair is pointing at.</summary>
    public Vector3 GetFireDirection(Vector3 origin)
    {
        Vector3 toAim = AimPoint - origin;
        Vector3 direction = toAim.normalized;

        // An aim point right next to the player, or behind the fire point, would send the shot sideways
        // or backwards. In that case just fire the way the camera is looking.
        if (toAim.sqrMagnitude < minAimDistance * minAimDistance || Vector3.Dot(direction, aimRay.direction) < 0.1f)
        {
            direction = aimRay.direction;
        }
        return direction;
    }

    // Runs in Update, when the camera is still exactly where the last rendered frame showed it,
    // so a shot goes where the crosshair was when the player pressed the key.
    private void UpdateAim()
    {
        if (aimCamera == null)
        {
            aimCamera = Camera.main;
            if (aimCamera == null)
            {
                return;
            }
        }

        aimRay = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        AimPoint = aimRay.GetPoint(maxAimDistance);
        AimingAtEnemy = false;

        int count = Physics.RaycastNonAlloc(aimRay, hitBuffer, maxAimDistance, aimLayers, QueryTriggerInteraction.Ignore);

        // RaycastNonAlloc isn't sorted, so pick the nearest hit that isn't the player's own body.
        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hitBuffer[i];
            if (hit.transform.IsChildOf(transform) || hit.distance >= nearest)
            {
                continue;
            }

            nearest = hit.distance;
            AimPoint = hit.point;
            AimingAtEnemy = hit.collider.GetComponentInParent<Health>() != null;
        }

        if (aimTarget != null)
        {
            aimTarget.position = AimPoint;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (aimTarget == null)
        {
            return;
        }

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(aimTarget.position, 0.3f);
        Vector3 origin = firePoint != null ? firePoint.position : transform.position;
        Gizmos.DrawLine(origin, aimTarget.position);
    }
}
