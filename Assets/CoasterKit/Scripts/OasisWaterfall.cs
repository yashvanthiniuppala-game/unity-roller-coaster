// OasisWaterfall.cs — a desert OASIS with a WATERFALL the coaster rides right through.
//   * a natural rock arch over the track (made from the rocks of your Desert pack),
//   * a wide waterfall pours down from the arch across the track, with mist at the top and bottom,
//   * a blue pool below, with palm trees and rocks around it,
//   * when the cart rides through the falling water: a big SPLASH over the riders,
//     water drops run down the screen, and you hear the splash.
//
// HOW TO USE:
//   1. GameObject > Create Empty, name it "Oasis", Add Component > Oasis Waterfall.
//   2. Type "Distance" (metres from the station) - pick a LOW part of the track (close to the ground), not the loop.
//      The oasis appears in the Scene view right away (palms + rocks come from Desert > Top-Down Desert Pack > Prefab).
//   3. Optional: sounds for the waterfall (loop) and the splash. Press Play.
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class OasisWaterfall : MonoBehaviour
{
    [Header("Where")]
    public CoasterTrack track;
    [Tooltip("Metres from the station where the cart rides through the waterfall")] public float distance = 160f;
    [Tooltip("Height of the rock arch above the rails")] public float archHeight = 8f;
    [Tooltip("Half the opening of the arch (metres from the rails to each pillar)")] public float archHalfWidth = 6.5f;

    [Header("Oasis")]
    [Tooltip("Radius of the pool under the waterfall")] public float poolRadius = 11f;
    [Range(0, 14)] public int palms = 8;
    [Range(0, 16)] public int rimRocks = 8;
    public Color waterColor = new Color(0.2f, 0.62f, 0.72f, 0.85f);
    [Tooltip("Palm / rock prefabs (found automatically in your Desert pack)")] public GameObject[] palmPrefabs;
    public GameObject[] rockPrefabs;

    [Header("Waterfall")]
    [Range(0.3f, 3f)] public float waterAmount = 1f;
    [Range(0.5f, 3f)] public float splashSize = 1.6f;
    [Tooltip("Water drops run down the screen after the splash")] public bool dropsOnScreen = true;

    [Header("Sound (optional)")]
    public AudioClip waterfallLoop;
    public AudioClip splashSound;
    [Range(0, 1)] public float volume = 1f;

    const string RootName = "Oasis (built)";
    bool queued;
    CoasterCart cart;
    float lastDist = -1f;
    // screen drops
    struct Drop { public Vector2 pos; public float size, life, speed; }
    readonly List<Drop> drops = new List<Drop>();
    static Texture2D dropTex;

    // ------------------------------------------------------------------ build
    void OnEnable() { if (!Application.isPlaying && transform.Find(RootName) == null) Queue(); }
    void OnValidate() { Queue(); }
    void Queue()
    {
#if UNITY_EDITOR
        if (Application.isPlaying || queued) return;
        queued = true;
        UnityEditor.EditorApplication.delayCall += () => { queued = false; if (this != null && isActiveAndEnabled) Rebuild(); };
#endif
    }

    void FindPrefabs()
    {
#if UNITY_EDITOR
        if (palmPrefabs == null || palmPrefabs.Length == 0 || palmPrefabs[0] == null) palmPrefabs = Load("palm");
        if (rockPrefabs == null || rockPrefabs.Length == 0 || rockPrefabs[0] == null) rockPrefabs = Load("rock_0");
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
#if UNITY_EDITOR
    static GameObject[] Load(string word)
    {
        var list = new List<GameObject>();
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets(word + " t:Prefab", new[] { "Assets/Desert" }))
        {
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) list.Add(go);
        }
        return list.ToArray();
    }
