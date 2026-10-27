// WorldTransition.cs — MAGIC PORTAL: the ride starts in the DESERT and turns into a FAIRYLAND.
//
// Two ways (choose "Mode" in the Inspector):
//   * Magic Portal (during the ride): the cart flies through a glowing magic portal on the track.
//     A white flash - and the world changes: pink-lilac sky, soft magic fog, pastel sunlight, the fairyland
//     grows out of the ground and the desert decorations you chose disappear.
//     When the cart is back in the station everything turns back to desert for the next ride.
//   * Next Ride: ride 1 is the desert, ride 2 is the fairyland, ride 3 desert again ... (the change happens in the station).
//
// HOW TO USE (just 2 steps):
//   1. GameObject > Create Empty, name it "Magic Portal", Add Component > World Transition.
//      The portal appears on the track and a "Fairyland" object is made for you automatically.
//   2. Set "Portal Distance" (metres from the station - not inside the loop) and press Play.
// Optional: drag desert things (palms, cactus ...) into "Hide In Fairyland", a sky from Fantasy Skybox FREE
// into "Fairy Skybox", and sounds into "Portal Whoosh" / "Fairy Music".
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class WorldTransition : MonoBehaviour
{
    public enum Mode { MagicPortal, NextRide }

    [Header("How the world changes")]
    public Mode mode = Mode.MagicPortal;
    public CoasterTrack track;
    public CoasterCart cart;
    [Tooltip("Metres from the station where the magic portal stands (Magic Portal mode)")]
    public float portalDistance = 380f;
    [Tooltip("Seconds the sky / light / fog take to change")] public float changeTime = 2.5f;

    [Header("The two worlds")]
    [Tooltip("Your Fairyland object (with the Fairyland Zone component)")] public FairylandZone fairyland;
    [Tooltip("Automatically start the fairyland just after the portal and end it before the brakes")] public bool fairylandAfterPortal = true;
    [Tooltip("Desert things that disappear in the fairyland (palms, cactus, the Decor group ...)")] public GameObject[] hideInFairyland;
    [Tooltip("Optional: a skybox material for the fairyland (e.g. one from Fantasy Skybox FREE)")] public Material fairySkybox;

    [Header("Fairyland look")]
    public Color skyTint = new Color(1f, 0.72f, 0.92f);
    [Tooltip("If no Fairy Skybox is set: make a soft pink fairy sky automatically (also turns the blue area below the horizon pink)")] public bool autoPinkSky = true;
    [Tooltip("Colour of the sky below the horizon / at the edge of the world")] public Color horizonColor = new Color(0.98f, 0.8f, 0.93f);
    public Color fogColor = new Color(0.95f, 0.78f, 0.95f);
    [Tooltip("The pink haze starts this far away (metres) - nearby things stay clear")] public float fogStart = 120f;
    [Tooltip("Things this far away disappear in the pink haze")] public float fogEnd = 650f;
    public Color sunColor = new Color(1f, 0.82f, 0.95f);
    [Range(0f, 3f)] public float sunIntensity = 1.1f;
    [Tooltip("Also tint the shadows / ambient light (off = keep the normal ambient light)")] public bool tintAmbient = false;
    public Color ambientColor = new Color(1f, 0.88f, 0.95f);

    [Header("Portal look")]
    public float portalRadius = 4.5f;
    public Color portalColor = new Color(0.8f, 0.45f, 1f);
    public Color portalColor2 = new Color(0.45f, 1f, 0.95f);

    [Header("On screen")]
    [Tooltip("Show a short title on screen when the world changes")] public bool showLevelTitle = false;
    public string levelTitle = "Welcome to Fairyland!";
    public string desertTitle = "";

    [Header("Sound (optional)")]
    public AudioClip portalWhoosh;
    [Tooltip("Music / ambience that plays only in the fairyland")] public AudioClip fairyMusic;
    [Range(0, 1)] public float volume = 0.8f;

    // ---- runtime state ----
    float blend, target;                 // 0 = desert, 1 = fairyland
    float flash, titleTimer; string title;
    float lastDist = -1f; int lastRides;
    bool inFairyland;
    Transform portal; ParticleSystem portalRing, portalCore, portalBurst;
    Light sun;
    Material skyCopy, desertSky;
    // desert originals
    bool dFog; Color dFogColor, dSunColor, dAmbient, dSkyTint; float dFogDensity, dFogStart, dFogEnd, dSunIntensity; FogMode dFogMode;
    UnityEngine.Rendering.AmbientMode dAmbientMode;
    readonly List<(Transform t, Vector3 scale, float delay)> grow = new List<(Transform, Vector3, float)>();
    readonly List<GameObject> hidden = new List<GameObject>();
    Transform hoop;
    float growClock; bool growing;
    AudioSource music;
    GUIStyle titleStyle;

    // ------------------------------------------------------------------ edit mode: put the portal on the track
    void OnValidate()
    {
#if UNITY_EDITOR
        if (Application.isPlaying) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            Find();
            BuildPortal();
            if (fairylandAfterPortal && fairyland != null && track != null && mode == Mode.MagicPortal)
            {
                float s = portalDistance + 25f, e = Mathf.Max(s + 30f, track.BrakeStart - 8f);
                if (Mathf.Abs(fairyland.startDistance - s) > 0.5f || Mathf.Abs(fairyland.endDistance - e) > 0.5f)
                {
                    fairyland.startDistance = s; fairyland.endDistance = e;
                    fairyland.Rebuild();
                    UnityEditor.EditorUtility.SetDirty(fairyland);
                }
            }
        };
#endif
    }

    // when you add the component: find everything and make the Fairyland automatically
    void Reset()
    {
        Find();
        MakeFairylandIfMissing();
        OnValidate();
    }

    void MakeFairylandIfMissing()
    {
        if (fairyland != null) return;
        var go = new GameObject("Fairyland");
        fairyland = go.AddComponent<FairylandZone>();
        fairyland.track = track;
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Make Fairyland");
#endif
    }

    void Find()
    {
        if (track == null) track = FindAnyObjectByType<CoasterTrack>();
        if (cart == null) cart = FindAnyObjectByType<CoasterCart>();
        if (fairyland == null) fairyland = FindAnyObjectByType<FairylandZone>();
        if (track != null && track.Length < 1f) track.Build();
    }

    // ------------------------------------------------------------------ play mode
    void Start()
    {
        if (!Application.isPlaying) return;
        Find();
        if (fairyland == null) { MakeFairylandIfMissing(); fairyland.startDistance = portalDistance + 25f; fairyland.endDistance = Mathf.Max(fairyland.startDistance + 30f, track != null ? track.BrakeStart - 8f : fairyland.startDistance + 200f); fairyland.Rebuild(); }
        BuildPortal();

        // remember the desert
        dFog = RenderSettings.fog; dFogColor = RenderSettings.fogColor; dFogDensity = RenderSettings.fogDensity; dFogMode = RenderSettings.fogMode;
        dFogStart = RenderSettings.fogStartDistance; dFogEnd = RenderSettings.fogEndDistance;
        dAmbientMode = RenderSettings.ambientMode; dAmbient = RenderSettings.ambientLight;
        sun = RenderSettings.sun;
        if (sun == null) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
        if (sun != null) { dSunColor = sun.color; dSunIntensity = sun.intensity; }
        desertSky = RenderSettings.skybox;
        if (desertSky != null)
        {
            skyCopy = new Material(desertSky);                 // never change the real asset
            RenderSettings.skybox = skyCopy;
            dSkyTint = skyCopy.HasProperty("_Tint") ? skyCopy.GetColor("_Tint") : Color.gray;
        }

        // the fairyland waits underground until the portal
        var root = fairyland != null ? fairyland.BuiltRoot : null;
        if (root != null)
        {
            Vector3 portalPos = portal != null ? portal.position : root.position;
            foreach (Transform c in root)
                grow.Add((c, c.localScale, Mathf.Clamp(Vector3.Distance(c.position, portalPos) / 120f, 0f, 2.5f)));
            SetGrow(0f);
        }
        if (hideInFairyland != null) foreach (var g in hideInFairyland) if (g != null && g.activeSelf) hidden.Add(g);

        if (fairyMusic != null)
        {
            music = gameObject.AddComponent<AudioSource>();
            music.clip = fairyMusic; music.loop = true; music.volume = 0f; music.spatialBlend = 0f; music.Play();
        }
        if (cart != null) { lastRides = cart.ridesCompleted; cart.RideFinished += OnRideFinished; }
        ShowTitle(desertTitle);
    }

    void OnDestroy()
    {
        if (cart != null) cart.RideFinished -= OnRideFinished;
        if (Application.isPlaying && desertSky != null) RenderSettings.skybox = desertSky;
    }

    void OnRideFinished()
    {
        if (mode == Mode.MagicPortal) GoTo(false, false);                 // back in the station: desert again
        else GoTo(!inFairyland, true);                                   // next ride = the other world
    }

    void Update()
    {
        if (!Application.isPlaying || cart == null) return;
        float dt = Time.deltaTime;

        // cart flies through the portal
        if (mode == Mode.MagicPortal && cart.state == CoasterCart.RideState.Riding)
        {
            if (lastDist >= 0f && lastDist < portalDistance && cart.distance >= portalDistance) GoTo(true, true);
            lastDist = cart.distance;
        }
        else lastDist = -1f;

        // blend desert <-> fairyland
        blend = Mathf.MoveTowards(blend, target, dt / Mathf.Max(0.1f, changeTime));
        float k = Mathf.SmoothStep(0f, 1f, blend);
        ApplyLook(k);

        // fairyland grows out of the ground, desert things shrink away
        if (growing) { growClock += dt; SetGrow(growClock); }
        foreach (var h in hidden) if (h != null && h.activeSelf != (k < 0.5f)) h.SetActive(k < 0.5f);   // vanish in the flash
        if (music != null) music.volume = volume * k;

        // spin the portal
        if (hoop != null) hoop.Rotate(0f, 0f, 40f * dt, Space.Self);
        flash = Mathf.MoveTowards(flash, 0f, dt * 1.4f);
        titleTimer -= dt;
    }

    void GoTo(bool fairy, bool withFlash)
    {
        if (fairy == inFairyland) return;
        inFairyland = fairy;
        target = fairy ? 1f : 0f;
        if (withFlash) flash = 1f;
        if (fairy)
        {
            growing = true; growClock = 0f;
            var sky = fairySkybox != null ? fairySkybox : (autoPinkSky ? PinkSky() : null);
            if (sky != null) { RenderSettings.skybox = sky; DynamicGI.UpdateEnvironment(); }
            if (portalBurst != null) portalBurst.Emit(250);
            if (portalWhoosh != null && cart != null) AudioSource.PlayClipAtPoint(portalWhoosh, cart.transform.position, volume);
            ShowTitle(levelTitle);
        }
        else
        {
            growing = false; SetGrow(0f);
            if (skyCopy != null && RenderSettings.skybox != skyCopy) { RenderSettings.skybox = skyCopy; DynamicGI.UpdateEnvironment(); }
            ShowTitle(desertTitle);
        }
    }

    void ApplyLook(float k)
    {
        bool on = k > 0.01f;
        RenderSettings.fog = dFog || on;
        if (on)
        {
            // soft LINEAR haze: clear near the cart, pink far away
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = Mathf.Lerp(fogStart * 6f, fogStart, k);
            RenderSettings.fogEndDistance = Mathf.Lerp(fogEnd * 6f, fogEnd, k);
            Color fc = fairySkybox == null && autoPinkSky ? Color.Lerp(fogColor, horizonColor, 0.5f) : fogColor;
            RenderSettings.fogColor = Color.Lerp(dFog ? dFogColor : fc, fc, k);
        }
        else
        {
            RenderSettings.fogMode = dFogMode; RenderSettings.fogColor = dFogColor; RenderSettings.fogDensity = dFogDensity;
            RenderSettings.fogStartDistance = dFogStart; RenderSettings.fogEndDistance = dFogEnd;
        }
        if (sun != null) { sun.color = Color.Lerp(dSunColor, sunColor, k); sun.intensity = Mathf.Lerp(dSunIntensity, sunIntensity, k); }
        if (tintAmbient && on) { RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = Color.Lerp(Color.white * 0.75f, ambientColor * 0.85f, k); }
        else if (RenderSettings.ambientMode != dAmbientMode || RenderSettings.ambientLight != dAmbient) { RenderSettings.ambientMode = dAmbientMode; RenderSettings.ambientLight = dAmbient; }
        if (skyCopy != null && RenderSettings.skybox == skyCopy && skyCopy.HasProperty("_Tint")) skyCopy.SetColor("_Tint", Color.Lerp(dSkyTint, new Color(dSkyTint.r * skyTint.r * 1.3f, dSkyTint.g * skyTint.g * 1.3f, dSkyTint.b * skyTint.b * 1.3f, dSkyTint.a), k));
    }

    void SetGrow(float clock)
    {
        foreach (var g in grow)
        {
            if (g.t == null) continue;
            float t = Mathf.Clamp01((clock - g.delay) / 1.2f);
            float s = t <= 0f ? 0f : t + Mathf.Sin(t * Mathf.PI) * 0.2f;   // grows with a little "pop", ends at full size
            g.t.localScale = g.scale * s;
            g.t.gameObject.SetActive(s > 0.001f);
        }
    }

    Material pinkSky;
    /// <summary>A soft pastel sky made from Unity's built-in procedural sky: pink-lilac above, pink at the horizon and below.</summary>
    Material PinkSky()
    {
        if (pinkSky != null) return pinkSky;
        var sh = Shader.Find("Skybox/Procedural");
        if (sh == null) return null;
        pinkSky = new Material(sh) { name = "Fairy Pink Sky (auto)" };
        pinkSky.SetColor("_SkyTint", new Color(skyTint.r * 0.9f, skyTint.g * 0.75f, skyTint.b));
        pinkSky.SetColor("_GroundColor", horizonColor);
        pinkSky.SetFloat("_AtmosphereThickness", 1.6f);
        pinkSky.SetFloat("_Exposure", 1.25f);
        pinkSky.SetFloat("_SunSize", 0.06f);
        return pinkSky;
    }

    void ShowTitle(string t) { if (!showLevelTitle || string.IsNullOrEmpty(t)) return; title = t; titleTimer = 3.5f; }

    void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (flash > 0.001f)
        {
            GUI.color = new Color(1f, 0.95f, 1f, Mathf.Clamp01(flash * 1.3f));
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        }
        if (titleTimer > 0f && !string.IsNullOrEmpty(title))
        {
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            titleStyle.fontSize = Mathf.Max(28, Screen.height / 14);
            float a = Mathf.Clamp01(titleTimer) * Mathf.Clamp01((3.5f - titleTimer) * 2f);
            var r = new Rect(0, Screen.height * 0.18f, Screen.width, Screen.height * 0.15f);
            GUI.color = new Color(0f, 0f, 0f, a * 0.6f);
            GUI.Label(new Rect(r.x + 3, r.y + 3, r.width, r.height), title, titleStyle);
            GUI.color = new Color(1f, 1f, 1f, a);
            titleStyle.normal.textColor = inFairyland ? new Color(1f, 0.75f, 0.95f) : new Color(1f, 0.85f, 0.45f);
            GUI.Label(r, title, titleStyle);
        }
        GUI.color = Color.white;
    }

    // ------------------------------------------------------------------ the magic portal
    void BuildPortal()
    {
        var old = transform.Find("Magic Portal");
        if (old != null) { if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject); }
        portal = null; hoop = null; portalRing = portalCore = portalBurst = null;
        if (mode != Mode.MagicPortal || track == null || track.Length < 1f) return;
        if (track.InLoop(portalDistance)) Debug.LogWarning("[World Transition] The portal is inside the loop - move Portal Distance a little.");

        track.Sample(portalDistance, out Vector3 p, out Vector3 f, out Vector3 u);
        portal = new GameObject("Magic Portal").transform;
        portal.SetParent(transform, false);
        portal.SetPositionAndRotation(p + u * 1.6f, Quaternion.LookRotation(f, u));

        // glowing hoop made of crystal pieces
        hoop = new GameObject("Hoop").transform;
        hoop.SetParent(portal, false);
        var m1 = GlowMat(portalColor, 2f); var m2 = GlowMat(portalColor2, 2f);
        const int n = 30;
        float seg = 2f * Mathf.PI * portalRadius / n * 1.2f;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            var c = GameObject.CreatePrimitive(i % 3 == 0 ? PrimitiveType.Cube : PrimitiveType.Cylinder);
            c.name = "Portal piece";
            if (Application.isPlaying) Destroy(c.GetComponent<Collider>()); else DestroyImmediate(c.GetComponent<Collider>());
            c.transform.SetParent(hoop, false);
            c.transform.localPosition = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * portalRadius;
            c.transform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg) * (i % 3 == 0 ? Quaternion.Euler(45f, 0f, 45f) : Quaternion.identity);
            c.transform.localScale = i % 3 == 0 ? Vector3.one * 0.8f : new Vector3(0.35f, seg * 0.5f, 0.35f);
            c.GetComponent<Renderer>().sharedMaterial = i % 2 == 0 ? m1 : m2;
        }
        var glow = new GameObject("Portal Light").AddComponent<Light>();
        glow.transform.SetParent(portal, false);
        glow.type = LightType.Point; glow.color = portalColor; glow.range = portalRadius * 5f; glow.intensity = 4f;

        var dot = PipelineUtil.MakeParticleMaterial(true, SoftDot());
        portalRing = Particles("Swirl", dot, 0.25f, 0.7f, 60f, portalRadius, 0f, true);
        portalCore = Particles("Shimmer", dot, 0.6f, 1.6f, 25f, portalRadius * 0.95f, 1f, false);
        portalBurst = Particles("Burst", dot, 0.2f, 0.6f, 0f, portalRadius, 0.5f, false);
        var bm = portalBurst.main; bm.startSpeed = new ParticleSystem.MinMaxCurve(4f, 14f); bm.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
    }

    ParticleSystem Particles(string name, Material mat, float minSize, float maxSize, float rate, float radius, float thickness, bool orbit)
    {
        var go = new GameObject(name);
        go.transform.SetParent(portal, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = true;
        main.simulationSpace = orbit ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor = new ParticleSystem.MinMaxGradient(portalColor, portalColor2);
        main.maxParticles = 1500;
        var em = ps.emission; em.rateOverTime = rate;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius; sh.radiusThickness = thickness; sh.rotation = Vector3.zero;
        if (orbit) { var v = ps.velocityOverLifetime; v.enabled = true; v.orbitalZ = new ParticleSystem.MinMaxCurve(1.5f); }
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(orbit ? 1f : 0.45f, 0.2f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        ps.Play();
        return ps;
    }

    static Material GlowMat(Color c, float strength)
    {
        var m = PipelineUtil.MakeLit(c, 0.8f, 0.1f);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * strength);
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
}
