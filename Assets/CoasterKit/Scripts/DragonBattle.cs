// DragonBattle.cs — FIRE vs WATER: your Fire Dragon and Water Dragon fight right above the track.
//   1. When the cart comes near, both dragons fly in and hover on both sides of the track, facing each other.
//   2. They roar and breathe at the same time - the fire and the water jet CRASH together in mid-air
//      above the track: hissing steam, sparks, a flickering light.
//   3. Then the clash BLOWS UP into a huge steam explosion (boom + camera shake) just as the cart rides under it,
//      and the dragons fly back to their normal flight.
// Uses your existing dragons (Fire Dragon components) - nothing about them is changed in the Inspector.
//
// HOW TO USE:
//   1. GameObject > Create Empty, name it "Dragon Battle", Add Component > Dragon Battle.
//   2. It finds your Fire Dragon and Water Dragon by itself (or drag them into the two slots).
//   3. Type "Battle Distance" (metres from the station) - the clash happens above that part of the track.
//      A yellow marker in the Scene view shows the spot. Press Play.
using UnityEngine;

[ExecuteAlways]
public class DragonBattle : MonoBehaviour
{
    [Header("Who")]
    public FireDragon fireDragon;
    public FireDragon waterDragon;
    public CoasterTrack track;

    [Header("Where / when")]
    [Tooltip("The clash happens above this part of the track (metres from the station)")] public float battleDistance = 700f;
    [Tooltip("How high above the rails the dragons fight")] public float height = 18f;
    [Tooltip("Distance between the two dragons")] public float gap = 46f;
    [Tooltip("The dragons are called this many seconds before the cart arrives")] public float callAhead = 6f;
    [Tooltip("Seconds they breathe at each other before it explodes")] public float clashTime = 3f;

    [Header("Effects")]
    [Range(0.5f, 3f)] public float explosionSize = 2.2f;
    public Color steamColor = new Color(0.92f, 0.95f, 1f);
    public bool cameraShake = true;

    [Header("Sound (optional)")]
    public AudioClip roarSound;
    public AudioClip hissSound;
    public AudioClip boomSound;
    [Range(0, 1)] public float volume = 1f;

    enum Phase { Waiting, Flying, Clash, Done }
    Phase phase = Phase.Waiting;
    float timer;
    CoasterCart cart;
    Vector3 spot, across;
    ParticleSystem steam, sparks;
    Light clashLight;
    AudioSource hiss;

    void Find()
    {
        if (track == null) track = FindAnyObjectByType<CoasterTrack>();
        if (fireDragon == null || waterDragon == null)
            foreach (var d in FindObjectsByType<FireDragon>(FindObjectsSortMode.None))
            {
                if (d.element == FireDragon.Element.Fire && fireDragon == null) fireDragon = d;
                if (d.element == FireDragon.Element.Water && waterDragon == null) waterDragon = d;
            }
        if (track != null && track.Length < 1f) track.Build();
    }

    void ComputeSpot()
    {
        if (track == null) return;
        track.Sample(battleDistance, out Vector3 p, out Vector3 f, out _);
        across = Vector3.Cross(Vector3.up, f); across.y = 0f;
        across = across.sqrMagnitude > 1e-4f ? across.normalized : Vector3.right;
        spot = p + Vector3.up * height;
    }

    void Start()
    {
        if (!Application.isPlaying) return;
        Find();
        ComputeSpot();
        cart = FindAnyObjectByType<CoasterCart>();
        if (fireDragon == null || waterDragon == null) Debug.LogWarning("[Dragon Battle] Needs one Fire Dragon (Element = Fire) and one Water Dragon (Element = Water) in the scene.");
        BuildClashFx();
    }

