using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A burst of fire centred on the player. Everything with Health inside the radius takes damage and is shoved away.
/// </summary>
public class BlastSkill : PlayerSkill
{
    [Header("Blast")]
    [SerializeField, Min(0.5f)] private float radius = 8f;
    [SerializeField, Min(0f)] private float damage = 3f;
    [Tooltip("Speed enemies are knocked away at. Heavier enemies resist it (see their Knockback Multiplier).")]
    [SerializeField, Min(0f)] private float knockbackSpeed = 14f;
    [SerializeField] private LayerMask hitLayers = ~0;

    [Header("Visuals")]
    [SerializeField] private GameObject effectPrefab;
    [Tooltip("The radius the effect prefab was built for. The effect is scaled so it always matches the real radius.")]
    [SerializeField, Min(0.5f)] private float effectReferenceRadius = 8f;

    protected override void Cast()
    {
        Vector3 center = transform.position;

        if (effectPrefab != null)
        {
            // Spawn at the player's feet so the ring spreads across the ground.
            Vector3 feet = center + Vector3.down * 0.9f;
            GameObject effect = Instantiate(effectPrefab, feet, Quaternion.identity);
            effect.transform.localScale = Vector3.one * (radius / effectReferenceRadius);
        }

        Collider[] colliders = Physics.OverlapSphere(center, radius, hitLayers, QueryTriggerInteraction.Ignore);
        HashSet<Health> alreadyHit = new HashSet<Health>();

        foreach (Collider other in colliders)
        {
            if (other.transform.IsChildOf(transform))
            {
                continue;
            }

            // A body can have several colliders; the set makes sure it only takes damage once.
            Health health = other.GetComponentInParent<Health>();
            if (health == null || !alreadyHit.Add(health))
            {
                continue;
            }

            health.TakeDamage(damage);

            Enemy enemy = health.GetComponent<Enemy>();
            if (enemy != null)
            {
                Vector3 away = health.transform.position - center;
                away.y = 0f;
                enemy.ApplyKnockback(away.sqrMagnitude > 0.001f ? away.normalized * knockbackSpeed : Vector3.zero);
            }
        }
    }
}
