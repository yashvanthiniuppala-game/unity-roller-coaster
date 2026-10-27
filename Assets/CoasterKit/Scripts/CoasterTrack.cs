// CoasterTrack.cs  —  CPS 499 HW#2 Roller Coaster Kit
// Put this on the same GameObject as your SplineContainer.
// It turns the spline into a "track": it measures the length in metres,
// works out which way is "up" for the cart (including twists / banking),
// and marks the special sections (lift hill, brakes) by KNOT NUMBER.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

[ExecuteAlways]
[RequireComponent(typeof(SplineContainer))]
public class CoasterTrack : MonoBehaviour
{
    [System.Serializable]
    public class RollZone
    {
        public string label = "Twist";
        [Tooltip("Knot number where the roll starts")] public int startKnot = 10;
        [Tooltip("Knot number where the roll ends")] public int endKnot = 11;
        [Tooltip("360 = one full barrel roll. Banked turn: + leans right, - leans left, e.g. 35 and tick 'Bank And Return'.")]
        public float degrees = 360f;
        [Tooltip("ON = lean to 'degrees' and come back level (banked turn). OFF = keep rotating (twist).")]
        public bool bankAndReturn = false;
    }

    [System.Serializable]
    public class LoopZone
    {
        public string label = "Vertical loop";
        [Tooltip("Straight lead-in knot (the loop is made by the 'Add a Vertical Loop' button - use its Remove button to delete it)")] public int startKnot;
        [Tooltip("Straight lead-out knot")] public int endKnot;
        public float radius = 9f;
        public float heightStretch = 1.25f;
        public float sideShift = 4f;
        [HideInInspector] public Vector3 bottomLocal, forwardLocal, rightLocal;
    }

    [Header("Special sections (use the knot numbers shown in the Scene view)")]
    [Tooltip("Knot where the chain lift starts pulling the cart up")] public int liftStartKnot = 1;
    [Tooltip("Knot at the top of the lift hill (cart is released here)")] public int liftEndKnot = 4;
    [Tooltip("Knot where the brakes start. The cart stops at knot 0 (the station). -1 = last knot")]
    public int brakeStartKnot = -1;
    public List<RollZone> rollZones = new List<RollZone>();
    [Tooltip("Vertical loops. Add / remove them with the buttons at the top of this component.")]
    public List<LoopZone> loops = new List<LoopZone>();

    // settings for the "Add a Vertical Loop" tool (drawn by the custom Inspector)
    [HideInInspector] public int newLoopAtKnot = 10;
    [HideInInspector] public float newLoopRadius = 9f;
    [HideInInspector] public float newLoopHeightStretch = 1.25f;
    [HideInInspector] public float newLoopSideShift = 4f;
    [HideInInspector] public float newLoopStraight = 12f;

    [Header("Accuracy")]
    [Range(500, 20000)] public int samples = 4000;
    public bool showKnotNumbers = true;

    // ---- look-up table (distance along track -> position / direction / up) ----
    float[] lutDist;
    Vector3[] lutPos, lutFwd, lutUp;
    float[] knotDist = new float[0];
    SplineContainer container;
    float lastBuildTime = -10f;

    public float Length { get; private set; }
    public bool IsClosed => Container != null && Container.Spline != null && Container.Spline.Closed;
    public SplineContainer Container { get { if (container == null) container = GetComponent<SplineContainer>(); return container; } }
    public int KnotCount => Container != null && Container.Spline != null ? Container.Spline.Count : 0;
    public bool IsBuilt => lutDist != null && lutDist.Length > 1;

    void OnEnable() { Build(); }
    void OnValidate() { lastBuildTime = -10f; }