    void BuildClashFx()
    {
        var root = new GameObject("Clash FX").transform;
        root.SetParent(transform, false); root.position = spot;
        var mat = PipelineUtil.MakeParticleMaterial(false, SoftDot());
        var add = PipelineUtil.MakeParticleMaterial(true, SoftDot());

        steam = NewPs("Steam", root, mat);
        var m = steam.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
        m.startSize = new ParticleSystem.MinMaxCurve(2.5f, 6f);
        m.startColor = new Color(steamColor.r, steamColor.g, steamColor.b, 0.55f);
        m.gravityModifier = -0.12f;
        var sh = steam.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 2f;
        var sol = steam.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));

        sparks = NewPs("Clash Sparks", root, add);
        m = sparks.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(8f, 20f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f), new Color(0.6f, 0.9f, 1f));
        m.gravityModifier = 0.6f;
        sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 1f;

        clashLight = new GameObject("Clash Light").AddComponent<Light>();
        clashLight.transform.SetParent(root, false);
        clashLight.type = LightType.Point; clashLight.range = 45f; clashLight.intensity = 0f; clashLight.color = new Color(1f, 0.8f, 0.6f);

        if (hissSound != null)
        {
            hiss = root.gameObject.AddComponent<AudioSource>();
            hiss.clip = hissSound; hiss.loop = true; hiss.volume = 0f; hiss.spatialBlend = 0.6f; hiss.minDistance = 25f; hiss.Play();
        }
    }

    static ParticleSystem NewPs(string name, Transform parent, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = true; m.playOnAwake = true; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 2000;
        var e = ps.emission; e.rateOverTime = 0f;
        var c = ps.colorOverLifetime; c.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
        c.color = new ParticleSystem.MinMaxGradient(g);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        ps.Play();
        return ps;
    }

    void Update()
    {
        if (!Application.isPlaying || cart == null || fireDragon == null || waterDragon == null) return;
        float dt = Time.deltaTime;

        // a new ride: get ready again
        if (cart.state == CoasterCart.RideState.WaitingInStation) { if (phase != Phase.Waiting) Release(); phase = Phase.Waiting; return; }

        float toSpot = battleDistance - cart.distance;                    // metres until the cart is under the clash
        float secondsAway = toSpot / Mathf.Max(3f, cart.speed);

        switch (phase)
        {
            case Phase.Waiting:
                if (toSpot > 0f && secondsAway < callAhead + clashTime)
                {
                    phase = Phase.Flying; timer = 0f;
                    Command(false);
                    if (roarSound != null) AudioSource.PlayClipAtPoint(roarSound, spot, volume);
                }
                break;

            case Phase.Flying:
                timer += dt;
                Command(false);
                // start breathing when both are close to their spots (or the cart is getting near)
                bool ready = Vector3.Distance(fireDragon.MouthPosition, FirePos()) < 14f && Vector3.Distance(waterDragon.MouthPosition, WaterPos()) < 14f;
                if (ready || secondsAway < clashTime) { phase = Phase.Clash; timer = 0f; }
                break;

            case Phase.Clash:
                timer += dt;
                Command(true);
                SetClash(1f);
                // explode just as the cart rides underneath (or after the clash time)
                if (toSpot < 6f || timer > clashTime + 4f) Explode();
                break;

            case Phase.Done:
                timer += dt;
                SetClash(Mathf.Clamp01(1f - timer * 2f));
                break;
        }
    }

    Vector3 FirePos() { return spot - across * gap * 0.5f; }
    Vector3 WaterPos() { return spot + across * gap * 0.5f; }

    void Command(bool breathe)
    {
        fireDragon.battleMode = true; waterDragon.battleMode = true;
        fireDragon.battleTarget = FirePos(); waterDragon.battleTarget = WaterPos();
        fireDragon.battleLookAt = spot; waterDragon.battleLookAt = spot;
        fireDragon.battleBreath = breathe; waterDragon.battleBreath = breathe;
    }

    void SetClash(float k)
    {
        if (steam != null) { var e = steam.emission; e.rateOverTime = 60f * k; }
        if (sparks != null) { var e = sparks.emission; e.rateOverTime = 120f * k; }
        if (clashLight != null) clashLight.intensity = k * (4f + Mathf.PerlinNoise(Time.time * 12f, 0f) * 4f);
        if (hiss != null) hiss.volume = volume * k;
    }

    void Explode()
    {
        phase = Phase.Done; timer = 0f;
        FxFactory.CreateExplosion(spot, explosionSize);
        if (steam != null) steam.Emit(120);          // big puff of steam
        if (sparks != null) sparks.Emit(250);
        if (boomSound != null) AudioSource.PlayClipAtPoint(boomSound, spot, volume);
        if (cameraShake) RideCamera.Shake(0.3f * explosionSize, 1f);
        var flash = new GameObject("Battle Flash").AddComponent<Light>();
        flash.transform.position = spot;
        flash.type = LightType.Point; flash.range = 80f; flash.intensity = 10f; flash.color = new Color(1f, 0.85f, 0.7f);
        flash.gameObject.AddComponent<BattleFlash>();
        Release();
    }

    void Release()
    {
        if (fireDragon != null) { fireDragon.battleMode = false; fireDragon.battleBreath = false; }
        if (waterDragon != null) { waterDragon.battleMode = false; waterDragon.battleBreath = false; }
    }

    void OnDisable() { Release(); }

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
        if (Application.isPlaying) return;
        Find();
        ComputeSpot();
        if (track == null) return;
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(spot, 3f);
        Gizmos.color = new Color(1f, 0.4f, 0.1f); Gizmos.DrawLine(spot, FirePos()); Gizmos.DrawSphere(FirePos(), 1.5f);
        Gizmos.color = new Color(0.3f, 0.7f, 1f); Gizmos.DrawLine(spot, WaterPos()); Gizmos.DrawSphere(WaterPos(), 1.5f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(spot + Vector3.up * 4f, "Dragon Battle");
#endif
    }
}

/// <summary>Fades a flash light out and removes it.</summary>
public class BattleFlash : MonoBehaviour
{
    Light l;
    void Start() { l = GetComponent<Light>(); }
    void Update()
    {
        if (l == null) { Destroy(gameObject); return; }
        l.intensity = Mathf.MoveTowards(l.intensity, 0f, Time.deltaTime * 15f);
        if (l.intensity <= 0.01f) Destroy(gameObject);
    }
}
