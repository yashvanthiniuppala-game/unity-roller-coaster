// SplashZone.cs  —  CPS 499 HW#2 Roller Coaster Kit
// Trigger zones that react when the cart drives through them.
// How to use any of them:
//   1. Create an empty GameObject where the effect should happen.
//   2. Add a Box Collider, tick "Is Trigger", make the box big enough for the cart to pass through.
//   3. Add one of these scripts and fill in the sound / settings.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Put on a trigger box at a pond. The cart makes a splash + sound when it passes.</summary>
public class SplashZone : MonoBehaviour
{
    [Tooltip("Optional: your own splash Particle System prefab")] public ParticleSystem customSplashPrefab;
    public AudioClip splashSound;
    [Range(0, 1)] public float volume = 1f;
    [Tooltip("Water surface height. Leave 0 to use this object's Y position")] public float waterLevelOverride = 0f;
    public float splashSize = 1.2f;
    [Tooltip("Keep spraying while the cart is inside the zone")] public bool sprayWhileInside = true;

    readonly HashSet<Collider> inside = new HashSet<Collider>();
    float sprayTimer;

    void Reset() { CartDetect.EnsureTrigger(gameObject); }
    void Awake() { CartDetect.EnsureTrigger(gameObject); }

    float WaterY => waterLevelOverride != 0f ? waterLevelOverride : transform.position.y;

    void OnTriggerEnter(Collider other)
    {
        if (!CartDetect.IsCart(other)) return;
        inside.Add(other);
        Splash(other.transform.position, splashSize * 1.4f);
        CartDetect.Play(splashSound, other.transform.position, volume);
    }

    void OnTriggerExit(Collider other)
    {
        if (inside.Remove(other)) Splash(other.transform.position, splashSize);
    }

    void Update()
    {
        if (!sprayWhileInside || inside.Count == 0) return;
        sprayTimer -= Time.deltaTime;
        if (sprayTimer > 0f) return;
        sprayTimer = 0.12f;
        inside.RemoveWhere(c => c == null);
        foreach (var c in inside) Splash(c.transform.position, splashSize * 0.6f);
    }

    void Splash(Vector3 at, float size)
    {
        Vector3 p = new Vector3(at.x, WaterY, at.z);
        if (customSplashPrefab != null)
        {
            var ps = Instantiate(customSplashPrefab, p, Quaternion.identity);
            ps.Play();
            Destroy(ps.gameObject, 5f);
        }
        else FxFactory.CreateSplash(p, size);
    }
}
