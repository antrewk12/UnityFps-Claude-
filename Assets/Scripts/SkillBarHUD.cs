using UnityEngine;

/// <summary>
/// Draws the skill bar along the bottom of the screen: one slot per skill with its key, a cooldown sweep,
/// and, for the ultimate, a charge meter that fills from the bottom and glows when it's ready.
/// </summary>
[RequireComponent(typeof(PlayerSkills))]
public class SkillBarHUD : MonoBehaviour
{
    [SerializeField, Min(32f)] private float slotSize = 72f;
    [SerializeField, Min(0f)] private float spacing = 12f;
    [SerializeField, Min(0f)] private float bottomMargin = 30f;

    private static readonly Color Background = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color CooldownShade = new Color(0f, 0f, 0f, 0.7f);
    private static readonly Color ReadyBorder = new Color(1f, 0.85f, 0.4f, 1f);
    private static readonly Color DimBorder = new Color(1f, 1f, 1f, 0.3f);
    private static readonly Color ChargeFill = new Color(1f, 0.45f, 0.1f, 0.85f);

    private PlayerSkills skills;
    private GUIStyle keyStyle;
    private GUIStyle centerStyle;
    private GUIStyle nameStyle;

    private void Awake()
    {
        skills = GetComponent<PlayerSkills>();
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || skills == null || skills.Skills.Count == 0)
        {
            return;
        }

        EnsureStyles();

        int count = skills.Skills.Count;
        float totalWidth = count * slotSize + (count - 1) * spacing;
        float x = (Screen.width - totalWidth) * 0.5f;
        float y = Screen.height - slotSize - bottomMargin;

        for (int i = 0; i < count; i++)
        {
            DrawSlot(new Rect(x + i * (slotSize + spacing), y, slotSize, slotSize), skills.Skills[i]);
        }
    }

    private void DrawSlot(Rect rect, PlayerSkill skill)
    {
        Fill(rect, Background);

        bool ready;
        string centerText = "";

        if (skill.IsUltimate)
        {
            float charge = skills.UltimateCharge01;
            ready = charge >= 1f;

            // Charge fills upward from the bottom edge.
            float height = rect.height * charge;
            Color fill = ready ? Color.Lerp(ChargeFill, ReadyBorder, 0.5f + 0.5f * Mathf.Sin(Time.time * 6f)) : ChargeFill;
            Fill(new Rect(rect.x, rect.yMax - height, rect.width, height), fill);
            centerText = ready ? "READY" : Mathf.FloorToInt(charge * 100f) + "%";
        }
        else
        {
            float remaining = skill.CooldownRemaining;
            ready = remaining <= 0f;

            // The shade shrinks downward as the cooldown runs out.
            Fill(new Rect(rect.x, rect.y, rect.width, rect.height * skill.CooldownFraction), CooldownShade);
            if (!ready)
            {
                centerText = remaining >= 10f ? Mathf.CeilToInt(remaining).ToString() : remaining.ToString("F1");
            }
        }

        DrawBorder(rect, ready ? ReadyBorder : DimBorder, ready ? 3f : 2f);

        GUI.Label(new Rect(rect.x + 5f, rect.y + 2f, rect.width, 20f), skill.BindingLabel, keyStyle);
        GUI.Label(rect, centerText, centerStyle);
        GUI.Label(new Rect(rect.x - 10f, rect.yMax + 2f, rect.width + 20f, 20f), skill.DisplayName, nameStyle);
    }

    private static void Fill(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private static void DrawBorder(Rect rect, Color color, float thickness)
    {
        Fill(new Rect(rect.x, rect.y, rect.width, thickness), color);
        Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        Fill(new Rect(rect.x, rect.y, thickness, rect.height), color);
        Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    private void EnsureStyles()
    {
        if (keyStyle != null)
        {
            return;
        }

        keyStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
        centerStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.UpperCenter };
    }
}
