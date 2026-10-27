// RuinsCollapse.cs — ANCIENT RUINS that collapse with REAL PHYSICS as the cart rushes past.
//   * a row of tall stone columns (from your Desert pack ruins) stands beside the track,
//   * when the cart comes, a blast hits the first column and they fall like DOMINOES -
//     every column is a Rigidbody, so gravity and the collisions between the stones do all the work,
//   * dust clouds puff up and you hear a thud where the stones hit the ground,
//   * everything stands up again for the next ride.
// The columns are placed so they fall ALONG the track (never onto it).
//
// HOW TO USE:
//   1. GameObject > Create Empty, name it "Ruins", Add Component > Ruins Collapse.
//   2. It finds the column prefabs in Desert > Top-Down Desert Pack > Prefab > Ruins by itself
//      (or drag your own into "Column Prefabs").
//   3. Type "Distance" (metres from the station) - the ruins appear next to that part of the track. Press Play.
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class RuinsCollapse : MonoBehaviour
{
    [Header("Where")]
    public CoasterTrack track;
    [Tooltip("Metres from the station where the row of columns starts")] public float distance = 600f;
    [Tooltip("Metres to the side of the track (+ = right, - = left)")] public float sideOffset = 12f;
    [Range(3, 14)] public int columns = 8;
    [Tooltip("Height of each column (metres)")] public float columnHeight = 9f;
    [Tooltip("Space between columns as a part of the height (smaller = they hit each other sooner)")] [Range(0.3f, 0.9f)] public float spacing = 0.55f;

    [Header("Stones")]
    [Tooltip("Column prefabs (found automatically in Desert > Top-Down Desert Pack > Prefab > Ruins)")] public GameObject[] columnPrefabs;
    [Tooltip("Mass of one column (kg) - heavier = slower, more powerful fall")] public float mass = 800f;

    [Header("The blast")]
    [Tooltip("The first column is hit when the cart is this far away (metres)")] public float triggerAhead = 30f;
    [Range(0.5f, 3f)] public float explosionSize = 1.4f;
    public bool cameraShake = true;
    public Color dustColor = new Color(0.8f, 0.66f, 0.48f);

    [Header("Sound (optional)")]
    public AudioClip blastSound;
    [Tooltip("Played when a stone hits the ground")] public AudioClip thudSound;
    [Range(0, 1)] public float volume = 1f;

    const string RootName = "Ruins (built)";
    class Piece { public Transform t; public Rigidbody rb; public Vector3 pos; public Quaternion rot; public Vector3 fallDir; }
    readonly List<Piece> pieces = new List<Piece>();
    CoasterCart cart;
    bool fallen, queued;

    // ------------------------------------------------------------------ build
    void OnEnable()
    {
        if (Application.isPlaying) { Rebuild(); return; }
        if (transform.Find(RootName) == null) Queue();
    }
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
        if (columnPrefabs != null && columnPrefabs.Length > 0 && columnPrefabs[0] != null) return;
        var list = new List<GameObject>();
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("column t:Prefab", new[] { "Assets/Desert" }))
        {
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) list.Add(go);
        }
        if (list.Count > 0) { columnPrefabs = list.ToArray(); UnityEditor.EditorUtility.SetDirty(this); }
