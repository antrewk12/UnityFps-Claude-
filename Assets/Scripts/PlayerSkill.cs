using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>A key or mouse button that triggers a skill. Editable in the Inspector.</summary>
[System.Serializable]
public class SkillBinding
{
    public enum Device { Mouse, Keyboard }

    [SerializeField] private Device device = Device.Keyboard;
    [Tooltip("0 = left, 1 = right, 2 = middle. Only used when Device is Mouse.")]
    [SerializeField] private int mouseButton;
    [Tooltip("Only used when Device is Keyboard.")]
    [SerializeField] private Key key = Key.E;

    public bool WasPressed()
    {
        if (device == Device.Mouse)
        {
            Mouse mouse = Mouse.current;
            return mouse != null && GetMouseButton(mouse).wasPressedThisFrame;
        }

        Keyboard keyboard = Keyboard.current;
        return keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame;
    }

    /// <summary>Short text for the HUD, e.g. "LMB", "E", "F1".</summary>
    public string Label
    {
        get
        {
            if (device == Device.Mouse)
            {
                return mouseButton == 1 ? "RMB" : (mouseButton == 2 ? "MMB" : "LMB");
            }
            return key.ToString();
        }
    }

    private ButtonControl GetMouseButton(Mouse mouse)
    {
        return mouseButton == 1 ? mouse.rightButton : (mouseButton == 2 ? mouse.middleButton : mouse.leftButton);
    }
}

/// <summary>
/// Base class for a player skill: a name, a key, a cooldown, and a <see cref="Cast"/> that does the work.
/// To add a skill, derive from this, override Cast, and add the component to the Player.
/// </summary>
[RequireComponent(typeof(PlayerSkills))]
public abstract class PlayerSkill : MonoBehaviour
{
    [SerializeField] private string displayName = "Skill";
    [SerializeField] private SkillBinding binding = new SkillBinding();
    [SerializeField, Min(0f)] private float cooldown = 1f;

    private float readyTime;
    private PlayerSkills caster;

    protected PlayerSkills Caster => caster != null ? caster : (caster = GetComponent<PlayerSkills>());

    public string DisplayName => displayName;
    public string BindingLabel => binding.Label;
    public float CooldownRemaining => Mathf.Max(0f, readyTime - Time.time);
    public float CooldownFraction => cooldown > 0f ? Mathf.Clamp01(CooldownRemaining / cooldown) : 0f;

    /// <summary>True for skills that are gated by the charge meter instead of (or as well as) a cooldown.</summary>
    public virtual bool IsUltimate => false;

    protected virtual void Update()
    {
        if (!Caster.InputBlocked && binding.WasPressed())
        {
            TryCast();
        }
    }

    /// <summary>
    /// Casts the skill if it's off cooldown and allowed. Input calls this, and so can anything else
    /// (a tutorial, an AI, a gamepad button). Returns whether it cast.
    /// </summary>
    public bool TryCast()
    {
        if (Caster.IsChanneling || Time.time < readyTime || !CanCast())
        {
            return false;
        }

        readyTime = Time.time + cooldown;
        Cast();
        return true;
    }

    /// <summary>Extra conditions beyond the cooldown, e.g. a full charge meter.</summary>
    protected virtual bool CanCast()
    {
        return true;
    }

    protected abstract void Cast();
}
