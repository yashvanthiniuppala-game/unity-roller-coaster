// SandTornado.cs — a swirling SAND TORNADO that wanders across the desert next to the track.
//   * a tall spinning funnel of sand with a dust cloud at the bottom and rocks / tumbleweeds flying around,
//   * it wanders slowly around the spot where you put it,
//   * "Cart Goes Through" (on): the tornado moves onto the track when the cart arrives - the cart rides THROUGH it,
//   * when the cart comes close: the air fills with sand (brown haze), sand blows past the camera,
//     the wind howls and the camera shakes a little. When the cart is gone it clears up again.
//
// HOW TO USE:
//   1. GameObject > Create Empty, name it "Sand Tornado", Add Component > Sand Tornado.
//   2. Type "Near Track Distance" (metres from the station) - the tornado jumps next to that part of the track
//      ("Side Offset" metres to the side). Or untick "Place Near Track" and move it yourself.
//   3. Optional: drag a wind sound into "Wind Sound". Press Play.
using UnityEngine;

[ExecuteAlways]
public class SandTornado : MonoBehaviour
{
    [Header("Where")]
    public CoasterTrack track;
    public bool placeNearTrack = true;
    [Tooltip("Metres from the station - the tornado stands next to this part of the track")] public float nearTrackDistance = 520f;
    [Tooltip("Metres to the side of the track (+ = right, - = left)")] public float sideOffset = 40f;
    [Tooltip("How far it wanders around its start point")] public float wanderRadius = 25f;
    [Tooltip("ON = the tornado moves ONTO the track when the cart comes, so the cart rides right THROUGH the sandstorm")]
    public bool cartGoesThrough = true;
    public float wanderSpeed = 4f;

    [Header("Look")]
    public float height = 70f;
    [Range(0.3f, 3f)] public float size = 1f;
    public Color sandColor = new Color(0.82f, 0.64f, 0.42f);
    [Range(0.2f, 3f)] public float spinSpeed = 1f;
    public bool flyingDebris = true;

    [Header("When the cart comes close")]
    [Tooltip("The sand haze starts when the cart is this close (metres)")] public float effectDistance = 90f;
    [Tooltip("How thick the sand haze gets (fog)")] [Range(0f, 1f)] public float hazeStrength = 0.6f;
    public bool sandOnCamera = true;
    public bool cameraShake = true;

    [Header("Sound (optional)")]
    public AudioClip windSound;
    [Range(0, 1)] public float volume = 1f;

    Vector3 home, crossPoint, across; float t;
    CoasterCart cart;
    Transform funnel, cam;
    ParticleSystem camSand;
    AudioSource wind;
    // remember the normal fog
    bool dFog; Color dFogColor; FogMode dFogMode; float dStart, dEnd, dDensity; bool saved;
    float k;   // 0 = clear, 1 = in the storm

    // ------------------------------------------------------------------ edit mode: snap next to the track
    void OnValidate()
    {
#if UNITY_EDITOR
        if (Application.isPlaying) return;
        UnityEditor.EditorApplication.delayCall += () => { if (this != null) Snap(); };
#endif
    }

    public void Snap()
    {
        if (!placeNearTrack) return;
        if (track == null) track = FindAnyObjectByType<CoasterTrack>();
        if (track == null) return;
        if (track.Length < 1f) track.Build();
        track.Sample(nearTrackDistance, out Vector3 p, out Vector3 f, out _);
        Vector3 r = Vector3.Cross(Vector3.up, f); r.y = 0f;
        r = r.sqrMagnitude > 1e-4f ? r.normalized : Vector3.right;
        Vector3 pos = p + r * sideOffset;
        pos.y = Ground(pos);
        transform.position = pos;
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
        Snap();
        home = transform.position;
        if (track != null)
        {
            track.Sample(nearTrackDistance, out Vector3 cp, out Vector3 cf, out _);
            crossPoint = cp; across = Vector3.Cross(Vector3.up, cf); across.y = 0f;
            across = across.sqrMagnitude > 1e-4f ? across.normalized : Vector3.right;
        }
        cart = FindAnyObjectByType<CoasterCart>();
        Build();
        dFog = RenderSettings.fog; dFogColor = RenderSettings.fogColor; dFogMode = RenderSettings.fogMode;
        dStart = RenderSettings.fogStartDistance; dEnd = RenderSettings.fogEndDistance; dDensity = RenderSettings.fogDensity;
        saved = true;
    }