    /// <summary>Re-measures the spline. Call after you move knots (TrackBuilder / cart do it for you).</summary>
    [ContextMenu("Rebuild Track Data")]
    public void Build()
    {
        lastBuildTime = Time.realtimeSinceStartup;
        var c = Container;
        if (c == null || c.Spline == null || c.Spline.Count < 2) { lutDist = null; Length = 0; return; }

        int n = Mathf.Max(100, samples);
        lutDist = new float[n + 1];
        lutPos = new Vector3[n + 1];
        lutFwd = new Vector3[n + 1];
        lutUp = new Vector3[n + 1];

        Vector3 prevUp = Vector3.up;
        float d = 0f;
        for (int i = 0; i <= n; i++)
        {
            float t = (float)i / n;
            Vector3 p = (Vector3)c.EvaluatePosition(t);
            Vector3 f = ((Vector3)c.EvaluateTangent(t));
            if (f.sqrMagnitude < 1e-8f) f = i > 0 ? lutFwd[i - 1] : Vector3.forward;
            f.Normalize();

            // "Up" = world up flattened against the track direction (track stays level
            // unless a RollZone tilts it). Near-vertical sections reuse the previous up.
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, f);
            if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(prevUp, f);
            up.Normalize();
            prevUp = up;

            if (i > 0) d += Vector3.Distance(p, lutPos[i - 1]);
            lutDist[i] = d; lutPos[i] = p; lutFwd[i] = f; lutUp[i] = up;
        }
        Length = d;

        // Where is each knot along the track (in metres)?
        int k = c.Spline.Count;
        knotDist = new float[k];
        int searchFrom = 0;
        for (int ki = 0; ki < k; ki++)
        {
            Vector3 kp = c.transform.TransformPoint((Vector3)c.Spline[ki].Position);
            int best = searchFrom; float bestSq = float.MaxValue;
            for (int i = searchFrom; i <= n; i++)
            {
                float sq = (lutPos[i] - kp).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            knotDist[ki] = lutDist[best];
            searchFrom = best;
        }
        if (k > 0) knotDist[0] = 0f;

        // Vertical loops: inside a loop "up" points to the loop's centre (so the cart
        // rides on the INSIDE, upside down at the top) instead of towards the sky.
        foreach (var z in loops)
        {
            if (z == null) continue;
            float a = KnotDistRaw(z.startKnot), a2 = KnotDistRaw(z.startKnot + 1);
            float b2 = KnotDistRaw(z.endKnot - 1), b = KnotDistRaw(z.endKnot);
            if (!(a < a2 && a2 < b2 && b2 < b)) continue;
            Vector3 right = transform.TransformDirection(z.rightLocal).normalized;
            for (int i = 0; i <= n; i++)
            {
                float di = lutDist[i];
                if (di < a || di > b) continue;
                Vector3 loopUp = Vector3.Cross(lutFwd[i], right);
                if (loopUp.sqrMagnitude < 1e-6f) continue;
                loopUp.Normalize();
                float w = di < a2 ? Mathf.SmoothStep(0f, 1f, (di - a) / (a2 - a))
                        : di > b2 ? Mathf.SmoothStep(0f, 1f, (b - di) / (b - b2)) : 1f;
                Vector3 u = Vector3.Slerp(lutUp[i], loopUp, w);
                u = Vector3.ProjectOnPlane(u, lutFwd[i]);
                if (u.sqrMagnitude > 1e-6f) lutUp[i] = u.normalized;
            }
        }

        // Apply twists / banking on top of the level "up".
        for (int i = 0; i <= n; i++)
        {
            float roll = RollAt(lutDist[i]);
            if (Mathf.Abs(roll) > 0.001f) lutUp[i] = Quaternion.AngleAxis(roll, lutFwd[i]) * lutUp[i];
        }
    }

    void EnsureBuilt() { if (!IsBuilt) Build(); }

    /// <summary>Distance (metres from knot 0) of a knot. -1 means "end of the track".</summary>
    public float KnotDistance(int knot)
    {
        EnsureBuilt();
        if (knotDist == null || knotDist.Length == 0) return 0f;
        if (knot < 0 || knot >= knotDist.Length) return Length;
        return knotDist[knot];
    }

