// CoasterAutoBuilder.cs — Tools > Coaster Kit > 2. Build Complete Coaster (one click)
// Builds the whole homework coaster inside the open desert scene:
//   track (spline + rails + supports), 3-car train with riders, station, 2 ponds with splash
//   zones, 4 fire cannons, a camp fire, an exploding barrel stack (real physics), a skull that
//   rises out of the sand (a real Animation clip + Animator), cameras, HUD and all sounds.
// It searches the terrain for the best open spot, lowers the sand where the track or ponds
// need room, and hides props that would stand in the way (Undo with Ctrl+Z).
// Must stay inside a folder named "Editor".
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

public static class CoasterAutoBuilder
{
    const string RootName = "=== DESERT SERPENT COASTER ===";
    const string GenFolder = "Assets/CoasterKit_Generated";
    const float StationHeight = 1.5f;     // station rails above the ground
    const float Clearance = 0.6f;         // minimum gap rails -> sand
    const float PondRailOffset = 0.1f;    // water surface above the rails

    // ------------------------------------------------------------------ menu items
    [MenuItem("Tools/Coaster Kit/1. Open Desert Scene", false, 1)]
    public static void OpenDesertScene()
    {
        string path = null;
        foreach (var g in AssetDatabase.FindAssets("demo2 t:Scene"))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (p.EndsWith("/demo2.unity")) { path = p; break; }
        }
        var saved = AssetDatabase.FindAssets("CoasterScene t:Scene", new[] { "Assets/Scenes" });
        if (saved.Length > 0) path = AssetDatabase.GUIDToAssetPath(saved[0]);
        if (path == null)
        {
            EditorUtility.DisplayDialog("Coaster Kit", "I couldn't find the desert scene (demo2).\n\nImport Desert.unitypackage first: Assets > Import Package > Custom Package...", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(path);
    }

    [MenuItem("Tools/Coaster Kit/2. Build Complete Coaster (one click)", false, 2)]
    public static void BuildAll()
    {
        var terrains = GetTerrains();
        if (terrains.Count == 0)
        {
            EditorUtility.DisplayDialog("Coaster Kit", "There is no terrain in the open scene.\n\nFirst use Tools > Coaster Kit > 1. Open Desert Scene.", "OK");
            return;
        }
        var old = GameObject.Find(RootName);
        if (old != null)
        {
            if (!EditorUtility.DisplayDialog("Coaster Kit", "A coaster is already built. Remove it and build a new one?", "Rebuild", "Cancel")) return;
            Undo.DestroyObjectImmediate(old);
        }

        int choice = EditorUtility.DisplayDialogComplex("Coaster Kit - choose a layout",
            "GRAND TOUR: one big track around the whole desert (about 1 km). It is sized automatically so the ride stays within the homework's 30-60 seconds.\n\n" +
            "COMPACT: the smaller 490 m track in one part of the desert.",
            "Grand Tour (whole desert)", "Cancel", "Compact");
        if (choice == 1) return;

        try
        {
            Build(terrains, choice == 0, pendingCenter);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("Coaster Kit", "Something went wrong while building:\n\n" + e.Message + "\n\nCopy the red error from the Console and send it to your helper.", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static Vector3? pendingCenter;

    [MenuItem("Tools/Coaster Kit/3. Build Coaster Centred On My Scene View", false, 3)]
    public static void BuildAtSceneView()
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) { EditorUtility.DisplayDialog("Coaster Kit", "Open the Scene tab first.", "OK"); return; }
        if (!EditorUtility.DisplayDialog("Coaster Kit",
            "The coaster will be centred on the middle of your Scene view (the point the camera orbits around).\n\nTip: switch to the top view (click the green Y arrow of the gizmo at the top-right), pan so the area you want is in the middle, then run this.", "Build here", "Cancel")) return;
        pendingCenter = sv.pivot;
        try { BuildAll(); } finally { pendingCenter = null; }
    }

    [MenuItem("Tools/Coaster Kit/Restore Original Desert Terrain", false, 31)]
    public static void RestoreTerrainMenu()
    {
        int n = 0;
        foreach (var t in GetTerrains()) if (RestoreTerrain(t)) n++;
        EditorUtility.DisplayDialog("Coaster Kit", n > 0 ? "The sand is back to how it was before the first build." : "No saved terrain found yet (it is saved automatically the first time you build).", "OK");
    }

    // ---- terrain backup: saved once before the first build, restored before every rebuild ----
    static string BackupPath(Terrain t)
    {
        string path = AssetDatabase.GetAssetPath(t.terrainData);
        string id = string.IsNullOrEmpty(path) ? t.terrainData.name : AssetDatabase.AssetPathToGUID(path);
        return $"{GenFolder}/TerrainBackup_{id}.bytes";
    }

    static void BackupOrRestoreTerrain(Terrain t)
    {
        if (!RestoreTerrain(t))
        {
            var td = t.terrainData; int res = td.heightmapResolution;
            float[,] h = td.GetHeights(0, 0, res, res);
            var bytes = new byte[4 + res * res * 4];
            System.Buffer.BlockCopy(System.BitConverter.GetBytes(res), 0, bytes, 0, 4);
            System.Buffer.BlockCopy(h, 0, bytes, 4, res * res * 4);
            EnsureFolder(GenFolder);
            System.IO.File.WriteAllBytes(BackupPath(t), bytes);
        }
    }

    static bool RestoreTerrain(Terrain t)
    {
        string p = BackupPath(t);
        if (!System.IO.File.Exists(p)) return false;
        var bytes = System.IO.File.ReadAllBytes(p);
        int res = System.BitConverter.ToInt32(bytes, 0);
        var td = t.terrainData;
        if (res != td.heightmapResolution) return false;
        var h = new float[res, res];
        System.Buffer.BlockCopy(bytes, 4, h, 0, res * res * 4);
        Undo.RegisterCompleteObjectUndo(td, "Restore terrain");
        td.SetHeights(0, 0, h);
        return true;
    }

    [MenuItem("Tools/Coaster Kit/Remove Built Coaster", false, 30)]
    public static void RemoveBuilt()
    {
        var old = GameObject.Find(RootName);
        if (old == null) { EditorUtility.DisplayDialog("Coaster Kit", "No built coaster found in this scene.", "OK"); return; }
        Undo.DestroyObjectImmediate(old);
    }

    // ------------------------------------------------------------------ main build
    static void Build(List<Terrain> terrains, bool grand, Vector3? forcedCenter)
    {
        Progress("Preparing materials", 0.05f);
        EnsureFolder(GenFolder);
        var M = new Mats();

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build coaster");
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // ---------- track + placement search ----------
        var trackGo = new GameObject("Track");
        trackGo.transform.SetParent(root.transform, false);
        trackGo.AddComponent<SplineContainer>();
        var track = trackGo.AddComponent<CoasterTrack>();
        var builder = trackGo.AddComponent<TrackBuilder>();
        builder.railMaterial = M.rail; builder.tieMaterial = M.tie; builder.supportMaterial = M.support;

        foreach (var t in terrains) BackupOrRestoreTerrain(t);   // start from the original sand every build
        var props = CollectProps();
        var hash = new PropHash(props, 5f);

        float lift = 4f, friction = 0.015f, drag = 0.0006f;
        Placement best = null;
        if (grand)
        {
            Bounds tb = TerrainBounds(terrains);
            // Design the tour at a comfortable 300 x 300 m, then squeeze / stretch it to cover
            // ~88% of the desert. If the ride would pass 57 s, shrink it a little and try again.
            float W = 300f, D = 300f, H = 50f;
            lift = 7f; friction = 0.01f; drag = 0.0003f;
            track.GenerateGrandTour(W, D, H);
            Vector3 mn = Vector3.one * 1e9f, mx = -Vector3.one * 1e9f;
            foreach (var v in LocalSamples(track, 5f)) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
            float side = Mathf.Min(tb.size.x, tb.size.z) * 0.88f;             // works for any rotation
            float sx = Mathf.Clamp(side / Mathf.Max(1f, mx.x - mn.x), 0.55f, 1.5f);
            float sz = Mathf.Clamp(side / Mathf.Max(1f, mx.z - mn.z), 0.55f, 1.5f);
            for (int attempt = 0; attempt < 16 && sx > 0.4f && sz > 0.4f; attempt++)
            {
                Progress($"Sizing the Grand Tour to fit the desert and 30-60 s ({sx * W:F0} x {sz * D:F0} m)", 0.08f + attempt * 0.02f);
                track.GenerateGrandTour(W, D, H, sx, sz);
                var sim = CoasterRideSim.Run(track, lift, friction, drag);
                if (sim.stalled && H < 70f) { H += 5f; continue; }
                if (sim.rideTime > 57f) { sx *= 0.93f; sz *= 0.93f; continue; }
                var p = FindPlacement(LocalSamples(track, 5f), terrains, props, hash, forcedCenter);
                p.W = W; p.D = D; p.H = H; p.grand = true; p.sx = sx; p.sz = sz;
                if (best == null || (p.valid && !best.valid) || (p.valid == best.valid && p.cost < best.cost)) best = p;
                if (p.valid) break;
                sx *= 0.92f; sz *= 0.92f;   // pokes over the edge somewhere: shrink and try again
            }
            if (best == null) { grand = false; lift = 4f; friction = 0.015f; drag = 0.0006f; }
        }
        if (!grand)
        {
            foreach (float scale in new[] { 1f, 0.8f, 0.65f })
            {
                Progress($"Finding the best spot on the desert (size {scale:P0})", 0.1f);
                track.GenerateStarterLayout(scale);
                var p = FindPlacement(LocalSamples(track, 4f), terrains, props, hash, forcedCenter);
                p.scale = scale;
                if (best == null || (p.valid && !best.valid) || (p.valid == best.valid && p.cost < best.cost)) best = p;
                if (p.valid && p.cost < 400f) break;
            }
            lift = best.scale >= 0.99f ? 4f : (best.scale >= 0.79f ? 3.6f : 3.2f);
        }
        trackGo.transform.SetPositionAndRotation(best.pos, Quaternion.Euler(0f, best.yaw, 0f));
        // follow the desert, then make sure gravity still carries the cart all the way round
        float liftH = best.H > 0f ? best.H : 32f;
        CoasterRideSim.Result rideSim = default;
        for (int tries = 0; tries < 6; tries++)
        {
            Progress("Fitting the track to the desert (hills, plateaus, ponds)", 0.42f);
            if (best.grand) track.GenerateGrandTour(best.W, best.D, liftH, best.sx, best.sz);
            else track.GenerateStarterLayout(best.scale);
            FitToTerrain(track, terrains);
            rideSim = CoasterRideSim.Run(track, lift, friction, drag);
            if (!rideSim.stalled || !best.grand || liftH >= 75f) break;
            liftH += 5f;                       // not enough energy for the climbs: raise the first hill
        }
        if (rideSim.rideTime < 30f) { lift = Mathf.Max(3f, lift - 2f); rideSim = CoasterRideSim.Run(track, lift, friction, drag); }
        if (rideSim.rideTime > 60f) { lift = 9f; rideSim = CoasterRideSim.Run(track, lift, friction, drag); }
        best.H = liftH;

        // ---------- ponds, terrain, props ----------
        Progress("Shaping the sand (ponds and track clearance)", 0.45f);
        float pondHalf = best.grand ? 16f : 13f * best.scale + 2f;
        var ponds = new List<Pond>();
        foreach (int k in track.pondKnots) ponds.Add(MakePond(track, k, pondHalf));
        ShapeTerrain(terrains, track, ponds);

        Progress("Clearing props out of the track's way", 0.55f);
        int hidden = ClearProps(track, props, hash, ponds);

        Progress("Building rails, ties and supports", 0.6f);
        builder.Build();

        var effects = Child(root, "Effects");
        var decor = Child(root, "Decor");

        // ---------- ponds (water + splash) ----------
        var splash = FindClip("splash");
        for (int i = 0; i < ponds.Count; i++)
        {
            var pd = ponds[i];
            // an oval disc of water that matches the dug-out pond
            var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            water.name = "Pond " + (i + 1);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.transform.SetParent(decor.transform, false);
            water.transform.SetPositionAndRotation(new Vector3(pd.center.x, pd.water - 0.02f, pd.center.z), Quaternion.LookRotation(pd.fwd, Vector3.up));
            water.transform.localScale = new Vector3(pd.b * 2f * 1.08f, 0.02f, pd.a * 2f * 1.08f);
            water.GetComponent<Renderer>().sharedMaterial = M.water;

            var sz = new GameObject("Splash Zone " + (i + 1));
            sz.transform.SetParent(effects.transform, false);
            sz.transform.SetPositionAndRotation(new Vector3(pd.center.x, pd.water, pd.center.z), Quaternion.LookRotation(pd.fwd, Vector3.up));
            var box = sz.AddComponent<BoxCollider>(); box.isTrigger = true;
            box.size = new Vector3(5f, 5f, pd.a * 1.8f); box.center = new Vector3(0f, 1f, 0f);
            var s = sz.AddComponent<SplashZone>(); s.splashSound = splash; s.splashSize = 1.3f;
        }

        // ---------- the cart (one car) with its animated rider ----------
        Progress("Building the cart and rider", 0.7f);
        var trainParent = Child(root, "Train");
        var lead = BuildCart(trainParent.transform, "Coaster Cart", M, 0, false);
        var cart = lead.AddComponent<CoasterCart>();
        cart.track = track;
        cart.liftSpeed = lift; cart.rollingFriction = friction; cart.airDrag = drag;
        var audio = lead.AddComponent<CartAudio>();
        audio.rollingLoop = FindClip("rolling_loop"); audio.windLoop = FindClip("wind_loop");
        audio.chainLoop = FindClip("chain_lift_loop"); audio.stationBell = FindClip("station_bell");
        audio.dropSound = FindClip("drop_whoosh");
        BuildRider(lead, M);
        PlaceCart(lead.transform, track, 0f);

        // ---------- camera + HUD ----------
        Progress("Setting up cameras, HUD and sound", 0.75f);
        var cam = Camera.main;
        if (cam == null) cam = Object.FindAnyObjectByType<Camera>();
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera");
            Undo.RegisterCreatedObjectUndo(camGo, "Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
        }
        if (cam.GetComponent<AudioListener>() == null) Undo.AddComponent<AudioListener>(cam.gameObject);
        var rc = cam.GetComponent<RideCamera>();
        if (rc == null) rc = Undo.AddComponent<RideCamera>(cam.gameObject);
        rc.cart = cart;
        rc.onboardOffset = new Vector3(0f, 2.0f, -1.5f);
        var hud = cam.GetComponent<RideHUD>();
        if (hud == null) hud = Undo.AddComponent<RideHUD>(cam.gameObject);
        hud.cart = cart;
        Undo.RecordObject(cam, "Camera far");
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, 3000f);
        PlaceCart(cam.transform, track, 0f);

        // ---------- station ----------
        Progress("Building the station", 0.8f);
        BuildStation(decor.transform, track, M);

        // ---------- fire cannons (along the twist) ----------
        Progress("Fire, explosion and animation", 0.85f);
        var fireLoop = FindClip("fire_loop"); var fireBurst = FindClip("fire_burst");
        float t0 = track.KnotDistance(track.twistStartKnot), t1 = track.KnotDistance(track.twistEndKnot);
        int n = 0;
        foreach (float f in new[] { 0.2f, 0.8f })
            foreach (float side in new[] { -1f, 1f })
                BuildFireCannon(effects.transform, track, Mathf.Lerp(t0, t1, f), side, M, fireLoop, fireBurst, ++n);

        // camp fire next to the station
        BuildCampFire(decor.transform, track, fireLoop);

        // ---------- explosion ----------
        BuildExplosion(effects.transform, track, M, FindClip("explosion"), fireLoop);

        // ---------- rising skull (real Animation clip) ----------
        BuildSkull(effects.transform, track, FindClip("drop_whoosh"));

        // ---------- save ----------
        Progress("Saving the scene", 0.95f);
        EditorSceneManager.MarkSceneDirty(root.scene);
        EnsureFolder("Assets/Scenes");
        string scenePath = root.scene.path;
        if (string.IsNullOrEmpty(scenePath) || !scenePath.StartsWith("Assets/Scenes/"))
            scenePath = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/CoasterScene.unity");
        EditorSceneManager.SaveScene(root.scene, scenePath);

        Selection.activeGameObject = trackGo;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();

        string sound = SoundCheck(true);
        string msg =
            $"Done! Your coaster is built and saved as {scenePath}.\n\n" +
            (best.grand ? $"Layout: GRAND TOUR covering about {best.W * best.sx:F0} x {best.D * best.sz:F0} m, lift hill {best.H:F0} m\n" : $"Layout: compact ({best.scale:P0})\n") +
            $"Track length: {track.Length:F0} m\nRide time (simulated): {rideSim.rideTime:F0} s, top speed {rideSim.topSpeed * 3.6f:F0} km/h\n" +
            $"Props moved out of the way (hidden): {hidden}\n" +
            (best.valid ? "" : "\nNote: the desert is a little small for this layout, so part of the track may overhang the edge. Try Compact, or build with option 3.\n") +
            "\nSound: " + sound + "\n" +
            "\nPress PLAY. Keys: C / 1-4 cameras, Space = start now, H = hide HUD.";
        Debug.Log("[Coaster Kit] " + msg.Replace("\n", " "));
        EditorUtility.DisplayDialog("Coaster Kit", msg, "Great!");
    }

    // ------------------------------------------------------------------ materials
    class Mats
    {
        public Material rail, tie, support, body, seat, wheel, trim, water, stone, roof, cannon;
        public Material[] shirts;
        public Material outfit, khaki, helmet, scarf, boots;
        public Mats()
        {
            outfit = Mat("Rider_Outfit", () => PipelineUtil.MakeLit(new Color(0.95f, 0.45f, 0.12f), 0.3f));
            khaki = Mat("Rider_Khaki", () => PipelineUtil.MakeLit(new Color(0.55f, 0.5f, 0.35f), 0.2f));
            helmet = Mat("Rider_Helmet", () => PipelineUtil.MakeLit(new Color(0.93f, 0.88f, 0.72f), 0.4f));
            scarf = Mat("Rider_Scarf", () => PipelineUtil.MakeLit(new Color(0.1f, 0.55f, 0.75f), 0.3f));
            boots = Mat("Rider_Boots", () => PipelineUtil.MakeLit(new Color(0.18f, 0.12f, 0.08f), 0.3f));
            rail = Mat("Rail_Red", () => PipelineUtil.MakeLit(new Color(0.8f, 0.12f, 0.08f), 0.6f, 0.7f));
            tie = Mat("Tie_Wood", () => PipelineUtil.MakeLit(new Color(0.36f, 0.25f, 0.16f), 0.2f));
            support = Mat("Support_Sand", () => PipelineUtil.MakeLit(new Color(0.92f, 0.86f, 0.72f), 0.3f, 0.2f));
            body = Mat("Cart_Gold", () => PipelineUtil.MakeLit(new Color(0.95f, 0.72f, 0.18f), 0.7f, 0.6f));
            seat = Mat("Cart_Seat", () => PipelineUtil.MakeLit(new Color(0.45f, 0.06f, 0.08f), 0.3f));
            wheel = Mat("Cart_Wheel", () => PipelineUtil.MakeLit(new Color(0.12f, 0.12f, 0.13f), 0.5f, 0.8f));
            trim = Mat("Cart_Trim", () => PipelineUtil.MakeLit(new Color(0.1f, 0.35f, 0.55f), 0.6f, 0.4f));
            water = Mat("Pond_Water", () => PipelineUtil.MakeWater(new Color(0.18f, 0.5f, 0.62f, 0.7f)));
            stone = Mat("Station_Stone", () => PipelineUtil.MakeLit(new Color(0.78f, 0.66f, 0.48f), 0.15f));
            roof = Mat("Station_Roof", () => PipelineUtil.MakeLit(new Color(0.55f, 0.1f, 0.1f), 0.25f));
            cannon = Mat("Fire_Cannon", () => PipelineUtil.MakeLit(new Color(0.2f, 0.18f, 0.17f), 0.5f, 0.8f));
            shirts = new[]
            {
                Mat("Rider_Blue", () => PipelineUtil.MakeLit(new Color(0.2f, 0.4f, 0.9f))),
                Mat("Rider_Green", () => PipelineUtil.MakeLit(new Color(0.2f, 0.7f, 0.3f))),
                Mat("Rider_Pink", () => PipelineUtil.MakeLit(new Color(0.9f, 0.3f, 0.6f))),
                Mat("Rider_Skin", () => PipelineUtil.MakeLit(new Color(0.85f, 0.65f, 0.5f))),
            };
        }
    }

    static Material Mat(string name, System.Func<Material> make)
    {
        string path = $"{GenFolder}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var m = make();
        m.name = name;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ------------------------------------------------------------------ placement search
    class Placement { public Vector3 pos; public float yaw; public float cost = float.MaxValue; public bool valid; public float scale = 1f; public bool grand; public float W, D, H, sx = 1f, sz = 1f; }

    static List<Vector3> LocalSamples(CoasterTrack track, float step)
    {
        var list = new List<Vector3>();
        for (float d = 0f; d < track.Length; d += step)
        {
            track.Sample(d, out var p, out _, out _);
            list.Add(p - track.transform.position);
        }
        return list;
    }

    /// <summary>
    /// Tries many positions and 8 directions and scores each: points off the terrain are very
    /// bad, props in the way and deep digging are bad. With forcedCenter, only the direction is searched.
    /// </summary>
    static Placement FindPlacement(List<Vector3> local, List<Terrain> terrains, List<Renderer> props, PropHash hash, Vector3? forcedCenter = null)
    {
        Bounds tb = TerrainBounds(terrains);
        float extent = Mathf.Max(tb.size.x, tb.size.z);
        float step = Mathf.Max(6f, extent / 70f);
        var best = new Placement();
        int[] stamp = new int[props.Count];
        int candidate = 0;

        var rotated = new Vector3[8][];
        var centre = new Vector3[8];
        for (int r = 0; r < 8; r++)
        {
            var q = Quaternion.Euler(0f, r * 45f, 0f);
            rotated[r] = new Vector3[local.Count];
            Vector3 mn = Vector3.one * 1e9f, mx = -Vector3.one * 1e9f;
            for (int i = 0; i < local.Count; i++)
            {
                var v = q * local[i]; rotated[r][i] = v;
                mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v);
            }
            centre[r] = (mn + mx) * 0.5f;
        }

        float xs = forcedCenter.HasValue ? forcedCenter.Value.x : tb.min.x;
        float xe = forcedCenter.HasValue ? forcedCenter.Value.x : tb.max.x;
        float zs = forcedCenter.HasValue ? forcedCenter.Value.z : tb.min.z;
        float ze = forcedCenter.HasValue ? forcedCenter.Value.z : tb.max.z;
        int total = Mathf.CeilToInt((xe - xs) / step) + 1, row = 0;
        for (float cx = xs; cx <= xe + 0.01f; cx += step, row++)
        {
            if (row % 4 == 0) EditorUtility.DisplayProgressBar("Coaster Kit", "Finding the best spot on the desert...", 0.1f + 0.3f * row / Mathf.Max(1, total));
            for (float cz = zs; cz <= ze + 0.01f; cz += step)
            {
                for (int r = 0; r < 8; r++)
                {
                    candidate++;
                    float x = cx - centre[r].x, z = cz - centre[r].z;   // track origin for this footprint centre
                    Vector3 st = rotated[r][0];                      // the station
                    float g0 = Ground(terrains, x + st.x, z + st.z);
                    if (float.IsNaN(g0)) continue;
                    float y0 = g0 + StationHeight - st.y;
                    float cost = 0f; int off = 0;
                    var pts = rotated[r];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float wx = x + pts[i].x, wy = y0 + pts[i].y, wz = z + pts[i].z;
                        float g = Ground(terrains, wx, wz);
                        if (float.IsNaN(g)) { off++; cost += 500f; }
                        else
                        {
                            float c = wy - g;
                            if (c < Clearance) { float dig = Mathf.Min(Clearance - c, 20f); cost += dig * dig * 0.5f; }   // the track will climb over most of this
                            foreach (int pi in hash.Near(wx, wz))
                            {
                                if (stamp[pi] == candidate) continue;
                                var b = hash.bounds[pi];
                                if (wx < b.min.x - 2.5f || wx > b.max.x + 2.5f || wz < b.min.z - 2.5f || wz > b.max.z + 2.5f) continue;
                                stamp[pi] = candidate;
                                cost += 3f + Mathf.Min(b.size.magnitude, 40f) * 1.5f;
                            }
                        }
                        if (cost > best.cost) break;
                    }
                    if (cost < best.cost) { best.cost = cost; best.pos = new Vector3(x, y0, z); best.yaw = r * 45f; best.valid = off == 0; }
                }
            }
        }
        if (best.cost == float.MaxValue)
        {
            // Nothing found at all (should not happen): centre the footprint on the terrain.
            float g = Ground(terrains, tb.center.x, tb.center.z); if (float.IsNaN(g)) g = tb.min.y;
            best.pos = new Vector3(tb.center.x - centre[0].x, g + StationHeight, tb.center.z - centre[0].z); best.yaw = 0f; best.cost = 1e9f;
        }
        return best;
    }