    void OnDisable()
    {
        if (!Application.isPlaying || !saved) return;
        RenderSettings.fog = dFog; RenderSettings.fogColor = dFogColor; RenderSettings.fogMode = dFogMode;
        RenderSettings.fogStartDistance = dStart; RenderSettings.fogEndDistance = dEnd; RenderSettings.fogDensity = dDensity;
    }

    void Build()
    {
        var dot = PipelineUtil.MakeParticleMaterial(false, SoftDot());
        float s = size;
        funnel = new GameObject("Funnel").transform;
        funnel.SetParent(transform, false);

        // 1) the funnel: sand shooting up and spinning, the cone gets wider at the top
        Layer("Funnel Sand", dot, ParticleSystemShapeType.Cone, 18f, 2.5f * s, new Vector2(height / 6f, height / 4.5f), new Vector2(2f * s, 5f * s),
              5f, 4f * spinSpeed, 0.9f, 220f, new Color(sandColor.r, sandColor.g, sandColor.b, 0.55f));
        // 2) thin darker core
        Layer("Funnel Core", dot, ParticleSystemShapeType.Cone, 8f, 1.2f * s, new Vector2(height / 5f, height / 4f), new Vector2(1.2f * s, 2.5f * s),
              4f, 6f * spinSpeed, 0.4f, 120f, new Color(sandColor.r * 0.7f, sandColor.g * 0.65f, sandColor.b * 0.6f, 0.7f));
        // 3) dust cloud rolling out at the bottom
        var dust = Layer("Ground Dust", dot, ParticleSystemShapeType.Circle, 0f, 6f * s, new Vector2(1f, 3f), new Vector2(5f * s, 10f * s),
              3.5f, 1.5f * spinSpeed, 0f, 40f, new Color(sandColor.r, sandColor.g, sandColor.b, 0.45f));
        var dsh = dust.shape; dsh.rotation = new Vector3(90f, 0f, 0f);        // lying flat on the ground
        var dv = dust.velocityOverLifetime; dv.radial = new ParticleSystem.MinMaxCurve(4f);

        // 4) rocks and tumbleweeds flying round
        if (flyingDebris)
        {
            var deb = Layer("Debris", PipelineUtil.MakeParticleMaterial(false, SoftDot()), ParticleSystemShapeType.Cone, 15f, 3f * s,
                            new Vector2(height / 8f, height / 5f), new Vector2(0.3f, 0.9f), 4f, 5f * spinSpeed, 0.6f, 25f,
                            new Color(0.35f, 0.25f, 0.15f, 1f));
            var dm = deb.main; dm.gravityModifier = 0.15f;
        }

        if (windSound != null)
        {
            wind = gameObject.AddComponent<AudioSource>();
            wind.clip = windSound; wind.loop = true; wind.volume = 0f; wind.spatialBlend = 0f; wind.Play();
        }

        // sand blowing past the camera
        var mainCam = Camera.main;
        if (sandOnCamera && mainCam != null)
        {
            cam = mainCam.transform;
            var go = new GameObject("Sand On Camera");
            go.transform.SetParent(cam, false);
            go.transform.localPosition = new Vector3(0f, 0f, 6f);
            camSand = go.AddComponent<ParticleSystem>();
            camSand.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = camSand.main;
            m.loop = true; m.playOnAwake = true; m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0f, 1f);                  // the wind (below) blows them past the lens
            m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.12f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(sandColor.r, sandColor.g, sandColor.b, 0.8f), new Color(1f, 0.9f, 0.7f, 0.6f));
            m.maxParticles = 1500;
            var e = camSand.emission; e.rateOverTime = 0f;
            var sh = camSand.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(14f, 8f, 6f);
            var v = camSand.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(-14f, -6f); v.y = new ParticleSystem.MinMaxCurve(-1f, 1f); v.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = dot;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.03f;
            camSand.Play();
        }
    }

    ParticleSystem Layer(string name, Material mat, ParticleSystemShapeType shape, float angle, float radius, Vector2 speed, Vector2 sizes,
                         float lifetime, float orbit, float grow, float rate, Color col)
    {
        var go = new GameObject(name);
        go.transform.SetParent(funnel, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.loop = true; m.playOnAwake = true; m.prewarm = true;
        m.simulationSpace = ParticleSystemSimulationSpace.Local;            // the funnel moves with the tornado
        m.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.8f, lifetime * 1.2f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        m.startSize = new ParticleSystem.MinMaxCurve(sizes.x, sizes.y);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.startColor = col;
        m.maxParticles = 3000;
        var e = ps.emission; e.rateOverTime = rate;
        var sh = ps.shape; sh.shapeType = shape; sh.angle = angle; sh.radius = radius;
        sh.rotation = new Vector3(-90f, 0f, 0f);                            // point up
        var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local;
        v.orbitalY = new ParticleSystem.MinMaxCurve(orbit);                 // SPIN around the vertical axis
        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 1f + grow * 2f));
        var c = ps.colorOverLifetime; c.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.7f, 0.7f), new GradientAlphaKey(0f, 1f) });
        c.color = new ParticleSystem.MinMaxGradient(g);
        var n = ps.noise; n.enabled = true; n.strength = 1.2f * size; n.frequency = 0.4f;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        ps.Play();
        return ps;
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        float dt = Time.deltaTime;

        // wander in a lazy figure-8 around home, following the ground
        t += dt * wanderSpeed / Mathf.Max(5f, wanderRadius);
        Vector3 p = home + new Vector3(Mathf.Sin(t) * wanderRadius, 0f, Mathf.Sin(t * 2f) * wanderRadius * 0.5f);
        if (cartGoesThrough && track != null && cart != null && cart.state == CoasterCart.RideState.Riding)
        {
            // the storm drifts ONTO the track so it is right on the rails when the cart arrives, then moves away to the other side
            float ahead = nearTrackDistance - cart.distance;
            if (ahead < 160f && ahead > -160f)
            {
                float side = Mathf.Abs(sideOffset) < 1f ? 40f : sideOffset;
                float offset = ahead > 0f ? Mathf.Clamp01(ahead / 140f) * side : -Mathf.Clamp01(-ahead / 140f) * side;
                p = crossPoint + across * offset;
            }
        }
        p.y = Ground(p);
        transform.position = Vector3.MoveTowards(transform.position, p, Mathf.Max(wanderSpeed, 30f) * dt);
        if (funnel != null) funnel.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 0.7f) * 4f, 0f, Mathf.Cos(Time.time * 0.5f) * 4f);  // sway

        // how close is the cart?
        float target = 0f;
        if (cart != null)
        {
            Vector3 d = cart.transform.position - transform.position; d.y = 0f;
            target = Mathf.Clamp01(1f - (d.magnitude - effectDistance * 0.3f) / (effectDistance * 0.7f));
        }
        k = Mathf.MoveTowards(k, target, dt * 0.8f);

        // sand haze
        if (k > 0.01f)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Color.Lerp(dFog ? dFogColor : sandColor, sandColor, k);
            float near = Mathf.Lerp(400f, 5f, k * hazeStrength), far = Mathf.Lerp(1500f, 120f, k * hazeStrength);
            RenderSettings.fogStartDistance = near; RenderSettings.fogEndDistance = far;
        }
        else if (RenderSettings.fogColor != dFogColor || RenderSettings.fog != dFog)
        {
            RenderSettings.fog = dFog; RenderSettings.fogColor = dFogColor; RenderSettings.fogMode = dFogMode;
            RenderSettings.fogStartDistance = dStart; RenderSettings.fogEndDistance = dEnd; RenderSettings.fogDensity = dDensity;
        }

        if (camSand != null) { var e = camSand.emission; e.rateOverTime = 400f * k; }
        if (wind != null) wind.volume = volume * Mathf.Lerp(0.15f, 1f, k);
        if (cameraShake && k > 0.5f && Random.value < dt * 4f) RideCamera.Shake(0.08f * k, 0.3f);
        // right inside the funnel: the cart is shaken hard
        if (cameraShake && cart != null)
        {
            Vector3 dd = cart.transform.position - transform.position; dd.y = 0f;
            if (dd.magnitude < 12f * size) RideCamera.Shake(0.25f, 0.2f);
        }
    }

    static Texture2D dotTex;
    static Texture2D SoftDot()
    {
        if (dotTex != null) return dotTex;
        const int N = 64;
        dotTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var rng = new System.Random(3);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (0.75f + (float)rng.NextDouble() * 0.25f);   // a bit grainy, like sand
                dotTex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        dotTex.Apply();
        return dotTex;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.85f, 0.65f, 0.35f, 0.8f);
        Vector3 b = transform.position;
        for (int i = 0; i < 6; i++)
        {
            float h0 = height * i / 6f, h1 = height * (i + 1) / 6f;
            float r0 = 2.5f * size + h0 * 0.3f * size, r1 = 2.5f * size + h1 * 0.3f * size;
            Gizmos.DrawLine(b + new Vector3(r0, h0, 0f), b + new Vector3(r1, h1, 0f));
            Gizmos.DrawLine(b + new Vector3(-r0, h0, 0f), b + new Vector3(-r1, h1, 0f));
        }
        Gizmos.DrawWireSphere(b, wanderRadius);
    }
}