    public float LiftStart => KnotDistance(liftStartKnot);
    public float LiftEnd => KnotDistance(liftEndKnot);
    public float BrakeStart => brakeStartKnot < 0 ? (KnotCount > 0 ? KnotDistance(KnotCount - 1) : Length) : KnotDistance(brakeStartKnot);

    float RollAt(float dist)
    {
        float total = 0f;
        foreach (var z in rollZones)
        {
            if (z == null) continue;
            float a = KnotDistRaw(z.startKnot), b = KnotDistRaw(z.endKnot);
            if (b <= a) continue;
            float x = Mathf.Clamp01((dist - a) / (b - a));
            float eased = Mathf.SmoothStep(0f, 1f, x);
            total += z.bankAndReturn ? Mathf.Sin(eased * Mathf.PI) * z.degrees : eased * z.degrees;
        }
        return total;
    }
    float KnotDistRaw(int knot)
    {
        if (knotDist == null || knotDist.Length == 0) return 0f;
        if (knot < 0 || knot >= knotDist.Length) return Length;
        return knotDist[knot];
    }

    /// <summary>Wraps (closed track) or clamps (open track) a distance into [0, Length].</summary>
    public float Wrap(float dist)
    {
        if (Length <= 0f) return 0f;
        if (IsClosed) { dist %= Length; if (dist < 0f) dist += Length; return dist; }
        return Mathf.Clamp(dist, 0f, Length);
    }

    /// <summary>Gets position, forward and up direction at a distance (metres) along the track.</summary>
    public void Sample(float dist, out Vector3 pos, out Vector3 forward, out Vector3 up)
    {
        EnsureBuilt();
        if (!IsBuilt) { pos = transform.position; forward = transform.forward; up = Vector3.up; return; }
        dist = Wrap(dist);
        // binary search
        int lo = 0, hi = lutDist.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (lutDist[mid] <= dist) lo = mid; else hi = mid;
        }
        float seg = lutDist[hi] - lutDist[lo];
        float f = seg > 1e-6f ? (dist - lutDist[lo]) / seg : 0f;
        pos = Vector3.Lerp(lutPos[lo], lutPos[hi], f);
        forward = Vector3.Slerp(lutFwd[lo], lutFwd[hi], f).normalized;
        up = Vector3.Slerp(lutUp[lo], lutUp[hi], f).normalized;
    }

    public bool InRange(float dist, float a, float b) { return dist >= a && dist < b; }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!Application.isPlaying && Time.realtimeSinceStartup - lastBuildTime > 0.5f) Build();
        if (!IsBuilt) return;

        float step = Mathf.Max(1f, Length / 600f);
        float ls = LiftStart, le = LiftEnd, bs = BrakeStart;
        for (float d = 0; d < Length; d += step)
        {
            Sample(d, out var p, out var f, out var u);
            Color col = Color.white;
            if (InRange(d, ls, le)) col = Color.yellow;
            else if (d >= bs) col = Color.red;
            else if (Mathf.Abs(RollAt(d)) > 1f) col = Color.magenta;
            else if (InLoop(d)) col = Color.green;
            else continue;
            Gizmos.color = col;
            Gizmos.DrawLine(p, p + u * 1.5f);
        }

        if (showKnotNumbers && Container != null && Container.Spline != null)
        {
            var style = new GUIStyle();
            style.normal.textColor = Color.cyan; style.fontSize = 16; style.fontStyle = FontStyle.Bold;
            for (int i = 0; i < Container.Spline.Count; i++)
            {
                Vector3 kp = Container.transform.TransformPoint((Vector3)Container.Spline[i].Position);
                UnityEditor.Handles.Label(kp + Vector3.up * 2.5f, "K" + i + (i == 0 ? " (station)" : ""), style);
            }
        }
    }
