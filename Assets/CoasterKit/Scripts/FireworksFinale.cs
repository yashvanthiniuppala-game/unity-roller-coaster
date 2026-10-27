// FireworksFinale.cs — a FIREWORKS show over the desert when the ride comes back to the station.
//   * rockets whistle up from the desert around the station, leaving sparkling trails,
//   * they burst into different shapes: big round flowers (peony), rings, golden willows that drip down,
//     two-colour bursts, crackling glitter and a HEART,
//   * every burst lights up the ground (a flash of coloured light) and you hear the BOOM a moment later
//     (sound is slower than light - real physics!),
//   * the sky gets darker for the show so the colours pop, then the day comes back,
//   * a GRAND FINALE: lots of rockets at once at the end,
//   * a camera shot from in front of the riders, looking up at the fireworks behind them.
//
// HOW TO USE: GameObject > Create Empty, name it "Fireworks", Add Component > Fireworks Finale. Press Play.
// Optional sounds: drag explosion.wav (CoasterKit > Audio) into "Booms", and a whistle / crackle if you have them
// (free: pixabay.com/sound-effects - search "firework whistle", "firework crackle").
// TEST: press  F  in Play mode to start the show any time.
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(400)]
public class FireworksFinale : MonoBehaviour
{
    public CoasterCart cart;

    [Header("When")]
    [Tooltip("Start the show already at the brakes (normally it starts when the cart stops in the station)")] public bool startAtBrakes = false;
    [Tooltip("Keep the train in the station until the show (and the ride photos) are over, so the next ride doesn't start during the fireworks")] public bool holdTrainForShow = true;
    public bool everyRide = true;

    [Header("Show")]
    [Range(6, 60)] public int rockets = 26;
    [Tooltip("Seconds of the show")] public float duration = 13f;
    [Tooltip("Rockets fired all together at the end")] [Range(0, 20)] public int grandFinale = 9;
    [Tooltip("Rockets that burst straight away when the ride ends (so the sky is full of colour the moment the camera turns)")] [Range(0, 8)] public int openingSalvo = 4;
    [Tooltip("Rockets start this far behind the station (metres) - in front of the finale camera")] public Vector2 distanceFromStation = new Vector2(55f, 85f);
    [Tooltip("How wide the rockets are spread out left/right (metres)")] public float spreadWidth = 45f;
    [Tooltip("Height of the bursts above the ground (metres)")] public Vector2 burstHeight = new Vector2(35f, 60f);
    [Range(0.5f, 2.5f)] public float burstSize = 1.2f;
    public bool heartBurst = true;
    [Tooltip("Make the sky darker during the show so the colours pop")] public bool darkenSky = true;
    [Range(0f, 1f)] public float skyDarkness = 0.7f;
    [Tooltip("Coloured flashes of light on the ground")] public bool burstLights = true;

    [Header("Camera")]
    [Tooltip("During the show: the camera looks up at the fireworks in the sky")] public bool finaleShot = true;
    [Tooltip("Seconds of the camera shot (0 = the whole show)")] public float shotSeconds = 0f;
    [Tooltip("How wide the sky shot is (bigger = more sky)")] [Range(40f, 90f)] public float skyFieldOfView = 70f;

    /// <summary>True while the fireworks show is running (riders wait before boarding).</summary>
    public static bool Busy;
    float savedWait = -1f;

    [Header("Sound (optional)")]
    public AudioClip[] launches;
    public AudioClip[] booms;
    public AudioClip[] crackles;
    [Range(0f, 1f)] public float volume = 1f;

    static readonly Color[] Palette =
    {
        new Color(1f, 0.25f, 0.3f), new Color(1f, 0.8f, 0.25f), new Color(0.3f, 0.7f, 1f), new Color(0.4f, 1f, 0.45f),
        new Color(0.85f, 0.4f, 1f), new Color(1f, 0.5f, 0.15f), new Color(1f, 1f, 1f), new Color(0.3f, 1f, 0.95f), new Color(1f, 0.45f, 0.75f)
    };
    enum Kind { Peony, Ring, Willow, TwoColour, Crackle, Heart }
    class Rocket { public Vector3 pos, vel; public float fuse; public Kind kind; public Color a, b; }
    class Pending { public float time; public Vector3 pos; public Color col; }
    class Flash { public Light l; public float t, peak; }

