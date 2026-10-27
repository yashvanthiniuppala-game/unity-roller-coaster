// FireGauntlet.cs — the cart rides THROUGH FIRE.
//   * a burning ring of fire at the entrance and at the exit (the cart flies through both),
//   * in between, flame jets on BOTH sides of the track shoot up and bend over the track,
//     so the cart races through a tunnel of fire,
//   * each pair of jets ROARS up just as the cart reaches it, the screen glows orange from the heat,
//     the camera shakes a little and you hear the fire.
// Nothing touches the cart - the flames start beside the rails.
//
// HOW TO USE:
//   1. GameObject > Create Empty, name it "Fire Gauntlet", Add Component > Fire Gauntlet.
//   2. Type "Start Distance" (metres from the station) and "Length". An orange line shows the zone in the Scene view.
//      Pick a part of the track that is NOT the loop. Press Play.
//   3. Optional: a looping fire sound ("Fire Loop") and a whoosh ("Burst Sound").
using System.Collections.Generic;
using UnityEngine;

public class FireGauntlet : MonoBehaviour
{
    [Header("Where")]
    public CoasterTrack track;
    [Tooltip("Metres from the station where the fire starts")] public float startDistance = 420f;
    [Tooltip("How long the fire tunnel is (metres)")] public float length = 60f;
    [Tooltip("Metres between the pairs of flame jets")] public float jetSpacing = 6f;
    [Tooltip("Distance of the jets from the rails (each side)")] public float jetOffset = 3.4f;

    [Header("Look")]
    public bool fireRings = true;
    [Range(0.5f, 2.5f)] public float flameSize = 1.2f;
    [Tooltip("The screen glows orange while the cart is in the fire")] public bool heatGlow = true;
    public bool cameraShake = true;

    [Header("Sound (optional)")]
    public AudioClip fireLoop;
    public AudioClip burstSound;
    [Range(0, 1)] public float volume = 1f;

    class Jet { public float dist; public ParticleSystem[] ps; public Light light; public bool burst; }
    readonly List<Jet> jets = new List<Jet>();
    CoasterCart cart;
    AudioSource loop;
    float heat;
    static Texture2D vignette;
    Material fireMat, smokeMat;

