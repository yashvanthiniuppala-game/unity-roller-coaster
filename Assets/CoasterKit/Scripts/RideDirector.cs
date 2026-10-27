// RideDirector.cs — makes your ride video look like a movie AND proves the physics:
//   * CINEMATIC CAMERA: cuts automatically between the rider's view, a chase camera and drone shots
//     (station = orbiting drone, lift hill = rider's view, first drop = chase, loop = wide drone shot from the side,
//      twist = rider's view in SLOW MOTION, ruins = drone shot of the columns falling,
//      dragon battle = low shot with the riders in front and the dragons fighting above (SLOW MOTION at the explosion),
//      oasis waterfall = camera in front of the cart looking back at the riders as they get soaked, brakes = chase).
//   * LIVE GRAPH (bottom-right): speed, height and G-force for the whole ride. You can see gravity at work -
//     when the height goes DOWN the speed goes UP (potential energy -> kinetic energy). Great for the report!
//   * Press C / 1-4 any time to take over the camera yourself (the director waits 10 s), G = hide the graph.
//
// HOW TO USE: select your Main Camera (the one with Ride Camera) > Add Component > Ride Director. Press Play.
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(RideCamera))]
[DefaultExecutionOrder(100)]          // runs after Ride Camera, so the special shots can take over
public class RideDirector : MonoBehaviour
{
    [Header("Cinematic camera")]
    public bool autoDirect = true;
    [Tooltip("On normal track parts, switch between rider view and chase view this often (seconds)")] public float cutEvery = 6f;
    [Tooltip("How far the drone camera stands from the loop (metres)")] public float loopCameraDistance = 22f;
    [Tooltip("How far the drone camera stands from the falling ruins (metres)")] public float ruinsCameraDistance = 18f;
    [Tooltip("The chase camera is never further behind the cart than this (metres)")] public float maxChaseDistance = 8f;
    [Tooltip("Camera in front of the riders on the lift hill (their faces, with the desert dropping away behind them)")] public bool ridersOnLift = true;
    [Tooltip("Show the riders' faces in the station (before the ride starts)")] public bool showRidersInStation = true;
    [Tooltip("Camera in front of the cart looking at the riders on the first big drop")] public bool ridersOnFirstDrop = true;

    [Header("Slow motion")]
    public bool slowMoInTwist = true;
    public bool slowMoAtExplosion = true;
    [Range(0.2f, 1f)] public float slowMoSpeed = 0.4f;

    [Header("Graph")]
    public bool showGraph = false;
    [Tooltip("Seconds shown on the graph (your whole ride)")] public float graphSeconds = 60f;
    public float maxSpeedKmh = 120f;
    public float maxG = 5f;

    RideCamera rc;
    CoasterCart cart;
    CoasterTrack track;
    RuinsCollapse ruins;
    DragonBattle battle;
    float manualTimer, cutTimer, baseFixed;
    bool chaseTurn;
    enum Shot { None, RidersAndBattle, RiderFaces, Boarding, LiftRiders }
    struct Zone { public float a, b; public RideCamera.Mode mode; public bool slow; public string name; public Shot shot; }
    Shot shot; Vector3 shotPos; Quaternion shotRot; bool shotStarted;
    OasisWaterfall oasis;
    FireGauntlet fire;
    SandTornado storm;
    readonly List<Zone> zones = new List<Zone>();
    readonly List<Transform> droneSpots = new List<Transform>();

    // graph data
    const int W = 320, H = 120;
    Texture2D graph;
    Color32[] pixels;
    readonly List<Vector4> samples = new List<Vector4>();   // x = time, y = speed km/h, z = height, w = G
    float sampleTimer, maxSpeedSeen, maxGSeen, minH = float.MaxValue, maxH = float.MinValue;
    GUIStyle label;

