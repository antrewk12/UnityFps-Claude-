using UnityEngine;

/// <summary>
/// Shows the player's health as a bar in the bottom-left corner, flashes the screen red when hit,
/// and announces death.
/// </summary>
[RequireComponent(typeof(Health))]
public class PlayerHealthHUD : MonoBehaviour
{
    [SerializeField, Min(50f)] private float barWidth = 280f;
    [SerializeField, Min(8f)] private float barHeight = 26f;
    [SerializeField, Min(0f)] private float margin = 30f;

    [Header("Damage Flash")]
    [SerializeField, Min(0.05f)] private float flashDuration = 0.4f;
    [Tooltip("Strongest the red flash gets, at a big hit.")]
    [SerializeField, Range(0f, 1f)] private float flashMaxAlpha = 0.45f;
    [Tooltip("A hit this big (or bigger) gives the strongest flash.")]
    [SerializeField, Min(1f)] private float bigHitDamage = 25f;

    private static readonly Color BarBackground = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color FullHealth = new Color(0.35f, 0.85f, 0.3f, 0.95f);
    private static readonly Color LowHealth = new Color(0.9f, 0.2f, 0.15f, 0.95f);

    private Health health;
    private GUIStyle barStyle;
    private GUIStyle deathStyle;
    private float flashStart = float.NegativeInfinity;
    private float flashStrength;

    private void Awake()
    {
        health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        health.Damaged += OnDamaged;
    }

    private void OnDisable()
    {
        health.Damaged -= OnDamaged;
    }

    private void OnDamaged(float amount)
    {
        flashStart = Time.time;
        flashStrength = Mathf.Clamp(amount / bigHitDamage, 0.35f, 1f);
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        EnsureStyles();
        DrawFlash();
        DrawBar();

        if (health.IsDead)
        {
            GUI.Label(new Rect(0f, Screen.height * 0.35f, Screen.width, 80f), "YOU DIED", deathStyle);
        }
    }

    private void DrawFlash()
    {
        float age = Time.time - flashStart;
        if (age < 0f || age >= flashDuration)
        {
            return;
        }

        // Starts at full strength and fades out smoothly.
        float alpha = flashMaxAlpha * flashStrength * (1f - age / flashDuration);
        Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.8f, 0f, 0f, alpha));
    }

    private void DrawBar()
    {
        float fraction = health.MaxHealth > 0f ? Mathf.Clamp01(health.CurrentHealth / health.MaxHealth) : 0f;
        Rect bar = new Rect(margin, Screen.height - margin - barHeight, barWidth, barHeight);

        Fill(new Rect(bar.x - 3f, bar.y - 3f, bar.width + 6f, bar.height + 6f), BarBackground);
        Fill(new Rect(bar.x, bar.y, bar.width * fraction, bar.height), Color.Lerp(LowHealth, FullHealth, fraction));

        string text = Mathf.CeilToInt(health.CurrentHealth) + " / " + Mathf.CeilToInt(health.MaxHealth);
        GUI.Label(bar, text, barStyle);
    }

    private static void Fill(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void EnsureStyles()
    {
        if (barStyle != null)
        {
            return;
        }

        barStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        deathStyle = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        deathStyle.normal.textColor = new Color(0.9f, 0.15f, 0.1f, 1f);
    }
}
