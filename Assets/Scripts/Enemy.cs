using UnityEngine;

/// <summary>
/// Walks toward the player and attacks in melee. One script drives every enemy type;
/// the differences (speed, circling, wind-up slam) are just settings on each prefab.
/// </summary>
[RequireComponent(typeof(CharacterController), typeof(Health))]
public class Enemy : MonoBehaviour
{
    private const string PlayerTag = "Player";
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 3.5f;
    [SerializeField, Min(0f)] private float turnSpeed = 540f;
    [SerializeField] private float gravity = -20f;
    [Tooltip("0 = run straight at the player. Higher values weave sideways while approaching.")]
    [SerializeField, Range(0f, 1f)] private float circleAmount = 0f;

    [Tooltip("How much of a knockback this enemy feels. 1 = fully, 0 = immovable.")]
    [SerializeField, Range(0f, 1f)] private float knockbackMultiplier = 1f;
    [Tooltip("How fast knockback fades, in metres per second lost every second.")]
    [SerializeField, Min(0.1f)] private float knockbackDrag = 30f;

    [Header("Attack")]
    [Tooltip("Starts attacking when the player is this close (horizontal distance).")]
    [SerializeField, Min(0.1f)] private float attackRange = 1.4f;
    [Tooltip("The attack only lands if the player is still within this distance when it strikes.")]
    [SerializeField, Min(0.1f)] private float attackRadius = 1.6f;
    [SerializeField, Min(0f)] private float attackDamage = 5f;
    [SerializeField, Min(0f)] private float attackCooldown = 1f;
    [Tooltip("Time spent rooted and telegraphing before the hit lands. 0 = instant.")]
    [SerializeField, Min(0f)] private float attackWindup = 0f;
    [Tooltip("Speed the player is shoved away at when hit.")]
    [SerializeField, Min(0f)] private float playerKnockback = 7f;
    [Tooltip("Upward hop given to the player when hit. 0 = a flat shove.")]
    [SerializeField, Min(0f)] private float playerKnockUp = 0f;

    [Header("Visuals")]
    [Tooltip("Child holding the shapes. It swells during a wind-up so the player can see the slam coming.")]
    [SerializeField] private Transform model;
    [SerializeField, Min(0f)] private float hitFlashTime = 0.08f;

    [Header("Death")]
    [Tooltip("Spawned where the enemy dies, e.g. a smoke cloud that lingers after the body is gone.")]
    [SerializeField] private GameObject deathEffectPrefab;
    [Tooltip("Scales the death effect so bigger enemies leave bigger clouds.")]
    [SerializeField, Min(0.1f)] private float deathEffectScale = 1f;

    private CharacterController controller;
    private Health health;
    private Transform target;
    private Health targetHealth;
    private PlayerMovement targetMovement;
    private float verticalVelocity;
    private float nextAttackTime;
    private float windupTimer;
    private bool windingUp;
    private float circleSign;
    private Vector3 knockbackVelocity;

    private Renderer[] renderers;
    private Color[] originalColors;
    private MaterialPropertyBlock propertyBlock;
    private float flashUntil;
    private bool flashing;
    private Vector3 modelBaseScale = Vector3.one;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>();
        circleSign = Random.value < 0.5f ? -1f : 1f;