    void Start()
    {
        rc = GetComponent<RideCamera>();
        cart = rc.cart != null ? rc.cart : FindAnyObjectByType<CoasterCart>();
        track = cart != null ? cart.track : FindAnyObjectByType<CoasterTrack>();
        ruins = FindAnyObjectByType<RuinsCollapse>();
        battle = FindAnyObjectByType<DragonBattle>();
        oasis = FindAnyObjectByType<OasisWaterfall>();
        fire = FindAnyObjectByType<FireGauntlet>();
        storm = FindAnyObjectByType<SandTornado>();
        baseFixed = Time.fixedDeltaTime;
        if (track != null && track.Length < 1f) track.Build();
        if (track != null) PlanShots();
        if (cart != null) cart.RideStarted += () => { samples.Clear(); maxSpeedSeen = maxGSeen = 0f; };

        graph = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        pixels = new Color32[W * H];
    }

    void OnDisable() { Time.timeScale = 1f; if (baseFixed > 0f) Time.fixedDeltaTime = baseFixed; }

    /// <summary>Works out where the special moments of YOUR track are.</summary>
    void PlanShots()
    {
        zones.Clear();
        float L = track.Length, ls = track.LiftStart, le = track.LiftEnd, bs = track.BrakeStart;
        zones.Add(new Zone { a = ls, b = le - 3f, mode = RideCamera.Mode.Onboard, shot = ridersOnLift ? Shot.LiftRiders : Shot.None, name = "Lift hill" });
        zones.Add(new Zone { a = le - 3f, b = le + 70f, mode = RideCamera.Mode.Chase, shot = ridersOnFirstDrop ? Shot.RiderFaces : Shot.None, name = "First drop" });

        // loops: drone camera at the side
        foreach (var z in track.loops)
        {
            float s = track.KnotDistance(z.startKnot), e = track.KnotDistance(Mathf.Min(z.endKnot, track.KnotCount - 1));
            float top = track.LoopTopDistance(z);
            track.Sample(s, out Vector3 pBottom, out Vector3 f, out _);
            track.Sample(top, out Vector3 pTop, out _, out _);
            track.LoopSides(z, out _, out _, out _, out _, out Vector3 right);
            Vector3 centre = (pBottom + pTop) * 0.5f;
            Vector3 spot = centre - right * loopCameraDistance - new Vector3(f.x, 0f, f.z).normalized * 6f;
            spot.y = Mathf.Max(spot.y, GroundY(spot) + 2f);
            var t = new GameObject("Drone Spot (loop)").transform;
            t.position = spot;
            droneSpots.Add(t);
            zones.Add(new Zone { a = s - 20f, b = e + 8f, mode = RideCamera.Mode.Trackside, name = "Loop" });
        }
        // twists: rider view in slow motion
        foreach (var r in track.rollZones)
        {
            if (r.bankAndReturn) continue;
            float s = track.KnotDistance(r.startKnot), e = track.KnotDistance(r.endKnot);
            zones.Add(new Zone { a = s - 6f, b = e + 6f, mode = RideCamera.Mode.Onboard, slow = slowMoInTwist, name = "Twist" });
        }
        // ruins: a drone camera on the other side of the track watches the columns fall as the cart passes
        if (ruins != null)
        {
            float rowLen = ruins.columns * ruins.columnHeight * ruins.spacing;
            float mid = ruins.distance + rowLen * 0.5f;
            track.Sample(mid, out Vector3 p, out Vector3 f, out _);
            Vector3 side = Vector3.Cross(Vector3.up, new Vector3(f.x, 0f, f.z).normalized);
            Vector3 spot = p - side * Mathf.Sign(ruins.sideOffset == 0f ? 1f : ruins.sideOffset) * ruinsCameraDistance - new Vector3(f.x, 0f, f.z).normalized * rowLen * 0.4f;
            spot.y = Mathf.Max(p.y, GroundY(spot)) + 4f;
            var t = new GameObject("Drone Spot (ruins)").transform; t.position = spot; droneSpots.Add(t);
            zones.Add(new Zone { a = ruins.distance - ruins.triggerAhead - 10f, b = ruins.distance + rowLen + 10f, mode = RideCamera.Mode.Trackside, name = "Ruins" });
        }
        // dragon battle: rider's view looking at the clash, slow motion at the explosion
        if (battle != null)
        {
            zones.Add(new Zone { a = battle.battleDistance - 80f, b = battle.battleDistance + 15f, mode = RideCamera.Mode.Onboard, shot = Shot.RidersAndBattle, name = "Dragon battle" });
            if (slowMoAtExplosion) zones.Add(new Zone { a = battle.battleDistance - 10f, b = battle.battleDistance + 6f, mode = RideCamera.Mode.Onboard, shot = Shot.RidersAndBattle, slow = true, name = "Battle (slow)" });
        }
        // fire gauntlet: camera in front of the cart - the riders' faces with the fire roaring all around them
        if (fire != null)
            zones.Add(new Zone { a = fire.startDistance - 15f, b = fire.startDistance + fire.length + 8f, mode = RideCamera.Mode.Onboard, shot = Shot.RiderFaces, name = "Fire" });
        // sandstorm: chase camera, so you see the cart disappear into the storm
        if (storm != null && storm.cartGoesThrough)
            zones.Add(new Zone { a = storm.nearTrackDistance - 60f, b = storm.nearTrackDistance + 25f, mode = RideCamera.Mode.Chase, name = "Sandstorm" });
        // oasis waterfall: camera in front of the cart looking back at the riders' faces as they get soaked
        if (oasis != null)
        {
            zones.Add(new Zone { a = oasis.distance - 30f, b = oasis.distance + 10f, mode = RideCamera.Mode.Onboard, shot = Shot.RiderFaces, name = "Oasis splash" });
        }
        zones.Add(new Zone { a = bs, b = L + 1f, mode = RideCamera.Mode.Chase, name = "Brakes" });
        rc.tracksidePoints = droneSpots.ToArray();
    }