    void Start()
    {
        if (track == null) track = FindAnyObjectByType<CoasterTrack>();
        cart = FindAnyObjectByType<CoasterCart>();
        if (track == null) { Debug.LogWarning("[Fire Gauntlet] No track found."); enabled = false; return; }
        if (track.Length < 1f) track.Build();
        for (float d = startDistance; d <= startDistance + length; d += 5f)
            if (track.InLoop(d)) { Debug.LogWarning($"[Fire Gauntlet] The fire overlaps the loop around {d:F0} m - move Start Distance."); break; }
        fireMat = PipelineUtil.MakeParticleMaterial(true, Soft());
        smokeMat = PipelineUtil.MakeParticleMaterial(false, Soft());

        // fire rings at both ends (uses your Fire Ring script)
        if (fireRings)
        {
            Ring(startDistance - 2f, "Fire Ring (entrance)");
            Ring(startDistance + length + 2f, "Fire Ring (exit)");
        }

        // pairs of flame jets that bend over the track
        var stone = PipelineUtil.MakeLit(new Color(0.28f, 0.22f, 0.18f), 0.2f, 0f);
        int n = 0;
        for (float d = startDistance + 2f; d <= startDistance + length - 2f; d += Mathf.Max(3f, jetSpacing), n++)
        {
            track.Sample(d, out Vector3 p, out Vector3 f, out Vector3 u);
            Vector3 r = Vector3.Cross(u, f).normalized;
            var jet = new Jet { dist = d, ps = new ParticleSystem[2] };
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                Vector3 basePos = p + r * side * jetOffset - u * 0.3f;
                // a stone fire-pot under each jet
                var pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(pot.GetComponent<Collider>());
                pot.name = "Fire Pot";
                pot.transform.SetParent(transform, true);
                pot.transform.position = basePos - u * 0.4f;
                pot.transform.rotation = Quaternion.LookRotation(f, u);
                pot.transform.localScale = new Vector3(1.1f, 0.45f, 1.1f);
                pot.GetComponent<Renderer>().sharedMaterial = stone;
                // the jet: up and INWARDS, so the flames from both sides meet above the cart
                Vector3 dir = (u * 1f - r * side * 0.62f).normalized;
                jet.ps[s] = Flame(basePos, dir, u);
            }
            if (n % 2 == 0)
            {
                var l = new GameObject("Fire Light").AddComponent<Light>();
                l.transform.SetParent(transform, false);
                l.transform.position = p + u * 3f;
                l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.15f); l.range = 14f; l.intensity = 1f; l.shadows = LightShadows.None;
                jet.light = l;
            }
            jets.Add(jet);
        }

        if (fireLoop != null)
        {
            loop = gameObject.AddComponent<AudioSource>();
            loop.clip = fireLoop; loop.loop = true; loop.volume = 0f; loop.spatialBlend = 0f; loop.Play();
        }
    }

    void Ring(float d, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ring = go.AddComponent<FireRing>();
        ring.track = track;
        ring.distanceAlongTrack = d;
        ring.Snap();
    }

    ParticleSystem Flame(Vector3 pos, Vector3 dir, Vector3 up)
    {
        var go = new GameObject("Flame Jet");
        go.transform.SetParent(transform, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir, up));
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.loop = true; m.playOnAwake = true; m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(7f * flameSize, 11f * flameSize);
        m.startSize = new ParticleSystem.MinMaxCurve(0.8f * flameSize, 1.8f * flameSize);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.15f), new Color(1f, 0.85f, 0.35f));
        m.gravityModifier = -0.35f;          // hot air rises -> the flame curls over
        m.maxParticles = 1500;
        var e = ps.emission; e.rateOverTime = 18f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 9f; sh.radius = 0.25f; sh.rotation = Vector3.zero;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.08f), 0.45f), new GradientColorKey(new Color(0.45f, 0.08f, 0.02f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f));
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.8f; noise.frequency = 1.5f;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = fireMat;
        // a little smoke on top
        var smoke = new GameObject("Smoke");
        smoke.transform.SetParent(go.transform, false);
        smoke.transform.localPosition = new Vector3(0f, 0f, 4f * flameSize);
        var sp = smoke.AddComponent<ParticleSystem>();
        sp.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var sm = sp.main;
        sm.loop = true; sm.playOnAwake = true; sm.simulationSpace = ParticleSystemSimulationSpace.World;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
        sm.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        sm.startColor = new Color(0.2f, 0.17f, 0.15f, 0.45f);
        sm.gravityModifier = -0.1f;
        var se = sp.emission; se.rateOverTime = 5f;
        var ssh = sp.shape; ssh.shapeType = ParticleSystemShapeType.Sphere; ssh.radius = 0.6f;
        smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial = smokeMat;
        sp.Play();
        ps.Play();
        return ps;
    }

    void Update()
    {
        if (cart == null) return;
        float d = cart.distance;
        bool riding = cart.state == CoasterCart.RideState.Riding;
        bool inside = riding && d >= startDistance - 4f && d <= startDistance + length + 4f;

        foreach (var j in jets)
        {
            // each pair ROARS up when the cart comes close
            bool near = riding && d >= j.dist - 22f && d <= j.dist + 6f;
            float rate = near ? 260f : 18f;
            foreach (var ps in j.ps)
            {
                var e = ps.emission; e.rateOverTime = rate;
                var m = ps.main; m.startSpeed = near ? new ParticleSystem.MinMaxCurve(10f * flameSize, 15f * flameSize) : new ParticleSystem.MinMaxCurve(5f * flameSize, 8f * flameSize);
            }
            if (j.light != null) j.light.intensity = (near ? 6f : 1f) + Mathf.PerlinNoise(Time.time * 9f, j.dist) * 2f;
            if (near && !j.burst)
            {
                j.burst = true;
                if (burstSound != null) AudioSource.PlayClipAtPoint(burstSound, cart.transform.position, volume * 0.7f);
            }
            if (!riding || d > j.dist + 10f || d < j.dist - 30f) j.burst = false;
        }

        heat = Mathf.MoveTowards(heat, inside ? 1f : 0f, Time.deltaTime * 2f);
        if (loop != null) loop.volume = volume * heat;
        if (cameraShake && inside && Random.value < Time.deltaTime * 6f) RideCamera.Shake(0.06f, 0.25f);
    }

    void OnGUI()
    {
        if (!heatGlow || heat < 0.01f) return;
        if (vignette == null) vignette = MakeVignette();
        float flicker = 0.85f + Mathf.PerlinNoise(Time.time * 6f, 0.3f) * 0.3f;
        GUI.color = new Color(1f, 1f, 1f, heat * 0.75f * flicker);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignette);
        GUI.color = Color.white;
    }

    static Texture2D MakeVignette()
    {
        const int N = 128;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                float a = Mathf.Clamp01((r - 0.35f) / 0.65f); a *= a;
                t.SetPixel(x, y, new Color(1f, 0.38f + (1f - a) * 0.2f, 0.05f, a));
            }
        t.Apply();
        return t;
    }

    static Texture2D soft;
    static Texture2D Soft()
    {
        if (soft != null) return soft;
        const int N = 64;
        soft = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)); a *= a;
                soft.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        soft.Apply();
        return soft;
    }

    void OnDrawGizmosSelected()
    {
        if (track == null) track = FindAnyObjectByType<CoasterTrack>();
        if (track == null) return;
        Gizmos.color = new Color(1f, 0.45f, 0.1f);
        for (float d = startDistance; d < startDistance + length; d += 3f)
        {
            track.Sample(d, out Vector3 a, out _, out Vector3 u); track.Sample(d + 3f, out Vector3 b, out _, out _);
            Gizmos.DrawLine(a + u * 4f, b + u * 4f);
        }
    }
}
