// MeteorShower.cs — a METEOR SHOWER over the desert during the ride.
//   * when the cart climbs the lift hill, the sky turns to a red-orange sunset,
//   * burning meteors streak down from the sky with fire and smoke trails,
//   * they crash into the desert beside the track: explosion, scorched crater, burning embers, smoke, camera shake,
//   * the last one is a BIG meteor that lands close to the track,
//   * afterwards the sky goes back to normal; the craters are cleared for the next ride.
// Nothing ever lands ON the track.
//
// HOW TO USE:
//   1. GameObject > Create Empty, name it "Meteor Shower", Add Component > Meteor Shower. Press Play.
//      (By default it starts when the cart reaches the top half of the lift hill.)
//   2. Optional - make it look even better with PyroParticles:
//      drag a fire/fireball effect prefab into "Meteor Trail Prefab" and an explosion prefab into "Impact Prefab".
//      Sounds: "Whoosh Sound" (falling) and "Impact Sound" (boom).
using System.Collections.Generic;
using UnityEngine;

public class MeteorShower : MonoBehaviour
{
    [Header("When")]
    public CoasterTrack track;
    [Tooltip("ON = start on the lift hill.  OFF = start at 'Start Distance'.")] public bool startOnLiftHill = true;
    [Tooltip("Metres from the station (only if 'Start On Lift Hill' is off)")] public float startDistance = 60f;
    [Tooltip("How long the shower lasts (seconds)")] public float duration = 12f;
    [Range(3, 40)] public int meteors = 14;

    [Header("Where they land")]
    [Tooltip("Never closer to the rails than this (metres)")] public float trackClearance = 16f;
    [Tooltip("How far to the sides of the track they can land")] public float spread = 80f;
    public bool bigFinale = true;

    [Header("Look")]
    [Range(0.5f, 3f)] public float meteorSize = 1.3f;
    public Color glowColor = new Color(1f, 0.45f, 0.08f);
    [Tooltip("Optional: a rock material (e.g. Desert > Materials > Rock)")] public Material rockMaterial;
    [Tooltip("Optional: a fire / fireball effect prefab (PyroParticles) that follows each meteor")] public GameObject meteorTrailPrefab;
    [Tooltip("Optional: an explosion effect prefab (PyroParticles) for the impacts")] public GameObject impactPrefab;
    [Tooltip("Turn the sky into a red-orange sunset during the shower")] public bool sunsetSky = true;
    public Color sunsetSun = new Color(1f, 0.5f, 0.28f);
    public bool cameraShake = true;

    [Header("Sound (optional)")]
    public AudioClip whooshSound;
    public AudioClip impactSound;
    [Range(0, 1)] public float volume = 1f;

    class Meteor { public Transform t; public Vector3 from, to; public float time, flight; public bool big; }
    readonly List<Meteor> flying = new List<Meteor>();
    readonly List<GameObject> leftovers = new List<GameObject>();
    readonly List<Vector3> trackPts = new List<Vector3>();
    CoasterCart cart;
    bool running; float clock, nextSpawn; int spawned;
    float sky, skyTarget;
    Light sun; Color sunColor; float sunIntensity;
    Material skyCopy, skyOriginal; Color skyTint; bool hasTint;
    Material fireMat, smokeMat;

    void Start()
    {
        if (track == null) track = FindAnyObjectByType<CoasterTrack>();
        cart = FindAnyObjectByType<CoasterCart>();
        if (track != null)
        {
            if (track.Length < 1f) track.Build();
            for (float d = 0f; d < track.Length; d += 4f) { track.Sample(d, out Vector3 p, out _, out _); trackPts.Add(p); }
        }
        sun = RenderSettings.sun;
        if (sun == null) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
        if (sun != null) { sunColor = sun.color; sunIntensity = sun.intensity; }
        skyOriginal = RenderSettings.skybox;
        if (skyOriginal != null && skyOriginal.HasProperty("_Tint"))
        {
            skyCopy = new Material(skyOriginal); RenderSettings.skybox = skyCopy;
            skyTint = skyCopy.GetColor("_Tint"); hasTint = true;
        }
        fireMat = PipelineUtil.MakeParticleMaterial(true, SoftDot());
        smokeMat = PipelineUtil.MakeParticleMaterial(false, SoftDot());
        if (cart != null) cart.RideStarted += Clear;
    }

    void OnDestroy() { if (skyOriginal != null) RenderSettings.skybox = skyOriginal; }

    void Clear()
    {
        foreach (var g in leftovers) if (g != null) Destroy(g);
        leftovers.Clear();
        running = false; spawned = 0;
    }

