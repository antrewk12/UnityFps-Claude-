using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs endless waves of enemies. Every wave picks one random point on the map, marks it with a beacon
/// for a few seconds, then spawns the whole wave in a cluster around it.
/// </summary>
public class WaveSpawner : MonoBehaviour
{
    private const string PlayerTag = "Player";

    [System.Serializable]
    public class WaveEntry
    {
        public GameObject prefab;
        [Min(1)] public int count = 1;
    }

    [System.Serializable]
    public class Wave
    {
        public WaveEntry[] entries;
    }

    private enum State { Waiting, Warning, Spawning, InProgress }

    [Header("Waves")]
    [SerializeField] private Wave[] waves;
    [Tooltip("After the last hand-made wave, it repeats with this much extra enemy count per wave (0.3 = +30%).")]
    [SerializeField, Min(0f)] private float endlessGrowth = 0.3f;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float initialDelay = 5f;
    [Tooltip("Seconds between clearing a wave and the next wave's warning.")]
    [SerializeField, Min(0f)] private float timeBetweenWaves = 6f;
    [Tooltip("How long the beacon shows before the enemies appear.")]
    [SerializeField, Min(0f)] private float warningTime = 4f;
    [SerializeField, Min(0f)] private float spawnInterval = 0.2f;

    [Header("Spawn Point Rules")]
    [SerializeField, Min(0f)] private float minDistanceFromPlayer = 25f;
    [Tooltip("Keeps waves from spawning so far away that the slow enemies take a minute to arrive. 0 = anywhere on the map.")]
    [SerializeField, Min(0f)] private float maxDistanceFromPlayer = 70f;
    [Tooltip("Keep this far from the terrain edge.")]
    [SerializeField, Min(0f)] private float mapMargin = 8f;
    [Tooltip("Ground steeper than this (degrees) is rejected as a spawn point.")]
    [SerializeField, Range(0f, 60f)] private float maxSpawnSteepness = 20f;
    [Tooltip("The straight line to the player can't cross ground steeper than this, since enemies can't climb it.")]
    [SerializeField, Range(0f, 60f)] private float maxPathSteepness = 35f;
    [SerializeField, Min(0.5f)] private float clusterRadius = 3f;
    [SerializeField, Min(0f)] private float enemySpacing = 1.8f;

    [Header("Warning Beacon")]
    [SerializeField] private Material beaconMaterial;

    private readonly List<GameObject> alive = new List<GameObject>();
    private Transform player;
    private Terrain terrain;
    private Transform enemyParent;
    private GameObject beacon;
    private State state = State.Waiting;
    private float stateEndTime;
    private int waveNumber = 1;
    private int waveTotal;
    private Vector3 spawnCenter;
    private GUIStyle labelStyle;

    private void Start()
    {
        terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            Debug.LogError("WaveSpawner needs a Terrain in the scene to choose spawn points.");
            enabled = false;
            return;
        }

