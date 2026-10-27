// ExplosionZone.cs  —  CPS 499 HW#2 Roller Coaster Kit
// Trigger zones that react when the cart drives through them.
// How to use any of them:
//   1. Create an empty GameObject where the effect should happen.
//   2. Add a Box Collider, tick "Is Trigger", make the box big enough for the cart to pass through.
//   3. Add one of these scripts and fill in the sound / settings.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// When the cart enters, something blows up: fireball, smoke, sparks, flash, camera shake,
/// a sound, and every Rigidbody nearby is thrown by AddExplosionForce (Unity physics!).
/// Objects are put back after 'rearmTime' so it works on every lap.
/// </summary>
public class ExplosionZone : MonoBehaviour
{
    [Tooltip("Where the explosion happens (e.g. a barrel stack). Empty = this object")] public Transform explosionPoint;
    public float delay = 0.25f;
    public float size = 1.5f;
    public float force = 900f;
    public float radius = 12f;
    public float upwardsModifier = 2f;
    public AudioClip explosionSound;
    [Range(0, 1)] public float volume = 1f;
    [Tooltip("Optional: your own explosion prefab")] public GameObject customExplosionPrefab;
    [Tooltip("Objects hidden when it explodes (e.g. the intact barrel)")] public GameObject[] hideOnExplode;
    [Tooltip("Objects shown when it explodes (e.g. fire that keeps burning)")] public GameObject[] showOnExplode;
    [Tooltip("Optional animation to play (Animator trigger name below)")] public Animator animator;
    public string animatorTrigger = "Explode";
    public float cameraShake = 0.6f;
    public float rearmTime = 8f;

    class Saved { public Rigidbody rb; public Vector3 pos; public Quaternion rot; public bool kinematic; }
    readonly List<Saved> saved = new List<Saved>();
    bool armed = true;

    void Reset() { CartDetect.EnsureTrigger(gameObject); }
    void Awake() { CartDetect.EnsureTrigger(gameObject); }

    void OnTriggerEnter(Collider other)
    {
        if (!armed || !CartDetect.IsCart(other)) return;
        StartCoroutine(Boom());
    }

    IEnumerator Boom()
    {
        armed = false;
        yield return new WaitForSeconds(delay);
        Vector3 p = explosionPoint != null ? explosionPoint.position : transform.position;

        if (customExplosionPrefab != null) Destroy(Instantiate(customExplosionPrefab, p, Quaternion.identity), 8f);
        else FxFactory.CreateExplosion(p, size);
        CartDetect.Play(explosionSound, p, volume);
        RideCamera.Shake(cameraShake, 0.7f);
        if (animator != null && !string.IsNullOrEmpty(animatorTrigger)) animator.SetTrigger(animatorTrigger);
        foreach (var g in hideOnExplode) if (g != null) g.SetActive(false);
        foreach (var g in showOnExplode) if (g != null) g.SetActive(true);

        // PHYSICS: push every non-cart rigidbody in range
        saved.Clear();
        var seen = new HashSet<Rigidbody>();
        foreach (var c in Physics.OverlapSphere(p, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var rb = c.attachedRigidbody;
            if (rb == null || seen.Contains(rb) || CartDetect.IsCart(c)) continue;
            seen.Add(rb);
            saved.Add(new Saved { rb = rb, pos = rb.position, rot = rb.rotation, kinematic = rb.isKinematic });
            rb.isKinematic = false;
            rb.AddExplosionForce(force, p, radius, upwardsModifier, ForceMode.Impulse);
        }

        yield return new WaitForSeconds(rearmTime);
        // put everything back for the next lap
        foreach (var s in saved)
        {
            if (s.rb == null) continue;
            s.rb.linearVelocity = Vector3.zero; s.rb.angularVelocity = Vector3.zero;
            s.rb.position = s.pos; s.rb.rotation = s.rot;
            s.rb.transform.SetPositionAndRotation(s.pos, s.rot);
            s.rb.isKinematic = s.kinematic;
        }
        foreach (var g in hideOnExplode) if (g != null) g.SetActive(true);
        foreach (var g in showOnExplode) if (g != null) g.SetActive(false);
        armed = true;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.4f);
        Gizmos.DrawWireSphere(explosionPoint != null ? explosionPoint.position : transform.position, radius);
    }
}