#endif

    // ------------------------------------------------------------------
    // Starter layout: one click gives you a complete track that meets the
    // homework rules (lift hill, big drop, right turn, camel hill, a 360°
    // twist, a left S-bend, a low section for a pond, brakes, station).
    // Move / reshape the knots afterwards with the Spline tool.
    // ------------------------------------------------------------------
    public static readonly Vector3[] StarterKnots =
    {
        new Vector3(  0,  0.0f,   0),  // K0  station
        new Vector3(  0,  0.0f,  18),  // K1  lift starts
        new Vector3(  0, 14.0f,  42),  // K2
        new Vector3(  0, 29.0f,  66),  // K3
        new Vector3(  0, 32.0f,  80),  // K4  top of lift (release)
        new Vector3(  0,  4.0f, 112),  // K5  bottom of first drop
        new Vector3( 14,  1.2f, 138),  // K6  valley: pond 2 here, start of right turn
        new Vector3( 44, 11.0f, 156),  // K7
        new Vector3( 72, 18.0f, 146),  // K8  camel hill (air-time)
        new Vector3( 82,  9.0f, 118),  // K9
        new Vector3( 82,  7.0f,  92),  // K10 twist starts
        new Vector3( 82,  7.0f,  58),  // K11 twist ends
        new Vector3( 94,  5.0f,  32),  // K12 left S-bend
        new Vector3( 86,  3.0f,   6),  // K13
        new Vector3( 56,  1.2f, -18),  // K14 low: put a pond here!
        new Vector3( 22,  1.0f, -26),  // K15 brakes start
        new Vector3(  4,  0.5f, -14),  // K16 into the station
    };

    [Header("Where things are (filled in by the layout generators)")]
    public int twistStartKnot = 10, twistEndKnot = 11;
    public int[] pondKnots = { 6, 14 };
    public int explosionKnot = 12;   // explosion happens between this knot and the next
    public int skullKnot = 9;

    /// <param name="horizontalScale">1 = full size (~490 m). Smaller (e.g. 0.65) squeezes it for small terrains.</param>
    public void GenerateStarterLayout(float horizontalScale = 1f)
    {
        var pts = new Vector3[StarterKnots.Length];
        for (int i = 0; i < pts.Length; i++)
            pts[i] = new Vector3(StarterKnots[i].x * horizontalScale, StarterKnots[i].y, StarterKnots[i].z * horizontalScale);
        SetKnots(pts);

        loops.Clear();
        liftStartKnot = 1; liftEndKnot = 4; brakeStartKnot = 15;
        twistStartKnot = 10; twistEndKnot = 11; pondKnots = new[] { 6, 14 }; explosionKnot = 12; skullKnot = 9;
        rollZones.Clear();
        rollZones.Add(new RollZone { label = "Barrel roll twist", startKnot = 10, endKnot = 11, degrees = 360f, bankAndReturn = false });
        rollZones.Add(new RollZone { label = "Bank into right turn", startKnot = 6, endKnot = 9, degrees = 35f, bankAndReturn = true });
        rollZones.Add(new RollZone { label = "Bank into left bend", startKnot = 11, endKnot = 13, degrees = -25f, bankAndReturn = true });
        Build();
    }

    // ------------------------------------------------------------------
    // GRAND TOUR: a big loop around the whole desert.
    //   width/depth = size of the area it covers (metres), liftHeight = first hill.
    //   Lift hill is built in metres (always the same steepness); the rest of the
    //   course is spread over the area: west side -> north edge -> east side (twist)
    //   -> south edge (pond) -> left S-bend -> brakes -> station.
    // ------------------------------------------------------------------
    public void GenerateGrandTour(float width, float depth, float liftHeight, float squeezeX = 1f, float squeezeZ = 1f)
    {
        float W = width, D = depth, H = liftHeight;
        float x0 = 0.08f * W, z0 = 0.10f * D;
        var k = new List<Vector3>
        {
            new Vector3(x0, 0f, z0),                      // K0  station
            new Vector3(x0, 0f, z0 + 18f),                // K1  lift starts
            new Vector3(x0, H * 0.5f, z0 + 48f),          // K2
            new Vector3(x0, H, z0 + 76f),                 // K3
            new Vector3(x0, H + 1f, z0 + 88f),            // K4  top of lift
        };
        float zDrop = z0 + 88f + H * 1.05f;
        k.Add(new Vector3(x0 + 0.02f * W, 3f, zDrop));                                    // K5  bottom of the big drop
        float z6 = Mathf.Max(0.55f * D, zDrop + 45f);
        k.Add(new Vector3(0.13f * W, 1.5f, z6));                                          // K6  pond 1 (west valley)
        float z7 = Mathf.Max(0.75f * D, z6 + 40f);
        k.Add(new Vector3(0.12f * W, H * 0.55f, z7));                                     // K7  big hill
        float z8 = Mathf.Max(0.92f * D, z7 + 35f);
        k.Add(new Vector3(0.25f * W, 5f, z8));                                            // K8  right turn (now heading east)
        k.Add(new Vector3(0.50f * W, H * 0.42f, Mathf.Max(0.95f * D, z8 + 5f)));          // K9  camel hill (skull here)
        k.Add(new Vector3(0.75f * W, 5f, Mathf.Max(0.90f * D, z8)));                      // K10
        k.Add(new Vector3(0.92f * W, H * 0.30f, 0.75f * Mathf.Max(D, z8)));               // K11 right turn (now heading south)
        k.Add(new Vector3(0.93f * W, 7f, 0.58f * Mathf.Max(D, z8)));                      // K12 twist starts
        k.Add(new Vector3(0.93f * W, 7f, 0.40f * Mathf.Max(D, z8)));                      // K13 twist ends
        k.Add(new Vector3(0.85f * W, 5f, 0.20f * D));                                     // K14 right turn (now heading west)
        k.Add(new Vector3(0.62f * W, 1.2f, 0.12f * D));                                   // K15 pond 2 (south valley)
        k.Add(new Vector3(0.45f * W, H * 0.20f, 0.24f * D));                              // K16 left S-bend hill
        k.Add(new Vector3(0.30f * W, 2.5f, 0.12f * D));                                   // K17 brakes start
        k.Add(new Vector3(Mathf.Max(0.18f * W, x0 + 30f), 1f, Mathf.Min(0.03f * D, z0 - 12f))); // K18 curve into station
        for (int i = 0; i < k.Count; i++) k[i] = new Vector3(k[i].x * squeezeX, k[i].y, k[i].z * squeezeZ);   // fit the desert
        SetKnots(k.ToArray());

        loops.Clear();
        liftStartKnot = 1; liftEndKnot = 4; brakeStartKnot = 17;
        twistStartKnot = 12; twistEndKnot = 13; pondKnots = new[] { 6, 15 }; explosionKnot = 16; skullKnot = 9;
        rollZones.Clear();
        rollZones.Add(new RollZone { label = "Barrel roll twist", startKnot = 12, endKnot = 13, degrees = 360f, bankAndReturn = false });
        rollZones.Add(new RollZone { label = "Bank: turn onto north edge", startKnot = 7, endKnot = 9, degrees = 35f, bankAndReturn = true });
        rollZones.Add(new RollZone { label = "Bank: turn onto east side", startKnot = 10, endKnot = 12, degrees = 30f, bankAndReturn = true });
        rollZones.Add(new RollZone { label = "Bank: turn onto south edge", startKnot = 13, endKnot = 15, degrees = 30f, bankAndReturn = true });
        rollZones.Add(new RollZone { label = "Bank: left S-bend", startKnot = 15, endKnot = 16, degrees = -25f, bankAndReturn = true });
        Build();
    }

    /// <summary>Knot position in the track's local space.</summary>
    public Vector3 KnotLocal(int i) { return (Vector3)Container.Spline[i].Position; }

    /// <summary>Moves one knot up/down (local space), keeping its smooth curve.</summary>
    public void SetKnotHeight(int i, float localY)
    {
        var k = Container.Spline[i];
        var p = k.Position; p.y = localY; k.Position = p;
        Container.Spline.SetKnot(i, k);
    }

    // ------------------------------------------------------------------
    // VERTICAL LOOP TOOL
    // A loop is built around one existing knot K (its lowest point):
    //   straight lead-in -> K -> 11 knots round an upright oval -> exit -> straight lead-out.
    // The exit is shifted sideways by 'sideShift' metres so the track doesn't hit itself.
    // ------------------------------------------------------------------
    public const int LoopArcKnots = 11;                 // one knot every 30 degrees
    public const int LoopAddedKnots = LoopArcKnots + 3; // + lead-in, exit, lead-out

    public bool InLoop(float dist)
    {
        foreach (var z in loops)
            if (z != null && dist >= KnotDistRaw(z.startKnot) && dist <= KnotDistRaw(z.endKnot)) return true;
        return false;
    }

    int BrakeKnotIndex => brakeStartKnot < 0 ? KnotCount - 1 : brakeStartKnot;

    /// <summary>null = OK, otherwise a plain-English reason why no loop can go at knot k.</summary>
    public string CanInsertLoop(int k, float straight)
    {
        int n = KnotCount;
        if (n < 4) return "The track needs at least 4 knots.";
        if (k <= 0 || k >= n) return $"Pick a knot between 1 and {n - 1}.";
        if (k <= liftEndKnot) return $"K{k} is on the station / lift hill. Pick a knot AFTER the top of the lift (K{liftEndKnot}).";
        if (k >= BrakeKnotIndex) return $"K{k} is in the brakes. Pick a knot BEFORE K{BrakeKnotIndex}.";
        foreach (var z in rollZones)
            if (z != null && z.startKnot < k && z.endKnot > k)
                return $"K{k} is inside the roll zone '{z.label}' (K{z.startKnot}-K{z.endKnot}). Pick another knot, or delete that roll zone first.";
        foreach (var z in loops)
            if (z != null && k >= z.startKnot && k <= z.endKnot)
                return $"K{k} is already part of '{z.label}'.";
        Vector3 o = KnotLocal(k), prev = KnotLocal(k - 1), next = KnotLocal((k + 1) % n);
        float room = straight + 6f;
        if (Flat(o - prev).magnitude < room) return $"K{k-1} is too close to K{k} (needs {room:F0} m). Pick another knot or make 'Straight before/after' shorter.";
        if (Flat(next - o).magnitude < room) return $"K{(k+1)%n} is too close to K{k} (needs {room:F0} m). Pick another knot or make 'Straight before/after' shorter.";
        return null;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    void LoopFrame(int k, out Vector3 o, out Vector3 f, out Vector3 u, out Vector3 r)
    {
        int n = KnotCount;
        o = KnotLocal(k);
        u = transform.InverseTransformDirection(Vector3.up).normalized;
        f = Vector3.ProjectOnPlane(KnotLocal((k + 1) % n) - KnotLocal((k - 1 + n) % n), u);
        if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
        f.Normalize();
        r = Vector3.Cross(u, f).normalized;
    }

    static Vector3 LoopPoint(Vector3 o, Vector3 f, Vector3 u, Vector3 r, float rad, float st, float shift, float th)
    {
        float phi = th + Mathf.PI * 0.5f;                     // 0 at the entry, 2π at the exit
        float side = shift * (phi - Mathf.Sin(phi)) / (2f * Mathf.PI);   // smooth sideways drift
        return o + u * (rad * st) + f * (rad * Mathf.Cos(th)) + u * (rad * st * Mathf.Sin(th)) + r * side;
    }

    static Vector3 LoopDeriv(Vector3 f, Vector3 u, Vector3 r, float rad, float st, float shift, float th)
    {
        float phi = th + Mathf.PI * 0.5f;
        return f * (-rad * Mathf.Sin(th)) + u * (rad * st * Mathf.Cos(th)) + r * (shift * (1f - Mathf.Cos(phi)) / (2f * Mathf.PI));
    }

    /// <summary>World-space outline of the loop that WOULD be built at knot k (for the Scene view preview).</summary>
    public List<Vector3> PreviewLoop(int k, float rad, float st, float shift, float straight)
    {
        var pts = new List<Vector3>();
        if (k <= 0 || k >= KnotCount) return pts;
        LoopFrame(k, out var o, out var f, out var u, out var r);
        pts.Add(transform.TransformPoint(o - f * straight));
        for (int i = 0; i <= 72; i++)
            pts.Add(transform.TransformPoint(LoopPoint(o, f, u, r, rad, st, shift, -Mathf.PI * 0.5f + i * Mathf.PI * 2f / 72f)));
        pts.Add(transform.TransformPoint(o + r * shift + f * straight));
        return pts;
    }

    /// <summary>Height of the loop top above its bottom (metres).</summary>
    public static float LoopTopHeight(float rad, float st) { return 2f * rad * st; }

    /// <summary>Distance along the track of the top of a loop.</summary>
    public float LoopTopDistance(LoopZone z) { return KnotDistance(z.startKnot + 1 + LoopArcKnots / 2 + 1); }

    /// <summary>World positions of the loop's front and back (the two upright sides) - used for support legs.</summary>
    public void LoopSides(LoopZone z, out Vector3 front, out Vector3 back, out Vector3 frontOut, out Vector3 backOut, out Vector3 rightW)
    {
        Vector3 o = z.bottomLocal, f = z.forwardLocal, r = z.rightLocal;
        Vector3 u = transform.InverseTransformDirection(Vector3.up).normalized;
        front = transform.TransformPoint(LoopPoint(o, f, u, r, z.radius, z.heightStretch, z.sideShift, 0f));
        back = transform.TransformPoint(LoopPoint(o, f, u, r, z.radius, z.heightStretch, z.sideShift, Mathf.PI));
        frontOut = transform.TransformDirection(f).normalized;
        backOut = -frontOut;
        rightW = transform.TransformDirection(r).normalized;
    }

    static int MapKnot(int i, int k, int add) { return i < k ? i : (i == k ? k + 1 : i + add); }

    /// <summary>Builds a vertical loop at knot k. Returns null when it worked, otherwise the reason.</summary>
    public string InsertLoop(int k, float rad, float st, float shift, float straight)
    {
        rad = Mathf.Clamp(rad, 4f, 30f);
        st = Mathf.Clamp(st, 1f, 1.6f);
        straight = Mathf.Clamp(straight, 4f, 40f);
        if (Mathf.Abs(shift) < 3f) shift = shift < 0f ? -3f : 3f;   // cart is 1.6 m wide: keep entry and exit apart
        string err = CanInsertLoop(k, straight);
        if (err != null) return err;

        var spline = Container.Spline;
        int n = KnotCount;
        LoopFrame(k, out var o, out var f, out var u, out var r);
        Vector3 prevP = KnotLocal(k - 1), nextP = KnotLocal((k + 1) % n);
        float dTh = Mathf.PI * 2f / (LoopArcKnots + 1);
        float h = 4f / 3f * Mathf.Tan(dTh / 4f);             // Bezier handle length for a round arc
        var q = quaternion.identity;

        Vector3 L = o - f * straight;
        Vector3 B = o + r * shift;
        Vector3 M = B + f * straight;
        Vector3 d0 = LoopDeriv(f, u, r, rad, st, shift, -Mathf.PI * 0.5f) * h;

        // the chosen knot becomes the bottom of the loop (entry)
        spline.SetTangentMode(k, TangentMode.Broken);
        spline.SetKnot(k, new BezierKnot(F3(o), F3(-f * straight / 3f), F3(d0), q));
        int idx = k + 1;
        for (int j = 1; j <= LoopArcKnots; j++)
        {
            float th = -Mathf.PI * 0.5f + j * dTh;
            Vector3 p = LoopPoint(o, f, u, r, rad, st, shift, th);
            Vector3 d = LoopDeriv(f, u, r, rad, st, shift, th) * h;
            spline.Insert(idx++, new BezierKnot(F3(p), F3(-d), F3(d), q), TangentMode.Broken);
        }
        spline.Insert(idx++, new BezierKnot(F3(B), F3(-d0), F3(f * straight / 3f), q), TangentMode.Broken);                 // exit
        spline.Insert(idx++, new BezierKnot(F3(M), F3(-f * straight / 3f), F3(f * (nextP - M).magnitude / 3f), q), TangentMode.Broken); // lead-out
        spline.Insert(k, new BezierKnot(F3(L), F3(-f * (L - prevP).magnitude / 3f), F3(f * straight / 3f), q), TangentMode.Broken);    // lead-in

        // every knot number after K moved up - fix all the references
        int add = LoopAddedKnots;
        if (brakeStartKnot >= 0) brakeStartKnot = MapKnot(brakeStartKnot, k, add);
        twistStartKnot = MapKnot(twistStartKnot, k, add);
        twistEndKnot = MapKnot(twistEndKnot, k, add);
        explosionKnot = MapKnot(explosionKnot, k, add);
        skullKnot = MapKnot(skullKnot, k, add);
        if (pondKnots != null) for (int i = 0; i < pondKnots.Length; i++) pondKnots[i] = MapKnot(pondKnots[i], k, add);
        foreach (var z in rollZones)
        {
            if (z == null) continue;
            if (z.startKnot >= k) z.startKnot += add;
            if (z.endKnot > k) z.endKnot += add;
        }
        foreach (var z in loops)
        {
            if (z == null) continue;
            if (z.startKnot > k) z.startKnot += add;
            if (z.endKnot > k) z.endKnot += add;
        }
        loops.Add(new LoopZone
        {
            label = "Vertical loop " + (loops.Count + 1),
            startKnot = k, endKnot = k + add,
            radius = rad, heightStretch = st, sideShift = shift,
            bottomLocal = o, forwardLocal = f, rightLocal = r
        });
        Build();
        return null;
    }

    /// <summary>Deletes a loop made by InsertLoop and puts the track back the way it was.</summary>
    public void RemoveLoop(int index)
    {
        if (index < 0 || index >= loops.Count) return;
        var z = loops[index];
        var spline = Container.Spline;
        int k = z.startKnot, e = z.endKnot, add = e - k;
        if (k < 0 || e >= KnotCount || add < 2) { loops.RemoveAt(index); Build(); return; }
        for (int i = e; i >= k + 2; i--) spline.RemoveAt(i);   // exit, arc, lead-out
        spline.RemoveAt(k);                                      // lead-in
        spline.SetTangentMode(k, TangentMode.AutoSmooth);        // the original knot is back to normal

        System.Func<int, int> back = i => i > e ? i - add : (i > k ? k : i);
        if (brakeStartKnot >= 0) brakeStartKnot = back(brakeStartKnot);
        twistStartKnot = back(twistStartKnot); twistEndKnot = back(twistEndKnot);
        explosionKnot = back(explosionKnot); skullKnot = back(skullKnot);
        if (pondKnots != null) for (int i = 0; i < pondKnots.Length; i++) pondKnots[i] = back(pondKnots[i]);
        foreach (var rz in rollZones) { if (rz == null) continue; rz.startKnot = back(rz.startKnot); rz.endKnot = back(rz.endKnot); }
        loops.RemoveAt(index);
        foreach (var lz in loops) { if (lz == null) continue; lz.startKnot = back(lz.startKnot); lz.endKnot = back(lz.endKnot); }
        Build();
    }

    static float3 F3(Vector3 v) { return new float3(v.x, v.y, v.z); }

    void SetKnots(Vector3[] pts)
    {
        if (Container.Spline == null) Container.Spline = new Spline();
        var spline = Container.Spline;
        spline.Clear();
        foreach (var p in pts)
            spline.Add(new BezierKnot(new float3(p.x, p.y, p.z)), TangentMode.AutoSmooth);
        spline.Closed = true;
    }
}