    static float GroundY(Vector3 p)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain != null) return terrain.SampleHeight(p) + terrain.transform.position.y;
        if (Physics.Raycast(p + Vector3.up * 300f, Vector3.down, out RaycastHit hit, 1000f, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
        return p.y;
    }

    void Update()
    {
        if (CoasterInput.Pressed('c') || CoasterInput.Pressed('1') || CoasterInput.Pressed('2') || CoasterInput.Pressed('3') || CoasterInput.Pressed('4'))
            manualTimer = 10f;
        if (GraphKey()) showGraph = !showGraph;
        manualTimer -= Time.unscaledDeltaTime;
        if (cart == null) return;
        rc.chaseDistance = Mathf.Min(rc.chaseDistance, maxChaseDistance);     // keep the chase camera close to the riders
        rc.chaseHeight = Mathf.Min(rc.chaseHeight, 3f);

        bool slow = false;
        Shot wantShot = Shot.None;
        if (autoDirect && manualTimer <= 0f)
        {
            if (cart.state == CoasterCart.RideState.WaitingInStation) { rc.mode = RideCamera.Mode.Overview; if (showRidersInStation) wantShot = RiderBoarding.Busy ? Shot.Boarding : Shot.RiderFaces; }
            else
            {
                // the LAST matching zone wins (so the special ones override the general ones)
                Zone? hit = null;
                foreach (var z in zones) if (cart.distance >= z.a && cart.distance < z.b) hit = z;
                if (hit.HasValue)
                {
                    rc.mode = hit.Value.mode;
                    slow = hit.Value.slow;
                    wantShot = hit.Value.shot;
                    cutTimer = cutEvery;
                }
                else
                {
                    cutTimer -= Time.unscaledDeltaTime;
                    if (cutTimer <= 0f) { cutTimer = cutEvery; chaseTurn = !chaseTurn; }
                    rc.mode = chaseTurn ? RideCamera.Mode.Chase : RideCamera.Mode.Onboard;
                }
            }
        }
        else if (manualTimer > 0f)
        {
            foreach (var z in zones) if (z.slow && cart.distance >= z.a && cart.distance < z.b) slow = true;
        }

        if (cart.state != CoasterCart.RideState.Riding && !(showRidersInStation && cart.state == CoasterCart.RideState.WaitingInStation)) wantShot = Shot.None;
        if (wantShot != shot) { shot = wantShot; shotStarted = false; }

        // smooth slow motion (physics stays correct, so the ride time in the Console is not changed)
        float want = slow ? slowMoSpeed : 1f;
        Time.timeScale = Mathf.MoveTowards(Time.timeScale, want, Time.unscaledDeltaTime * 2.5f);
        Time.fixedDeltaTime = baseFixed * Time.timeScale;

        // record the graph
        if (cart.state == CoasterCart.RideState.Riding)
        {
            sampleTimer -= Time.deltaTime;
            if (sampleTimer <= 0f)
            {
                sampleTimer = 0.1f;
                float kmh = cart.speed * 3.6f, h = cart.Height;
                samples.Add(new Vector4(cart.rideTime, kmh, h, cart.gForce));
                maxSpeedSeen = Mathf.Max(maxSpeedSeen, kmh); maxGSeen = Mathf.Max(maxGSeen, cart.gForce);
                minH = Mathf.Min(minH, h); maxH = Mathf.Max(maxH, h);
            }
        }
    }

    // ------------------------------------------------------------------ special shots (run after Ride Camera)
    void LateUpdate()
    {
        if (shot == Shot.None || cart == null) return;
        Transform c = cart.transform;
        Vector3 riders = c.position + c.up * 1.6f;
        Vector3 pos, look;
        if (shot == Shot.RidersAndBattle && battle != null)
        {
            // low, just behind and beside the cart: the riders in front of us, the dragons fighting in the sky ahead
            Vector3 clash = BattleSpot();
            pos = c.position - c.forward * 5.5f + c.right * 2.2f + c.up * 1.1f;
            look = Vector3.Lerp(riders + c.forward * 3f, clash, 0.45f);
        }
        else if (shot == Shot.LiftRiders)
        {
            // close in front of the riders, slowly swinging from one side to the other while the chain pulls them up
            float t = track != null ? Mathf.InverseLerp(track.LiftStart, track.LiftEnd, cart.distance) : 0.5f;
            float swing = Mathf.Lerp(-1.4f, 1.4f, Mathf.SmoothStep(0f, 1f, t));
            pos = c.position + c.forward * 3.6f + c.up * 1.9f + c.right * swing;
            look = riders - c.up * 0.2f;
        }
        else if (shot == Shot.Boarding)
        {
            // on the platform side: we see the riders waiting, walking over and climbing into the cart
            float side = RiderBoarding.Side;
            pos = c.position + c.right * side * 10f + c.up * 3.2f - c.forward * 3f;
            look = c.position + c.right * side * 2.5f + c.up * 1.0f;
        }
        else
        {
            // in front of the cart, looking back at the riders' faces
            pos = c.position + c.forward * 4.8f + c.up * 2.1f + c.right * 0.6f;
            look = riders;
        }
        Quaternion rot = Quaternion.LookRotation(look - pos, Vector3.up);
        if (!shotStarted) { shotPos = pos; shotRot = rot; shotStarted = true; }
        float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f);
        shotPos = Vector3.Lerp(shotPos, pos, k);
        shotRot = Quaternion.Slerp(shotRot, rot, k);
        transform.SetPositionAndRotation(shotPos, shotRot);
        var cam = GetComponent<Camera>();
        if (cam != null) cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, shot == Shot.RidersAndBattle ? 72f : shot == Shot.Boarding ? 55f : shot == Shot.LiftRiders ? 58f : 62f, k);
    }

    Vector3 BattleSpot()
    {
        track.Sample(battle.battleDistance, out Vector3 p, out _, out _);
        return p + Vector3.up * battle.height;
    }

    static bool GraphKey()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        return kb != null && kb.gKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.G);