#endif
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        if (track == null) track = FindAnyObjectByType<CoasterTrack>();
        var old = transform.Find(RootName);
        if (old != null) Kill(old.gameObject);
        pieces.Clear(); fallen = false;
        if (track == null) { Debug.LogWarning("[Ruins] Drag your track into the 'Track' slot."); return; }
        if (track.Length < 1f) track.Build();
        FindPrefabs();

        var root = new GameObject(RootName).transform;
        root.SetParent(transform, false);
        root.position = Vector3.zero; root.rotation = Quaternion.identity;

        float gapM = columnHeight * spacing;
        // keep them far enough away that a falling column can never reach the rails
        float off = Mathf.Sign(sideOffset == 0f ? 1f : sideOffset) * Mathf.Max(Mathf.Abs(sideOffset), columnHeight + 3f);
        for (int i = 0; i < columns; i++)
        {
            float d = distance + i * gapM;
            track.Sample(d, out Vector3 p, out Vector3 f, out _);
            Vector3 flatF = new Vector3(f.x, 0f, f.z); flatF = flatF.sqrMagnitude > 1e-4f ? flatF.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, flatF);
            Vector3 pos = p + side * off;
            pos.y = Ground(pos);

            GameObject col = MakeColumn(i, root);
            col.name = "Ruin Column " + (i + 1);
            col.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(flatF, Vector3.up) * Quaternion.Euler(0f, Random.Range(-8f, 8f), 0f));
            col.transform.position += Vector3.up * (pos.y - Bounds(col).min.y);      // stand exactly on the ground

            // physics: one box collider that fits the stone + a rigidbody (asleep until the blast)
            foreach (var c in col.GetComponentsInChildren<Collider>(true)) Kill(c);
            FitBox(col);
            var rb = col.GetComponent<Rigidbody>();
            if (rb == null) rb = col.AddComponent<Rigidbody>();
            rb.mass = mass; rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var ps = col.GetComponent<RuinPiece>();
            if (ps == null) ps = col.AddComponent<RuinPiece>();
            ps.owner = this;
            pieces.Add(new Piece { t = col.transform, rb = rb, pos = col.transform.position, rot = col.transform.rotation, fallDir = flatF });
        }
