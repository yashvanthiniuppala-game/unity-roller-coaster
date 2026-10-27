// TrackBuilder.cs  —  CPS 499 HW#2 Roller Coaster Kit
// Builds the visible track from the spline: two steel rails, a spine tube,
// wooden-look cross ties and support pillars that go down to the terrain.
// Put it on the same object as CoasterTrack and press "Build Track Mesh".
// The twist / banking from CoasterTrack is included, so you SEE the rails roll.
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CoasterTrack))]
public class TrackBuilder : MonoBehaviour
{
    [Header("Rails")]
    public float gauge = 1.2f;
    public float railRadius = 0.08f;
    public float spineRadius = 0.18f;
    public float spineDrop = 0.35f;
    [Tooltip("Metres between mesh rings (smaller = smoother, heavier)")] public float step = 0.5f;

    [Header("Ties")]
    public float tieSpacing = 1.5f;
    public Vector3 tieSize = new Vector3(1.5f, 0.12f, 0.25f);

    [Header("Supports")]
    public bool buildSupports = true;
    public float supportSpacing = 8f;
    public float supportWidth = 0.4f;
    [Tooltip("Don't build a support when the track is lower than this above the ground")] public float minSupportHeight = 1.0f;
    [Tooltip("How far beside a vertical loop its two support legs stand (metres)")] public float loopLegOffset = 2.4f;

    [Header("Materials (leave empty for default colours)")]
    public Material railMaterial;
    public Material tieMaterial;
    public Material supportMaterial;
    public Color railColor = new Color(0.85f, 0.15f, 0.1f);
    public Color tieColor = new Color(0.35f, 0.25f, 0.18f);
    public Color supportColor = new Color(0.9f, 0.85f, 0.75f);

    void Start()
    {
        // Build automatically if nobody pressed the button yet.
        if (transform.Find("Generated Track") == null) Build();
    }

    [ContextMenu("Build Track Mesh")]
    public void Build()
    {
        var track = GetComponent<CoasterTrack>();
        track.Build();
        if (!track.IsBuilt) { Debug.LogWarning("TrackBuilder: the spline needs at least 2 knots."); return; }

        Clear();
        var root = new GameObject("Generated Track").transform;
        root.SetParent(transform, false);
        // Generated meshes are in WORLD space, so undo the parent's transform:
        root.position = Vector3.zero; root.rotation = Quaternion.identity;
        SetWorldScaleOne(root);

        // ---------- rails + spine ----------
        var rails = new MeshData();
        float L = track.Length;
        int rings = Mathf.Max(2, Mathf.CeilToInt(L / step));
        bool closed = track.IsClosed;
        var left = new List<Vector3[]>(); var right = new List<Vector3[]>(); var spine = new List<Vector3[]>();
        for (int i = 0; i <= rings; i++)
        {
            float d = L * i / rings;
            track.Sample(d, out var p, out var f, out var u);
            Vector3 r = Vector3.Cross(u, f).normalized;
            left.Add(Ring(p - r * gauge * 0.5f, f, u, railRadius));
            right.Add(Ring(p + r * gauge * 0.5f, f, u, railRadius));
            spine.Add(Ring(p - u * spineDrop, f, u, spineRadius));
        }
        rails.AddTube(left); rails.AddTube(right); rails.AddTube(spine);
        MakeChild(root, "Rails", rails, railMaterial, railColor);

        // ---------- ties ----------
        var ties = new MeshData();
        for (float d = 0; d < L; d += tieSpacing)
        {
            track.Sample(d, out var p, out var f, out var u);
            Quaternion q = Quaternion.LookRotation(f, u);
            ties.AddBox(p - u * (spineDrop * 0.5f), q, tieSize);
            // little connector from tie to spine
            ties.AddBox(p - u * (spineDrop * 0.75f), q, new Vector3(0.12f, spineDrop * 0.6f, 0.12f));
        }
        MakeChild(root, "Ties", ties, tieMaterial, tieColor);

        // ---------- supports ----------
        if (buildSupports)
        {
            var sup = new MeshData();
            for (float d = supportSpacing * 0.5f; d < L; d += supportSpacing)
            {
                track.Sample(d, out var p, out var f, out var u);
                Vector3 top = p - u * spineDrop;
                if (u.y < 0.2f) continue;               // upside down (in the twist): skip
                float ground;
                if (!GroundHeight(top, out ground)) continue;
                float h = top.y - ground;
                if (h < minSupportHeight) continue;
                Vector3 flatF = Vector3.ProjectOnPlane(f, Vector3.up);
                if (flatF.sqrMagnitude < 1e-4f) flatF = Vector3.forward;
                Quaternion q = Quaternion.LookRotation(flatF.normalized, Vector3.up);
                sup.AddBox(new Vector3(top.x, ground + h * 0.5f, top.z), q, new Vector3(supportWidth, h, supportWidth));
                sup.AddBox(new Vector3(top.x, ground + 0.15f, top.z), q, new Vector3(supportWidth * 2.2f, 0.3f, supportWidth * 2.2f)); // footing
            }
            // vertical loops: a leg beside each upright side, with an arm across to the track
            foreach (var z in track.loops)
            {
                if (z == null) continue;
                track.LoopSides(z, out var front, out var back, out var fOut, out var bOut, out var rightW);
                float sgn = z.sideShift >= 0f ? 1f : -1f;
                // front side: leg away from the exit track; back side: leg away from the entry track
                LoopLeg(sup, front + fOut * spineDrop, -rightW * sgn);
                LoopLeg(sup, back + bOut * spineDrop, rightW * sgn);
            }
            MakeChild(root, "Supports", sup, supportMaterial, supportColor);
        }
        // keep the cart sitting on the new rails
        if (!Application.isPlaying)
            foreach (var c in FindObjectsByType<CoasterCart>(FindObjectsInactive.Include))
                if (c.track == null || c.track == track) c.SnapToTrack();
        Debug.Log($"TrackBuilder: built {L:F0} m of track.");
    }