#else
        return false;
#endif
    }

    // ------------------------------------------------------------------ graph drawing
    void DrawGraph()
    {
        var bg = new Color32(10, 12, 20, 170);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;
        var grid = new Color32(255, 255, 255, 35);
        for (int gx = 0; gx < W; gx += W / 6) for (int y = 0; y < H; y++) pixels[y * W + gx] = grid;
        for (int gy = 0; gy < H; gy += H / 4) for (int x = 0; x < W; x++) pixels[gy * W + x] = grid;
        // 1 g line
        int oneG = Mathf.RoundToInt((1f + 1f) / (maxG + 1f) * (H - 1));
        for (int x = 0; x < W; x += 3) Set(x, oneG, new Color32(90, 220, 255, 90));

        float hRange = Mathf.Max(1f, maxH - minH);
        for (int i = 1; i < samples.Count; i++)
        {
            Vector4 a = samples[i - 1], b = samples[i];
            int x0 = X(a.x), x1 = X(b.x);
            Line(x0, Y01((a.z - minH) / hRange), x1, Y01((b.z - minH) / hRange), new Color32(120, 230, 120, 255));   // height = green
            Line(x0, Y01(a.y / maxSpeedKmh), x1, Y01(b.y / maxSpeedKmh), new Color32(255, 210, 60, 255));         // speed  = yellow
            Line(x0, Y01((a.w + 1f) / (maxG + 1f)), x1, Y01((b.w + 1f) / (maxG + 1f)), new Color32(90, 220, 255, 255)); // G = cyan
        }
        graph.SetPixels32(pixels);
        graph.Apply(false);
    }

    int X(float t) { return Mathf.Clamp(Mathf.RoundToInt(t / Mathf.Max(1f, graphSeconds) * (W - 1)), 0, W - 1); }
    int Y01(float v) { return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(v) * (H - 1)), 0, H - 1); }
    void Set(int x, int y, Color32 c) { if (x >= 0 && x < W && y >= 0 && y < H) pixels[y * W + x] = c; }
    void Line(int x0, int y0, int x1, int y1, Color32 c)
    {
        int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy;
        for (int guard = 0; guard < 1000; guard++)
        {
            Set(x0, y0, c); Set(x0, y0 + 1, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    void OnGUI()
    {
        if (!showGraph || cart == null || graph == null) return;
        if (Event.current.type == EventType.Repaint) DrawGraph();
        if (label == null) { label = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true }; label.normal.textColor = Color.white; }
        float gw = W * 1.2f, gh = H * 1.2f;
        var r = new Rect(Screen.width - gw - 14, Screen.height - gh - 58, gw, gh);
        GUI.DrawTexture(r, graph);
        GUI.Label(new Rect(r.x, r.y - 40, gw, 40),
            $"<b><color=#ffd23c>Speed {cart.speed * 3.6f:F0} km/h</color></b> (max {maxSpeedSeen:F0})   " +
            $"<b><color=#5adcff>G {cart.gForce:F1}</color></b> (max {maxGSeen:F1})\n" +
            $"<color=#78e678>Height {cart.Height:F0} m</color>   time {cart.rideTime:F1} s", label);
        GUI.Label(new Rect(r.x, r.yMax + 2, gw, 20), "<size=11>Height down = speed up: gravity at work.   G = hide graph</size>", label);
        if (Time.timeScale < 0.95f)
            GUI.Label(new Rect(Screen.width * 0.5f - 80, 20, 160, 30), "<size=20><b><color=#ffffff>SLOW MOTION</color></b></size>", label);
    }
}
