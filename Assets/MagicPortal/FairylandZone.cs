// FairylandZone.cs — turns one stretch of your coaster into a magical FAIRYLAND, while the rest stays desert.
//   * a flower arch over the track where the fairyland starts (and one where it ends),
//   * giant glowing mushrooms, cotton-candy trees and shining crystals beside the track,
//   * soft pink / mint / lilac "magic grass" patches over the sand,
//   * a pastel fairy castle and a big rainbow in the distance,
//   * thousands of floating fairy lights, and a sparkle burst + chime when the cart rides through the arch.
// Nothing is built ON the rails - everything keeps a safe distance from the track (also where the track passes overhead).
//
// HOW TO USE:
//   1. GameObject > Create Empty, name it "Fairyland".
//   2. Add Component > Fairyland Zone.
//   3. Type "Start Distance" and "End Distance" (metres from the station - the Console shows the track length
//      when you press Play, e.g. 978 m). The fairyland appears right away in the Scene view.
//   4. Change colours / amounts in the Inspector - it rebuilds automatically. Press "Rebuild" to get a new random layout.
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class FairylandZone : MonoBehaviour
{
    [Header("Where on the track")]
    public CoasterTrack track;
    [Tooltip("Fairyland begins this many metres after the station")] public float startDistance = 420f;
    [Tooltip("Fairyland ends here")] public float endDistance = 640f;
    [Tooltip("How far to the sides of the track the fairyland spreads (metres)")] public float width = 40f;
    [Tooltip("Nothing is placed closer than this to the rails")] public float clearance = 6f;
    [Tooltip("Change for a different random layout")] public int seed = 7;

    [Header("What to build")]
    public bool flowerArches = true;
    [Range(0f, 3f)] public float mushrooms = 1f;
    [Range(0f, 3f)] public float candyTrees = 1f;
    [Range(0f, 3f)] public float crystals = 1f;
    [Range(0f, 3f)] public float magicGrass = 1f;
    public bool castle = true;
    public bool rainbow = true;
    [Range(0f, 3f)] public float fairyLights = 1f;
    [Tooltip("Small glowing lights inside some mushrooms (max 10, they cost performance)")] public bool mushroomGlow = true;

    [Header("Colours")]
    public Color[] pastel =
    {
        new Color(1f, 0.55f, 0.78f),   // pink
        new Color(0.78f, 0.6f, 1f),    // lilac
        new Color(0.5f, 0.92f, 0.85f), // mint
        new Color(1f, 0.85f, 0.45f),   // butter yellow
        new Color(0.55f, 0.75f, 1f),   // baby blue
    };
    public Color glowColor = new Color(0.65f, 1f, 0.95f);

    [Header("Sound (optional)")]
    [Tooltip("A looping magical ambience played in the fairyland")] public AudioClip ambience;
    [Tooltip("Played when the cart rides through the first arch")] public AudioClip chime;
    [Range(0, 1)] public float volume = 0.8f;

    const string RootName = "Fairyland (built)";
    Transform root;
    /// <summary>Everything the fairyland built (World Transition hides / grows it).</summary>
    public Transform BuiltRoot => root != null ? root : transform.Find(RootName);
    System.Random rng;
    readonly List<Vector3> trackPts = new List<Vector3>();
    readonly List<ParticleSystem> archSparkles = new List<ParticleSystem>();
    readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
    Material dotMat;
    int lights;
    CoasterCart cart;
    float lastCartDist = -1f;
    bool rebuildQueued;

    // ------------------------------------------------------------------ lifecycle
    void OnEnable()
    {
        if (Application.isPlaying) { Rebuild(); return; }
        if (transform.Find(RootName) == null) QueueRebuild();
    }
    void OnValidate() { QueueRebuild(); }

    void QueueRebuild()
    {
#if UNITY_EDITOR
        if (Application.isPlaying || rebuildQueued) return;
        rebuildQueued = true;
        UnityEditor.EditorApplication.delayCall += () => { rebuildQueued = false; if (this != null && isActiveAndEnabled) Rebuild(); };
#endif
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        if (track == null) track = FindAnyObjectByType<CoasterTrack>();
        var old = transform.Find(RootName);
        if (old != null) Kill(old.gameObject);
        if (track == null) { Debug.LogWarning("[Fairyland] Drag your track into the 'Track' slot."); return; }
        if (track.Length < 1f) track.Build();

        rng = new System.Random(seed);
        mats.Clear(); archSparkles.Clear(); lights = 0;
        dotMat = PipelineUtil.MakeParticleMaterial(true, SoftDot());
        root = new GameObject(RootName).transform;
        root.SetParent(transform, false);
        root.position = Vector3.zero; root.rotation = Quaternion.identity;

        float a = Mathf.Clamp(Mathf.Min(startDistance, endDistance), 0f, track.Length);
        float b = Mathf.Clamp(Mathf.Max(startDistance, endDistance), 0f, track.Length);
        trackPts.Clear();
        for (float d = 0f; d < track.Length; d += 3f) { track.Sample(d, out Vector3 p, out _, out _); trackPts.Add(p); }

        if (flowerArches) { Arch(a, true); Arch(b, false); }

        // scatter things along both sides of the zone
        float step = 5f;
        for (float d = a; d <= b; d += step)
        {
            track.Sample(d, out Vector3 p, out Vector3 f, out _);
            Vector3 right = Vector3.Cross(Vector3.up, f); right.y = 0f;
            if (right.sqrMagnitude < 1e-4f) continue;
            right.Normalize();
            for (int side = -1; side <= 1; side += 2)
            {
                TryPlace(magicGrass * 0.55f, p, right * side, 0.3f, 12f, MagicGrass);
                TryPlace(mushrooms * 0.35f, p, right * side, 1f, 7f, Mushroom);
                TryPlace(candyTrees * 0.3f, p, right * side, 1f, 10f, CandyTree);
                TryPlace(crystals * 0.2f, p, right * side, 1f, 5f, Crystal);
            }
        }

        // fairy lights: one cloud every ~70 m
        if (fairyLights > 0f)
            for (float d = a; d < b; d += 70f)
            {
                float len = Mathf.Min(70f, b - d);
                track.Sample(d + len * 0.5f, out Vector3 p, out Vector3 f, out _);
                FairyLights(p, f, len);
            }

        // castle + rainbow beside the middle of the zone
        track.Sample((a + b) * 0.5f, out Vector3 mid, out Vector3 mf, out _);
        Vector3 mr = Vector3.Cross(Vector3.up, mf); mr.y = 0f; mr = mr.sqrMagnitude > 1e-4f ? mr.normalized : Vector3.right;
        int castleSide = 1;
        if (castle)
        {
            castleSide = Spot(mid + mr * (width + 18f), 16f, 26f, out _) ? 1 : -1;
            if (Spot(mid + mr * castleSide * (width + 18f), 16f, 26f, out Vector3 g)) Castle(g, mid);
            else Debug.Log("[Fairyland] No free space for the castle next to the middle of the zone (the track is in the way). Try a bigger Width or other distances.");
        }
        if (rainbow) Rainbow(mid + mr * castleSide * (width + 60f), mr * castleSide);

        if (ambience != null)
        {
            var src = new GameObject("Fairyland Ambience").AddComponent<AudioSource>();
            src.transform.SetParent(root, false); src.transform.position = mid;
            src.clip = ambience; src.loop = true; src.volume = volume; src.spatialBlend = 1f;
            src.minDistance = 30f; src.maxDistance = Mathf.Max(80f, (b - a) * 0.6f); src.rolloffMode = AudioRolloffMode.Linear;
            src.playOnAwake = true;
            if (Application.isPlaying) src.Play();
        }
#if UNITY_EDITOR
        if (!Application.isPlaying && gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    void Update()
    {
        if (!Application.isPlaying || !flowerArches) return;
        if (cart == null) cart = FindAnyObjectByType<CoasterCart>();
        if (cart == null) return;
        float s = Mathf.Min(startDistance, endDistance);
        if (lastCartDist >= 0f && lastCartDist < s && cart.distance >= s)
        {
            foreach (var ps in archSparkles) if (ps != null) ps.Emit(60);
            if (chime != null) AudioSource.PlayClipAtPoint(chime, cart.transform.position, volume);
        }
        lastCartDist = cart.distance;
    }

    // ------------------------------------------------------------------ placement helpers
    void TryPlace(float chance, Vector3 trackPos, Vector3 outDir, float minScale, float height, System.Action<Vector3, float> build)
    {
        while (chance > 0f)
        {
            if (R() < chance)
            {
                float off = Mathf.Lerp(clearance + 2f, width, Mathf.Sqrt(R()));
                Vector3 along = Vector3.Cross(outDir, Vector3.up) * Mathf.Lerp(-2.5f, 2.5f, R());
                float s = Mathf.Lerp(minScale, minScale * 1.8f, R());
                if (Spot(trackPos + outDir * off + along, 3f * s, height * s, out Vector3 g)) build(g, s);
            }
            chance -= 1f;
        }
    }

    /// <summary>Finds the ground under 'p' and checks the track is far enough away (also above it).</summary>
    bool Spot(Vector3 p, float radius, float height, out Vector3 ground)
    {
        ground = p;
        float gy;
        var terrain = Terrain.activeTerrain;
        if (terrain != null) gy = terrain.SampleHeight(p) + terrain.transform.position.y;
        else if (Physics.Raycast(new Vector3(p.x, p.y + 300f, p.z), Vector3.down, out RaycastHit hit, 1000f, ~0, QueryTriggerInteraction.Ignore)) gy = hit.point.y;
        else gy = p.y - 3f;
        ground = new Vector3(p.x, gy, p.z);
        float need = clearance + radius;
        foreach (var t in trackPts)
        {
            float dx = t.x - p.x, dz = t.z - p.z;
            if (dx * dx + dz * dz < need * need && t.y < gy + height + 4f) return false;
        }
        return true;
    }

    float R() { return (float)rng.NextDouble(); }
    Color Pastel() { return pastel != null && pastel.Length > 0 ? pastel[rng.Next(pastel.Length)] : Color.magenta; }

    // ------------------------------------------------------------------ builders
    void Arch(float dist, bool entrance)
    {
        if (track.InLoop(dist)) { Debug.LogWarning($"[Fairyland] No arch at {dist:F0} m - that is inside the loop. Move Start/End Distance a little."); return; }
        track.Sample(dist, out Vector3 p, out Vector3 f, out _);
        Vector3 right = Vector3.Cross(Vector3.up, f); right.y = 0f;
        right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
        var arch = new GameObject(entrance ? "Flower Arch (entrance)" : "Flower Arch (exit)").transform;
        arch.SetParent(root, false);
        arch.position = p;
        arch.rotation = Quaternion.LookRotation(new Vector3(f.x, 0f, f.z).sqrMagnitude > 1e-4f ? new Vector3(f.x, 0f, f.z) : Vector3.forward, Vector3.up);
        const float r = 5.2f;
        var leaf = Mat(new Color(0.35f, 0.75f, 0.45f));
        const int n = 34;
        for (int i = 0; i <= n; i++)
        {
            float ang = Mathf.PI * i / n;
            Vector3 lp = new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r + 0.6f, 0f);
            Prim(PrimitiveType.Sphere, arch, lp, Vector3.zero, Vector3.one * 0.95f, leaf);
            var flower = Prim(PrimitiveType.Sphere, arch, lp + new Vector3(R() - 0.5f, R() - 0.5f, -0.45f) * 0.6f, Vector3.zero, Vector3.one * Mathf.Lerp(0.4f, 0.65f, R()), Glow(Pastel(), 0.6f));
            if (i % 6 == 3) archSparkles.Add(Sparkles(flower.transform, 1.2f, 6f));
        }
        // pillars down to the ground on both sides
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 top = p + right * side * r + Vector3.up * 0.6f;
            Spot(top, 0f, 0f, out Vector3 g);
            float h = Mathf.Max(0.1f, top.y - g.y);
            var pillar = Prim(PrimitiveType.Cylinder, root, (top + g) * 0.5f, Vector3.zero, new Vector3(0.7f, h * 0.5f, 0.7f), Mat(new Color(0.97f, 0.93f, 0.98f)));
            pillar.name = "Arch Pillar";
            Prim(PrimitiveType.Sphere, root, top, Vector3.zero, Vector3.one * 1.3f, Glow(glowColor, 1.5f));
        }
    }

    void Mushroom(Vector3 g, float s)
    {
        var m = new GameObject("Glow Mushroom").transform;
        m.SetParent(root, false); m.position = g;
        m.rotation = Quaternion.Euler(R() * 10f - 5f, R() * 360f, R() * 10f - 5f);
        float h = 2.2f * s;
        Prim(PrimitiveType.Cylinder, m, new Vector3(0f, h * 0.5f, 0f), Vector3.zero, new Vector3(0.6f * s, h * 0.5f, 0.6f * s), Mat(new Color(1f, 0.96f, 0.88f)));
        Color capC = Pastel();
        Prim(PrimitiveType.Sphere, m, new Vector3(0f, h, 0f), Vector3.zero, new Vector3(2.6f * s, 1.3f * s, 2.6f * s), Glow(capC, 0.35f));
        // white glowing dots on the cap
        for (int i = 0; i < 7; i++)
        {
            float ang = R() * Mathf.PI * 2f, rad = Mathf.Lerp(0.2f, 1.0f, R()) * s;
            float y = h + Mathf.Sqrt(Mathf.Max(0f, 1f - (rad / (1.3f * s)) * (rad / (1.3f * s)))) * 0.62f * s;
            Prim(PrimitiveType.Sphere, m, new Vector3(Mathf.Cos(ang) * rad, y, Mathf.Sin(ang) * rad), Vector3.zero, Vector3.one * 0.32f * s, Glow(Color.white, 1.2f));
        }
        if (mushroomGlow && lights < 10)
        {
            lights++;
            var l = new GameObject("Mushroom Glow").AddComponent<Light>();
            l.transform.SetParent(m, false); l.transform.localPosition = new Vector3(0f, h * 0.6f, 0f);
            l.type = LightType.Point; l.color = capC; l.range = 9f * s; l.intensity = 2.5f; l.shadows = LightShadows.None;
        }
    }

    void CandyTree(Vector3 g, float s)
    {
        var t = new GameObject("Cotton Candy Tree").transform;
        t.SetParent(root, false); t.position = g; t.rotation = Quaternion.Euler(0f, R() * 360f, 0f);
        float h = 4f * s;
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, h * 0.5f, 0f), new Vector3(R() * 8f - 4f, 0f, R() * 8f - 4f), new Vector3(0.45f * s, h * 0.5f, 0.45f * s), Mat(new Color(0.85f, 0.75f, 0.95f)));
        Color c = Pastel();
        int puffs = 3 + rng.Next(3);
        for (int i = 0; i < puffs; i++)
        {
            Vector3 lp = new Vector3((R() - 0.5f) * 2.4f * s, h + (R() - 0.2f) * 1.8f * s, (R() - 0.5f) * 2.4f * s);
            Prim(PrimitiveType.Sphere, t, lp, Vector3.zero, Vector3.one * Mathf.Lerp(2.2f, 3.4f, R()) * s, Glow(Color.Lerp(c, Color.white, R() * 0.35f), 0.15f));
        }
    }

    void Crystal(Vector3 g, float s)
    {
        var c = new GameObject("Magic Crystals").transform;
        c.SetParent(root, false); c.position = g; c.rotation = Quaternion.Euler(0f, R() * 360f, 0f);
        Color col = R() < 0.5f ? glowColor : Pastel();
        int n = 3 + rng.Next(4);
        for (int i = 0; i < n; i++)
        {
            float h = Mathf.Lerp(1.5f, 4.5f, R()) * s;
            Prim(PrimitiveType.Cube, c, new Vector3((R() - 0.5f) * 1.6f * s, h * 0.4f, (R() - 0.5f) * 1.6f * s),
                 new Vector3(R() * 40f - 20f, R() * 90f, R() * 40f - 20f), new Vector3(0.5f * s, h, 0.5f * s), Glow(col, 1.4f, 0.9f));
        }
    }

    void MagicGrass(Vector3 g, float s)
    {
        float r = Mathf.Lerp(4f, 9f, R()) * s;
        var patch = Prim(PrimitiveType.Cylinder, root, g + Vector3.up * 0.02f, Vector3.zero, new Vector3(r, 0.04f, r),
                         Mat(Color.Lerp(R() < 0.6f ? new Color(0.55f, 0.88f, 0.6f) : Pastel(), Color.white, 0.15f)));
        patch.name = "Magic Grass";
        // a few tiny flowers on it
        for (int i = 0; i < 5; i++)
        {
            float ang = R() * Mathf.PI * 2f, rad = R() * r * 0.45f;
            Prim(PrimitiveType.Sphere, root, g + new Vector3(Mathf.Cos(ang) * rad, 0.25f, Mathf.Sin(ang) * rad), Vector3.zero, Vector3.one * 0.35f, Glow(Pastel(), 0.5f)).name = "Flower";
        }
    }

    void Castle(Vector3 g, Vector3 lookAt)
    {
        var c = new GameObject("Fairy Castle").transform;
        c.SetParent(root, false); c.position = g;
        Vector3 to = lookAt - g; to.y = 0f;
        c.rotation = Quaternion.LookRotation(to.sqrMagnitude > 1e-3f ? to : Vector3.forward);
        var wall = Mat(new Color(1f, 0.88f, 0.93f));
        var roof = Glow(new Color(0.55f, 0.6f, 1f), 0.25f);
        var roof2 = Glow(new Color(1f, 0.55f, 0.8f), 0.25f);
        Prim(PrimitiveType.Cube, c, new Vector3(0f, 5f, 0f), Vector3.zero, new Vector3(18f, 10f, 14f), wall);
        Prim(PrimitiveType.Cube, c, new Vector3(0f, 2.5f, 7.05f), Vector3.zero, new Vector3(3.2f, 5f, 0.2f), Mat(new Color(0.55f, 0.35f, 0.6f))).name = "Door";
        // corner towers
        for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 tp = new Vector3(x * 9f, 0f, z * 7f);
                Prim(PrimitiveType.Cylinder, c, tp + Vector3.up * 7f, Vector3.zero, new Vector3(4f, 7f, 4f), wall);
                Cone(c, tp + Vector3.up * 14f, 2.8f, 6f, (x + z) == 0 ? roof : roof2);
                Prim(PrimitiveType.Sphere, c, tp + Vector3.up * 20.2f, Vector3.zero, Vector3.one * 0.6f, Glow(new Color(1f, 0.85f, 0.4f), 2f));
            }
        // big centre tower
        Prim(PrimitiveType.Cylinder, c, new Vector3(0f, 11f, 0f), Vector3.zero, new Vector3(6f, 11f, 6f), wall);
        Cone(c, new Vector3(0f, 22f, 0f), 4f, 9f, roof2);
        Prim(PrimitiveType.Sphere, c, new Vector3(0f, 31.3f, 0f), Vector3.zero, Vector3.one * 1f, Glow(new Color(1f, 0.85f, 0.4f), 2.5f));
        // glowing windows
        for (int i = 0; i < 6; i++)
            Prim(PrimitiveType.Cube, c, new Vector3(-6f + i * 2.4f, 7f, 7.05f), Vector3.zero, new Vector3(1f, 1.6f, 0.15f), Glow(new Color(1f, 0.9f, 0.55f), 1.8f)).name = "Window";
        Sparkles(c, 14f, 25f).transform.localPosition = new Vector3(0f, 18f, 0f);
    }

    void Rainbow(Vector3 centre, Vector3 outward)
    {
        Color[] cols =
        {
            new Color(1f, 0.35f, 0.4f), new Color(1f, 0.65f, 0.3f), new Color(1f, 0.92f, 0.4f),
            new Color(0.45f, 0.9f, 0.5f), new Color(0.4f, 0.65f, 1f), new Color(0.7f, 0.5f, 1f),
        };
        Spot(centre, 0f, 0f, out Vector3 g);
        var rb = new GameObject("Rainbow").transform;
        rb.SetParent(root, false); rb.position = g - Vector3.up * 6f;
        rb.rotation = Quaternion.LookRotation(outward, Vector3.up);   // arc faces the track
        const float inner = 70f, band = 4f;
        for (int i = 0; i < cols.Length; i++)
        {
            var go = new GameObject("Rainbow " + i);
            go.transform.SetParent(rb, false);
            go.AddComponent<MeshFilter>().sharedMesh = ArcMesh(inner + (cols.Length - 1 - i) * band, band);
            var m = Glow(cols[i], 0.9f, 0.7f);
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
        }
    }

    void FairyLights(Vector3 centre, Vector3 fwd, float length)
    {
        var go = new GameObject("Fairy Lights");
        go.transform.SetParent(root, false);
        go.transform.position = centre + Vector3.up * 2f;
        go.transform.rotation = Quaternion.LookRotation(new Vector3(fwd.x, 0f, fwd.z).sqrMagnitude > 1e-4f ? new Vector3(fwd.x, 0f, fwd.z) : Vector3.forward);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = true; main.prewarm = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(glowColor, 0f), new GradientColorKey(pastel.Length > 0 ? pastel[0] : Color.magenta, 0.5f), new GradientColorKey(new Color(1f, 0.95f, 0.6f), 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(grad) { mode = ParticleSystemGradientMode.RandomColor };
        main.maxParticles = 2000;
        var em = ps.emission; em.rateOverTime = 45f * fairyLights;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(width * 2f, 8f, length);
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.8f; noise.frequency = 0.3f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(fade);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = dotMat;
        ps.Play();
    }

    ParticleSystem Sparkles(Transform parent, float radius, float rate)
    {
        var go = new GameObject("Sparkles");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, glowColor);
        main.gravityModifier = -0.05f;
        main.maxParticles = 800;
        var em = ps.emission; em.rateOverTime = rate;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius;
        var col = ps.colorOverLifetime; col.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(fade);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = dotMat;
        ps.Play();
        return ps;
    }

    // ------------------------------------------------------------------ meshes & materials
    GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale, Material m)
    {
        var go = GameObject.CreatePrimitive(type);
        Kill(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        if (parent == root) go.transform.position = pos; else go.transform.localPosition = pos;
        go.transform.localEulerAngles = euler;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // keeps it fast
        return go;
    }

    void Cone(Transform parent, Vector3 baseLocal, float radius, float height, Material m)
    {
        var go = new GameObject("Roof");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = baseLocal;
        go.AddComponent<MeshFilter>().sharedMesh = ConeMesh(radius, height);
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
    }

    static Mesh ConeMesh(float r, float h)
    {
        const int n = 24;
        var v = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i < n; i++)
        {
            float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
            int k = v.Count;
            v.Add(new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r));
            v.Add(new Vector3(0f, h, 0f));
            v.Add(new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r));
            t.Add(k); t.Add(k + 1); t.Add(k + 2);
        }
        var mesh = new Mesh { name = "Cone" };
        mesh.SetVertices(v); mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh ArcMesh(float r, float band)
    {
        const int n = 64;
        var v = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i <= n; i++)
        {
            float a = Mathf.PI * i / n;
            Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            v.Add(dir * r); v.Add(dir * (r + band));
        }
        for (int i = 0; i < n; i++)
        {
            int k = i * 2;
            t.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 });   // front
            t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });   // back (visible from both sides)
        }
        var mesh = new Mesh { name = "Rainbow Band" };
        mesh.SetVertices(v); mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    Material Mat(Color c)
    {
        if (mats.TryGetValue(c, out var m)) return m;
        m = PipelineUtil.MakeLit(c, 0.35f, 0f);
        mats[c] = m;
        return m;
    }

    /// <summary>Material that glows softly in its own colour (and is see-through if alpha &lt; 1).</summary>
    Material Glow(Color c, float strength, float alpha = 1f)
    {
        var key = new Color(c.r, c.g, c.b, strength * 0.01f + alpha * 0.0001f);
        if (mats.TryGetValue(key, out var m)) return m;
        var col = new Color(c.r, c.g, c.b, alpha);
        m = PipelineUtil.MakeLit(col, 0.6f, 0f);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * strength);
        if (alpha < 0.999f && PipelineUtil.IsSRP) { PipelineUtil.SetTransparent(m, false); if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f); }
        mats[key] = m;
        return m;
    }

    static Texture2D dot;
    static Texture2D SoftDot()
    {
        if (dot != null) return dot;
        const int N = 32;
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

    static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    void OnDrawGizmosSelected()
    {
        if (track == null) return;
        Gizmos.color = new Color(1f, 0.5f, 0.85f, 0.8f);
        float a = Mathf.Min(startDistance, endDistance), b = Mathf.Max(startDistance, endDistance);
        for (float d = a; d < b; d += 4f)
        {
            track.Sample(d, out Vector3 p0, out _, out _); track.Sample(Mathf.Min(b, d + 4f), out Vector3 p1, out _, out _);
            Gizmos.DrawLine(p0 + Vector3.up * 3f, p1 + Vector3.up * 3f);
        }
    }
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(FairylandZone))]
public class FairylandZoneEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        var z = (FairylandZone)target;
        UnityEditor.EditorGUILayout.HelpBox("Set Start / End Distance (metres from the station). The fairyland rebuilds by itself when you change a value.\nChange 'Seed' or press Rebuild for a different random layout.", UnityEditor.MessageType.Info);
        DrawDefaultInspector();
        GUILayout.Space(6);
        if (GUILayout.Button("Rebuild Fairyland", GUILayout.Height(30))) z.Rebuild();
    }
}
#endif