    void LoopLeg(MeshData sup, Vector3 spinePoint, Vector3 away)
    {
        Vector3 legTop = spinePoint + away * loopLegOffset;
        if (!GroundHeight(legTop, out float ground)) return;
        float h = legTop.y - ground;
        if (h < minSupportHeight) return;
        Quaternion up = Quaternion.LookRotation(away, Vector3.up);
        sup.AddBox(new Vector3(legTop.x, ground + h * 0.5f, legTop.z), up, new Vector3(supportWidth * 1.3f, h + supportWidth, supportWidth * 1.3f));
        sup.AddBox(new Vector3(legTop.x, ground + 0.15f, legTop.z), up, new Vector3(supportWidth * 3f, 0.3f, supportWidth * 3f));
        Vector3 mid = (legTop + spinePoint) * 0.5f;
        sup.AddBox(mid, Quaternion.LookRotation(away, Vector3.up), new Vector3(supportWidth, supportWidth, loopLegOffset));   // arm
    }

    [ContextMenu("Clear Track Mesh")]
    public void Clear()
    {
        var old = transform.Find("Generated Track");
        if (old == null) return;
        if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
    }

    static void SetWorldScaleOne(Transform t)
    {
        Vector3 ps = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(1f / Mathf.Max(ps.x, 1e-4f), 1f / Mathf.Max(ps.y, 1e-4f), 1f / Mathf.Max(ps.z, 1e-4f));
    }

    static bool GroundHeight(Vector3 p, out float y)
    {
        foreach (var t in Terrain.activeTerrains)
        {
            if (t == null || t.terrainData == null) continue;
            Vector3 o = t.transform.position, s = t.terrainData.size;
            if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z)
            { y = t.SampleHeight(p) + o.y; return true; }
        }
        if (Physics.Raycast(p + Vector3.down * 0.5f, Vector3.down, out var hit, 500f, ~0, QueryTriggerInteraction.Ignore))
        { y = hit.point.y; return true; }
        y = 0f; return false;
    }

    const int RingSides = 8;
    static Vector3[] Ring(Vector3 c, Vector3 f, Vector3 u, float radius)
    {
        var r = Vector3.Cross(u, f).normalized;
        var ring = new Vector3[RingSides];
        for (int i = 0; i < RingSides; i++)
        {
            float a = i * Mathf.PI * 2f / RingSides;
            ring[i] = c + (r * Mathf.Cos(a) + u * Mathf.Sin(a)) * radius;
        }
        return ring;
    }

    void MakeChild(Transform root, string name, MeshData data, Material mat, Color fallback)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = data.ToMesh(name);
        mr.sharedMaterial = mat != null ? mat : FxFactory.MakeLitMaterial(fallback);
    }

    // ---- tiny mesh helper ----
    class MeshData
    {
        public List<Vector3> v = new List<Vector3>();
        public List<int> tri = new List<int>();

        public void AddTube(List<Vector3[]> rings)
        {
            int start = v.Count;
            foreach (var r in rings) v.AddRange(r);
            for (int i = 0; i < rings.Count - 1; i++)
                for (int s = 0; s < RingSides; s++)
                {
                    int a = start + i * RingSides + s;
                    int b = start + i * RingSides + (s + 1) % RingSides;
                    int c = a + RingSides, d = b + RingSides;
                    tri.Add(a); tri.Add(b); tri.Add(c);
                    tri.Add(b); tri.Add(d); tri.Add(c);
                }
        }

        public void AddBox(Vector3 center, Quaternion q, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3[] n = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var nn in n)
            {
                Vector3 a = new Vector3(nn.y, nn.z, nn.x), b = Vector3.Cross(nn, a);
                int s = v.Count;
                Vector3 c = Vector3.Scale(nn, h);
                Vector3 ea = Vector3.Scale(a, h), eb = Vector3.Scale(b, h);
                v.Add(center + q * (c - ea - eb));
                v.Add(center + q * (c + ea - eb));
                v.Add(center + q * (c + ea + eb));
                v.Add(center + q * (c - ea + eb));
                tri.Add(s); tri.Add(s + 1); tri.Add(s + 2);
                tri.Add(s); tri.Add(s + 2); tri.Add(s + 3);
            }
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