#endif

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        if (track == null) track = FindAnyObjectByType<CoasterTrack>();
        var old = transform.Find(RootName);
        if (old != null) Kill(old.gameObject);
        if (track == null) { Debug.LogWarning("[Oasis] Drag your track into the 'Track' slot."); return; }
        if (track.Length < 1f) track.Build();
        FindPrefabs();
        if (track.InLoop(distance)) Debug.LogWarning("[Oasis] The waterfall is inside the loop - move 'Distance'.");

        var root = new GameObject(RootName).transform;
        root.SetParent(transform, false);
        root.position = Vector3.zero; root.rotation = Quaternion.identity;

        track.Sample(distance, out Vector3 p, out Vector3 f, out _);
        Vector3 fwd = new Vector3(f.x, 0f, f.z); fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, fwd);
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
        float groundY = Ground(p);
        float railY = p.y;
        if (railY - groundY > 25f) Debug.LogWarning("[Oasis] The track is very high here - pick a lower part of the track for a nicer oasis.");
        float topY = railY + archHeight;

        // 1) the pool under the waterfall
        var pool = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Kill(pool.GetComponent<Collider>());
        pool.name = "Oasis Pool";
        pool.transform.SetParent(root, false);
        pool.transform.position = new Vector3(p.x, groundY + 0.15f, p.z);
        pool.transform.localScale = new Vector3(poolRadius * 2f, 0.05f, poolRadius * 2f);
        pool.GetComponent<Renderer>().sharedMaterial = PipelineUtil.MakeWater(waterColor);
        var bank = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Kill(bank.GetComponent<Collider>());
        bank.name = "Wet Sand";
        bank.transform.SetParent(root, false);
        bank.transform.position = new Vector3(p.x, groundY + 0.06f, p.z);
        bank.transform.localScale = new Vector3(poolRadius * 2.5f, 0.04f, poolRadius * 2.5f);
        bank.GetComponent<Renderer>().sharedMaterial = PipelineUtil.MakeLit(new Color(0.55f, 0.45f, 0.3f), 0.5f, 0f);

        // 2) the rock arch: two pillars + a rock slab across the top
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 basePos = p + side * s * (archHalfWidth + 1.5f);
            float gy = Ground(basePos);
            Rock(root, new Vector3(basePos.x, gy, basePos.z), rot * Quaternion.Euler(0f, s * 20f, 0f), new Vector3(4.5f, topY - gy + 1.5f, 5f), 0);
        }
        Rock(root, p + Vector3.up * (archHeight - 0.5f), rot, new Vector3(archHalfWidth * 2f + 8f, 3.2f, 5.5f), 1, false);

        // 3) the waterfall pouring from the front edge of the arch (towards the coming cart)
        Vector3 lip = p - fwd * 2.2f + Vector3.up * (archHeight - 0.8f);
        var fallRoot = new GameObject("Waterfall").transform;
        fallRoot.SetParent(root, false);
        fallRoot.SetPositionAndRotation(lip, Quaternion.LookRotation(Vector3.down, fwd));   // emits DOWN, box wide across the track
        var mat = PipelineUtil.MakeParticleMaterial(false, Soft());
        float fallH = Mathf.Max(3f, lip.y - groundY);
        var curtain = Ps("Water Curtain", fallRoot, mat, ParticleSystemShapeType.Box, new Vector3(archHalfWidth * 2f, 0.6f, 0.3f),
                         new Vector2(1f, 3f), new Vector2(0.5f, 1.2f), Mathf.Sqrt(2f * fallH / 9.81f) + 0.3f, 380f * waterAmount, 1f,
                         new Color(0.85f, 0.94f, 1f, 0.75f));
        var cr = curtain.GetComponent<ParticleSystemRenderer>(); cr.renderMode = ParticleSystemRenderMode.Stretch; cr.velocityScale = 0.08f; cr.lengthScale = 1.5f;
        // mist where it starts and where it lands
        Ps("Top Foam", fallRoot, mat, ParticleSystemShapeType.Box, new Vector3(archHalfWidth * 2f, 0.4f, 0.4f),
           new Vector2(0.2f, 0.8f), new Vector2(0.8f, 1.6f), 0.8f, 40f * waterAmount, 0.05f, new Color(1f, 1f, 1f, 0.6f));
        var mist = new GameObject("Landing Mist").transform;
        mist.SetParent(root, false);
        mist.SetPositionAndRotation(new Vector3(lip.x, groundY + 0.5f, lip.z), Quaternion.LookRotation(Vector3.up, fwd));
        Ps("Mist", mist, mat, ParticleSystemShapeType.Box, new Vector3(archHalfWidth * 2.2f, 2f, 0.5f),
           new Vector2(1f, 3f), new Vector2(2.5f, 5f), 2.2f, 30f * waterAmount, -0.05f, new Color(1f, 1f, 1f, 0.35f));
        Ps("Splashing", mist, mat, ParticleSystemShapeType.Box, new Vector3(archHalfWidth * 2f, 2f, 0.5f),
           new Vector2(3f, 6f), new Vector2(0.2f, 0.5f), 0.9f, 120f * waterAmount, 1.2f, new Color(0.9f, 0.97f, 1f, 0.8f));

        if (waterfallLoop != null)
        {
            var a = fallRoot.gameObject.AddComponent<AudioSource>();
            a.clip = waterfallLoop; a.loop = true; a.volume = volume * 0.8f; a.spatialBlend = 1f; a.minDistance = 10f; a.maxDistance = 120f;
            a.playOnAwake = true;
        }

        // 4) palms and rocks around the pool (never near the rails)
        var rng = new System.Random(9);
        var trackPts = new List<Vector3>();
        for (float d = 0f; d < track.Length; d += 3f) { track.Sample(d, out Vector3 q, out _, out _); trackPts.Add(q); }
        int placed = 0;
        for (int tries = 0; tries < 200 && placed < palms; tries++)
        {
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f, rad = poolRadius * (0.9f + (float)rng.NextDouble() * 0.7f);
            Vector3 q = p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * rad;
            if (!Free(q, trackPts, 6f, Ground(q), 14f)) continue;
            Palm(root, q, rng, placed);
            placed++;
        }
        placed = 0;
        for (int tries = 0; tries < 200 && placed < rimRocks; tries++)
        {
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f, rad = poolRadius * (0.95f + (float)rng.NextDouble() * 0.2f);
            Vector3 q = p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * rad;
            if (!Free(q, trackPts, 3.5f, Ground(q), 3f)) continue;
            float sz = 1.2f + (float)rng.NextDouble() * 1.6f;
            Rock(root, new Vector3(q.x, Ground(q), q.z), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), new Vector3(sz * 1.4f, sz, sz * 1.2f), placed + 2);
            placed++;
        }