    /// <summary>
    /// Makes the track follow the desert: knots are lifted so the rails stay above plateaus and
    /// dunes, and the pond knots are set just above the ground so the ponds sit in the sand.
    /// </summary>
    static void FitToTerrain(CoasterTrack track, List<Terrain> terrains)
    {
        var tr = track.transform;
        int n = track.KnotCount;
        var locked = new HashSet<int> { 0, 1, 2, 3, 4 };          // station + lift hill stay as designed
        var ponds = new HashSet<int>(track.pondKnots);
        for (int pass = 0; pass < 4; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                if (locked.Contains(i)) continue;
                Vector3 w = tr.TransformPoint(track.KnotLocal(i));
                float g = Ground(terrains, w.x, w.z);
                if (float.IsNaN(g)) continue;
                float y = track.KnotLocal(i).y;
                if (ponds.Contains(i)) y = g + 0.5f - tr.position.y;                 // skim the pond, just above the sand
                else y = Mathf.Max(y, g + 2.5f - tr.position.y);                      // stay above the sand
                track.SetKnotHeight(i, y);
            }
            track.Build();
            // check between the knots and lift the nearest knots where the curve dips into the sand
            bool changed = false;
            for (float d = 0f; d < track.Length; d += 3f)
            {
                track.Sample(d, out var p, out _, out _);
                float g = Ground(terrains, p.x, p.z);
                if (float.IsNaN(g) || p.y >= g + 1.2f) continue;
                int k = KnotBefore(track, d);
                foreach (int j in new[] { k, (k + 1) % n })
                {
                    if (locked.Contains(j) || ponds.Contains(j)) continue;
                    track.SetKnotHeight(j, track.KnotLocal(j).y + (g + 1.2f - p.y) + 0.5f);
                    changed = true;
                }
            }
            track.Build();
            if (!changed) break;
        }
    }

    static int KnotBefore(CoasterTrack track, float d)
    {
        int best = 0;
        for (int i = 0; i < track.KnotCount; i++) if (track.KnotDistance(i) <= d) best = i;
        return best;
    }

    // ------------------------------------------------------------------ terrain helpers
    static List<Terrain> GetTerrains()
    {
        var list = new List<Terrain>();
        foreach (var t in Terrain.activeTerrains) if (t != null && t.terrainData != null) list.Add(t);
        return list;
    }

    static Bounds TerrainBounds(List<Terrain> terrains)
    {
        var b = new Bounds(terrains[0].GetPosition() + terrains[0].terrainData.size * 0.5f, terrains[0].terrainData.size);
        foreach (var t in terrains) b.Encapsulate(new Bounds(t.GetPosition() + t.terrainData.size * 0.5f, t.terrainData.size));
        return b;
    }

    static float Ground(List<Terrain> terrains, float x, float z)
    {
        foreach (var t in terrains)
        {
            Vector3 o = t.GetPosition(), s = t.terrainData.size;
            if (x >= o.x && x <= o.x + s.x && z >= o.z && z <= o.z + s.z)
                return t.SampleHeight(new Vector3(x, 0f, z)) + o.y;
        }
        return float.NaN;
    }

    class Pond { public Vector3 center, fwd, right; public float water, a, b; }

    static Pond MakePond(CoasterTrack track, int knot, float halfLength)
    {
        track.Sample(track.KnotDistance(knot), out var p, out var f, out _);
        var flat = Vector3.ProjectOnPlane(f, Vector3.up);
        if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
        flat.Normalize();
        return new Pond { center = p, fwd = flat, right = Vector3.Cross(Vector3.up, flat), water = p.y + PondRailOffset, a = halfLength, b = 8f };
    }

    static void ShapeTerrain(List<Terrain> terrains, CoasterTrack track, List<Pond> ponds)
    {
        foreach (var t in terrains)
        {
            var td = t.terrainData;
            Undo.RegisterCompleteObjectUndo(td, "Coaster terrain");
            int res = td.heightmapResolution;
            float[,] h = td.GetHeights(0, 0, res, res);
            Vector3 o = t.GetPosition(), size = td.size;
            bool changed = false;

            System.Func<float, float> toNorm = wy => Mathf.Clamp01((wy - o.y) / size.y);

            // 1) ponds: a bowl below the water and a bank around it
            const float rim = 10f;
            foreach (var pd in ponds)
            {
                float reach = pd.a + rim;
                ForArea(o, size, res, pd.center, reach, (ix, iz, wp) =>
                {
                    Vector3 d = wp - pd.center; d.y = 0f;
                    float u = Vector3.Dot(d, pd.fwd), v = Vector3.Dot(d, pd.right);
                    float e = Mathf.Sqrt((u / pd.a) * (u / pd.a) + (v / pd.b) * (v / pd.b));
                    float orig = h[iz, ix];
                    float target;
                    if (e < 1f) target = toNorm(pd.water - 0.3f - 1.2f * (1f - e * e));
                    else
                    {
                        float beyond = (e - 1f) * pd.b;          // metres past the shore (approx.)
                        if (beyond > rim) return;
                        target = Mathf.Lerp(toNorm(pd.water + 0.05f), orig, Mathf.SmoothStep(0f, 1f, beyond / rim));
                    }
                    h[iz, ix] = target; changed = true;
                });
            }

            // 2) keep the sand below the rails everywhere (cuts little trenches through dunes)
            for (float dist = 0f; dist < track.Length; dist += 1f)
            {
                track.Sample(dist, out var p, out _, out _);
                float cap = toNorm(p.y - Clearance);
                ForArea(o, size, res, p, 2.2f, (ix, iz, wp) =>
                {
                    if (h[iz, ix] > cap) { h[iz, ix] = cap; changed = true; }
                });
            }

            if (changed) td.SetHeights(0, 0, h);
        }
    }

    static void ForArea(Vector3 o, Vector3 size, int res, Vector3 center, float radius, System.Action<int, int, Vector3> act)
    {
        float cell = size.x / (res - 1), cellZ = size.z / (res - 1);
        int x0 = Mathf.Max(0, Mathf.FloorToInt((center.x - radius - o.x) / cell));
        int x1 = Mathf.Min(res - 1, Mathf.CeilToInt((center.x + radius - o.x) / cell));
        int z0 = Mathf.Max(0, Mathf.FloorToInt((center.z - radius - o.z) / cellZ));
        int z1 = Mathf.Min(res - 1, Mathf.CeilToInt((center.z + radius - o.z) / cellZ));
        for (int iz = z0; iz <= z1; iz++)
            for (int ix = x0; ix <= x1; ix++)
            {
                var wp = new Vector3(o.x + ix * cell, 0f, o.z + iz * cellZ);
                float dx = wp.x - center.x, dz = wp.z - center.z;
                if (dx * dx + dz * dz > radius * radius) continue;
                act(ix, iz, wp);
            }
    }

    // ------------------------------------------------------------------ props
    static List<Renderer> CollectProps()
    {
        var list = new List<Renderer>();
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (go.name == RootName) continue;
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                var b = r.bounds;
                if (b.size.x > 120f || b.size.z > 120f) continue;   // ground / sea planes
                string n = r.gameObject.name.ToLower();
                if (n.Contains("water") || n.Contains("ocean")) continue;
                list.Add(r);
            }
        }
        return list;
    }

    class PropHash
    {
        public readonly Bounds[] bounds;
        readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        readonly float size;
        static readonly List<int> Empty = new List<int>();
        public PropHash(List<Renderer> props, float cellSize)
        {
            size = cellSize;
            bounds = new Bounds[props.Count];
            for (int i = 0; i < props.Count; i++)
            {
                var b = props[i].bounds; bounds[i] = b;
                int x0 = Cell(b.min.x - 2.5f), x1 = Cell(b.max.x + 2.5f), z0 = Cell(b.min.z - 2.5f), z1 = Cell(b.max.z + 2.5f);
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        long k = Key(x, z);
                        if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>();
                        l.Add(i);
                    }
            }
        }
        int Cell(float v) { return Mathf.FloorToInt(v / size); }
        static long Key(int x, int z) { return ((long)x << 32) ^ (uint)z; }
        public List<int> Near(float x, float z) { return cells.TryGetValue(Key(Cell(x), Cell(z)), out var l) ? l : Empty; }
    }

    static int ClearProps(CoasterTrack track, List<Renderer> props, PropHash hash, List<Pond> ponds)
    {
        var toHide = new HashSet<GameObject>();
        for (float d = 0f; d < track.Length; d += 1.5f)
        {
            track.Sample(d, out var p, out _, out _);
            foreach (int i in hash.Near(p.x, p.z))
            {
                var b = hash.bounds[i];
                if (p.x < b.min.x - 2.2f || p.x > b.max.x + 2.2f || p.z < b.min.z - 2.2f || p.z > b.max.z + 2.2f) continue;
                if (b.max.y < p.y - 1.5f || b.min.y > p.y + 2.5f) continue;
                toHide.Add(PropRoot(props[i].gameObject));
            }
        }
        foreach (var pd in ponds)
            for (int i = 0; i < props.Count; i++)
            {
                var c = hash.bounds[i].center; c.y = pd.center.y;
                Vector3 d = c - pd.center;
                float u = Vector3.Dot(d, pd.fwd), v = Vector3.Dot(d, pd.right);
                if ((u / (pd.a + 6f)) * (u / (pd.a + 6f)) + (v / (pd.b + 6f)) * (v / (pd.b + 6f)) < 1f) toHide.Add(PropRoot(props[i].gameObject));
            }
        int n = 0;
        foreach (var go in toHide)
        {
            if (go == null || !go.activeSelf) continue;
            Undo.RecordObject(go, "Hide prop");
            go.SetActive(false); n++;
        }
        return n;
    }

    static GameObject PropRoot(GameObject go)
    {
        // Hide the whole prop (e.g. a tent), but never a big group like the whole environment.
        var r = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
        if (r != null && CombinedBounds(r).size.magnitude < 40f) return r;
        return go;
    }

    // ------------------------------------------------------------------ cart / train
    static GameObject BuildCart(Transform parent, string name, Mats M, int index, bool riders = true)
    {
        var cart = new GameObject(name);
        cart.transform.SetParent(parent, false);
        Part(cart, PrimitiveType.Cube, "Body", new Vector3(0, 0.1f, 0), Vector3.zero, new Vector3(1.4f, 0.5f, 2.4f), M.body);
        if (index == 0) Part(cart, PrimitiveType.Cube, "Nose", new Vector3(0, 0.25f, 1.25f), new Vector3(35, 0, 0), new Vector3(1.3f, 0.35f, 0.6f), M.trim);
        Part(cart, PrimitiveType.Cube, "Stripe", new Vector3(0, 0.2f, 0), Vector3.zero, new Vector3(1.42f, 0.12f, 2.42f), M.trim);
        Part(cart, PrimitiveType.Cube, "Seat_Front", new Vector3(0, 0.45f, 0.35f), Vector3.zero, new Vector3(1.2f, 0.15f, 0.6f), M.seat);
        Part(cart, PrimitiveType.Cube, "Back_Front", new Vector3(0, 0.75f, 0.05f), new Vector3(-10, 0, 0), new Vector3(1.2f, 0.6f, 0.12f), M.seat);
        Part(cart, PrimitiveType.Cube, "Seat_Rear", new Vector3(0, 0.45f, -0.65f), Vector3.zero, new Vector3(1.2f, 0.15f, 0.6f), M.seat);
        Part(cart, PrimitiveType.Cube, "Back_Rear", new Vector3(0, 0.75f, -0.95f), new Vector3(-10, 0, 0), new Vector3(1.2f, 0.6f, 0.12f), M.seat);
        Part(cart, PrimitiveType.Cylinder, "LapBar_Front", new Vector3(0, 0.8f, 0.6f), new Vector3(0, 0, 90), new Vector3(0.08f, 0.6f, 0.08f), M.wheel);
        Part(cart, PrimitiveType.Cylinder, "LapBar_Rear", new Vector3(0, 0.8f, -0.4f), new Vector3(0, 0, 90), new Vector3(0.08f, 0.6f, 0.08f), M.wheel);
        foreach (float x in new[] { -0.6f, 0.6f })
            foreach (float z in new[] { -0.8f, 0.8f })
                Part(cart, PrimitiveType.Cylinder, "Wheel", new Vector3(x, -0.4f, z), new Vector3(0, 0, 90), new Vector3(0.35f, 0.08f, 0.35f), M.wheel);
        // riders
        int k = index * 2;
        if (riders) foreach (float z in new[] { 0.35f, -0.65f })
        {
            Part(cart, PrimitiveType.Capsule, "Rider", new Vector3(0, 0.9f, z), Vector3.zero, new Vector3(0.45f, 0.4f, 0.4f), M.shirts[k++ % 3]);
            Part(cart, PrimitiveType.Sphere, "Head", new Vector3(0, 1.45f, z), Vector3.zero, new Vector3(0.3f, 0.3f, 0.3f), M.shirts[3]);
        }
        if (index == 0)
        {
            var lightGo = new GameObject("Headlight");
            lightGo.transform.SetParent(cart.transform, false);
            lightGo.transform.localPosition = new Vector3(0, 0.3f, 1.6f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Spot; l.range = 35f; l.spotAngle = 60f; l.intensity = 3f; l.color = new Color(1f, 0.95f, 0.8f);
        }
        var rb = cart.AddComponent<Rigidbody>();
        rb.isKinematic = true; rb.useGravity = false;
        var box = cart.AddComponent<BoxCollider>();
        box.size = new Vector3(1.6f, 1.2f, 2.6f); box.center = new Vector3(0, 0.4f, 0);
        return cart;
    }

    static GameObject Part(GameObject parent, PrimitiveType type, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos; go.transform.localEulerAngles = euler; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static void PlaceCart(Transform t, CoasterTrack track, float dist)
    {
        track.Sample(track.Wrap(dist), out var p, out var f, out var u);
        t.SetPositionAndRotation(p + u * 0.55f, Quaternion.LookRotation(f, u));
        if (t.GetComponent<Camera>() != null) t.position += u * 1.1f - f * 0.2f;
    }

    // ------------------------------------------------------------------ rider "Captain Dune"
    // An original desert-explorer character built from simple shapes, with separate
    // arms / head / body so RiderAnimator can animate them.
    static GameObject BuildRider(GameObject cart, Mats M)
    {
        var rider = new GameObject("Captain Dune (rider)");
        rider.transform.SetParent(cart.transform, false);
        rider.transform.localPosition = new Vector3(0f, 0.53f, 0.3f);

        var body = new GameObject("Body");
        body.transform.SetParent(rider.transform, false);
        Part(body, PrimitiveType.Capsule, "Torso", new Vector3(0, 0.35f, 0), Vector3.zero, new Vector3(0.42f, 0.32f, 0.3f), M.outfit);
        foreach (float x in new[] { -0.11f, 0.11f })
        {
            Part(body, PrimitiveType.Cube, "Thigh", new Vector3(x, 0.05f, 0.22f), Vector3.zero, new Vector3(0.15f, 0.15f, 0.45f), M.khaki);
            Part(body, PrimitiveType.Cube, "Shin", new Vector3(x, -0.17f, 0.44f), Vector3.zero, new Vector3(0.13f, 0.4f, 0.13f), M.khaki);
            Part(body, PrimitiveType.Cube, "Boot", new Vector3(x, -0.38f, 0.5f), Vector3.zero, new Vector3(0.15f, 0.1f, 0.24f), M.boots);
        }
        Part(body, PrimitiveType.Cylinder, "Scarf", new Vector3(0, 0.64f, 0), Vector3.zero, new Vector3(0.3f, 0.045f, 0.3f), M.scarf);
        Part(body, PrimitiveType.Cube, "Scarf Tail", new Vector3(0.08f, 0.55f, -0.16f), new Vector3(20, 0, 10), new Vector3(0.08f, 0.2f, 0.03f), M.scarf);

        var head = new GameObject("Head");
        head.transform.SetParent(body.transform, false);
        head.transform.localPosition = new Vector3(0, 0.68f, 0);
        Part(head, PrimitiveType.Sphere, "Face", new Vector3(0, 0.15f, 0), Vector3.zero, new Vector3(0.3f, 0.3f, 0.3f), M.shirts[3]);
        Part(head, PrimitiveType.Sphere, "Explorer Helmet", new Vector3(0, 0.25f, 0), Vector3.zero, new Vector3(0.34f, 0.2f, 0.34f), M.helmet);
        Part(head, PrimitiveType.Cylinder, "Helmet Brim", new Vector3(0, 0.22f, 0.02f), Vector3.zero, new Vector3(0.44f, 0.012f, 0.46f), M.helmet);
        Part(head, PrimitiveType.Cube, "Goggle Band", new Vector3(0, 0.29f, 0.12f), new Vector3(-15, 0, 0), new Vector3(0.28f, 0.035f, 0.06f), M.boots);
        foreach (float x in new[] { -0.065f, 0.065f })
            Part(head, PrimitiveType.Cylinder, "Goggle", new Vector3(x, 0.3f, 0.15f), new Vector3(75, 0, 0), new Vector3(0.09f, 0.02f, 0.09f), M.scarf);

        var anim = rider.AddComponent<RiderAnimator>();
        anim.cart = cart.GetComponent<CoasterCart>();
        anim.leftArm = MakeArm(body, "Left Arm", -0.27f, M);
        anim.rightArm = MakeArm(body, "Right Arm", 0.27f, M);
        anim.head = head.transform;
        anim.body = body.transform;
        return rider;
    }

    static Transform MakeArm(GameObject body, string name, float x, Mats M)
    {
        var pivot = new GameObject(name);
        pivot.transform.SetParent(body.transform, false);
        pivot.transform.localPosition = new Vector3(x, 0.57f, 0f);
        pivot.transform.localEulerAngles = new Vector3(-60f, 0f, x < 0 ? -8f : 8f);   // holding the lap bar
        Part(pivot, PrimitiveType.Capsule, "Arm", new Vector3(0, -0.25f, 0), Vector3.zero, new Vector3(0.12f, 0.25f, 0.12f), M.outfit);
        Part(pivot, PrimitiveType.Sphere, "Hand", new Vector3(0, -0.52f, 0), Vector3.zero, new Vector3(0.11f, 0.11f, 0.11f), M.shirts[3]);
        return pivot.transform;
    }

    [MenuItem("Tools/Coaster Kit/Put Selected Character In The Cart", false, 20)]
    public static void PutCharacterInCart()
    {
        var sel = Selection.activeGameObject;
        var cart = Object.FindAnyObjectByType<CoasterCart>();
        if (cart == null) { EditorUtility.DisplayDialog("Coaster Kit", "Build the coaster first (Tools > Coaster Kit > 2. Build Complete Coaster).", "OK"); return; }
        bool partOfCoaster = sel != null && !EditorUtility.IsPersistent(sel) &&
                             (sel.transform.root.name == RootName || sel.GetComponentInChildren<CoasterCart>() != null || sel.GetComponentInParent<CoasterCart>() != null);
        if (sel == null || partOfCoaster || sel.GetComponentInChildren<Renderer>() == null)
        {
            EditorUtility.DisplayDialog("Coaster Kit", "In the Project panel, click your character model (.fbx or prefab) once to select it, then run this menu again.", "OK");
            return;
        }
        GameObject inst = sel;
        if (EditorUtility.IsPersistent(sel))
        {
            inst = (GameObject)PrefabUtility.InstantiatePrefab(sel);
            Undo.RegisterCreatedObjectUndo(inst, "Add character");
        }
        Undo.SetTransformParent(inst.transform, cart.transform, "Put character in cart");
        inst.transform.localRotation = Quaternion.identity;
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localScale = Vector3.one;

        var animator = inst.GetComponentInChildren<Animator>();
        bool human = animator != null && animator.avatar != null && animator.avatar.isHuman;
        NormalizeHeight(inst, human ? 1.7f : 1.2f);
        if (human)
        {
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 seat = cart.transform.TransformPoint(new Vector3(0f, 0.62f, 0.2f));
            if (hips != null) inst.transform.position += seat - hips.position;
        }
        else
        {
            var b = CombinedBounds(inst);
            Vector3 seat = cart.transform.TransformPoint(new Vector3(0f, 0.53f, 0.3f));
            inst.transform.position += new Vector3(seat.x - b.center.x, seat.y - b.min.y, seat.z - b.center.z);
        }

        foreach (var old in cart.GetComponentsInChildren<RiderAnimator>(true))
        {
            if (old.gameObject == inst) continue;
            Undo.RecordObject(old.gameObject, "Hide old rider");
            old.gameObject.SetActive(false);
        }
        var ra = inst.GetComponent<RiderAnimator>();
        if (ra == null) ra = Undo.AddComponent<RiderAnimator>(inst);
        ra.cart = cart;
        if (human) ra.humanoid = animator; else ra.body = inst.transform;
        EditorSceneManager.MarkSceneDirty(inst.scene);
        Selection.activeGameObject = inst;

        EditorUtility.DisplayDialog("Coaster Kit", human
            ? "Your character is in the cart. In Play mode it sits, waves in the station, looks around on the lift and throws its arms up on the drops.\n\nIf the legs look wrong, select it and adjust 'Sit Thighs' / 'Bend Knees' on Rider Animator."
            : "Your character is in the cart and will lean and bob, but its Rig is not Humanoid, so it can't sit or raise its arms.\n\nTo fix: click the model file in the Project panel > Inspector > Rig tab > Animation Type: Humanoid > Apply. Then run this menu again.", "OK");
    }

    // ------------------------------------------------------------------ sound
    [MenuItem("Tools/Coaster Kit/Check and Fix Sound", false, 21)]
    public static void SoundMenu()
    {
        string r = SoundCheck(true);
        EditorUtility.DisplayDialog("Coaster Kit - sound check", r +
            "\n\nStill silent? Check:\n- the Windows volume and output device (headphones?)\n- Game tab: the speaker icon must NOT be crossed out\n- Recorder window: tick 'Capture audio' / 'Include audio' in the Movie recorder", "OK");
    }

    static string SoundCheck(bool fix)
    {
        var found = new List<string>();
        var objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
        if (objs != null && objs.Length > 0 && objs[0] != null)
        {
            var so = new SerializedObject(objs[0]);
            var off = so.FindProperty("m_DisableAudio");
            if (off != null && off.boolValue) { found.Add("Unity audio was switched OFF in Project Settings" + (fix ? " - turned it ON" : "")); if (fix) off.boolValue = false; }
            var vol = so.FindProperty("m_Volume");
            if (vol != null && vol.floatValue < 0.05f) { found.Add("Project volume was 0" + (fix ? " - set to 1" : "")); if (fix) vol.floatValue = 1f; }
            if (fix) so.ApplyModifiedProperties();
        }
        if (EditorUtility.audioMasterMute) { found.Add("the Game view was MUTED" + (fix ? " - unmuted" : "")); if (fix) EditorUtility.audioMasterMute = false; }
        var missing = new List<string>();
        foreach (var n in new[] { "rolling_loop", "wind_loop", "chain_lift_loop", "station_bell", "drop_whoosh", "splash", "fire_loop", "fire_burst", "explosion" })
            if (FindClip(n) == null) missing.Add(n);
        if (missing.Count > 0) found.Add("missing sound files: " + string.Join(", ", missing) + " (re-import CoasterKit.unitypackage)");
        return found.Count == 0 ? "Everything looks OK (audio on, not muted, all 9 sounds found)." : "Found: " + string.Join("; ", found) + ".";
    }

    // ------------------------------------------------------------------ station
    static void BuildStation(Transform parent, CoasterTrack track, Mats M)
    {
        float len = Mathf.Min(16f, track.LiftStart * 0.9f);
        track.Sample(len * 0.5f, out var p, out var f, out _);
        var flat = Vector3.ProjectOnPlane(f, Vector3.up).normalized;
        var terrains = GetTerrains();
        float g = Ground(terrains, p.x, p.z); if (float.IsNaN(g)) g = p.y - StationHeight;
        var station = new GameObject("Station");
        station.transform.SetParent(parent, false);
        station.transform.SetPositionAndRotation(new Vector3(p.x, g, p.z), Quaternion.LookRotation(flat));
        float h = Mathf.Max(0.3f, p.y - g - 0.05f);
        Part(station, PrimitiveType.Cube, "Platform", new Vector3(-2.6f, h * 0.5f, 0), Vector3.zero, new Vector3(3f, h, len), M.stone);
        Part(station, PrimitiveType.Cube, "Steps", new Vector3(-4.6f, h * 0.25f, 0), Vector3.zero, new Vector3(1.2f, h * 0.5f, 4f), M.stone);
        foreach (float x in new[] { -3.8f, 1.6f })
            foreach (float z in new[] { -len * 0.45f, len * 0.45f })
                Part(station, PrimitiveType.Cylinder, "Column", new Vector3(x, h + 2f, z), Vector3.zero, new Vector3(0.35f, 2f, 0.35f), M.stone);
        Part(station, PrimitiveType.Cube, "Roof", new Vector3(-1.1f, h + 4.1f, 0), new Vector3(0, 0, 4), new Vector3(6.4f, 0.25f, len + 1f), M.roof);

        // waving flags on the roof (code animation)
        var flag = FindPrefab("flag_02") ?? FindPrefab("flag");
        if (flag != null)
            foreach (float z in new[] { -len * 0.45f, len * 0.45f })
            {
                var fl = (GameObject)PrefabUtility.InstantiatePrefab(flag);
                fl.transform.SetParent(station.transform, false);
                NormalizeHeight(fl, 2.2f);
                fl.transform.localPosition = new Vector3(-1.1f, h + 4.25f, z);
                var anim = fl.AddComponent<SimpleAnimator>();
                anim.kind = SimpleAnimator.Kind.Swing; anim.amount = 8f; anim.speed = 0.6f; anim.axis = Vector3.forward;
            }
    }

    // ------------------------------------------------------------------ fire
    static void BuildFireCannon(Transform parent, CoasterTrack track, float dist, float side, Mats M, AudioClip loop, AudioClip burst, int n)
    {
        track.Sample(dist, out var p, out var f, out _);
        var flat = Vector3.ProjectOnPlane(f, Vector3.up).normalized;
        var right = Vector3.Cross(Vector3.up, flat);
        Vector3 basePos = p + right * side * 5f;
        float g = Ground(GetTerrains(), basePos.x, basePos.z); if (float.IsNaN(g)) g = p.y - 6f;
        basePos.y = g;

        var cannon = new GameObject("Fire Cannon " + n);
        cannon.transform.SetParent(parent, false);
        cannon.transform.SetPositionAndRotation(basePos, Quaternion.LookRotation(flat));
        float towerH = Mathf.Clamp(p.y - g - 1f, 1.5f, 12f);
        Part(cannon, PrimitiveType.Cylinder, "Tower", new Vector3(0, towerH * 0.5f, 0), Vector3.zero, new Vector3(1.1f, towerH * 0.5f, 1.1f), M.cannon);
        Part(cannon, PrimitiveType.Cylinder, "Nozzle", new Vector3(0, towerH + 0.2f, 0), Vector3.zero, new Vector3(0.7f, 0.2f, 0.7f), M.wheel);

        var jetGo = new GameObject("Flame");
        jetGo.transform.SetParent(cannon.transform, false);
        jetGo.transform.localPosition = new Vector3(0, towerH + 0.4f, 0);
        var box = jetGo.AddComponent<BoxCollider>(); box.isTrigger = true;
        box.center = jetGo.transform.InverseTransformPoint(p);
        box.size = new Vector3(6f, 8f, 14f);
        var jet = jetGo.AddComponent<FireJet>();
        jet.flameSize = 1.5f; jet.fireLoop = loop; jet.burstSound = burst; jet.burstMultiplier = 3.5f;
    }

    static void BuildCampFire(Transform parent, CoasterTrack track, AudioClip loop)
    {
        track.Sample(4f, out var p, out var f, out _);
        var flat = Vector3.ProjectOnPlane(f, Vector3.up).normalized;
        var right = Vector3.Cross(Vector3.up, flat);
        Vector3 pos = p - right * 9f;
        float g = Ground(GetTerrains(), pos.x, pos.z); if (float.IsNaN(g)) g = p.y - StationHeight;
        pos.y = g;
        var holder = new GameObject("Camp Fire");
        holder.transform.SetParent(parent, false);
        holder.transform.position = pos;
        var bonfire = FindPrefab("bonfire");
        if (bonfire != null)
        {
            var b = (GameObject)PrefabUtility.InstantiatePrefab(bonfire);
            b.transform.SetParent(holder.transform, false);
            NormalizeHeight(b, 0.8f);
            SitOnGround(b, pos.x, pos.z, g);
        }
        var fire = new GameObject("Flame");
        fire.transform.SetParent(holder.transform, false);
        fire.transform.localPosition = new Vector3(0, 0.3f, 0);
        var box = fire.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = Vector3.one;
        var jet = fire.AddComponent<FireJet>(); jet.flameSize = 0.6f; jet.fireLoop = loop;
    }

    // ------------------------------------------------------------------ explosion
    static void BuildExplosion(Transform parent, CoasterTrack track, Mats M, AudioClip boom, AudioClip fireLoop)
    {
        float dist = Mathf.Lerp(track.KnotDistance(track.explosionKnot), track.KnotDistance(track.explosionKnot + 1), 0.6f);
        track.Sample(dist, out var p, out var f, out _);
        var flat = Vector3.ProjectOnPlane(f, Vector3.up).normalized;
        var right = Vector3.Cross(Vector3.up, flat);
        var terrains = GetTerrains();
        Vector3 c = p + right * 9f;
        float g = Ground(terrains, c.x, c.z); if (float.IsNaN(g)) g = p.y - 3f;
        c.y = g;

        var group = new GameObject("Explosion Barrels");
        group.transform.SetParent(parent, false);
        group.transform.SetPositionAndRotation(c, Quaternion.LookRotation(flat));

        var barrel = FindPrefab("barrel"); var crate = FindPrefab("box");
        int[] rows = { 3, 2, 1 };
        float w = 1.1f, y = g + 0.02f;
        for (int r = 0; r < rows.Length; r++)
        {
            float top = y;
            for (int i = 0; i < rows[r]; i++)
            {
                float off = (i - (rows[r] - 1) * 0.5f) * w;
                Vector3 at = c + right * off;
                var item = MakeDebris(barrel, group.transform, 1.2f, M);
                SitOnGround(item, at.x, at.z, y);
                top = Mathf.Max(top, CombinedBounds(item).max.y);
            }
            y = top + 0.02f;
        }
        foreach (float off in new[] { -2.4f, 2.4f })
        {
            Vector3 at = c + right * off + flat * 1.2f;
            var item = MakeDebris(crate, group.transform, 0.9f, M);
            SitOnGround(item, at.x, at.z, g + 0.02f);
        }

        var blast = new GameObject("Blast Point");
        blast.transform.SetParent(group.transform, false);
        blast.transform.position = c + Vector3.up * 1.2f;

        // fire that keeps burning after the blast (hidden until then)
        var after = new GameObject("After-fire");
        after.transform.SetParent(group.transform, false);
        after.transform.position = c + Vector3.up * 0.2f;
        var afterJet = after.AddComponent<FireJet>(); afterJet.flameSize = 1.2f; afterJet.fireLoop = fireLoop;
        var afterBox = after.AddComponent<BoxCollider>(); afterBox.isTrigger = true; afterBox.size = Vector3.one;
        after.SetActive(false);

        // the trigger on the track, a little before the barrels
        track.Sample(dist - 6f, out var tp, out var tf, out var tu);
        var zone = new GameObject("Explosion Zone");
        zone.transform.SetParent(parent, false);
        zone.transform.SetPositionAndRotation(tp, Quaternion.LookRotation(tf, tu));
        var zb = zone.AddComponent<BoxCollider>(); zb.isTrigger = true; zb.size = new Vector3(6f, 6f, 6f); zb.center = new Vector3(0, 0.5f, 0);
        var ez = zone.AddComponent<ExplosionZone>();
        ez.explosionPoint = blast.transform; ez.explosionSound = boom; ez.size = 1.6f;
        ez.force = 900f; ez.radius = 12f; ez.showOnExplode = new[] { after };
        ez.hideOnExplode = new GameObject[0];
    }

    static GameObject MakeDebris(GameObject prefab, Transform parent, float height, Mats M)
    {
        GameObject go;
        if (prefab != null)
        {
            go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, true);
            NormalizeHeight(go, height);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, true);
            go.transform.localScale = new Vector3(0.9f, height * 0.5f, 0.9f);
            go.GetComponent<Renderer>().sharedMaterial = M.wheel;
        }
        // physics: convex colliders on every mesh + a Rigidbody on the root
        foreach (var mc in go.GetComponentsInChildren<MeshCollider>(true)) mc.convex = true;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
            var col = mf.gameObject.AddComponent<MeshCollider>();
            col.sharedMesh = mf.sharedMesh; col.convex = true;
        }
        var rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.mass = 20f; rb.isKinematic = true;    // ExplosionZone switches it to dynamic on the blast
        return go;
    }

    // ------------------------------------------------------------------ skull animation
    static void BuildSkull(Transform parent, CoasterTrack track, AudioClip sound)
    {
        float dist = track.KnotDistance(track.skullKnot);
        track.Sample(dist, out var p, out var f, out _);
        var flat = Vector3.ProjectOnPlane(f, Vector3.up).normalized;
        var right = Vector3.Cross(Vector3.up, flat);
        Vector3 at = p - right * 12f;
        float g = Ground(GetTerrains(), at.x, at.z); if (float.IsNaN(g)) g = p.y - 8f;

        var holder = new GameObject("Rising Skull (animated)");
        holder.transform.SetParent(parent, false);   // parent chain is at the world origin
        holder.transform.SetPositionAndRotation(new Vector3(at.x, g, at.z), Quaternion.LookRotation(-right));

        var prefab = FindPrefab("Skull_01") ?? FindPrefab("Skull_02");
        GameObject skull;
        if (prefab != null)
        {
            skull = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            skull.transform.SetParent(holder.transform, false);
            NormalizeHeight(skull, 4f);
            SitOnGround(skull, at.x, at.z, g);
        }
        else
        {
            skull = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(skull.GetComponent<Collider>());
            skull.transform.SetParent(holder.transform, false);
            skull.transform.localScale = Vector3.one * 4f; skull.transform.localPosition = Vector3.up * 2f;
        }
        float rise = CombinedBounds(skull).size.y + 0.6f;
        float up = g, down = g - rise;
        holder.transform.position = new Vector3(at.x, down, at.z);   // resting place: buried
        float yaw = holder.transform.eulerAngles.y;

        // --- the Animation clip (keyframes) ---
        var clip = new AnimationClip { name = "SkullRise" };
        clip.SetCurve("", typeof(Transform), "m_LocalPosition.y", new AnimationCurve(
            new Keyframe(0f, down), new Keyframe(0.5f, up), new Keyframe(1.5f, up), new Keyframe(2f, down)));
        clip.SetCurve("", typeof(Transform), "localEulerAnglesRaw.x", AnimationCurve.Constant(0f, 2f, 0f));
        clip.SetCurve("", typeof(Transform), "localEulerAnglesRaw.y", new AnimationCurve(
            new Keyframe(0f, yaw), new Keyframe(0.5f, yaw), new Keyframe(1.5f, yaw + 360f), new Keyframe(2f, yaw + 360f)));
        clip.SetCurve("", typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Constant(0f, 2f, 0f));
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        string clipPath = $"{GenFolder}/SkullRise.anim";
        AssetDatabase.DeleteAsset(clipPath);
        AssetDatabase.CreateAsset(clip, clipPath);

        // --- the Animator Controller: Hidden --(Play trigger)--> SkullRise --> Hidden ---
        string ctrlPath = $"{GenFolder}/SkullRise.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
        ctrl.AddParameter("Play", AnimatorControllerParameterType.Trigger);
        var sm = ctrl.layers[0].stateMachine;
        var hiddenState = sm.AddState("Hidden");
        var riseState = sm.AddState("SkullRise");
        riseState.motion = clip;
        sm.defaultState = hiddenState;
        var go = hiddenState.AddTransition(riseState);
        go.hasExitTime = false; go.duration = 0f;
        go.AddCondition(AnimatorConditionMode.If, 0f, "Play");
        var back = riseState.AddTransition(hiddenState);
        back.hasExitTime = true; back.exitTime = 1f; back.duration = 0f;

        var animator = holder.AddComponent<Animator>();
        animator.runtimeAnimatorController = ctrl;
        animator.applyRootMotion = false;

        // trigger zone on the track, 25 m before
        track.Sample(dist - 25f, out var tp, out var tf, out var tu);
        var zone = new GameObject("Skull Animation Trigger");
        zone.transform.SetParent(parent, false);
        zone.transform.SetPositionAndRotation(tp, Quaternion.LookRotation(tf, tu));
        var zb = zone.AddComponent<BoxCollider>(); zb.isTrigger = true; zb.size = new Vector3(6f, 6f, 6f);
        var atz = zone.AddComponent<AnimationTriggerZone>();
        atz.animator = animator; atz.triggerName = "Play"; atz.sound = sound;
    }

    // ------------------------------------------------------------------ small helpers
    static GameObject Child(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    static GameObject FindPrefab(string name)
    {
        foreach (var g in AssetDatabase.FindAssets(name + " t:Prefab"))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (System.IO.Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(p);
        }
        return null;
    }

    static AudioClip FindClip(string name)
    {
        foreach (var g in AssetDatabase.FindAssets(name + " t:AudioClip"))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (System.IO.Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<AudioClip>(p);
        }
        return null;
    }

    static Bounds CombinedBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static void NormalizeHeight(GameObject go, float height)
    {
        var b = CombinedBounds(go);
        if (b.size.y < 1e-4f) return;
        go.transform.localScale *= height / b.size.y;
    }

    static void SitOnGround(GameObject go, float x, float z, float groundY)
    {
        var b = CombinedBounds(go);
        go.transform.position += new Vector3(x - b.center.x, groundY - b.min.y, z - b.center.z);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int i = path.LastIndexOf('/');
        string parent = path.Substring(0, i), name = path.Substring(i + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    static void Progress(string info, float t) { EditorUtility.DisplayProgressBar("Coaster Kit", info, t); }
}
