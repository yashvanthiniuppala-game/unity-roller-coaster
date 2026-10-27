// RidePhoto.cs — ON-RIDE PHOTOS, like at a real theme park - up to 4 per ride, each from a different angle!
//   * hidden cameras FLASH and photograph the riders at the best moments:
//       FIRST DROP (from the front), UPSIDE DOWN in the loop (from the side), THROUGH THE FIRE (from the front),
//       WATERFALL SPLASH (low, from the front), DRAGON BATTLE (from high behind) and SANDSTORM (from the side),
//   * when the ride is over the photos slide onto the screen as white photo prints with captions,
//     with the ride's numbers next to them: ride time, top speed, lift hill, biggest drop, max G-force and airtime G,
//   * every photo is also SAVED as a .png in a "RidePhotos" folder next to your Assets folder
//     (great for your report!).
// Tick which moments you want under "Which moments" (the first 4 ticked ones along the track are used).
//
// HOW TO USE: select "Coaster Cart" > Add Component > Ride Photo. Press Play.
// Optional: drag a camera-shutter / "click" sound into "Shutter Sound".
// Keys: P = show / hide the photos.
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(350)]
public class RidePhoto : MonoBehaviour
{
    public CoasterCart cart;

    [Header("Which moments (found automatically)")]
    public bool firstDrop = true;
    public bool upsideDown = true;
    public bool fire = true;
    public bool waterfall = true;
    public bool dragonBattle = false;
    public bool sandstorm = false;
    [Range(1, 4)] public int maxPhotos = 4;

    [Header("Front-view cameras")]
    [Tooltip("The front photo camera stands this far ahead of the cart on the track")] public float cameraAhead = 8f;
    [Tooltip("Height of the front photo camera above the rails")] public float cameraHeight = 1.6f;
    [Range(20f, 70f)] public float photoFieldOfView = 42f;

    [Header("Showing the photos")]
    [Tooltip("Seconds after the cart stops before the photo appears (leaves time for the fireworks)")] public float showDelay = 6.5f;
    public float showSeconds = 8f;
    public string rideName = "DESERT DRAGON COASTER";
    [Tooltip("Show the ride numbers (time, speed, height, G-force) next to the photos")] public bool showStats = false;
    [Tooltip("Save each photo as a .png in the RidePhotos folder")] public bool saveToFile = true;

    [Header("Flash")]
    public bool flashLight = true;
    public AudioClip shutterSound;
    [Range(0f, 1f)] public float volume = 1f;

    Camera photoCam;
    RenderTexture rt;
    enum Angle { Front, Side, LowFront, HighBehind }
    class Spot { public string label; public float dist = -1f; public bool onDrop, onUpsideDown, done; public Angle angle; public float fov; }
    class Photo { public Texture2D tex; public string label; }
    class Shot { public Vector3 pos; public Quaternion rot; public float fov; public string label; }
    readonly List<Spot> spots = new List<Spot>();
    readonly List<Photo> photos = new List<Photo>();        // this ride's photos
    readonly List<Photo> shown = new List<Photo>();         // the photos on screen (last finished ride)
    readonly Queue<Shot> queue = new Queue<Shot>();
    Shot current;
    int captureStep;               // 0 idle, 1 camera rendering this frame, 2 read it back
    bool armed;
    float dropTimer = -1f, lastDist, flash, snapText, showAt = -1f, showT = -1f;
    string snapLabel = "";
    Light flashL;
    // stats of this ride
    float topSpeed, maxG, minG = 9f, maxHeight, startY, peakY, maxDrop;          // live, during the ride
    float sTop, sMaxG, sMinG = 1f, sHeight, sDrop, rideTime;                     // frozen when the ride ends (shown on screen)
    int savedCount;
    GUIStyle big, small, statName, statVal, caption;

