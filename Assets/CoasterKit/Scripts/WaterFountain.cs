// WaterFountain.cs — realistic little fountains for the ponds: a water jet that rises and
// falls back as droplets, fine mist, and rings rippling out on the water surface.
// The jets pulse gently and SURGE when the cart splashes past.
// HOW TO USE: GameObject > Create Empty, name it "Fountain 1", set its Y to the water surface
// (same Y as the Pond object), move it into the pond 3-6 m from the track,
// Add Component > Water Fountain, press Play. Use Jets = 5 and Ring Radius = 2 for a fountain ring.
using UnityEngine;

public class WaterFountain : MonoBehaviour
{
    [Header("Jets")]
    [Range(1, 12)] public int jets = 1;
    [Tooltip("With more than one jet: they stand on a circle this big, leaning outwards")] public float ringRadius = 2f;
    public float jetHeight = 5f;
    [Range(0f, 0.8f)] public float pulseAmount = 0.3f;
    public float pulseSpeed = 0.4f;

    [Header("Cart passes -> surge")]
    public bool surgeWhenCartPasses = true;
    public float surgeDistance = 25f;
    public float surgeMultiplier = 1.7f;

    [Header("Look")]
    public Color waterColor = new Color(0.78f, 0.9f, 1f, 0.85f);
    public bool mist = true;
    public bool ripples = true;

    [Header("Sound (optional)")]
    public AudioClip fountainLoop;
    public AudioClip surgeSound;
    [Range(0, 1)] public float volume = 0.7f;

    struct Jet { public ParticleSystem column, drops; public float baseSpeed; }
    Jet[] js;
    ParticleSystem ripplePS;
    float rippleTimer;
    CoasterCart cart;
    float surge, phase;
    bool wasNear;
    AudioSource loopSrc;