    ParticleSystem trail, stars, willow, glitter, glow;
    readonly List<Rocket> flying = new List<Rocket>();
    readonly List<float> schedule = new List<float>();
    readonly List<Pending> crackleList = new List<Pending>();
    readonly List<Flash> flashes = new List<Flash>();
    float showTime = -1f, shotTime = -1f, dark, darkTarget;
    bool firedThisRide, heartDone;
    int quickLeft;                 // opening rockets with a short fuse, so the sky lights up the moment the ride ends
    Vector3 station, stationFwd;
    // the sky before the show
    Light sun; float sunI, ambI, skyExp = -1f; Color ambC, fogC;

    void Start()
    {
        if (cart == null) cart = FindAnyObjectByType<CoasterCart>();
        if (cart == null) { Debug.LogWarning("[Fireworks] No Coaster Cart found."); enabled = false; return; }
        if (cart.track != null) { if (cart.track.Length < 1f) cart.track.Build(); cart.track.Sample(0f, out station, out stationFwd, out _); }
        else { station = cart.transform.position; stationFwd = cart.transform.forward; }
        stationFwd = Vector3.ProjectOnPlane(stationFwd, Vector3.up).normalized;
        BuildSystems();
        cart.RideStarted += () =>
        {
            firedThisRide = false;
            if (savedWait >= 0f) { cart.waitInStation = savedWait; savedWait = -1f; }
        };
        cart.RideFinished += () =>
        {
            if (!firedThisRide && (everyRide || cart.ridesCompleted <= 1)) Begin();
            if (finaleShot && showTime >= 0f) shotTime = 0f;
            // keep the train in the station during the show (and while the ride photos are on screen)
            if (holdTrainForShow && showTime >= 0f)
            {
                var photo = FindAnyObjectByType<RidePhoto>();
                float hold = duration + 3f + (photo != null && photo.isActiveAndEnabled ? photo.showSeconds + 1f : 0f);
                savedWait = cart.waitInStation;
                cart.waitInStation = Mathf.Max(savedWait, hold);     // CoasterCart reads this right after RideFinished
            }
        };
    }

    // ------------------------------------------------------------------ particle systems
    void BuildSystems()
    {
        var soft = PipelineUtil.MakeParticleMaterial(true, SoftTex());
        trail = Sys("Rocket Sparks", soft, 0.25f, false, 0f);
        stars = Sys("Stars", soft, 0.32f, true, 1.1f);
        willow = Sys("Willow", soft, 0.55f, true, 0.6f);
        glitter = Sys("Glitter", soft, 0.05f, false, 0f);
        glow = Sys("Burst Glow", soft, 0f, false, 0f);
        var n = glitter.noise; n.enabled = true; n.strength = 1.2f; n.frequency = 2f;
    }