    void Start()
    {
        if (cart == null) cart = GetComponent<CoasterCart>();
        if (cart == null) cart = FindAnyObjectByType<CoasterCart>();
        if (cart == null) { enabled = false; return; }
        startY = cart.transform.position.y;
        rt = new RenderTexture(1280, 800, 24) { antiAliasing = 4, name = "Ride Photo" };
        var go = new GameObject("Ride Photo Camera");
        photoCam = go.AddComponent<Camera>();
        photoCam.targetTexture = rt;
        photoCam.fieldOfView = photoFieldOfView;
        photoCam.nearClipPlane = 0.1f; photoCam.farClipPlane = 2000f;
        photoCam.depth = -50;
        photoCam.enabled = false;
        BuildSpots();
        cart.RideStarted += () =>
        {
            foreach (var sp in spots) sp.done = false;
            photos.Clear(); armed = false; dropTimer = -1f; showAt = -1f; lastDist = 0f;
            topSpeed = 0f; maxG = 0f; minG = 9f; maxHeight = 0f; maxDrop = 0f; startY = peakY = cart.transform.position.y;
        };
        cart.RideFinished += () =>
        {
            rideTime = cart.lastRideTime > 0f ? cart.lastRideTime : cart.rideTime;
            // freeze this ride's numbers (the next ride may start before the photo is shown)
            sTop = topSpeed; sMaxG = maxG; sMinG = minG < 9f ? minG : 1f; sHeight = maxHeight; sDrop = maxDrop;
            Report();
            if (photos.Count > 0)
            {
                shown.Clear(); shown.AddRange(photos);
                // show the photos AFTER the fireworks (if there is a fireworks show)
                var fw = FindAnyObjectByType<FireworksFinale>();
                showAt = Time.time + (fw != null && fw.isActiveAndEnabled ? fw.duration + 2f : showDelay);
            }
        };
    }

