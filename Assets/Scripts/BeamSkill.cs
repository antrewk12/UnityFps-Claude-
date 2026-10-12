using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ultimate: a beam of fire that follows the crosshair for a few seconds, burning everything it passes through.
/// It needs a full charge meter (earned by landing Ember Bolts), and slows the player while it's running.
/// Enemies don't stop the beam, but terrain and trees do.
/// </summary>
public class BeamSkill : PlayerSkill
{
    [Header("Beam")]
    [SerializeField, Min(0.1f)] private float duration = 5f;
    [SerializeField, Min(1f)] private float maxLength = 40f;
    [Tooltip("How wide the damaging part of the beam is.")]
    [SerializeField, Min(0.1f)] private float radius = 0.7f;
    [SerializeField, Min(0.02f)] private float tickInterval = 0.1f;
    [Tooltip("Damage dealt to each enemy in the beam every tick. 1 per 0.1 s = 10 damage per second.")]
    [SerializeField, Min(0f)] private float damagePerTick = 1f;
    [Tooltip("The player moves at this fraction of normal speed while the beam is on.")]
    [SerializeField, Range(0.1f, 1f)] private float moveSpeedWhileFiring = 0.5f;
    [SerializeField] private LayerMask hitLayers = ~0;

    [Header("Visuals")]
    [Tooltip("Needs two LineRenderers plus children named Muzzle and Impact. See Assets/Prefabs/InfernoBeam.")]
    [SerializeField] private GameObject beamPrefab;

    private readonly RaycastHit[] rayBuffer = new RaycastHit[32];
    private readonly RaycastHit[] sphereBuffer = new RaycastHit[32];
    private readonly Collider[] overlapBuffer = new Collider[16];
    private readonly HashSet<Health> damagedThisTick = new HashSet<Health>();

    private bool beaming;
    private float elapsed;
    private float nextTickTime;
    private GameObject beam;
    private LineRenderer[] lines;
    private float[] lineBaseWidths;
    private Transform muzzle;
    private Transform impact;

    public override bool IsUltimate => true;

    protected override bool CanCast()
    {
        return Caster.UltimateReady && !Caster.IsChanneling;
    }

    protected override void Cast()
    {
        if (!Caster.ConsumeUltimate())
        {
            return;
        }

        beaming = true;
        elapsed = 0f;
        nextTickTime = 0f;
        Caster.IsChanneling = true;
        if (Caster.Movement != null)
        {
            Caster.Movement.SpeedMultiplier = moveSpeedWhileFiring;
        }

        SpawnBeamVisual();

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.TriggerMusicStinger();
        }
    }

    protected override void Update()
    {
        base.Update();
        if (!beaming)
        {
            return;
        }

        elapsed += Time.deltaTime;
        float timeLeft = duration - elapsed;

        Vector3 origin = Caster.GetFireOrigin();
        Vector3 direction = Caster.GetFireDirection(origin);
        float length = ObstacleDistance(origin, direction);

        // Ticks are counted against the beam's own clock, so slow frames don't make it weaker.
        // (Capped at 3 per frame so a long hitch can't cause a burst of damage.)
        int ticks = 0;
        while (elapsed >= nextTickTime && ticks < 3)
        {
            nextTickTime += tickInterval;
            DamageAlongBeam(origin, direction, length);
            ticks++;
        }

        UpdateVisual(origin, direction, length, timeLeft);

        if (timeLeft <= 0f)
        {
            StopBeam();
        }
    }

    private void OnDisable()
    {
        if (beaming)
        {
            StopBeam();
        }
    }

    // ---------- Damage ----------

    // Distance to the first solid thing that isn't an enemy or the player.
    private float ObstacleDistance(Vector3 origin, Vector3 direction)
    {
        int count = Physics.RaycastNonAlloc(origin, direction, rayBuffer, maxLength, hitLayers, QueryTriggerInteraction.Ignore);
        float nearest = maxLength;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = rayBuffer[i];
            if (hit.distance >= nearest || hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<Health>() != null)
            {
                continue;
            }
            nearest = hit.distance;
        }
        return nearest;
    }

    private void DamageAlongBeam(Vector3 origin, Vector3 direction, float length)
    {
        damagedThisTick.Clear();

        // A sphere cast skips anything already touching its starting sphere, so check the area around the
        // fire point separately: this catches enemies right in the player's face.
        int overlaps = Physics.OverlapSphereNonAlloc(origin, radius, overlapBuffer, hitLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps; i++)
        {
            Damage(overlapBuffer[i]);
        }

        int hits = Physics.SphereCastNonAlloc(origin, radius, direction, sphereBuffer, length, hitLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits; i++)
        {
            Damage(sphereBuffer[i].collider);
        }
    }

    private void Damage(Collider other)
    {
        if (other == null || other.transform.IsChildOf(transform))
        {
            return;
        }

        Health health = other.GetComponentInParent<Health>();
        if (health != null && damagedThisTick.Add(health))
        {
            health.TakeDamage(damagePerTick);
        }
    }

    // ---------- Visuals ----------

    private void SpawnBeamVisual()
    {
        if (beamPrefab == null)
        {
            return;
        }

        beam = Instantiate(beamPrefab);
        lines = beam.GetComponentsInChildren<LineRenderer>();
        lineBaseWidths = new float[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            lineBaseWidths[i] = lines[i].widthMultiplier;
        }
        muzzle = beam.transform.Find("Muzzle");
        impact = beam.transform.Find("Impact");
    }

    private void UpdateVisual(Vector3 origin, Vector3 direction, float length, float timeLeft)
    {
        if (beam == null)
        {
            return;
        }

        Vector3 end = origin + direction * length;

        // Swell in quickly, shrink away at the end, and flicker slightly in between.
        float fade = Mathf.Clamp01(Mathf.Min(elapsed / 0.15f, timeLeft / 0.3f));
        float flicker = 1f + 0.12f * Mathf.Sin(Time.time * 45f);

        for (int i = 0; i < lines.Length; i++)
        {
            lines[i].SetPosition(0, origin);
            lines[i].SetPosition(1, end);
            lines[i].widthMultiplier = lineBaseWidths[i] * fade * flicker;
        }

        if (muzzle != null)
        {
            muzzle.position = origin;
            muzzle.rotation = Quaternion.LookRotation(direction);
        }
        if (impact != null)
        {
            impact.position = end;
            impact.rotation = Quaternion.LookRotation(-direction);
        }
    }

    private void StopBeam()
    {
        beaming = false;
        Caster.IsChanneling = false;
        if (Caster.Movement != null)
        {
            Caster.Movement.SpeedMultiplier = 1f;
        }

        if (beam == null)
        {
            return;
        }

        // Hide the lines and light now, let the sparks and flames drift away, then clean up.
        foreach (LineRenderer line in lines)
        {
            line.enabled = false;
        }
        foreach (Light light in beam.GetComponentsInChildren<Light>())
        {
            light.enabled = false;
        }
        foreach (ParticleSystem system in beam.GetComponentsInChildren<ParticleSystem>())
        {
            system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        Destroy(beam, 1f);
        beam = null;
    }
}