    void Start()
    {
        cart = FindAnyObjectByType<CoasterCart>();
        phase = Random.value * 10f;
        var waterMat = PipelineUtil.MakeParticleMaterial(false, Tex(false));
        var ringMat = PipelineUtil.MakeParticleMaterial(false, Tex(true));
        float g = Physics.gravity.magnitude;
        float v = Mathf.Sqrt(2f * g * jetHeight);           // speed that reaches jetHeight

        js = new Jet[jets];
        for (int i = 0; i < jets; i++)
        {
            float a = i * Mathf.PI * 2f / jets;
            Vector3 off = jets > 1 ? new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ringRadius : Vector3.zero;
            // lean ring jets outwards a little
            Quaternion lean = jets > 1 ? Quaternion.AngleAxis(10f, Vector3.Cross(Vector3.up, off.normalized)) : Quaternion.identity;
            Quaternion up = lean * Quaternion.Euler(-90f, 0f, 0f);      // particle cones point along +Z, so tip them up

            // 1) the main column: stretched streaks that look like running water
            var col = NewPS("Jet " + (i + 1), off, up, waterMat, true);
            var m = col.main;
            m.startSpeed = new ParticleSystem.MinMaxCurve(v * 0.93f, v);
            m.startLifetime = 2f * v / g;
            m.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
            m.gravityModifier = 1f;
            var e = col.emission; e.rateOverTime = 140f;
            var sh = col.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 2.5f; sh.radius = 0.06f;
            var r = col.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 1.5f;

            // 2) droplets breaking away and falling back
            var drops = NewPS("Droplets " + (i + 1), off, up, waterMat, true);
            m = drops.main;
            m.startSpeed = new ParticleSystem.MinMaxCurve(v * 0.6f, v * 0.95f);
            m.startLifetime = 2f * v / g + 0.3f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            m.gravityModifier = 1f;
            e = drops.emission; e.rateOverTime = 90f;
            sh = drops.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 0.1f;
            r = drops.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.04f;
            var coll = drops.collision; coll.enabled = true; coll.type = ParticleSystemCollisionType.Planes; coll.bounce = 0.1f; coll.lifetimeLoss = 0.6f;
            coll.SetPlane(0, transform);                              // bounce on the water surface

            js[i] = new Jet { column = col, drops = drops, baseSpeed = v };
        }

        if (mist)
        {
            var ms = NewPS("Mist", Vector3.up * jetHeight * 0.5f, Quaternion.Euler(-90f, 0f, 0f), waterMat, false);
            var m = ms.main;
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            m.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
            m.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            m.startColor = new Color(1f, 1f, 1f, 0.12f);
            m.gravityModifier = -0.02f;
            var e = ms.emission; e.rateOverTime = 8f * jets;
            var sh = ms.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = Mathf.Max(1f, ringRadius) * (jets > 1 ? 1.2f : 0.6f);
        }

        if (ripples)
        {
            // rings spreading out on the water where each jet lands (emitted by hand in Update)
            ripplePS = NewPS("Ripples", Vector3.zero, Quaternion.identity, ringMat, false);
            var m = ripplePS.main;
            m.startSpeed = 0f;
            m.startLifetime = 2.2f;
            m.startSize = 1f;
            m.startColor = new Color(1f, 1f, 1f, 0.5f);
            var e = ripplePS.emission; e.rateOverTime = 0f;
            var sh = ripplePS.shape; sh.enabled = false;
            var sol = ripplePS.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 5f));
            var r = ripplePS.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        }

        if (fountainLoop != null)
        {
            loopSrc = gameObject.AddComponent<AudioSource>();
            loopSrc.clip = fountainLoop; loopSrc.loop = true; loopSrc.volume = volume * 0.5f;
            loopSrc.spatialBlend = 1f; loopSrc.minDistance = 5f; loopSrc.maxDistance = 60f;
            loopSrc.Play();
        }
    }

    void Update()
    {
        if (js == null) return;
        float dt = Time.deltaTime;
        bool near = surgeWhenCartPasses && cart != null && Vector3.Distance(cart.transform.position, transform.position) < surgeDistance;
        if (near && !wasNear)
        {
            surge = 2.5f;
            if (surgeSound != null) AudioSource.PlayClipAtPoint(surgeSound, transform.position, volume);
        }
        wasNear = near;
        surge = Mathf.Max(0f, surge - dt);
        float boost = Mathf.Lerp(1f, surgeMultiplier, Mathf.Clamp01(surge));
        float pulse = 1f + Mathf.Sin((Time.time + phase) * pulseSpeed * Mathf.PI * 2f) * pulseAmount * 0.5f;
        float k = Mathf.Sqrt(pulse * boost);          // speed ~ sqrt(height)
        foreach (var j in js)
        {
            var m = j.column.main; m.startSpeedMultiplier = k;
            var d = j.drops.main; d.startSpeedMultiplier = k;
        }
        if (loopSrc != null) loopSrc.volume = volume * 0.5f * boost;

        if (ripplePS != null)
        {
            rippleTimer -= dt * boost;
            if (rippleTimer <= 0f)
            {
                rippleTimer = Random.Range(0.25f, 0.6f);
                var j = js[Random.Range(0, js.Length)];
                Vector3 p = j.column.transform.position;
                var ep = new ParticleSystem.EmitParams();
                ep.position = new Vector3(p.x + Random.Range(-0.3f, 0.3f), transform.position.y + 0.03f, p.z + Random.Range(-0.3f, 0.3f));
                ripplePS.Emit(ep, 1);
            }
        }
    }

    ParticleSystem NewPS(string name, Vector3 localPos, Quaternion localRot, Material mat, bool water)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.loop = true; m.playOnAwake = false;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.maxParticles = 2000;
        if (water) m.startColor = new ParticleSystem.MinMaxGradient(waterColor, Color.white);
        var col = ps.colorOverLifetime; col.enabled = true;
        var gr = new Gradient();
        gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                   new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(gr);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        ps.Play();
        return ps;
    }

    static Texture2D dotTex, ringTex;
    static Texture2D Tex(bool ring)
    {
        if (ring && ringTex != null) return ringTex;
        if (!ring && dotTex != null) return dotTex;
        const int N = 64;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = ring ? Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.12f) : Mathf.Clamp01(1f - d);
                t.SetPixel(x, y, new Color(1, 1, 1, a * a));
            }
        t.Apply();
        if (ring) ringTex = t; else dotTex = t;
        return t;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.8f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * jetHeight);
        Gizmos.DrawWireSphere(transform.position, jets > 1 ? ringRadius : 0.4f);
    }
}
