using UnityEngine;

/// <summary>
/// Draws a crosshair in the centre of the screen. It turns red while aiming at something that can take damage.
/// </summary>
[RequireComponent(typeof(PlayerSkills))]
public class Crosshair : MonoBehaviour
{
    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.9f);
    [SerializeField] private Color enemyColor = new Color(1f, 0.25f, 0.2f, 1f);
    [SerializeField, Min(1f)] private float thickness = 2f;
    [Tooltip("Empty space between the centre and the start of each line.")]
    [SerializeField, Min(0f)] private float gap = 5f;
    [SerializeField, Min(1f)] private float length = 8f;

    private PlayerSkills skills;

    private void Awake()
    {
        skills = GetComponent<PlayerSkills>();
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        GUI.color = skills.AimingAtEnemy ? enemyColor : normalColor;
        Texture2D pixel = Texture2D.whiteTexture;
        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;
        float half = thickness * 0.5f;

        GUI.DrawTexture(new Rect(cx - half, cy - half, thickness, thickness), pixel);                       // centre dot
        GUI.DrawTexture(new Rect(cx - half, cy - gap - length, thickness, length), pixel);                  // up
        GUI.DrawTexture(new Rect(cx - half, cy + gap, thickness, length), pixel);                           // down
        GUI.DrawTexture(new Rect(cx - gap - length, cy - half, length, thickness), pixel);                  // left
        GUI.DrawTexture(new Rect(cx + gap, cy - half, length, thickness), pixel);                           // right
        GUI.color = Color.white;
    }
}