        enemyParent = new GameObject("Spawned Enemies").transform;
        StartCoroutine(RunWaves());
    }

    private void Update()
    {
        alive.RemoveAll(enemy => enemy == null);
        UpdateMusic();
    }

    // The music follows the action: calm between waves, building during the warning, full band while
    // the wave is alive, and easing back down as enemies die.
    private void UpdateMusic()
    {
        AudioManager audioManager = AudioManager.Instance;
        if (audioManager == null)
        {
            return;
        }

        float intensity;
        switch (state)
        {
            case State.Waiting:
                intensity = 0.12f;
                break;
            case State.Warning:
                float progress = warningTime > 0f ? 1f - Mathf.Clamp01((stateEndTime - Time.time) / warningTime) : 1f;
                intensity = Mathf.Lerp(0.3f, 0.55f, progress);
                break;
            default:
                float aliveFraction = waveTotal > 0 ? Mathf.Clamp01(alive.Count / (float)waveTotal) : 1f;
                intensity = Mathf.Lerp(0.55f, 1f, aliveFraction);
                break;
        }

        audioManager.SetMusicIntensity(intensity);
    }

    // ---------- Wave loop ----------

    private IEnumerator RunWaves()
    {
        while (player == null)
        {
            GameObject found = GameObject.FindWithTag(PlayerTag);
            if (found != null)
            {
                player = found.transform;
            }
            yield return null;
        }

        state = State.Waiting;
        stateEndTime = Time.time + initialDelay;
        yield return new WaitForSeconds(initialDelay);

        while (true)
        {
            Wave wave = GetWave(waveNumber - 1);

            spawnCenter = PickSpawnCenter();
            ShowBeacon(spawnCenter);
            state = State.Warning;
            stateEndTime = Time.time + warningTime;
            yield return new WaitForSeconds(warningTime);
            HideBeacon();

            state = State.Spawning;
            yield return SpawnWave(wave, spawnCenter);

            state = State.InProgress;
            while (alive.Count > 0)
            {
                yield return null;
            }

            waveNumber++;
            state = State.Waiting;
            stateEndTime = Time.time + timeBetweenWaves;
            yield return new WaitForSeconds(timeBetweenWaves);
        }
    }

    private Wave GetWave(int index)
    {
        if (waves == null || waves.Length == 0)
        {
            return new Wave { entries = new WaveEntry[0] };
        }

        if (index < waves.Length)
        {
            return waves[index];
        }

        // Past the hand-made waves: repeat the last one, a bit bigger each time.
        Wave last = waves[waves.Length - 1];
        float factor = 1f + endlessGrowth * (index - (waves.Length - 1));
        Wave scaled = new Wave { entries = new WaveEntry[last.entries.Length] };
        for (int i = 0; i < last.entries.Length; i++)
        {
            scaled.entries[i] = new WaveEntry
            {
                prefab = last.entries[i].prefab,
                count = Mathf.CeilToInt(last.entries[i].count * factor)
            };
        }
        return scaled;
    }

    private IEnumerator SpawnWave(Wave wave, Vector3 center)
    {
        List<GameObject> queue = new List<GameObject>();
        foreach (WaveEntry entry in wave.entries)
        {
            if (entry.prefab == null)
            {
                continue;
            }
            for (int i = 0; i < entry.count; i++)
            {
                queue.Add(entry.prefab);
            }
        }

        // Shuffle so the cluster mixes enemy types instead of spawning all of one kind first.
        for (int i = queue.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (queue[i], queue[j]) = (queue[j], queue[i]);
        }

        waveTotal = queue.Count;
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.TriggerMusicStinger();
        }

        float radius = clusterRadius + Mathf.Sqrt(queue.Count) * 0.6f;
        List<Vector3> used = new List<Vector3>();

        foreach (GameObject prefab in queue)
        {
            Vector3 position = FindClusterPosition(center, radius, used);
            used.Add(position);

            Vector3 toPlayer = player.position - position;
            toPlayer.y = 0f;
            Quaternion rotation = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;

            alive.Add(Instantiate(prefab, position, rotation, enemyParent));
            yield return new WaitForSeconds(spawnInterval);
        }
    }

    // ---------- Choosing where to spawn ----------

    /// <summary>Picks a random spot on the map that is flat, clear, far from the player, and walkable to them.</summary>
    public Vector3 PickSpawnCenter()
    {
        EnsureReferences();
        Vector3 size = terrain.terrainData.size;
        Vector3 origin = terrain.transform.position;
        Vector3 playerPos = player.position;

        // Second pass relaxes the rules so a wave always spawns somewhere.
        for (int pass = 0; pass < 2; pass++)
        {
            float minDistance = pass == 0 ? minDistanceFromPlayer : minDistanceFromPlayer * 0.5f;
            for (int attempt = 0; attempt < 200; attempt++)
            {
                float x = Random.Range(origin.x + mapMargin, origin.x + size.x - mapMargin);
                float z = Random.Range(origin.z + mapMargin, origin.z + size.z - mapMargin);
                Vector3 point = GroundAt(x, z);

                float distance = HorizontalDistance(point, playerPos);
                if (distance < minDistance
                    || (maxDistanceFromPlayer > 0f && distance > maxDistanceFromPlayer)
                    || SteepnessAt(point) > maxSpawnSteepness
                    || IsBlocked(point)
                    || (pass == 0 && !IsPathWalkable(point, playerPos)))
                {
                    continue;
                }

                return point;
            }
        }

        Debug.LogWarning("WaveSpawner couldn't find a valid spawn point; spawning in front of the player.");
        Vector3 flat = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized * 15f;
        return GroundAt(playerPos.x + flat.x, playerPos.z + flat.z);
    }

    private Vector3 FindClusterPosition(Vector3 center, float radius, List<Vector3> used)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 point = GroundAt(center.x + offset.x, center.z + offset.y);

            if (SteepnessAt(point) > maxSpawnSteepness + 10f || IsBlocked(point) || IsTooCloseToOthers(point, used))
            {
                continue;
            }

            return point;
        }

        // Crowded or rough ground: fall back to a small jitter around the centre.
        Vector2 jitter = Random.insideUnitCircle * 1.5f;
        return GroundAt(center.x + jitter.x, center.z + jitter.y);
    }

    private bool IsTooCloseToOthers(Vector3 point, List<Vector3> used)
    {
        foreach (Vector3 other in used)
        {
            if (HorizontalDistance(point, other) < enemySpacing)
            {
                return true;
            }
        }
        return false;
    }

    // Enemies walk in a straight line, so the straight line has to be walkable.
    private bool IsPathWalkable(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        delta.y = 0f;
        int steps = Mathf.CeilToInt(delta.magnitude / 2f);

        for (int i = 1; i < steps; i++)
        {
            Vector3 sample = from + delta * (i / (float)steps);
            if (SteepnessAt(sample) > maxPathSteepness)
            {
                return false;
            }
        }
        return true;
    }

    // A sphere at body height catches tree trunks and other enemies; ground sits below it on gentle slopes.
    private static bool IsBlocked(Vector3 point)
    {
        return Physics.CheckSphere(point + Vector3.up * 1.4f, 0.9f, ~0, QueryTriggerInteraction.Ignore);
    }

    private Vector3 GroundAt(float x, float z)
    {
        float y = terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
        return new Vector3(x, y + 0.05f, z);
    }

    private float SteepnessAt(Vector3 world)
    {
        Vector3 size = terrain.terrainData.size;
        Vector3 local = world - terrain.transform.position;
        return terrain.terrainData.GetSteepness(Mathf.Clamp01(local.x / size.x), Mathf.Clamp01(local.z / size.z));
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void EnsureReferences()
    {
        if (terrain == null)
        {
            terrain = Terrain.activeTerrain;
        }
        if (player == null)
        {
            GameObject found = GameObject.FindWithTag(PlayerTag);
            player = found != null ? found.transform : null;
        }
    }

    // ---------- Beacon ----------

    private void ShowBeacon(Vector3 position)
    {
        if (beaconMaterial == null)
        {
            return;
        }

        beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beacon.name = "SpawnBeacon";
        Destroy(beacon.GetComponent<Collider>());
        beacon.GetComponent<MeshRenderer>().sharedMaterial = beaconMaterial;
        // The cylinder primitive is 2 units tall, so y scale 20 gives a 40 m beam standing on the ground.
        beacon.transform.position = position + Vector3.up * 20f;
        beacon.transform.localScale = new Vector3(0.5f, 20f, 0.5f);
    }

    private void HideBeacon()
    {
        if (beacon != null)
        {
            Destroy(beacon);
        }
    }

    // ---------- HUD ----------

    private void OnGUI()
    {
        if (player == null)
        {
            return;
        }

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = 26,
                fontStyle = FontStyle.Bold
            };
        }

        string title;
        string detail;
        float remaining = Mathf.Max(0f, stateEndTime - Time.time);

        switch (state)
        {
            case State.Waiting:
                title = waveNumber == 1 ? "Get ready" : $"Wave {waveNumber - 1} cleared!";
                detail = $"Wave {waveNumber} starts in {Mathf.CeilToInt(remaining)}";
                break;
            case State.Warning:
                title = $"Wave {waveNumber} incoming";
                detail = $"{DescribeDirection(spawnCenter)}  -  {Mathf.RoundToInt(HorizontalDistance(spawnCenter, player.position))} m  -  {Mathf.CeilToInt(remaining)}";
                break;
            default:
                title = $"Wave {waveNumber}";
                detail = $"Enemies left: {alive.Count}";
                break;
        }

        DrawShadowedLabel(new Rect(0f, 20f, Screen.width, 40f), title, 26);
        DrawShadowedLabel(new Rect(0f, 56f, Screen.width, 30f), detail, 18);
    }

    private void DrawShadowedLabel(Rect rect, string text, int fontSize)
    {
        labelStyle.fontSize = fontSize;
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, labelStyle);
        GUI.color = Color.white;
        GUI.Label(rect, text, labelStyle);
    }

    // Direction relative to where the player is facing, since a compass means nothing in first person.
    private string DescribeDirection(Vector3 target)
    {
        Vector3 to = target - player.position;
        to.y = 0f;
        float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(player.forward, Vector3.up), to, Vector3.up);

        string[] names = { "ahead", "ahead right", "to your right", "behind right", "behind you", "behind left", "to your left", "ahead left" };
        int index = Mathf.RoundToInt(Mathf.Repeat(angle, 360f) / 45f) % 8;
        return names[index];
    }
}