#if UNITY_EDITOR
        if (!Application.isPlaying && gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    static bool Free(Vector3 q, List<Vector3> pts, float clear, float groundY, float height)
    {
        foreach (var t in pts)
        {
            float dx = t.x - q.x, dz = t.z - q.z;
            if (dx * dx + dz * dz < clear * clear && t.y < groundY + height + 3f) return false;
        }
        return true;
    }

    /// <summary>Places a rock prefab (or a simple rock) stretched to 'size' metres. bottomAtPos = stand on 'pos', else centre on it.</summary>
    void Rock(Transform root, Vector3 pos, Quaternion rot, Vector3 size, int pick, bool bottomAtPos = true)
    {
        GameObject go = Spawn(rockPrefabs, pick, root);
        if (go == null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Kill(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            go.GetComponent<Renderer>().sharedMaterial = PipelineUtil.MakeLit(new Color(0.66f, 0.45f, 0.3f), 0.1f, 0f);
        }
        go.name = "Oasis Rock";
        go.transform.rotation = rot;
        go.transform.localScale = Vector3.one;
        Bounds lb = LocalBounds(go);
        go.transform.localScale = new Vector3(size.x / Mathf.Max(0.01f, lb.size.x), size.y / Mathf.Max(0.01f, lb.size.y), size.z / Mathf.Max(0.01f, lb.size.z));
        go.transform.position = pos;
        Bounds wb = WorldBounds(go);
        go.transform.position += bottomAtPos ? Vector3.up * (pos.y - wb.min.y) : (pos - wb.center);
    }

    void Palm(Transform root, Vector3 q, System.Random rng, int i)
    {
        GameObject go = Spawn(palmPrefabs, i, root);
        if (go == null) return;
        go.name = "Oasis Palm";
        go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        go.transform.localScale = Vector3.one;
        float h = WorldBounds(go).size.y;
        go.transform.localScale = Vector3.one * ((8f + (float)rng.NextDouble() * 5f) / Mathf.Max(0.01f, h));
        go.transform.position = new Vector3(q.x, Ground(q), q.z);
        go.transform.position += Vector3.up * (Ground(q) - WorldBounds(go).min.y);
    }

    GameObject Spawn(GameObject[] list, int i, Transform parent)
    {
        if (list == null || list.Length == 0) return null;
        var prefab = list[i % list.Length];
        if (prefab == null) return null;
        GameObject go = null;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
            UnityEditor.PrefabUtility.UnpackPrefabInstance(go, UnityEditor.PrefabUnpackMode.Completely, UnityEditor.InteractionMode.AutomatedAction);
        }
#endif
        if (go == null) go = Instantiate(prefab, parent);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Kill(c);   // nothing for the cart to bump into
        return go;
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Bounds LocalBounds(GameObject go)
    {
        bool any = false; Bounds lb = new Bounds(Vector3.zero, Vector3.one);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int k = 0; k < 8; k++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                Vector3 lp = go.transform.InverseTransformPoint(r.transform.TransformPoint(c));
                if (!any) { lb = new Bounds(lp, Vector3.zero); any = true; } else lb.Encapsulate(lp);
            }
        }
        return lb;
    }

    static float Ground(Vector3 p)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain != null) return terrain.SampleHeight(p) + terrain.transform.position.y;
        if (Physics.Raycast(p + Vector3.up * 300f, Vector3.down, out RaycastHit hit, 1000f, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
        return p.y - 2f;
    }

    ParticleSystem Ps(string name, Transform parent, Material mat, ParticleSystemShapeType shape, Vector3 boxSize, Vector2 speed, Vector2 size,
                      float life, float rate, float gravity, Color col)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.loop = true; m.playOnAwake = true; m.prewarm = true; m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.85f, life * 1.1f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        m.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        m.startColor = col;
        m.gravityModifier = gravity;
        m.maxParticles = 5000;
        var e = ps.emission; e.rateOverTime = rate;
        var sh = ps.shape; sh.shapeType = shape; sh.scale = boxSize;
        var c = ps.colorOverLifetime; c.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0.2f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.8f), new GradientAlphaKey(0f, 1f) });
        c.color = new ParticleSystem.MinMaxGradient(g);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        ps.Play();
        return ps;
    }

    // ------------------------------------------------------------------ play: the splashdown
    void Update()
    {
        if (!Application.isPlaying) return;
        if (cart == null) cart = FindAnyObjectByType<CoasterCart>();
        if (cart == null) return;
        float d = cart.distance;
        float hitAt = distance - 2.2f;                      // the curtain hangs at the front edge of the arch
        if (cart.state == CoasterCart.RideState.Riding && lastDist >= 0f && lastDist < hitAt && d >= hitAt)
        {
            Vector3 at = cart.transform.position + cart.transform.up * 1.5f;
            BigSplash(at, cart.transform);
            if (splashSound != null) AudioSource.PlayClipAtPoint(splashSound, at, volume);
            RideCamera.Shake(0.15f, 0.4f);
            if (dropsOnScreen) LensDrops.Splash(Mathf.RoundToInt(38 * splashSize), 1.1f);
        }
        lastDist = cart.state == CoasterCart.RideState.Riding ? d : -1f;

        for (int i = drops.Count - 1; i >= 0; i--)
        {
            var dr = drops[i];
            dr.life -= Time.deltaTime;
            dr.pos.y += dr.speed * Time.deltaTime;          // slide down the screen
            drops[i] = dr;
            if (dr.life <= 0f) drops.RemoveAt(i);
        }
    }

    // ------------------------------------------------------------------ a big, layered splash
    void BigSplash(Vector3 at, Transform car)
    {
        var root = new GameObject("Waterfall Splash");
        root.transform.position = at;
        Destroy(root, 5f);
        var mat = PipelineUtil.MakeParticleMaterial(false, Soft());
        var shiny = PipelineUtil.MakeParticleMaterial(false, Shiny());
        float s = splashSize;
        Vector3 up = car.up, fwd = car.forward;
        // 1) thousands of shiny droplets thrown up and forward, falling in arcs (stretched along their motion)
        Burst(root.transform, shiny, "Droplets", (int)(420 * s), new Vector2(5f, 13f) * Mathf.Sqrt(s), new Vector2(0.05f, 0.18f), new Vector2(0.7f, 1.5f),
              1.3f, ParticleSystemShapeType.Cone, 55f, Quaternion.LookRotation(up + fwd * 0.6f), new Color(0.85f, 0.95f, 1f, 1f), true);
        // 2) a sheet of white water spraying out to the sides
        Burst(root.transform, mat, "Spray Sheet", (int)(140 * s), new Vector2(4f, 9f), new Vector2(0.5f, 1.4f) * s, new Vector2(0.5f, 1f),
              1.1f, ParticleSystemShapeType.Cone, 75f, Quaternion.LookRotation(up), new Color(0.95f, 0.98f, 1f, 0.7f), false);
        // 3) a ring of water bursting out around the cart
        Burst(root.transform, mat, "Splash Ring", (int)(90 * s), new Vector2(6f, 10f), new Vector2(0.4f, 0.9f) * s, new Vector2(0.4f, 0.8f),
              0.9f, ParticleSystemShapeType.Circle, 0f, Quaternion.LookRotation(up) * Quaternion.Euler(90f, 0f, 0f), new Color(0.9f, 0.97f, 1f, 0.8f), false);
        // 4) soft mist hanging in the air afterwards
        Burst(root.transform, mat, "Mist", (int)(25 * s), new Vector2(0.5f, 2f), new Vector2(2.5f, 5f) * s, new Vector2(1.5f, 3f),
              -0.05f, ParticleSystemShapeType.Sphere, 0f, Quaternion.identity, new Color(1f, 1f, 1f, 0.28f), false);
    }

    static void Burst(Transform parent, Material mat, string name, int count, Vector2 speed, Vector2 size, Vector2 life, float gravity,
                      ParticleSystemShapeType shape, float angle, Quaternion rot, Color col, bool stretch)
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
        var sh = ps.shape; sh.shapeType = shape; sh.angle = angle; sh.radius = 1.2f;
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

    static Texture2D shiny;
    static Texture2D Shiny()
    {
        if (shiny != null) return shiny;
        const int N = 32;
        shiny = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r); a = a * a * (0.5f + 0.5f * Mathf.Clamp01(1f - r * 2f));   // bright core, soft edge
                shiny.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        shiny.Apply();
        return shiny;
    }

    void MakeDrops()
    {
        for (int i = 0; i < 28; i++)
            drops.Add(new Drop
            {
                pos = new Vector2(Random.value, Random.value * 0.85f),
                size = Random.Range(0.025f, 0.075f),
                life = Random.Range(1.5f, 3.2f),
                speed = Random.Range(0.01f, 0.06f)
            });
    }

    void OnGUI()
    {
        if (!Application.isPlaying || drops.Count == 0) return;
        if (dropTex == null) dropTex = DropTexture();
        foreach (var dr in drops)
        {
            float s = dr.size * Screen.height;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(dr.life) * 0.85f);
            GUI.DrawTexture(new Rect(dr.pos.x * Screen.width - s * 0.5f, dr.pos.y * Screen.height - s * 0.5f, s, s * 1.25f), dropTex);
        }
        GUI.color = Color.white;
    }

    static Texture2D DropTexture()
    {
        const int N = 48;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float rim = Mathf.Clamp01(1f - Mathf.Abs(r - 0.8f) / 0.2f);          // bright rim
                float body = Mathf.Clamp01(1f - r) * 0.25f;                          // see-through middle
                float shine = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(dx, dy), new Vector2(-0.3f, 0.35f)) / 0.22f);
                float a = r > 1f ? 0f : Mathf.Max(rim * 0.55f, Mathf.Max(body, shine));
                t.SetPixel(x, y, new Color(0.85f, 0.93f, 1f, a));
            }
        t.Apply();
        return t;
    }

    static Texture2D soft;
    static Texture2D Soft()
    {
        if (soft != null) return soft;
        const int N = 64;
        soft = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)); a *= a;
                soft.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        soft.Apply();
        return soft;
    }

    static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    void OnDrawGizmosSelected()
    {
        if (track == null) return;
        track.Sample(distance, out Vector3 p, out _, out _);
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.9f);
        Gizmos.DrawWireSphere(p, poolRadius);
    }
}
