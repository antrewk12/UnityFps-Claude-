using System;
using UnityEngine;

/// <summary>
/// Hit points for anything that can be damaged. Raises events instead of deciding what death means.
/// </summary>
public class Health : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maxHealth = 10f;
    [SerializeField] private float currentHealth;
    [Tooltip("After taking a hit, ignore further damage for this many seconds. Gives the player a moment to react.")]
    [SerializeField, Min(0f)] private float invulnerableAfterHit;

    private bool initialized;
    private float invulnerableUntil;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public bool IsDead { get; private set; }

    /// <summary>Raised with the amount of damage actually taken.</summary>
    public event Action<float> Damaged;
    public event Action Died;

    private void Awake()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        currentHealth = maxHealth;
        IsDead = false;
    }

    /// <summary>Brings back to full health and clears death and the post-hit grace period, e.g. on respawn.</summary>
    public void ResetToFull()
    {
        initialized = true;
        currentHealth = maxHealth;
        IsDead = false;
        invulnerableUntil = 0f;
    }

    /// <summary>Deals damage. Returns false if nothing happened (already dead, or in the grace period after a hit).</summary>
    public bool TakeDamage(float amount)
    {
        Initialize();
        if (IsDead || amount <= 0f || Time.time < invulnerableUntil)
        {
            return false;
        }

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        invulnerableUntil = Time.time + invulnerableAfterHit;
        Damaged?.Invoke(amount);

        if (currentHealth <= 0f)
        {
            IsDead = true;
            Died?.Invoke();
        }
        return true;
    }
}
