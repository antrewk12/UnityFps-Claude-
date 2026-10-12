using UnityEngine;

/// <summary>
/// The one place every sound in the game goes through. It owns the background music and a pool of
/// sound-effect players, so no other script should create its own AudioSource.
///
///   AudioManager.Instance.PlaySfx(clip, position);        // 3D sound effect
///   AudioManager.Instance.PlaySfx2D(clip);                // UI / non-positional sound
///   AudioManager.Instance.SetMusicIntensity(0..1);        // calm -> full band
///   AudioManager.Instance.TriggerMusicStinger();          // short rising flourish
///
/// It survives scene loads. If a scene has none, the first call to Instance creates one.
/// </summary>
public class AudioManager : MonoBehaviour
{
    private static AudioManager instance;
    private static bool quitting;

    /// <summary>The single AudioManager. Created automatically if the scene doesn't have one.</summary>
    public static AudioManager Instance
    {
        get
        {
            if (instance == null && Application.isPlaying && !quitting)
            {
                instance = FindFirstObjectByType<AudioManager>();
                if (instance == null)
                {
                    new GameObject("AudioManager").AddComponent<AudioManager>();
                }
            }
            return instance;
        }
    }

    [Header("Volume")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.55f;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;
    [SerializeField] private bool muted;

    [Header("Music")]
    [SerializeField] private bool playMusicOnStart = true;
    [Tooltip("Where the music currently sits between calm (0) and full band (1). Set by gameplay code; shown here for debugging.")]
    [SerializeField, Range(0f, 1f)] private float musicIntensity = 0.1f;

    [Header("Sound Effects")]
    [SerializeField, Min(1)] private int sfxPoolSize = 16;
    [SerializeField, Min(0.1f)] private float sfxMinDistance = 3f;
    [SerializeField, Min(1f)] private float sfxMaxDistance = 60f;

    private ProceduralMusic music;
    private AudioSource musicSource;
    private AudioClip musicClip;
    private AudioSource[] sfxSources;
    private int nextSfxSource;

    public float MasterVolume { get => masterVolume; set => masterVolume = Mathf.Clamp01(value); }
    public float MusicVolume { get => musicVolume; set => musicVolume = Mathf.Clamp01(value); }
    public float SfxVolume { get => sfxVolume; set => sfxVolume = Mathf.Clamp01(value); }
    public bool Muted { get => muted; set => muted = value; }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        BuildSfxPool();
        BuildMusic();
    }

    private void Start()
    {
        if (playMusicOnStart)
        {
            PlayMusic();
        }
    }

    private void Update()
    {
        // Applied every frame so the Inspector sliders work live while playing.
        float master = muted ? 0f : masterVolume;
        if (musicSource != null)
        {
            musicSource.volume = master * musicVolume;
        }

        if (music != null)
        {
            music.SetIntensity(musicIntensity);
        }
    }

    private void OnApplicationQuit()
    {
        quitting = true;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
        if (musicClip != null)
        {
            Destroy(musicClip);
        }
    }

    // ---------- Music ----------

    private void BuildMusic()
    {
        int sampleRate = AudioSettings.outputSampleRate;
        music = new ProceduralMusic(sampleRate);

        GameObject host = new GameObject("Music");
        host.transform.SetParent(transform, false);
        musicSource = host.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        musicSource.priority = 0;      // lowest number = last to be cut when voices run out

        // A streaming clip asks the music generator for more audio whenever the sound card needs it.
        musicClip = AudioClip.Create("ProceduralMusic", sampleRate * 10, 2, sampleRate, true, OnMusicRead);
        musicSource.clip = musicClip;
    }

    // Runs on the audio thread: no Unity API calls in here.
    private void OnMusicRead(float[] data)
    {
        music.Render(data, data.Length / 2);
    }

    public void PlayMusic()
    {
        if (musicSource != null && !musicSource.isPlaying)
        {
            musicSource.Play();
        }
    }

    public void StopMusic()
    {
        if (musicSource != null)
        {
            musicSource.Stop();
        }
    }

    /// <summary>0 = calm and sparse, 1 = full band. The music fades between levels over a couple of seconds.</summary>
    public void SetMusicIntensity(float value)
    {
        musicIntensity = Mathf.Clamp01(value);
    }

    /// <summary>A short rising flourish, good for the start of a wave.</summary>
    public void TriggerMusicStinger()
    {
        if (music != null)
        {
            music.TriggerStinger();
        }
    }

    // ---------- Sound effects ----------

    private void BuildSfxPool()
    {
        sfxSources = new AudioSource[sfxPoolSize];
        Transform pool = new GameObject("SFX Pool").transform;
        pool.SetParent(transform, false);

        for (int i = 0; i < sfxPoolSize; i++)
        {
            AudioSource source = new GameObject("SFX " + i).AddComponent<AudioSource>();
            source.transform.SetParent(pool, false);
            source.playOnAwake = false;
            source.loop = false;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = sfxMinDistance;
            source.maxDistance = sfxMaxDistance;
            sfxSources[i] = source;
        }
    }

    /// <summary>Plays a sound effect at a position in the world.</summary>
    public AudioSource PlaySfx(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        return PlayOnPooledSource(clip, position, 1f, volume, pitch);
    }

    /// <summary>Plays a sound effect that doesn't come from anywhere, such as a UI click.</summary>
    public AudioSource PlaySfx2D(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        return PlayOnPooledSource(clip, Vector3.zero, 0f, volume, pitch);
    }

    private AudioSource PlayOnPooledSource(AudioClip clip, Vector3 position, float spatialBlend, float volume, float pitch)
    {
        if (clip == null || sfxSources == null)
        {
            return null;
        }

        AudioSource source = GetFreeSfxSource();
        source.transform.position = position;
        source.clip = clip;
        source.spatialBlend = spatialBlend;
        source.pitch = pitch;
        source.volume = (muted ? 0f : masterVolume) * sfxVolume * Mathf.Clamp01(volume);
        source.Play();
        return source;
    }

    // Prefer an idle source; if everything is busy, reuse them in rotation (cutting the oldest).
    private AudioSource GetFreeSfxSource()
    {
        for (int i = 0; i < sfxSources.Length; i++)
        {
            if (!sfxSources[i].isPlaying)
            {
                return sfxSources[i];
            }
        }

        AudioSource stolen = sfxSources[nextSfxSource];
        nextSfxSource = (nextSfxSource + 1) % sfxSources.Length;
        return stolen;
    }

    public void StopAllSfx()
    {
        if (sfxSources == null)
        {
            return;
        }

        foreach (AudioSource source in sfxSources)
        {
            source.Stop();
        }
    }
}