        propertyBlock = new MaterialPropertyBlock();
        renderers = GetComponentsInChildren<Renderer>();
        originalColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            Material material = renderers[i].sharedMaterial;
            originalColors[i] = material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.white;
        }

        if (model != null)
        {
            modelBaseScale = model.localScale;
        }
    }

    private void OnEnable()
    {
        health.Damaged += OnDamaged;
        health.Died += OnDied;
    }

    private void OnDisable()
    {
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
    }

    private void Start()
    {
        FindTarget();
        // Stagger attacks so a pack arriving together doesn't all swing on the same frame.
        nextAttackTime = Time.time + Random.Range(0f, 0.4f);
    }

    private void Update()
    {
        UpdateFlash();

        if (target == null)
        {
            FindTarget();
            ApplyGravityOnly();
            return;
        }

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        Vector3 direction = distance > 0.01f ? toTarget / distance : transform.forward;

        FaceDirection(direction);

        Vector3 move = Vector3.zero;
        if (windingUp)
        {
            UpdateWindup(distance);
        }
        else if (distance <= attackRange && Time.time >= nextAttackTime)
        {
            BeginAttack(distance);
        }
        else if (distance > attackRange * 0.9f)
        {
            move = GetMoveDirection(direction, distance) * moveSpeed;
        }

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        verticalVelocity += gravity * Time.deltaTime;

        controller.Move((move + knockbackVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);
        knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, knockbackDrag * Time.deltaTime);
    }

    /// <summary>Shoves the enemy; the push fades out on its own. Heavy enemies feel less of it.</summary>
    public void ApplyKnockback(Vector3 velocity)
    {
        knockbackVelocity += velocity * knockbackMultiplier;
    }

    private Vector3 GetMoveDirection(Vector3 toTarget, float distance)
    {
        // Only weave while still far away; up close, commit and run straight in.
        if (circleAmount <= 0f || distance < attackRange * 2f)
        {
            return toTarget;
        }

        Vector3 sideways = Vector3.Cross(Vector3.up, toTarget) * circleSign * circleAmount;
        return (toTarget + sideways).normalized;
    }

    private void FaceDirection(Vector3 direction)
    {
        Quaternion desired = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, turnSpeed * Time.deltaTime);
    }

    private void BeginAttack(float distance)
    {
        if (attackWindup <= 0f)
        {
            Strike();
            return;
        }

        windingUp = true;
        windupTimer = attackWindup;
    }

    private void UpdateWindup(float distance)
    {
        windupTimer -= Time.deltaTime;
        float progress = 1f - Mathf.Clamp01(windupTimer / attackWindup);

        if (model != null)
        {
            model.localScale = Vector3.Lerp(modelBaseScale, modelBaseScale * 1.25f, progress);
        }

        if (windupTimer <= 0f)
        {
            windingUp = false;
            if (model != null)
            {
                model.localScale = modelBaseScale;
            }
            Strike();
        }
    }

    /// <summary>
    /// Lands the attack now: damages and shoves the player if they're within the attack radius.
    /// Called when the wind-up ends, and public so an attack animation event could call it too.
    /// </summary>
    public void Strike()
    {
        nextAttackTime = Time.time + attackCooldown;

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.magnitude > attackRadius || targetHealth == null)
        {
            return;
        }

        // Only a hit that actually landed shoves the player: no knockback during the grace period or after death.
        if (targetHealth.TakeDamage(attackDamage) && targetMovement != null)
        {
            Vector3 away = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : transform.forward;
            targetMovement.ApplyKnockback(away * playerKnockback, playerKnockUp);
        }
    }

    private void ApplyGravityOnly()
    {
        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        verticalVelocity += gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);
    }

    private void FindTarget()
    {
        GameObject player = GameObject.FindWithTag(PlayerTag);
        if (player == null)
        {
            return;
        }

        target = player.transform;
        targetHealth = player.GetComponent<Health>();
        targetMovement = player.GetComponent<PlayerMovement>();
    }

    private void OnDamaged(float amount)
    {
        flashing = true;
        flashUntil = Time.time + hitFlashTime;
        SetFlash(true);
    }

    private void OnDied()
    {
        if (deathEffectPrefab != null)
        {
            // Spawn at the middle of the body, not the feet, so the cloud rises from where the enemy stood.
            Vector3 center = transform.TransformPoint(controller.center);
            GameObject effect = Instantiate(deathEffectPrefab, center, Quaternion.identity);
            effect.transform.localScale = Vector3.one * deathEffectScale;
        }

        Destroy(gameObject);
    }

    private void UpdateFlash()
    {
        if (flashing && Time.time >= flashUntil)
        {
            flashing = false;
            SetFlash(false);
        }
    }

    // Tint every shape white for a moment so hits read clearly even on plain coloured shapes.
    private void SetFlash(bool on)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, on ? Color.white : originalColors[i]);
            renderers[i].SetPropertyBlock(propertyBlock);
        }
    }
}