#if UNITY_EDITOR
        if (!Application.isPlaying && gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    GameObject MakeColumn(int i, Transform parent)
    {
        GameObject go = null;
        GameObject prefab = columnPrefabs != null && columnPrefabs.Length > 0 ? columnPrefabs[i % columnPrefabs.Length] : null;
        if (prefab != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
#endif
            if (go == null) go = Instantiate(prefab, parent);
            if (!Application.isPlaying)
            {
#if UNITY_EDITOR
                UnityEditor.PrefabUtility.UnpackPrefabInstance(go, UnityEditor.PrefabUnpackMode.Completely, UnityEditor.InteractionMode.AutomatedAction);
#endif
            }
            // scale to the wanted height
            go.transform.localScale = Vector3.one;
            var b = Bounds(go);
            if (b.size.y > 0.01f) go.transform.localScale = Vector3.one * (columnHeight / b.size.y);
        }
        else
        {
            // no prefab found: a simple stone column
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            var mesh = go.GetComponent<MeshFilter>();
            go.transform.localScale = Vector3.one;
            Kill(go.GetComponent<Collider>());
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Kill(vis.GetComponent<Collider>());
            vis.transform.SetParent(go.transform, false);
            vis.transform.localPosition = Vector3.up * columnHeight * 0.5f;
            vis.transform.localScale = new Vector3(1.6f, columnHeight * 0.5f, 1.6f);
            vis.GetComponent<Renderer>().sharedMaterial = PipelineUtil.MakeLit(new Color(0.85f, 0.72f, 0.55f), 0.1f, 0f);
            Kill(go.GetComponent<MeshRenderer>()); Kill(mesh);
        }
        return go;
    }

    /// <summary>Adds a BoxCollider that fits all the stone's meshes (in its own space).</summary>
    static void FitBox(GameObject go)
    {
        bool any = false; Bounds lb = new Bounds();
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int k = 0; k < 8; k++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                Vector3 p = go.transform.InverseTransformPoint(r.transform.TransformPoint(c));
                if (!any) { lb = new Bounds(p, Vector3.zero); any = true; } else lb.Encapsulate(p);
            }
        }
        var box = go.AddComponent<BoxCollider>();
        if (any) { box.center = lb.center; box.size = lb.size; }
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static float Ground(Vector3 p)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain != null) return terrain.SampleHeight(p) + terrain.transform.position.y;
        if (Physics.Raycast(p + Vector3.up * 300f, Vector3.down, out RaycastHit hit, 1000f, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
        return p.y;
    }

    // ------------------------------------------------------------------ play
    void Start()
    {
        if (!Application.isPlaying) return;
        cart = FindAnyObjectByType<CoasterCart>();
        if (cart != null) cart.RideStarted += StandUp;
    }

    void Update()
    {
        if (!Application.isPlaying || cart == null || fallen || pieces.Count == 0) return;
        if (cart.state == CoasterCart.RideState.Riding && cart.distance >= distance - triggerAhead && cart.distance < distance + 20f) Collapse();
    }

    public void Collapse()
    {
        fallen = true;
        foreach (var p in pieces) if (p.rb != null) { p.rb.isKinematic = false; p.rb.WakeUp(); }
        var first = pieces[0];
        Vector3 basePos = first.t.position - first.fallDir * 2f + Vector3.up * 1f;
        FxFactory.CreateExplosion(basePos, explosionSize);
        if (blastSound != null) AudioSource.PlayClipAtPoint(blastSound, basePos, volume);
        if (cameraShake) RideCamera.Shake(0.25f * explosionSize, 0.7f);
        // push the TOP of the first column forward -> it falls onto the next one, and so on
        first.rb.AddForceAtPosition(first.fallDir * mass * 6f, first.t.position + Vector3.up * columnHeight * 0.85f, ForceMode.Impulse);
    }

    void StandUp()
    {
        fallen = false;
        foreach (var p in pieces)
        {
            if (p.t == null) continue;
            p.rb.isKinematic = true;
            p.t.SetPositionAndRotation(p.pos, p.rot);
        }
    }

    // called by RuinPiece when a stone hits something hard
    public void Dust(Vector3 at, float strength)
    {
        var go = new GameObject("Ruin Dust");
        go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.loop = false; m.playOnAwake = false; m.duration = 0.3f; m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f * strength);
        m.startSize = new ParticleSystem.MinMaxCurve(2f, 5f * strength);
        m.startColor = new Color(dustColor.r, dustColor.g, dustColor.b, 0.6f);
        m.gravityModifier = -0.03f;
        m.stopAction = ParticleSystemStopAction.Destroy;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 1.5f; sh.rotation = new Vector3(-90f, 0f, 0f);
        var c = ps.colorOverLifetime; c.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        c.color = new ParticleSystem.MinMaxGradient(g);
        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = DustMat();
        ps.Emit(Mathf.RoundToInt(20 * strength));
        ps.Play();
        if (thudSound != null) AudioSource.PlayClipAtPoint(thudSound, at, volume * Mathf.Clamp01(strength));
    }

    static Material dustMat;
    static Material DustMat()
    {
        if (dustMat != null) return dustMat;
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)); a *= a;
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        tex.Apply();
        dustMat = PipelineUtil.MakeParticleMaterial(false, tex);
        return dustMat;
    }

    static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    void OnDrawGizmosSelected()
    {
        if (track == null) return;
        Gizmos.color = new Color(0.9f, 0.75f, 0.5f);
        track.Sample(distance - triggerAhead, out Vector3 tp, out _, out _);
        Gizmos.DrawWireSphere(tp, 2f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(tp + Vector3.up * 3f, "Ruins collapse when the cart is here");
#endif
    }
}

/// <summary>Sits on every ruin column: makes dust (and a thud) when it hits the ground or another stone.</summary>
public class RuinPiece : MonoBehaviour
{
    public RuinsCollapse owner;
    float cooldown;
    void Update() { cooldown -= Time.deltaTime; }
    void OnCollisionEnter(Collision c)
    {
        if (owner == null || cooldown > 0f) return;
        float v = c.relativeVelocity.magnitude;
        if (v < 2.5f) return;
        cooldown = 0.4f;
        Vector3 at = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
        owner.Dust(at, Mathf.Clamp(v / 8f, 0.4f, 1.8f));
    }
}
