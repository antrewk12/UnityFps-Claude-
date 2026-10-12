using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Flies straight ahead and explodes on the first thing it hits, or fizzles out after its lifetime.
/// Used for both the Ember Bolt and the Fireball; they are different prefabs with different settings.
/// Movement is swept with a sphere cast each frame so fast projectiles can't tunnel through thin objects.
/// </summary>
public class Fireball : MonoBehaviour
{
    [SerializeField, Min(0f)] private float speed = 25f;
    [SerializeField, Min(0.1f)] private float lifetime = 3f;
    [SerializeField, Min(0.01f)] private float radius = 0.25f;
    [SerializeField] private LayerMask hitLayers = ~0;
    [SerializeField] private GameObject explosionPrefab;
    [Tooltip("Scales the explosion effect (the prefab's particle systems must use Hierarchy scaling).")]
    [SerializeField, Min(0.05f)] private float explosionScale = 1f;
    [Tooltip("How long the flame trail keeps fading after impact before the object is destroyed.")]
    [SerializeField, Min(0f)] private float trailFadeTime = 0.6f;

    [Header("Damage")]
    [SerializeField, Min(0f)] private float damage = 1f;
    [Tooltip("Everything with Health inside this radius of the impact takes damage once. 0 = only what it hits directly.")]
    [SerializeField, Min(0f)] private float splashRadius = 2f;

    private Transform owner;
    private float age;
    private bool exploded;
    private ParticleSystem[] particleSystems;
    private Light glow;

    /// <summary>Raised when this projectile damages something, with how many things it damaged.</summary>
    public event Action<int> Hit;

    private void Awake()
    {
        particleSystems = GetComponentsInChildren<ParticleSystem>();
        glow = GetComponentInChildren<Light>();
    }

    /// <summary>Call right after spawning so the projectile never hits whoever fired it.</summary>
    public void Launch(Transform shooter)
    {
        owner = shooter;
    }

    private void Update()
    {
        if (exploded)
        {
            return;
        }

        age += Time.deltaTime;
        if (age >= lifetime)
        {
            Explode(transform.position, null);
            return;
        }

        float step = speed * Time.deltaTime;
        if (Physics.SphereCast(transform.position, radius, transform.forward, out RaycastHit hit, step, hitLayers, QueryTriggerInteraction.Ignore)
            && !IsOwner(hit.transform))
        {
            transform.position = hit.point;
            Explode(hit.point, hit.collider);
            return;
        }

        transform.position += transform.forward * step;
    }

    private bool IsOwner(Transform other)
    {
        return owner != null && (other == owner || other.IsChildOf(owner));
    }

    private void Explode(Vector3 point, Collider directHit)
    {
        exploded = true;

        int damaged = DealDamage(point, directHit);
        if (damaged > 0)
        {
            Hit?.Invoke(damaged);
        }

        if (explosionPrefab != null)
        {
            GameObject effect = Instantiate(explosionPrefab, point, Quaternion.identity);
            effect.transform.localScale = Vector3.one * explosionScale;
        }

        // Stop emitting but let the existing flames fade out naturally instead of popping.
        foreach (ParticleSystem system in particleSystems)
        {
            system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
        if (glow != null)
        {
            glow.enabled = false;
        }

        Destroy(gameObject, trailFadeTime);
    }

    // The thing we hit always takes damage, even with no splash; splash then adds everything else nearby.
    private int DealDamage(Vector3 point, Collider directHit)
    {
        HashSet<Health> alreadyHit = new HashSet<Health>();

        if (directHit != null)
        {
            TryDamage(directHit, alreadyHit);
        }

        if (splashRadius > 0f)
        {
            Collider[] colliders = Physics.OverlapSphere(point, splashRadius, hitLayers, QueryTriggerInteraction.Ignore);
            foreach (Collider other in colliders)
            {
                TryDamage(other, alreadyHit);
            }
        }

        return alreadyHit.Count;
    }

    private void TryDamage(Collider other, HashSet<Health> alreadyHit)
    {
        if (IsOwner(other.transform))
        {
            return;
        }

        // A body can have several colliders; the set makes sure it only takes damage once.
        Health health = other.GetComponentInParent<Health>();
        if (health != null && alreadyHit.Add(health))
        {
            health.TakeDamage(damage);
        }
    }
}
