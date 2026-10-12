using UnityEngine;

/// <summary>
/// Shoots a projectile toward the crosshair. Used for both the Ember Bolt and the Fireball;
/// they differ only in the prefab, cooldown and key set on each component.
/// </summary>
public class ProjectileSkill : PlayerSkill
{
    [Header("Projectile")]
    [SerializeField] private Fireball projectilePrefab;

    [Header("Ultimate Charge")]
    [Tooltip("When on, every enemy this projectile hits adds to the ultimate's charge meter.")]
    [SerializeField] private bool chargesUltimate;
    [SerializeField, Min(0f)] private float chargePerHit = 10f;

    protected override void Cast()
    {
        if (projectilePrefab == null)
        {
            return;
        }

        Vector3 origin = Caster.GetFireOrigin();
        Vector3 direction = Caster.GetFireDirection(origin);

        Fireball projectile = Instantiate(projectilePrefab, origin, Quaternion.LookRotation(direction));
        projectile.Launch(transform);

        if (chargesUltimate)
        {
            projectile.Hit += OnProjectileHit;
        }
    }

    private void OnProjectileHit(int enemiesHit)
    {
        Caster.AddUltimateCharge(enemiesHit * chargePerHit);
    }
}
