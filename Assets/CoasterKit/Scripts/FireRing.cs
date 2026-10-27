// FireRing.cs — a spinning ring of fire that the cart flies through.
// HOW TO USE: GameObject > Create Empty, name it "Fire Ring 1", Add Component > Fire Ring.
// Type a "Distance Along Track" (metres from the station) — the ring jumps onto the track
// at that spot and turns to face the cart. Make more rings with different distances.
using UnityEngine;

[ExecuteAlways]
public class FireRing : MonoBehaviour
{
    [Header("Where on the track")]
    public CoasterTrack track;
    [Tooltip("Metres from the station. Press Play once: the Console shows the track length.")]
    public float distanceAlongTrack = 150f;
    [Tooltip("Ring centre above the rails (the cart + rider are about 2 m tall)")]
    public float heightAboveRails = 1.3f;

    [Header("Look")]
    public float radius = 3.2f;
    public Color hotColor = new Color(1f, 0.9f, 0.45f);
    public Color edgeColor = new Color(1f, 0.25f, 0.03f);
    [Range(0.2f, 3f)] public float flameAmount = 1f;
    [Tooltip("Degrees per second the bright 'comets' race around the ring")]
    public float spinSpeed = 220f;
    public bool metalHoop = true;

    [Header("When the cart flies through")]
    public float burstMultiplier = 3f;
    public AudioClip whooshSound;
    [Range(0, 1)] public float volume = 1f;

    ParticleSystem flames;
    Light glow;
    CoasterCart cart;
    float burst, spin, emitCarry;
    int lastLap = -1;

    void OnEnable() { SnapSoon(); }
    void OnValidate() { SnapSoon(); }

    void SnapSoon()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) { UnityEditor.EditorApplication.delayCall += () => { if (this != null) { Snap(); BuildHoop(); } }; return; }
#endif
        Snap();
    }

    /// <summary>Puts the ring on the track at 'distanceAlongTrack', facing along the track.</summary>
    public void Snap()
    {
        if (track == null) track = FindAnyObjectByType<CoasterTrack>();
        if (track == null) return;
        track.Sample(distanceAlongTrack, out Vector3 p, out Vector3 f, out Vector3 u);
        transform.SetPositionAndRotation(p + u * heightAboveRails, Quaternion.LookRotation(f, u));
    }

    void BuildHoop()
    {
        var old = transform.Find("Hoop");
        if (old != null) { if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject); }
        if (!metalHoop) return;
        var hoop = new GameObject("Hoop").transform;
        hoop.SetParent(transform, false);
        var mat = PipelineUtil.MakeLit(new Color(0.15f, 0.12f, 0.1f), 0.6f, 0.9f);
        const int n = 28;
        float seg = 2f * Mathf.PI * radius / n * 1.15f;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            c.name = "Hoop piece";
            if (Application.isPlaying) Destroy(c.GetComponent<Collider>()); else DestroyImmediate(c.GetComponent<Collider>());
            c.transform.SetParent(hoop, false);
            c.transform.localPosition = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (radius + 0.25f);
            c.transform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg);   // tangent to the circle
            c.transform.localScale = new Vector3(0.18f, seg * 0.5f, 0.18f);
            c.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    void Start()
    {
        if (!Application.isPlaying) return;
        Snap();
        if (transform.Find("Hoop") == null) BuildHoop();
        cart = FindAnyObjectByType<CoasterCart>();

        var go = new GameObject("Flames");
        go.transform.SetParent(transform, false);
        flames = go.AddComponent<ParticleSystem>();
        flames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = flames.main;
        main.loop = true; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f * radius / 3f, 1.1f * radius / 3f);
        main.startColor = new ParticleSystem.MinMaxGradient(hotColor, edgeColor);
        main.gravityModifier = -0.25f;                 // flames rise
        main.maxParticles = 3000;
        var em = flames.emission; em.rateOverTime = 0f;   // we emit by hand around the ring
        var col = flames.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(hotColor, 0f), new GradientColorKey(edgeColor, 0.6f), new GradientColorKey(new Color(0.3f, 0.05f, 0f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var sol = flames.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
        var noise = flames.noise; noise.enabled = true; noise.strength = 0.8f; noise.frequency = 1.2f;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = PipelineUtil.MakeParticleMaterial(true, SoftDot());
        flames.Play();

        var lg = new GameObject("Glow");
        lg.transform.SetParent(transform, false);
        glow = lg.AddComponent<Light>();
        glow.type = LightType.Point; glow.color = edgeColor; glow.range = radius * 6f;
    }

    void Update()
    {
        if (!Application.isPlaying || flames == null) return;
        float dt = Time.deltaTime;

        // Cart about to fly through?  -> burst + whoosh, once per lap
        if (cart != null && track != null && cart.state == CoasterCart.RideState.Riding)
        {
            float ahead = track.Wrap(distanceAlongTrack - cart.distance);
            if (ahead > 0f && ahead < 12f && lastLap != cart.ridesCompleted)
            {
                lastLap = cart.ridesCompleted;
                burst = 1.5f;
                if (whooshSound != null) AudioSource.PlayClipAtPoint(whooshSound, transform.position, volume);
            }
        }
        burst = Mathf.Max(0f, burst - dt);
        float k = burst > 0f ? burstMultiplier : 1f;

        // emit flames around the circle; two bright "comets" race around it
        spin += spinSpeed * dt;
        emitCarry += 260f * flameAmount * k * dt;
        int count = (int)emitCarry; emitCarry -= count;
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            float a;
            if (i % 3 == 0) a = (spin + (i % 2) * 180f + Random.Range(-12f, 12f)) * Mathf.Deg2Rad;   // comets
            else a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 local = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
            ep.position = transform.TransformPoint(local);
            ep.velocity = transform.TransformDirection(local.normalized * 0.6f * k) + Vector3.up * 0.5f;
            flames.Emit(ep, 1);
        }
        if (glow != null) glow.intensity = (2f + Mathf.PerlinNoise(Time.time * 7f, 0f) * 1.5f) * (burst > 0f ? 2.5f : 1f);
    }

    static Texture2D dot;
    static Texture2D SoftDot()
    {
        if (dot != null) return dot;
        const int N = 64;
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

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        for (int i = 0; i < 32; i++)
        {
            float a0 = i * Mathf.PI * 2f / 32f, a1 = (i + 1) * Mathf.PI * 2f / 32f;
            Gizmos.DrawLine(new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * radius, new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * radius);
        }
    }
}
