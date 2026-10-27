// AnimationTriggerZone.cs  —  CPS 499 HW#2 Roller Coaster Kit
// Trigger zones that react when the cart drives through them.
// How to use any of them:
//   1. Create an empty GameObject where the effect should happen.
//   2. Add a Box Collider, tick "Is Trigger", make the box big enough for the cart to pass through.
//   3. Add one of these scripts and fill in the sound / settings.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Plays an animation (Animator trigger or legacy Animation) when the cart enters.</summary>
public class AnimationTriggerZone : MonoBehaviour
{
    public Animator animator;
    public string triggerName = "Play";
    [Tooltip("Alternative: an object with an Animation component + clip")] public Animation legacyAnimation;
    public AudioClip sound;
    [Range(0, 1)] public float volume = 1f;

    void Reset() { CartDetect.EnsureTrigger(gameObject); }
    void Awake() { CartDetect.EnsureTrigger(gameObject); }

    void OnTriggerEnter(Collider other)
    {
        if (!CartDetect.IsCart(other)) return;
        if (animator != null) animator.SetTrigger(triggerName);
        if (legacyAnimation != null) legacyAnimation.Play();
        CartDetect.Play(sound, transform.position, volume);
    }
}
