// SplashZoneExtras.cs — makes the POND splashes (the kit's "Splash Zone") much more exciting:
//   * a big layered splash when the cart hits the water: shiny droplets flying in arcs, a white spray sheet,
//     a ring of water bursting out and mist hanging in the air,
//   * white WAKE SPRAY from both sides of the cart while it rides through the pond,
//   * WATER DROPS land on the camera lens (only when the camera is close - not on a far-away drone shot),
//   * a small camera shake.
//
// HOW TO USE: nothing to do! When you press Play it adds itself to every "Splash Zone" in your scene.
// To change the settings for one pond: select that pond's Splash Zone object > Add Component > Splash Zone Extras.
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(SplashZone))]
public class SplashZoneExtras : MonoBehaviour
{
    [Range(0.3f, 3f)] public float splashSize = 1.3f;
    [Tooltip("White spray from the sides of the cart while it is in the water")] public bool wakeSpray = true;
    [Header("Water drops on the camera")]
    public bool lensDrops = true;
    [Tooltip("Number of drops when the cart hits the water")] [Range(5, 80)] public int drops = 35;
    [Tooltip("Only put drops on the camera if it is closer than this to the splash (metres)")] public float cameraDistance = 25f;
    [Tooltip("Shake the camera when the cart hits the water")] public bool shake = true;

    SplashZone zone;
    readonly HashSet<Collider> inside = new HashSet<Collider>();
    float wakeTimer, dropTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAdd()
    {
        int n = 0;
        foreach (var z in FindObjectsByType<SplashZone>(FindObjectsSortMode.None))
            if (z.GetComponent<SplashZoneExtras>() == null) { z.gameObject.AddComponent<SplashZoneExtras>(); n++; }
        if (n > 0) Debug.Log($"[Splash Zone Extras] Bigger splashes + camera drops added to {n} pond(s).");
    }

    void Awake() { zone = GetComponent<SplashZone>(); }

    float WaterY => zone != null && zone.waterLevelOverride != 0f ? zone.waterLevelOverride : transform.position.y;

    void OnTriggerEnter(Collider other)
    {
        if (!CartDetect.IsCart(other)) return;
        bool first = inside.Count == 0;
        inside.Add(other);
        if (!first) return;                                  // one big splash per car group, not one per collider
        var car = other.transform;
        Vector3 at = new Vector3(car.position.x, WaterY, car.position.z);
        WaterFx.BigSplash(at, Vector3.up, car.forward, splashSize);
        if (shake) RideCamera.Shake(0.12f, 0.35f);
        if (lensDrops) LensDrops.SplashIfClose(at, cameraDistance, Mathf.RoundToInt(drops * Mathf.Sqrt(splashSize)), 1f);
        Debug.Log($"[Splash Zone Extras] Splash at '{name}'.");
    }

    void OnTriggerExit(Collider other) { inside.Remove(other); }

    void Update()
    {
        if (inside.Count == 0) return;
        inside.RemoveWhere(c => c == null || !c.gameObject.activeInHierarchy);
        wakeTimer -= Time.deltaTime; dropTimer -= Time.deltaTime;
        foreach (var c in inside)
        {
            var car = c.transform;
            Vector3 at = new Vector3(car.position.x, WaterY, car.position.z);
            if (wakeSpray && wakeTimer <= 0f) WaterFx.Wake(at, car.forward, car.right, splashSize);
            // spray keeps hitting the lens while an onboard / chase camera is in the water
            if (lensDrops && dropTimer <= 0f) LensDrops.SplashIfClose(at, cameraDistance * 0.5f, 3, 0.8f);
            break;                                           // the first car is enough
        }
        if (wakeTimer <= 0f) wakeTimer = 0.08f;
        if (dropTimer <= 0f) dropTimer = 0.35f;
    }
}

/// <summary>Shared water effects (splash, wake spray).</summary>
public static class WaterFx
{
    static Material soft, shiny;

    static void Mats()
    {
        if (soft == null) soft = PipelineUtil.MakeParticleMaterial(false, Tex(false));
        if (shiny == null) shiny = PipelineUtil.MakeParticleMaterial(false, Tex(true));
    }