    ParticleSystem Sys(string name, Material mat, float gravity, bool trails, float drag)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.loop = false; m.playOnAwake = false; m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.gravityModifier = gravity; m.maxParticles = 20000;
        var e = ps.emission; e.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var c = ps.colorOverLifetime; c.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
        c.color = new ParticleSystem.MinMaxGradient(g);
        if (drag > 0f) { var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.drag = drag; lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = true; }
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        if (trails)
        {
            var t = ps.trails; t.enabled = true; t.lifetime = 0.35f; t.dieWithParticles = true; t.inheritParticleColor = true;
            t.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            r.trailMaterial = mat;
        }
        ps.Play();
        return ps;
    }

    // ------------------------------------------------------------------ the show
    public void Begin()
    {
        firedThisRide = true; heartDone = false;
        quickLeft = openingSalvo;
        showTime = 0f;
        schedule.Clear();
        int normal = Mathf.Max(1, rockets - grandFinale);
        float main = duration - 2.5f;
        for (int i = 0; i < normal; i++)
        {
            float x = (i + Random.Range(-0.3f, 0.3f)) / normal;
            schedule.Add(Mathf.Clamp(Mathf.Pow(x, 0.85f) * main, 0f, main));    // a bit faster towards the end
        }
        for (int i = 0; i < grandFinale; i++) schedule.Add(main + 0.6f + Random.Range(0f, 0.9f));
        for (int i = 0; i < openingSalvo; i++) schedule.Add(i * 0.12f);     // opening salvo: bursts right away
        schedule.Sort();
        if (darkenSky) { CaptureSky(); darkTarget = skyDarkness; }
        Debug.Log("[Fireworks] Show starts!");
    }

    void Update()
    {
        if (cart == null) return;
        if (TestKey()) { Begin(); if (finaleShot) shotTime = 0f; }
        bool riding = cart.state == CoasterCart.RideState.Riding;
        if (startAtBrakes && !firedThisRide && riding && cart.braking && (everyRide || cart.ridesCompleted == 0)) Begin();
        // the sky already starts getting dark on the brakes, so it's dark when the show begins
        if (darkenSky && !firedThisRide && riding && cart.braking && darkTarget <= 0f && (everyRide || cart.ridesCompleted == 0)) { CaptureSky(); darkTarget = skyDarkness; }
        Busy = showTime >= 0f;

        float dt = Time.deltaTime;
        if (showTime >= 0f)
        {
            showTime += dt;
            while (schedule.Count > 0 && schedule[0] <= showTime) { Launch(); schedule.RemoveAt(0); }
            if (schedule.Count == 0 && flying.Count == 0 && crackleList.Count == 0 && showTime > duration) { showTime = -1f; darkTarget = 0f; }
        }
        UpdateRockets(dt);
        UpdateCrackles();
        UpdateFlashes(dt);
        UpdateSky(dt);
    }

    void Launch()
    {
        // behind the station (the finale camera looks that way, over the riders' heads)
        Vector3 right = Vector3.Cross(Vector3.up, stationFwd);
        Vector3 p = station - stationFwd * Random.Range(distanceFromStation.x, distanceFromStation.y) + right * Random.Range(-spreadWidth, spreadWidth);
        p.y = GroundY(p);
        float h = Random.Range(burstHeight.x, burstHeight.y);
        float fuse = quickLeft > 0 ? Random.Range(0.55f, 0.8f) : Random.Range(1.4f, 1.9f);
        if (quickLeft > 0) quickLeft--;
        // speed needed to reach the height in 'fuse' seconds against gravity: h = v t - g t²/2
        float v = h / fuse + 0.5f * 9.81f * 0.6f * fuse;
        Kind k = (Kind)Random.Range(0, 5);
        if (heartBurst && !heartDone && showTime > duration * 0.4f) { k = Kind.Heart; heartDone = true; }
        var rk = new Rocket
        {
            pos = p, vel = new Vector3(Random.Range(-2f, 2f), v, Random.Range(-2f, 2f)), fuse = fuse, kind = k,
            a = Palette[Random.Range(0, Palette.Length)], b = Palette[Random.Range(0, Palette.Length)]
        };
        if (k == Kind.Heart) rk.a = rk.b = new Color(1f, 0.3f, 0.5f);
        if (k == Kind.Willow) rk.a = rk.b = new Color(1f, 0.75f, 0.35f);
        flying.Add(rk);
        PlayAt(launches, p, 0.6f);
    }

    void UpdateRockets(float dt)
    {
        var ep = new ParticleSystem.EmitParams();
        for (int i = flying.Count - 1; i >= 0; i--)
        {
            var rk = flying[i];
            rk.vel += Vector3.up * (-9.81f * 0.6f) * dt;
            rk.pos += rk.vel * dt;
            rk.fuse -= dt;
            // sparkly trail behind the rocket
            for (int s = 0; s < 3; s++)
            {
                ep.position = rk.pos - rk.vel * dt * Random.value;
                ep.velocity = Random.insideUnitSphere * 1.5f - rk.vel * 0.05f;
                ep.startSize = Random.Range(0.25f, 0.5f);
                ep.startLifetime = Random.Range(0.3f, 0.7f);
                ep.startColor = new Color(1f, Random.Range(0.6f, 0.85f), 0.4f, 1f);
                trail.Emit(ep, 1);
            }
            if (rk.fuse <= 0f) { Burst(rk); flying.RemoveAt(i); }
        }
    }

    void Burst(Rocket rk)
    {
        Vector3 c = rk.pos;
        float s = burstSize;
        var ep = new ParticleSystem.EmitParams();
        var cam = Camera.main;
        Vector3 camRight = cam != null ? cam.transform.right : Vector3.right;
        Vector3 camUp = Vector3.up;
        switch (rk.kind)
        {
            case Kind.Peony:
            case Kind.TwoColour:
            case Kind.Crackle:
                for (int i = 0; i < 170; i++)
                {
                    Vector3 d = Random.onUnitSphere;
                    Color col = rk.kind == Kind.TwoColour && d.y > 0f ? rk.b : rk.a;
                    Star(stars, ep, c, d * Random.Range(15f, 17f) * s, col, Random.Range(1.6f, 2.2f), 0.55f * s);
                }
                if (rk.kind == Kind.Crackle)
                    for (int i = 0; i < 40; i++)
                        crackleList.Add(new Pending { time = Time.time + Random.Range(1.1f, 1.8f), pos = c + Random.onUnitSphere * Random.Range(10f, 17f) * s, col = Color.white });
                break;
            case Kind.Ring:
            {
                Quaternion tilt = Quaternion.Euler(Random.Range(50f, 80f), Random.Range(0f, 360f), 0f);
                for (int i = 0; i < 100; i++)
                {
                    float a = i / 100f * Mathf.PI * 2f;
                    Vector3 d = tilt * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Star(stars, ep, c, d * 16f * s, rk.a, Random.Range(1.6f, 2f), 0.6f * s);
                }
                for (int i = 0; i < 40; i++) Star(stars, ep, c, Random.onUnitSphere * 6f * s, rk.b, 1.4f, 0.5f * s);   // a small core
                break;
            }
            case Kind.Willow:
                for (int i = 0; i < 130; i++)
                    Star(willow, ep, c, Random.onUnitSphere * Random.Range(9f, 11f) * s, rk.a, Random.Range(3.2f, 4.2f), 0.45f * s);
                break;
            case Kind.Heart:
                for (int i = 0; i < 140; i++)
                {
                    float t = i / 140f * Mathf.PI * 2f;
                    float x = 16f * Mathf.Pow(Mathf.Sin(t), 3f);
                    float y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
                    Vector3 d = (camRight * x + camUp * y) / 16f;
                    Star(stars, ep, c, d * 15f * s, rk.a, 2.2f, 0.6f * s);
                }
                break;
        }
        // a soft glow ball where it burst
        ep = new ParticleSystem.EmitParams { position = c, velocity = Vector3.zero, startSize = 22f * s, startLifetime = 0.35f, startColor = Color.Lerp(rk.a, Color.white, 0.4f) };
        glow.Emit(ep, 1);
        if (burstLights) AddFlash(c, rk.a);
        // sound travels at 343 m/s: the boom comes a little after the flash
        var cm = Camera.main;
        float delay = cm != null ? Vector3.Distance(cm.transform.position, c) / 343f : 0f;
        PlayAt(booms, c, 1f, delay);
        if (rk.kind == Kind.Crackle) PlayAt(crackles, c, 0.8f, delay + 1.1f);
        if (cm != null && Vector3.Distance(cm.transform.position, c) < 70f) RideCamera.Shake(0.05f, 0.25f);
    }

    static void Star(ParticleSystem ps, ParticleSystem.EmitParams ep, Vector3 at, Vector3 vel, Color col, float life, float size)
    {
        ep.position = at;
        ep.velocity = vel;
        ep.startColor = col;
        ep.startLifetime = life;
        ep.startSize = size;
        ps.Emit(ep, 1);
    }

    void UpdateCrackles()
    {
        var ep = new ParticleSystem.EmitParams();
        for (int i = crackleList.Count - 1; i >= 0; i--)
        {
            if (Time.time < crackleList[i].time) continue;
            for (int k = 0; k < 6; k++)
            {
                ep.position = crackleList[i].pos;
                ep.velocity = Random.onUnitSphere * 2.5f;
                ep.startColor = new Color(1f, 0.95f, 0.8f);
                ep.startLifetime = Random.Range(0.12f, 0.3f);
                ep.startSize = Random.Range(0.4f, 0.8f);
                glitter.Emit(ep, 1);
            }
            crackleList.RemoveAt(i);
        }
    }

    // ------------------------------------------------------------------ light flashes
    void AddFlash(Vector3 at, Color col)
    {
        if (flashes.Count >= 6) return;
        var go = new GameObject("Firework Light");
        go.transform.position = at;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point; l.color = col; l.range = 140f; l.intensity = 0f; l.shadows = LightShadows.None;
        flashes.Add(new Flash { l = l, t = 0f, peak = 9f });
    }

    void UpdateFlashes(float dt)
    {
        for (int i = flashes.Count - 1; i >= 0; i--)
        {
            var f = flashes[i];
            f.t += dt;
            f.l.intensity = f.peak * Mathf.Clamp01(1f - f.t / 0.9f) * Mathf.Clamp01(f.t / 0.05f);
            if (f.t > 0.9f) { Destroy(f.l.gameObject); flashes.RemoveAt(i); }
        }
    }

    // ------------------------------------------------------------------ darker sky during the show
    void CaptureSky()
    {
        if (dark > 0.01f) return;                                   // already captured
        sun = RenderSettings.sun;
        if (sun == null) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
        if (sun != null) sunI = sun.intensity;
        ambI = RenderSettings.ambientIntensity; ambC = RenderSettings.ambientLight; fogC = RenderSettings.fogColor;
        var sky = RenderSettings.skybox;
        skyExp = sky != null && sky.HasProperty("_Exposure") ? sky.GetFloat("_Exposure") : -1f;
    }

    void UpdateSky(float dt)
    {
        if (!darkenSky) return;
        float before = dark;
        dark = Mathf.MoveTowards(dark, darkTarget, dt / (darkTarget > dark ? 2f : 3f));
        if (Mathf.Approximately(before, dark)) return;
        float k = 1f - dark * 0.85f;
        if (sun != null) sun.intensity = sunI * k;
        RenderSettings.ambientIntensity = ambI * k;
        RenderSettings.ambientLight = ambC * k;
        RenderSettings.fogColor = fogC * Mathf.Lerp(1f, 0.35f, dark);
        if (skyExp >= 0f && RenderSettings.skybox != null) RenderSettings.skybox.SetFloat("_Exposure", skyExp * Mathf.Lerp(1f, 0.25f, dark));
    }

    void OnDestroy() { Busy = false; }
    void OnDisable() { if (dark > 0f) { dark = 0.0001f; darkTarget = 0f; UpdateSky(1f); } }
    void OnApplicationQuit() { if (skyExp >= 0f && RenderSettings.skybox != null) RenderSettings.skybox.SetFloat("_Exposure", skyExp); }

    // ------------------------------------------------------------------ the finale camera shot
    void LateUpdate()
    {
        if (!finaleShot || shotTime < 0f) return;
        shotTime += Time.deltaTime;
        float len = shotSeconds > 0f ? shotSeconds : duration + 2f;
        if (shotTime > len) { shotTime = -1f; return; }
        var cam = Camera.main;
        if (cam == null) return;
        // stand in front of the station and look UP at the sky where the rockets burst, slowly panning across it
        Vector3 fwd = stationFwd, right = Vector3.Cross(Vector3.up, fwd);
        float slide = Mathf.SmoothStep(0f, 1f, shotTime / len);
        float mid = (distanceFromStation.x + distanceFromStation.y) * 0.5f, burst = (burstHeight.x + burstHeight.y) * 0.5f;
        Vector3 sky = station - fwd * mid + Vector3.up * (GroundY(station) - station.y + burst * 0.85f);
        Vector3 pos = station + fwd * 10f + right * Mathf.Lerp(-3f, 3f, slide) + Vector3.up * 2.5f;
        Vector3 look = sky + right * Mathf.Lerp(-spreadWidth * 0.35f, spreadWidth * 0.35f, slide);
        cam.fieldOfView = skyFieldOfView;
        cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
    }

    // ------------------------------------------------------------------ helpers
    static float GroundY(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 300f, Vector3.down, out RaycastHit hit, 600f)) return hit.point.y;
        return p.y;
    }

    void PlayAt(AudioClip[] list, Vector3 at, float vol, float delay = 0f)
    {
        if (list == null || list.Length == 0) return;
        var clip = list[Random.Range(0, list.Length)];
        if (clip == null) return;
        var go = new GameObject("Firework Sound");
        go.transform.position = at;
        var a = go.AddComponent<AudioSource>();
        a.clip = clip; a.volume = vol * volume; a.pitch = Random.Range(0.9f, 1.1f);
        a.spatialBlend = 0.6f; a.minDistance = 60f; a.maxDistance = 600f;     // loud even far away
        a.PlayDelayed(delay);
        Destroy(go, clip.length + delay + 0.5f);
    }

    static bool TestKey()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        return kb != null && kb.fKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.F);
#else
        return false;
#endif
    }

    static Texture2D SoftTex()
    {
        const int N = 32;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r); a = a * a * (0.6f + 0.4f * Mathf.Clamp01(1f - r * 2.5f));
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply();
        return t;
    }
}