    void Update()
    {
        if (cart == null) return;
        if (PhotoKey()) showT = showT >= 0f ? -1f : 0f;

        // finish the capture started last frame, then start the next one in the queue
        if (captureStep == 2) ReadBack();
        if (captureStep == 1) captureStep = 2;
        if (captureStep == 0 && queue.Count > 0) StartCapture(queue.Dequeue());

        if (cart.state == CoasterCart.RideState.Riding)
        {
            topSpeed = Mathf.Max(topSpeed, cart.speed);
            maxG = Mathf.Max(maxG, cart.gForce);
            if (!cart.onLift) minG = Mathf.Min(minG, cart.gForce);
            float y = cart.transform.position.y;
            maxHeight = Mathf.Max(maxHeight, y - startY);
            peakY = Mathf.Max(peakY, y);
            maxDrop = Mathf.Max(maxDrop, peakY - y);
            WatchForPhotoSpots();
            lastDist = cart.distance;
        }

        if (showAt > 0f && Time.time >= showAt) { showAt = -1f; showT = 0f; }
        if (showT >= 0f) { showT += Time.deltaTime; if (showT > showSeconds) showT = -1f; }
        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 3.5f);
        snapText = Mathf.MoveTowards(snapText, 0f, Time.deltaTime);
        if (flashL != null) { flashL.intensity = 12f * flash; if (flash <= 0f) { Destroy(flashL.gameObject); flashL = null; } }
    }

    // ------------------------------------------------------------------ the photo spots
    void BuildSpots()
    {
        spots.Clear();
        var t = cart.track;
        if (firstDrop) spots.Add(new Spot { label = "FIRST DROP!", onDrop = true, angle = Angle.Front, fov = photoFieldOfView });
        if (upsideDown) spots.Add(new Spot { label = "UPSIDE DOWN!", onUpsideDown = true, angle = Angle.Side, fov = 50f });
        if (fire)
        {
            var g = FindAnyObjectByType<FireGauntlet>();
            if (g != null) spots.Add(new Spot { label = "THROUGH THE FIRE!", dist = g.startDistance + g.length * 0.45f, angle = Angle.Front, fov = 55f });
        }
        if (waterfall)
        {
            var o = FindAnyObjectByType<OasisWaterfall>();
            if (o != null) spots.Add(new Spot { label = "SPLASHDOWN!", dist = o.distance - 1.5f, angle = Angle.LowFront, fov = 55f });
        }
        if (dragonBattle)
        {
            var d = FindAnyObjectByType<DragonBattle>();
            if (d != null) spots.Add(new Spot { label = "DRAGON BATTLE!", dist = d.battleDistance - 4f, angle = Angle.HighBehind, fov = 65f });
        }
        if (sandstorm)
        {
            var st = FindAnyObjectByType<SandTornado>();
            if (st != null) spots.Add(new Spot { label = "INTO THE SANDSTORM!", dist = st.nearTrackDistance, angle = Angle.Side, fov = 60f });
        }
        // keep the first 'maxPhotos' along the track (drop and loop come early in the ride)
        float lift = t != null ? t.LiftEnd : 0f;
        spots.Sort((x, y) => (x.dist < 0f ? lift + (x.onUpsideDown ? 30f : 0f) : x.dist).CompareTo(y.dist < 0f ? lift + (y.onUpsideDown ? 30f : 0f) : y.dist));
        while (spots.Count > maxPhotos) spots.RemoveAt(spots.Count - 1);
        Debug.Log("[Ride Photo] Photo moments: " + string.Join(", ", spots.ConvertAll(x => x.label)));
    }

    void WatchForPhotoSpots()
    {
        var track = cart.track;
        float d = cart.distance;
        bool afterLift = track == null || (!cart.onLift && d >= track.LiftEnd);
        // first drop: just after the cart tips over the top of the lift hill
        if (afterLift && !armed && cart.transform.forward.y < -0.45f) { armed = true; dropTimer = 0.35f; }
        if (armed && dropTimer > 0f) { dropTimer -= Time.deltaTime; if (dropTimer <= 0f) Fire(s => s.onDrop); }
        // upside down: near the top of the loop / in the twist
        if (afterLift && cart.transform.up.y < -0.6f) Fire(s => s.onUpsideDown);
        // the others: when the cart passes their distance
        foreach (var sp in spots)
            if (!sp.done && sp.dist >= 0f && lastDist < sp.dist && d >= sp.dist) Snap(sp);
    }

    void Fire(System.Predicate<Spot> match)
    {
        foreach (var sp in spots) if (!sp.done && match(sp)) { Snap(sp); return; }
    }

    void Snap(Spot sp)
    {
        sp.done = true;
        var c = cart.transform;
        var track = cart.track;
        Vector3 target = c.position + c.up * 1.1f, pos;
        Vector3 up = c.up;
        switch (sp.angle)
        {
            case Angle.Side:                      // from the side, a bit back: shows the cart and the loop / storm around it
                pos = target + c.right * 11f - c.forward * 2f;
                up = Vector3.up;
                break;
            case Angle.HighBehind:                // high behind the cart: riders, track ahead and the dragons above
                pos = target - c.forward * 9f + Vector3.up * 6f + c.right * 2f;
                target = c.position + c.forward * 12f + Vector3.up * 8f;
                up = Vector3.up;
                break;
            default:                              // front: ahead on the track, looking back at the riders' faces
                float ahead = sp.angle == Angle.LowFront ? cameraAhead * 0.8f : cameraAhead;
                float hgt = sp.angle == Angle.LowFront ? cameraHeight * 0.6f : cameraHeight;
                if (track != null)
                {
                    track.Sample(track.Wrap(cart.distance + ahead), out Vector3 p, out Vector3 f, out Vector3 u);
                    pos = p + u * hgt + Vector3.Cross(u, f).normalized * 1.2f;
                }
                else pos = target + c.forward * ahead;
                break;
        }
        queue.Enqueue(new Shot { pos = pos, rot = Quaternion.LookRotation(target - pos, up), fov = sp.fov, label = sp.label });

        // the flash
        flash = 1f; snapText = 1.2f; snapLabel = sp.label;
        if (flashLight)
        {
            if (flashL == null) flashL = new GameObject("Photo Flash").AddComponent<Light>();
            flashL.transform.position = pos;
            flashL.type = LightType.Point; flashL.range = 25f; flashL.color = new Color(0.95f, 0.97f, 1f); flashL.shadows = LightShadows.None;
        }
        var main = Camera.main;
        if (shutterSound != null) AudioSource.PlayClipAtPoint(shutterSound, main != null ? main.transform.position : pos, volume);
        Debug.Log($"[Ride Photo] Click! {sp.label} at {cart.distance:F0} m.");
    }

    void StartCapture(Shot shot)
    {
        current = shot;
        photoCam.transform.SetPositionAndRotation(shot.pos, shot.rot);
        photoCam.fieldOfView = shot.fov;
        var main = Camera.main;
        if (main != null) { photoCam.cullingMask = main.cullingMask; photoCam.clearFlags = main.clearFlags; photoCam.backgroundColor = main.backgroundColor; }
        photoCam.enabled = true;               // renders at the end of this frame
        captureStep = 1;
    }

    void ReadBack()
    {
        captureStep = 0;
        photoCam.enabled = false;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false) { name = "Ride Photo " + current.label };
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = old;
        photos.Add(new Photo { tex = tex, label = current.label });
        if (!saveToFile) return;
        try
        {
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "RidePhotos"));
            System.IO.Directory.CreateDirectory(dir);
            string name = current.label.Replace("!", "").Replace(" ", "_").ToLower();
            string path = System.IO.Path.Combine(dir, "ride_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + name + ".png");
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            savedCount++;
            Debug.Log("[Ride Photo] Saved: " + path);
        }
        catch (System.Exception e) { Debug.LogWarning("[Ride Photo] Could not save the photo: " + e.Message); }
    }

    /// <summary>Prints the ride's numbers in the Console (for the report) and how to fix the ride time.</summary>
    void Report()
    {
        var t = cart.track;
        string msg = $"[Ride Photo] RIDE REPORT - ride time {rideTime:F1} s, top speed {sTop * 3.6f:F0} km/h, lift hill {sHeight:F0} m, " +
                     $"biggest drop {sDrop:F0} m, max {sMaxG:F1} G, airtime {sMinG:F1} G" + (t != null ? $", track length {t.Length:F0} m" : "");
        Debug.Log(msg);
        if (t == null || rideTime <= 60f) return;
        // the chain lift runs at a constant speed, so a faster lift shortens the ride by a predictable amount
        float lift = t.LiftEnd - t.LiftStart, liftTime = lift / Mathf.Max(0.1f, cart.liftSpeed);
        float needed = liftTime - (rideTime - 55f);                    // aim for 55 s
        if (needed > 1.5f)
            Debug.LogWarning($"[Ride Photo] Ride is {rideTime:F1} s (must be 30-60 s). Coaster Cart > Lift Speed: change {cart.liftSpeed:F1} to {Mathf.Ceil(lift / needed * 2f) / 2f:F1} m/s for about 55 s.");
        else
            Debug.LogWarning($"[Ride Photo] Ride is {rideTime:F1} s (must be 30-60 s). Set Lift Speed to {Mathf.Ceil(lift / 2.5f):F0} m/s AND Dispatch Speed to 6 m/s, then check again.");
    }

    // ------------------------------------------------------------------ on screen
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        float W = Screen.width, H = Screen.height;
        if (flash > 0f)
        {
            GUI.color = new Color(1f, 1f, 1f, flash * 0.75f);
            GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
        }
        MakeStyles(H);
        if (snapText > 0f)
        {
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(snapText));
            Shadowed(new Rect(0, H * 0.12f, W, H * 0.1f), "SNAP!  " + snapLabel, big);
        }
        GUI.color = Color.white;
        if (showT < 0f || shown.Count == 0) return;

        // dark background, fading in and out
        float inT = Mathf.SmoothStep(0f, 1f, showT / 0.7f), outT = Mathf.Clamp01((showSeconds - showT) / 0.6f);
        GUI.color = new Color(0f, 0f, 0f, 0.55f * inT * outT);
        GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);

        // the photo prints: 1 big, or a 2x2 grid, each one sliding in a little after the one before
        int n = shown.Count;
        float areaW = showStats ? W * 0.6f : W * 0.9f, areaH = H * 0.86f, ax = showStats ? W * 0.03f : W * 0.05f, ay = H * 0.07f;
        int cols = n == 1 ? 1 : 2, rowsN = n <= 2 ? 1 : 2;
        float cellW = areaW / cols, cellH = areaH / rowsN;
        float pw = Mathf.Min(cellW * 0.86f, cellH * 0.78f * 1.6f), ph = pw / 1.6f, border = pw * 0.04f, capH = ph * 0.2f;
        var frameAll = new Rect(ax, ay, areaW, areaH);
        for (int i = 0; i < n; i++)
        {
            float t = Mathf.SmoothStep(0f, 1f, (showT - i * 0.35f) / 0.7f);
            if (t <= 0f) continue;
            float cx = ax + cellW * (i % cols + 0.5f), cy = ay + cellH * (i / cols + 0.5f);
            float fw = pw + border * 2f, fh = ph + border * 2f + capH;
            var frame = new Rect(cx - fw * 0.5f, cy - fh * 0.5f + (1f - t) * H, fw, fh);
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(i % 2 == 0 ? -3f : 2.5f, frame.center);
            GUI.color = new Color(0f, 0f, 0f, 0.35f * outT);                              // shadow
            GUI.DrawTexture(new Rect(frame.x + 7, frame.y + 9, frame.width, frame.height), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, outT);
            GUI.DrawTexture(frame, Texture2D.whiteTexture);                                // white photo paper
            GUI.DrawTexture(new Rect(frame.x + border, frame.y + border, pw, ph), shown[i].tex, ScaleMode.ScaleAndCrop);
            GUI.color = new Color(0.15f, 0.15f, 0.2f, outT);
            caption.fontSize = Mathf.RoundToInt(capH * 0.45f);
            GUI.Label(new Rect(frame.x, frame.y + border + ph, frame.width, capH), shown[i].label, caption);
            GUI.matrix = matrix;
        }
        var fx = frameAll.x; var frameR = frameAll;

        if (!showStats) { GUI.color = Color.white; return; }
        // the ride's numbers
        float sx = frameR.xMax + W * 0.03f, sy = H * 0.14f, line = H * 0.08f;
        GUI.color = new Color(1f, 1f, 1f, outT);
        Shadowed(new Rect(sx, sy - line * 1.1f, W - sx, line), rideName, statVal, TextAnchor.MiddleLeft);
        Shadowed(new Rect(sx, sy, W - sx, line), "YOUR RIDE  #" + Mathf.Max(1, cart.ridesCompleted), big, TextAnchor.MiddleLeft);
        var rows = new List<(string, string)>
        {
            ("Ride time", rideTime.ToString("F1") + " s"),
            ("Top speed", (sTop * 3.6f).ToString("F0") + " km/h  (" + (sTop * 2.237f).ToString("F0") + " mph)"),
            ("Lift hill height", sHeight.ToString("F0") + " m"),
            ("Biggest drop", sDrop.ToString("F0") + " m"),
            ("Max G-force", sMaxG.ToString("F1") + " G"),
            ("Airtime", sMinG.ToString("F1") + " G  (floating!)"),
        };
        for (int i = 0; i < rows.Count; i++)
        {
            float y = sy + line * (1.3f + i);
            Shadowed(new Rect(sx, y, W - sx, line * 0.5f), rows[i].Item1, statName, TextAnchor.MiddleLeft);
            Shadowed(new Rect(sx, y + line * 0.42f, W - sx, line * 0.6f), rows[i].Item2, statVal, TextAnchor.MiddleLeft);
        }
        if (savedCount > 0)
            Shadowed(new Rect(sx, sy + line * 7.6f, W - sx, line * 0.6f), "Photos saved in the RidePhotos folder", small, TextAnchor.MiddleLeft);
        GUI.color = Color.white;
    }

    void MakeStyles(float H)
    {
        int b = Mathf.RoundToInt(H * 0.042f), s = Mathf.RoundToInt(H * 0.022f), v = Mathf.RoundToInt(H * 0.027f);
        if (big != null && big.fontSize == b) return;
        big = new GUIStyle(GUI.skin.label) { fontSize = b, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        small = new GUIStyle(GUI.skin.label) { fontSize = s, alignment = TextAnchor.MiddleCenter };
        statName = new GUIStyle(GUI.skin.label) { fontSize = v, alignment = TextAnchor.MiddleLeft };
        statVal = new GUIStyle(GUI.skin.label) { fontSize = v, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        caption = new GUIStyle(GUI.skin.label) { fontSize = s, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        caption.normal.textColor = Color.white;
    }

    static void Shadowed(Rect r, string text, GUIStyle st, TextAnchor? align = null)
    {
        var a = st.alignment;
        if (align.HasValue) st.alignment = align.Value;
        var c = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, c.a * 0.8f);
        st.normal.textColor = Color.white;
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, st);
        GUI.color = c;
        GUI.Label(r, text, st);
        st.alignment = a;
    }

    static bool PhotoKey()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        return kb != null && kb.pKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.P);
#else
        return false;
#endif
    }

    void OnDestroy() { if (rt != null) rt.Release(); }
}