    /// <summary>A big layered splash: droplets in arcs, a spray sheet, a ring and mist.</summary>
    public static void BigSplash(Vector3 at, Vector3 up, Vector3 fwd, float s)
    {
        Mats();
        var root = new GameObject("Water Splash");
        root.transform.position = at;
        Object.Destroy(root, 5f);
        fwd = Vector3.ProjectOnPlane(fwd, up).normalized;
        Burst(root.transform, shiny, "Droplets", (int)(380 * s), new Vector2(5f, 12f) * Mathf.Sqrt(s), new Vector2(0.05f, 0.17f), new Vector2(0.7f, 1.5f),
              1.3f, ParticleSystemShapeType.Cone, 50f, Quaternion.LookRotation(up + fwd * 0.5f), new Color(0.85f, 0.95f, 1f, 1f), true, 1.2f);
        Burst(root.transform, soft, "Spray Sheet", (int)(130 * s), new Vector2(4f, 9f), new Vector2(0.5f, 1.3f) * s, new Vector2(0.5f, 1f),
              1.1f, ParticleSystemShapeType.Cone, 70f, Quaternion.LookRotation(up), new Color(0.95f, 0.98f, 1f, 0.7f), false, 1.2f);
        Burst(root.transform, soft, "Splash Ring", (int)(90 * s), new Vector2(6f, 10f), new Vector2(0.4f, 0.9f) * s, new Vector2(0.4f, 0.8f),
              0.9f, ParticleSystemShapeType.Circle, 0f, Quaternion.LookRotation(up) * Quaternion.Euler(90f, 0f, 0f), new Color(0.9f, 0.97f, 1f, 0.8f), false, 1.2f);
        Burst(root.transform, soft, "Mist", (int)(22 * s), new Vector2(0.5f, 2f), new Vector2(2.5f, 5f) * s, new Vector2(1.5f, 3f),
              -0.05f, ParticleSystemShapeType.Sphere, 0f, Quaternion.identity, new Color(1f, 1f, 1f, 0.28f), false, 1.2f);
    }

    /// <summary>A small burst of white spray thrown out to both sides (call it every few frames).</summary>
    public static void Wake(Vector3 at, Vector3 fwd, Vector3 right, float s)
    {
        Mats();
        var root = new GameObject("Wake Spray");
        root.transform.position = at;
        Object.Destroy(root, 2f);
        Vector3 back = -Vector3.ProjectOnPlane(fwd, Vector3.up).normalized;
        right = Vector3.ProjectOnPlane(right, Vector3.up).normalized;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 dir = (right * side + Vector3.up * 0.9f + back * 0.4f).normalized;
            var t = new GameObject("Side").transform;
            t.SetParent(root.transform, false);
            t.position = at + right * side * 0.8f;
            Burst(t, shiny, "Wake Droplets", (int)(28 * s), new Vector2(3f, 7f), new Vector2(0.05f, 0.14f), new Vector2(0.5f, 1f),
                  1.2f, ParticleSystemShapeType.Cone, 20f, Quaternion.LookRotation(dir), new Color(0.88f, 0.96f, 1f, 1f), true, 0.3f);
            Burst(t, soft, "Wake Foam", (int)(5 * s), new Vector2(1.5f, 3f), new Vector2(0.4f, 0.9f), new Vector2(0.5f, 0.9f),
                  0.8f, ParticleSystemShapeType.Cone, 25f, Quaternion.LookRotation(dir), new Color(1f, 1f, 1f, 0.55f), false, 0.3f);
        }
    }

    static void Burst(Transform parent, Material mat, string name, int count, Vector2 speed, Vector2 size, Vector2 life, float gravity,
                      ParticleSystemShapeType shape, float angle, Quaternion rot, Color col, bool stretch, float radius)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.rotation = rot;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.loop = false; m.playOnAwake = false; m.duration = 0.2f; m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        m.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        m.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        m.startColor = col; m.gravityModifier = gravity; m.maxParticles = 4000;
        var e = ps.emission; e.rateOverTime = 0f;
        var sh = ps.shape; sh.shapeType = shape; sh.angle = angle; sh.radius = radius;
        var c = ps.colorOverLifetime; c.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
        c.color = new ParticleSystem.MinMaxGradient(g);
        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, stretch ? 0.6f : 1.8f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        if (stretch) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 1.2f; }
        ps.Emit(count);
    }

    static Texture2D Tex(bool shinyCore)
    {
        const int N = 48;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r); a *= a;
                if (shinyCore) a *= 0.5f + 0.5f * Mathf.Clamp01(1f - r * 2f);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply();
        return t;
    }
}