    void Update()
    {
        if (cart == null || track == null) return;
        float dt = Time.deltaTime;

        // start?
        float trigger = startOnLiftHill ? Mathf.Lerp(track.LiftStart, track.LiftEnd, 0.35f) : startDistance;
        if (!running && spawned == 0 && cart.state == CoasterCart.RideState.Riding && cart.distance >= trigger && cart.distance < trigger + 200f)
        {
            running = true; clock = 0f; nextSpawn = 0.6f;
        }

        if (running)
        {
            clock += dt;
            if (clock >= nextSpawn && spawned < meteors)
            {
                bool big = bigFinale && spawned == meteors - 1;
                Spawn(big);
                spawned++;
                nextSpawn = clock + (duration * 0.8f / meteors) * Random.Range(0.6f, 1.4f);
            }
            if (spawned >= meteors && flying.Count == 0 && clock > duration) running = false;
        }

        // sunset in / out
        skyTarget = running && sunsetSky ? 1f : 0f;
        sky = Mathf.MoveTowards(sky, skyTarget, dt / 2.5f);
        if (sun != null) { sun.color = Color.Lerp(sunColor, sunsetSun, sky); sun.intensity = Mathf.Lerp(sunIntensity, sunIntensity * 0.75f, sky); }
        if (hasTint && skyCopy != null) skyCopy.SetColor("_Tint", Color.Lerp(skyTint, new Color(skyTint.r * 1.35f, skyTint.g * 0.8f, skyTint.b * 0.65f, skyTint.a), sky));

        // move the meteors
        for (int i = flying.Count - 1; i >= 0; i--)
        {
            var m = flying[i];
            if (m.t == null) { flying.RemoveAt(i); continue; }
            m.time += dt;
            float k = Mathf.Clamp01(m.time / m.flight);
            m.t.position = Vector3.Lerp(m.from, m.to, k * k * 0.35f + k * 0.65f);    // speeds up as it falls
            m.t.Rotate(120f * dt, 70f * dt, 0f, Space.Self);
            if (k >= 1f) { Impact(m); flying.RemoveAt(i); }
        }
    }

