// WorldEdge.cs — fills the empty BLUE space outside the desert map.
//   * Dunes mode (default): endless sand all around the map + a ring of far-away mesas / dunes on the horizon,
//     with a soft sandy haze so the edge of the map blends in.
//   * Ocean mode: the desert becomes an island in a blue-green sea.
// Your map, track and everything on it stay exactly as they are - this only adds things OUTSIDE the map.
//
// HOW TO USE: GameObject > Create Empty, name it "World Edge", Add Component > World Edge.
// It finds the edge of your terrain by itself and builds right away. Change Mode / colours in the Inspector.
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class WorldEdge : MonoBehaviour
{
    public enum Mode { Dunes, Ocean }
    public Mode mode = Mode.Dunes;

    [Header("Ground outside the map")]
    [Tooltip("How far the ground goes (metres)")] public float size = 8000f;
    [Tooltip("Move the outside ground up / down if it doesn't meet the edge of the map")] public float heightOffset = -0.5f;
    public Color sandColor = new Color(0.86f, 0.69f, 0.47f);
    public Color seaColor = new Color(0.12f, 0.5f, 0.62f);

    [Header("Horizon")]
    [Tooltip("A ring of far-away mesas / dunes all around")] public bool horizonHills = true;
    [Tooltip("How far the hills are from the edge of the map")] public float hillDistance = 450f;
    public float hillHeight = 140f;
    public Color hillColor = new Color(0.72f, 0.5f, 0.33f);

    [Header("Haze (only while playing)")]
    [Tooltip("Soft haze far away so the horizon blends into the sky")] public bool haze = true;
    public Color hazeColor = new Color(0.86f, 0.8f, 0.72f);
    public float hazeStart = 450f;
    public float hazeEnd = 2600f;

    const string RootName = "World Edge (built)";
    bool queued;

    void OnEnable() { if (transform.Find(RootName) == null || Application.isPlaying) Queue(); }
    void OnValidate() { Queue(); }

    void Awake()
    {
        // set the haze before other scripts remember the "normal" fog (e.g. Sand Tornado)
        if (Application.isPlaying && haze)
        {
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = mode == Mode.Ocean ? Color.Lerp(hazeColor, new Color(0.7f, 0.85f, 0.95f), 0.6f) : hazeColor;
            RenderSettings.fogStartDistance = hazeStart; RenderSettings.fogEndDistance = hazeEnd;
        }
    }

    void Queue()
    {
#if UNITY_EDITOR
        if (Application.isPlaying) return;
        if (queued) return;
        queued = true;
        UnityEditor.EditorApplication.delayCall += () => { queued = false; if (this != null && isActiveAndEnabled) Rebuild(); };
#endif
    }

    /// <summary>Finds the map area (all terrains together, or the track area if there is no terrain) and its edge height.</summary>
    bool MapArea(out Rect area, out float edgeY)
    {
        area = new Rect(); edgeY = 0f;
        var terrains = Terrain.activeTerrains;
        if (terrains != null && terrains.Length > 0)
        {
            bool first = true; float minX = 0, minZ = 0, maxX = 0, maxZ = 0;
            foreach (var t in terrains)
            {
                if (t == null || t.terrainData == null) continue;
                Vector3 p = t.GetPosition(), s = t.terrainData.size;
                if (first) { minX = p.x; minZ = p.z; maxX = p.x + s.x; maxZ = p.z + s.z; first = false; }
                else { minX = Mathf.Min(minX, p.x); minZ = Mathf.Min(minZ, p.z); maxX = Mathf.Max(maxX, p.x + s.x); maxZ = Mathf.Max(maxZ, p.z + s.z); }
            }
            if (!first)
            {
                area = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
                // height along the border (lowest point) so the outside ground meets the map edge
                float lo = float.MaxValue;
                for (int i = 0; i <= 40; i++)
                {
                    float f = i / 40f;
                    foreach (var q in new[] { new Vector3(Mathf.Lerp(minX, maxX, f), 0, minZ + 1f), new Vector3(Mathf.Lerp(minX, maxX, f), 0, maxZ - 1f),
                                              new Vector3(minX + 1f, 0, Mathf.Lerp(minZ, maxZ, f)), new Vector3(maxX - 1f, 0, Mathf.Lerp(minZ, maxZ, f)) })
                        lo = Mathf.Min(lo, HeightAt(q));
                }
                edgeY = lo;
                return true;
            }
        }
        // no terrain: use the area around the track
        var track = FindAnyObjectByType<CoasterTrack>();
        if (track == null) return false;
        if (track.Length < 1f) track.Build();
        Bounds b = new Bounds(); bool any = false;
        for (float d = 0f; d < track.Length; d += 5f)
        {
            track.Sample(d, out Vector3 p, out _, out _);
            if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
        }
        b.Expand(new Vector3(300f, 0f, 300f));
        area = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
        edgeY = Physics.Raycast(b.center + Vector3.up * 500f, Vector3.down, out RaycastHit hit, 2000f) ? hit.point.y : b.min.y - 5f;
        return true;
    }

    static float HeightAt(Vector3 p)
    {
        foreach (var t in Terrain.activeTerrains)
        {
            if (t == null || t.terrainData == null) continue;
            Vector3 tp = t.GetPosition(), s = t.terrainData.size;
            if (p.x >= tp.x && p.x <= tp.x + s.x && p.z >= tp.z && p.z <= tp.z + s.z) return t.SampleHeight(p) + tp.y;
        }
        return 0f;
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        var old = transform.Find(RootName);
        if (old != null) { if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject); }
        if (!MapArea(out Rect area, out float edgeY)) { Debug.LogWarning("[World Edge] Couldn't find a terrain or a track."); return; }

        var root = new GameObject(RootName).transform;
        root.SetParent(transform, false);
        root.position = Vector3.zero; root.rotation = Quaternion.identity; root.localScale = Vector3.one;
        float y = edgeY + heightOffset;

        // 1) the outside ground: a huge square with a hole where your map is
        var ground = new GameObject(mode == Mode.Ocean ? "Sea" : "Outside Sand");
        ground.transform.SetParent(root, false);
        ground.AddComponent<MeshFilter>().sharedMesh = RingQuad(area, size, y, mode == Mode.Ocean ? 40f : 0.5f);
        Material gm = mode == Mode.Ocean ? PipelineUtil.MakeLit(seaColor, 0.92f, 0f) : PipelineUtil.MakeLit(sandColor, 0.12f, 0f);
        ground.AddComponent<MeshRenderer>().sharedMaterial = gm;

        // 2) far-away hills on the horizon
        if (horizonHills && mode == Mode.Dunes)
        {
            var hills = new GameObject("Horizon Hills");
            hills.transform.SetParent(root, false);
            hills.AddComponent<MeshFilter>().sharedMesh = HillRing(area, y, hillDistance, hillHeight);
            hills.AddComponent<MeshRenderer>().sharedMaterial = PipelineUtil.MakeLit(hillColor, 0.08f, 0f);
        }
#if UNITY_EDITOR
        if (!Application.isPlaying && gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    /// <summary>Big flat square at height y with a rectangular hole (the map). 'overlap' tucks the hole edge slightly under the map.</summary>
    static Mesh RingQuad(Rect hole, float size, float y, float overlap)
    {
        Vector2 c = hole.center;
        float h = size * 0.5f;
        Rect inner = Rect.MinMaxRect(hole.xMin + overlap, hole.yMin + overlap, hole.xMax - overlap, hole.yMax - overlap);
        var v = new List<Vector3>
        {
            new Vector3(c.x - h, y, c.y - h), new Vector3(c.x + h, y, c.y - h), new Vector3(c.x + h, y, c.y + h), new Vector3(c.x - h, y, c.y + h),   // outer 0-3
            new Vector3(inner.xMin, y, inner.yMin), new Vector3(inner.xMax, y, inner.yMin), new Vector3(inner.xMax, y, inner.yMax), new Vector3(inner.xMin, y, inner.yMax), // inner 4-7
        };
        // 4 strips between outer and inner edge (facing up)
        var t = new List<int> { 0, 4, 1, 1, 4, 5,   1, 5, 2, 2, 5, 6,   2, 6, 3, 3, 6, 7,   3, 7, 0, 0, 7, 4 };
        var uv = new List<Vector2>();
        foreach (var p in v) uv.Add(new Vector2(p.x / 20f, p.z / 20f));
        var m = new Mesh { name = "Outside Ground" };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    /// <summary>A ring of bumpy mesas / dunes around the map, facing inwards.</summary>
    static Mesh HillRing(Rect area, float y, float distance, float height)
    {
        const int n = 160;
        Vector2 c = area.center;
        float baseR = Mathf.Max(area.width, area.height) * 0.72f + distance;
        var v = new List<Vector3>(); var t = new List<int>();
        var rng = new System.Random(4);
        float[] phase = { (float)rng.NextDouble() * 10f, (float)rng.NextDouble() * 10f, (float)rng.NextDouble() * 10f };
        for (int i = 0; i <= n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            // layered waves = natural-looking skyline; flat tops look like mesas
            float hgt = 0.55f + 0.25f * Mathf.Sin(a * 3f + phase[0]) + 0.15f * Mathf.Sin(a * 7f + phase[1]) + 0.08f * Mathf.Sin(a * 17f + phase[2]);
            hgt = Mathf.Clamp(hgt, 0.2f, 0.85f) * height;
            if (hgt > height * 0.7f) hgt = height * 0.7f + (hgt - height * 0.7f) * 0.2f;           // mesa tops
            float r = baseR + 80f * Mathf.Sin(a * 5f + phase[1]);
            Vector2 inner = c + dir * r, top = c + dir * (r + 180f), outer = c + dir * (r + 420f);
            v.Add(new Vector3(inner.x, y - 1f, inner.y));
            v.Add(new Vector3(top.x, y + hgt, top.y));
            v.Add(new Vector3(outer.x, y + hgt * 0.4f, outer.y));
        }
        for (int i = 0; i < n; i++)
        {
            int k = i * 3, k2 = (i + 1) * 3;
            t.AddRange(new[] { k, k2, k + 1, k + 1, k2, k2 + 1 });            // inner slope (faces the map)
            t.AddRange(new[] { k + 1, k2 + 1, k + 2, k + 2, k2 + 1, k2 + 2 }); // back slope
        }
        var m = new Mesh { name = "Horizon Hills" };
        m.SetVertices(v); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }
}