    // ------------------------------------------------------------------ meteors
    void Spawn(bool big)
    {
        if (!FindLandingSpot(big, out Vector3 hit)) return;
        Vector3 dirIn = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized * 0.7f + Vector3.up;
        float size = meteorSize * (big ? 2.6f : Random.Range(0.7f, 1.3f));
        var m = new Meteor { from = hit + dirIn.normalized * (big ? 320f : 240f), to = hit, flight = big ? 3.2f : Random.Range(1.6f, 2.4f), big = big };

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = big ? "BIG Meteor" : "Meteor";
        go.transform.position = m.from;
        go.transform.localScale = new Vector3(1f, 0.85f, 0.95f) * 2f * size;
        go.GetComponent<Renderer>().sharedMaterial = LavaRock();
        // glowing fire trail + smoke trail
        Trail(go.transform, fireMat, new Color(1f, 0.75f, 0.3f), glowColor, 1.6f * size, 0.5f, 6f);
        Trail(go.transform, smokeMat, new Color(0.25f, 0.2f, 0.18f, 0.7f), new Color(0.45f, 0.42f, 0.4f, 0f), 2.5f * size, 2.5f, 3f);
        var l = new GameObject("Meteor Light").AddComponent<Light>();
        l.transform.SetParent(go.transform, false);
        l.type = LightType.Point; l.color = glowColor; l.range = 30f * size; l.intensity = 4f; l.shadows = LightShadows.None;
        if (meteorTrailPrefab != null)
        {
            // make it inside a switched-off holder, so the effect's own scripts never start (we only want its looks)
            var holder = new GameObject("fx holder"); holder.SetActive(false);
            var fx = Instantiate(meteorTrailPrefab, holder.transform);
            foreach (var mb in fx.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
            foreach (var c in fx.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (var rb in fx.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            fx.transform.SetParent(go.transform, false);
            Destroy(holder);
            fx.transform.localPosition = Vector3.zero; fx.transform.localRotation = Quaternion.identity;
            fx.transform.localScale = Vector3.one / Mathf.Max(0.01f, go.transform.localScale.x) * size;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) { var mm = ps.main; mm.loop = true; ps.Play(); }
        }
        m.t = go.transform;
        flying.Add(m);
        if (whooshSound != null) AudioSource.PlayClipAtPoint(whooshSound, Vector3.Lerp(m.from, hit, 0.6f), volume * (big ? 1f : 0.6f));
    }

    bool FindLandingSpot(bool big, out Vector3 hit)
    {
        hit = Vector3.zero;
        for (int tries = 0; tries < 30; tries++)
        {
            float ahead = big ? Random.Range(60f, 110f) : Random.Range(15f, 220f);
            track.Sample(cart.distance + ahead, out Vector3 p, out Vector3 f, out _);
            Vector3 side = Vector3.Cross(Vector3.up, f); side.y = 0f;
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            float off = big ? trackClearance + Random.Range(6f, 14f) : Random.Range(trackClearance + 4f, spread);
            Vector3 q = p + side * off * (Random.value < 0.5f ? -1f : 1f);
            q.y = Ground(q);
            bool ok = true;
            foreach (var t in trackPts)
            {
                float dx = t.x - q.x, dz = t.z - q.z;
                if (dx * dx + dz * dz < trackClearance * trackClearance && t.y < q.y + 25f) { ok = false; break; }
            }
            if (ok) { hit = q; return true; }
        }
        return false;
    }

    void Impact(Meteor m)
    {
        Vector3 at = m.to;
        float s = m.big ? 2.6f : 1.4f;
        Destroy(m.t.gameObject);
        if (impactPrefab != null)
        {
            var fx = Instantiate(impactPrefab, at, Quaternion.identity);
            fx.transform.localScale *= s;
            Destroy(fx, 8f);
        }
        FxFactory.CreateExplosion(at + Vector3.up, s * meteorSize);

        // scorched crater with the meteor rock, glowing embers and a smoke column
        var crater = new GameObject("Meteor Crater").transform;
        crater.position = at;
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(disc.GetComponent<Collider>());
        disc.name = "Scorch";
        disc.transform.SetParent(crater, false);
        disc.transform.localPosition = Vector3.up * 0.03f;
        disc.transform.localScale = new Vector3(5f * s, 0.05f, 5f * s);
        disc.GetComponent<Renderer>().sharedMaterial = Scorch();
        var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(rock.GetComponent<Collider>());
        rock.name = "Meteor Rock";
        rock.transform.SetParent(crater, false);
        rock.transform.localPosition = Vector3.up * 0.4f * s;
        rock.transform.localRotation = Random.rotation;
        rock.transform.localScale = new Vector3(1.4f, 0.9f, 1.2f) * s * meteorSize;
        rock.GetComponent<Renderer>().sharedMaterial = LavaRock();
        FxFactory.CreateFire(crater, at + Vector3.up * 0.5f, 0.9f * s);
        Trail(crater, smokeMat, new Color(0.2f, 0.18f, 0.17f, 0.6f), new Color(0.5f, 0.48f, 0.45f, 0f), 4f * s, 5f, 0f, 8f * s);
        leftovers.Add(crater.gameObject);
        Destroy(crater.gameObject, 40f);

        if (impactSound != null) AudioSource.PlayClipAtPoint(impactSound, at, volume * (m.big ? 1f : 0.8f));
        if (cameraShake && Camera.main != null)
        {
            float d = Vector3.Distance(Camera.main.transform.position, at);
            float amount = (m.big ? 0.9f : 0.45f) * Mathf.Clamp01(1f - d / 220f);
            if (amount > 0.02f) RideCamera.Shake(amount, m.big ? 1.2f : 0.5f);
        }
    }

    // ------------------------------------------------------------------ helpers
    /// <summary>Particle trail: emits by distance while moving (rateOverDistance), or by time when standing still (rateOverTime).</summary>
    ParticleSystem Trail(Transform parent, Material mat, Color start, Color end, float size, float life, float perMetre, float perSecond = 0f)
    {
        var go = new GameObject("Trail");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.loop = true; m.playOnAwake = true; m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life * 1.3f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0f, 1.5f);
        m.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        m.startColor = Color.white;
        m.gravityModifier = perSecond > 0f ? -0.08f : 0f;
        m.maxParticles = 3000;
        m.scalingMode = ParticleSystemScalingMode.Local;      // not squashed by the parent's scale
        var e = ps.emission; e.rateOverTime = perSecond; e.rateOverDistance = perMetre;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = size * 0.25f;
        var c = ps.colorOverLifetime; c.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(start.r, start.g, start.b), 0f), new GradientColorKey(new Color(end.r, end.g, end.b), 1f) },
                  new[] { new GradientAlphaKey(start.a, 0f), new GradientAlphaKey(start.a * 0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
        c.color = new ParticleSystem.MinMaxGradient(g);
        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.8f, 1f, 1.8f));
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        ps.Play();
        return ps;
    }

    static float Ground(Vector3 p)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain != null) return terrain.SampleHeight(p) + terrain.transform.position.y;
        if (Physics.Raycast(p + Vector3.up * 400f, Vector3.down, out RaycastHit hit, 1000f, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
        return p.y;
    }

    Material lava, scorch;
    Material LavaRock()
    {
        if (lava != null) return lava;
        lava = rockMaterial != null ? new Material(rockMaterial) : PipelineUtil.MakeLit(new Color(0.18f, 0.12f, 0.1f), 0.2f, 0f);
        lava.EnableKeyword("_EMISSION");
        lava.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        if (lava.HasProperty("_EmissionColor")) lava.SetColor("_EmissionColor", glowColor * 1.4f);
        return lava;
    }
    Material Scorch()
    {
        if (scorch == null) scorch = PipelineUtil.MakeLit(new Color(0.12f, 0.09f, 0.07f), 0.05f, 0f);
        return scorch;
    }

    static Texture2D dot;
    static Texture2D SoftDot()
    {
        if (dot != null) return dot;
        const int N = 64;
        dot = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)); a *= a;
                dot.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        dot.Apply();
        return dot;
    }
}
