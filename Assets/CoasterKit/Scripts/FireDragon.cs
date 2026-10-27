// FireDragon.cs — a serpent dragon made of fire (or WATER: set Element = Water). It circles above its "lair" in a lazy
// figure-8, and when the cart comes close it swoops down, flies alongside and breathes fire.
// HOW TO USE: GameObject > Create Empty, name it "Fire Dragon", put it in the middle of the
// desert (Y = ground), Add Component > Fire Dragon, press Play. Everything is built by the script.
// REAL MODEL: drag a dragon prefab (e.g. from Four Evil Dragons HP) into "Dragon Model" and it is
// used instead of the built-in fire serpent - with its own Fly and Flame-Attack animations.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public class FireDragon : MonoBehaviour
{
    public enum Element { Fire, Water }

    [Header("Element")]
    [Tooltip("Fire = rising glowing flames.  Water = a falling, splashing water jet + mist trail.")]
    public Element element = Element.Fire;
    public Color waterColor = new Color(0.35f, 0.72f, 1f);

    [Header("Water dragon effects (only used when Element = Water)")]
    [Tooltip("How strong / thick the water jet is")] [Range(0.3f, 3f)] public float waterJetPower = 1.3f;
    [Tooltip("Swirling ring of water orbs around the dragon's head")] public bool waterAura = true;
    [Tooltip("Sparkling droplets drift around the whole dragon while it flies (off = cleaner look)")] public bool rainTrail = false;
    [Tooltip("The jet splashes on the cart when the dragon attacks")] public bool splashOnCart = true;
    [Tooltip("Optional: sound of the water jet (if empty, 'Fire Breath' sound is used)")] public AudioClip waterJetSound;

    [Header("Body")]
    [Range(0.5f, 3f)] public float size = 1.4f;
    [Range(8, 40)] public int bodySegments = 24;
    public Color scaleColor = new Color(0.55f, 0.08f, 0.02f);
    public Color fireColor = new Color(1f, 0.45f, 0.08f);
    public Color eyeColor = new Color(1f, 0.95f, 0.3f);

    [Header("Use a real dragon model (optional)")]
    [Tooltip("Drag a dragon PREFAB here (Project panel). Leave empty for the built-in fire serpent.")]
    public GameObject dragonModel;
    [Tooltip("Length of the model in metres (nose to tail) - multiplied by Size")]
    public float modelLength = 9f;
    [Tooltip("Turn the model if it flies sideways/backwards (e.g. 0,180,0)")]
    public Vector3 modelRotationFix = Vector3.zero;
    [Tooltip("Part of the animation name used while flying")] public string flyAnimation = "Fly Forward";
    [Tooltip("Part of the animation name used while breathing fire")] public string attackAnimation = "Fly Flame Attack";

    [Header("Flight around the lair (this object)")]
    public float flightRadius = 60f;
    public float flightHeight = 28f;
    public float cruiseSpeed = 16f;

    [Header("Attack the cart")]
    public bool swoopAtCart = true;
    [Tooltip("Swoops when the cart is this close to the dragon")] public float noticeDistance = 90f;
    public float swoopTime = 4f;
    public float restTime = 10f;

    [Header("Sound (optional)")]
    public AudioClip roar;
    public AudioClip fireBreath;
    [Range(0, 1)] public float volume = 1f;

    Transform head, jaw, wingL, wingR;
    readonly List<Transform> segs = new List<Transform>();
    readonly List<Vector3> trail = new List<Vector3>();
    ParticleSystem breath;
    Light headLight;
    Vector3 pos, vel;
    float t, swoop, rest;
    CoasterCart cart;
    AudioSource breathSrc;
    GameObject model; Animator modelAnim; Transform mouth;
    PlayableGraph graph; AnimationClip flyClip, attackClip, currentClip;
    float prevYaw, bank;
    ParticleSystem spray, mouthMist, rings; float cartSplashTimer;

    bool IsWater => element == Element.Water;

    // ---- used by DragonBattle.cs: another script can take control for a dragon fight ----
    [HideInInspector] public bool battleMode;
    [HideInInspector] public Vector3 battleTarget, battleLookAt;
    [HideInInspector] public bool battleBreath;
    /// <summary>World position of the mouth (where the breath comes out).</summary>
    public Vector3 MouthPosition => breath != null ? breath.transform.position : (head != null ? head.position : transform.position);
    Color ElementColor => IsWater ? waterColor : fireColor;

    void Start()
    {
        cart = FindAnyObjectByType<CoasterCart>();
        var scale = MakeMat(scaleColor, ElementColor * 0.6f);
        var glowMat = MakeMat(ElementColor, ElementColor * 2f);
        var eyeMat = MakeMat(eyeColor, eyeColor * 3f);
        var fireMat = PipelineUtil.MakeParticleMaterial(!IsWater, SoftDot());   // water = see-through, not glowing

        // ---- head ----
        head = new GameObject("Dragon Head").transform;
        head.SetParent(transform, false);
        if (dragonModel == null) BuildSerpentHead(scale, glowMat, eyeMat);
        else BuildModel();

        headLight = new GameObject("Glow").AddComponent<Light>();
        headLight.transform.SetParent(head, false);
        headLight.type = LightType.Point; headLight.color = ElementColor; headLight.range = 25f * size; headLight.intensity = 3f;

        // fire breath (cone points along the head's forward)
        breath = MakeFire(IsWater ? "Water Breath" : "Fire Breath", head, new Vector3(0, -0.1f, 1.8f), fireMat, 0.5f, 0f);
        var bm = breath.main; bm.startSpeed = new ParticleSystem.MinMaxCurve(18f * size, 28f * size);
        bm.startSize = new ParticleSystem.MinMaxCurve(0.8f * size, 2.2f * size); bm.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        bm.gravityModifier = -0.1f;
        var bs = breath.shape; bs.shapeType = ParticleSystemShapeType.Cone; bs.angle = 14f; bs.radius = 0.2f; bs.rotation = Vector3.zero;
        if (IsWater) BuildWaterEffects(fireMat);

        // ---- body: glowing scales that each trail flames (built-in serpent only) ----
        int n = dragonModel == null ? bodySegments : 0;
        for (int i = 0; i < n; i++)
        {
            float f = 1f - (float)i / bodySegments;
            var s = Shape(PrimitiveType.Sphere, transform, Vector3.zero, Vector3.zero, Vector3.one * Mathf.Lerp(0.25f, 1.05f, f) * size, i % 2 == 0 ? scale : glowMat).transform;
            s.name = "Body " + i;
            segs.Add(s);
            if (i % 2 == 0) MakeFire("Flames", s, Vector3.zero, fireMat, Mathf.Lerp(0.35f, 1f, f) * size, 35f);
        }

        pos = transform.position + Vector3.up * flightHeight + Vector3.right * flightRadius;
        vel = Vector3.forward * cruiseSpeed;
        for (int i = 0; i < 400; i++) trail.Add(pos);

        var breathClip = IsWater && waterJetSound != null ? waterJetSound : fireBreath;
        if (breathClip != null)
        {
            breathSrc = head.gameObject.AddComponent<AudioSource>();
            breathSrc.clip = breathClip; breathSrc.loop = true; breathSrc.volume = 0f; breathSrc.spatialBlend = 0.7f; breathSrc.minDistance = 20f;
            breathSrc.Play();
        }
    }

    void BuildSerpentHead(Material scale, Material glowMat, Material eyeMat)
    {
        Shape(PrimitiveType.Sphere, head, new Vector3(0, 0, 0), Vector3.zero, new Vector3(1.1f, 0.8f, 1.8f), scale);
        Shape(PrimitiveType.Cube, head, new Vector3(0, 0.05f, 1.1f), new Vector3(8, 0, 0), new Vector3(0.7f, 0.35f, 1.0f), scale);   // snout
        jaw = Shape(PrimitiveType.Cube, head, new Vector3(0, -0.3f, 0.9f), Vector3.zero, new Vector3(0.6f, 0.15f, 1.1f), glowMat).transform;
        foreach (float x in new[] { -0.35f, 0.35f })
        {
            Shape(PrimitiveType.Sphere, head, new Vector3(x, 0.3f, 0.55f), Vector3.zero, Vector3.one * 0.22f, eyeMat);
            Shape(PrimitiveType.Cylinder, head, new Vector3(x * 1.1f, 0.6f, -0.5f), new Vector3(-50, 0, x * 40f), new Vector3(0.12f, 0.5f, 0.12f), glowMat);   // horns
        }
        head.localScale = Vector3.one * size;

        // wings on the head's neck
        wingL = Shape(PrimitiveType.Cube, head, new Vector3(-1.4f, 0.2f, -1.2f), Vector3.zero, new Vector3(2.4f, 0.06f, 1.2f), glowMat).transform;
        wingR = Shape(PrimitiveType.Cube, head, new Vector3(1.4f, 0.2f, -1.2f), Vector3.zero, new Vector3(2.4f, 0.06f, 1.2f), glowMat).transform;
    }

    // ---- a real, animated dragon model ----
    void BuildModel()
    {
        model = Instantiate(dragonModel, head);
        model.name = dragonModel.name + " (flying)";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(modelRotationFix);
        foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);        // don't bump into the coaster
        foreach (var rb in model.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);

        // scale it to the wanted length and centre it on the flight point
        var b = ModelBounds();
        float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (longest > 0.01f) model.transform.localScale *= modelLength * size / longest;
        model.transform.position += head.position - ModelBounds().center;

        mouth = FindPart(model.transform, "jaw") ?? FindPart(model.transform, "mouth") ?? FindPart(model.transform, "head");

        modelAnim = model.GetComponentInChildren<Animator>();
        if (modelAnim != null && modelAnim.runtimeAnimatorController != null)
        {
            modelAnim.applyRootMotion = false;
            var clips = modelAnim.runtimeAnimatorController.animationClips;
            flyClip = FindClip(clips, flyAnimation) ?? FindClip(clips, "Fly") ?? (clips.Length > 0 ? clips[0] : null);
            attackClip = FindClip(clips, attackAnimation) ?? FindClip(clips, "Flame") ?? FindClip(clips, "Attack");
            Debug.Log($"[Fire Dragon] Using animations - flying: {(flyClip ? flyClip.name : "none")}, fire breath: {(attackClip ? attackClip.name : "none")}");
            PlayClip(flyClip);
        }
    }

    Bounds ModelBounds()
    {
        var rs = model.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(model.transform.position, Vector3.one);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Transform FindPart(Transform root, string word)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.ToLower().Contains(word)) return t;
        return null;
    }

    static AnimationClip FindClip(AnimationClip[] clips, string word)
    {
        if (string.IsNullOrEmpty(word)) return null;
        foreach (var c in clips) if (c != null && c.name.ToLower().Contains(word.ToLower())) return c;
        return null;
    }

    void PlayClip(AnimationClip clip)
    {
        if (clip == null || clip == currentClip || modelAnim == null) return;
        if (graph.IsValid()) graph.Destroy();
        AnimationPlayableUtilities.PlayClip(modelAnim, clip, out graph);
        currentClip = clip;
    }

    void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }

    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        t += dt * cruiseSpeed / Mathf.Max(10f, flightRadius);

        // where does the dragon want to go?
        Vector3 lair = transform.position;
        Vector3 target = lair + new Vector3(Mathf.Cos(t) * flightRadius,
                                            flightHeight + Mathf.Sin(t * 3f) * 6f,
                                            Mathf.Sin(t * 2f) * flightRadius * 0.6f);
        rest -= dt;
        if (swoopAtCart && cart != null && swoop <= 0f && rest <= 0f && cart.state == CoasterCart.RideState.Riding &&
            Vector3.Distance(cart.transform.position, pos) < noticeDistance)
        {
            swoop = swoopTime; rest = restTime;
            if (roar != null) AudioSource.PlayClipAtPoint(roar, pos, volume);
        }
        float speed = cruiseSpeed;
        bool breathing = false;
        if (battleMode)
        {
            swoop = 0f; rest = Mathf.Max(rest, 3f);
            target = battleTarget;
            speed = Mathf.Min(cruiseSpeed * 2.2f, Vector3.Distance(battleTarget, pos) * 1.2f + 0.5f);   // slows down to hover at its spot
            breathing = battleBreath;
        }
        else if (swoop > 0f && cart != null)
        {
            swoop -= dt;
            // fly ahead of the cart, a bit to the side and above, facing it
            target = cart.transform.position + cart.transform.forward * (14f + cart.speed * 0.6f) + cart.transform.right * 7f + Vector3.up * 5f;
            speed = Mathf.Max(cruiseSpeed * 1.8f, cart.speed * 1.3f);
            breathing = Vector3.Distance(pos, cart.transform.position) < 40f;
        }

        // smooth snake-like steering
        Vector3 want = (target - pos);
        if (want.sqrMagnitude > 1f) want = want.normalized * speed;
        vel = Vector3.Lerp(vel, want, dt * (swoop > 0f || battleMode ? 2.5f : 1.2f));
        pos += vel * dt;
        head.position = pos;
        Vector3 look = battleMode && (breathing || (battleTarget - pos).sqrMagnitude < 400f) ? (battleLookAt - pos)
                     : breathing && cart != null ? (cart.transform.position - pos) : vel;
        // lean (bank) into turns like a real flyer
        float yaw = Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg;
        bank = Mathf.Lerp(bank, Mathf.Clamp(-Mathf.DeltaAngle(prevYaw, yaw) / Mathf.Max(dt, 1e-4f) * 0.6f, -40f, 40f), dt * 3f);
        prevYaw = yaw;
        if (look.sqrMagnitude > 0.01f) head.rotation = Quaternion.Slerp(head.rotation, Quaternion.LookRotation(look.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, bank), dt * 5f);
        if (mouth != null)
        {
            breath.transform.position = mouth.position; headLight.transform.position = mouth.position;
            if (spray != null) spray.transform.position = mouth.position;
            if (mouthMist != null) mouthMist.transform.position = mouth.position;
        }
        if (model != null) PlayClip(breathing && attackClip != null ? attackClip : flyClip);

        // wings flap, jaw opens when breathing fire
        float flap = Mathf.Sin(Time.time * (swoop > 0f ? 9f : 5f)) * 35f;
        if (wingL != null) wingL.localRotation = Quaternion.Euler(0, 0, flap);
        if (wingR != null) wingR.localRotation = Quaternion.Euler(0, 0, -flap);
        if (jaw != null) jaw.localRotation = Quaternion.Slerp(jaw.localRotation, Quaternion.Euler(breathing ? 25f : 0f, 0, 0), dt * 8f);

        var em = breath.emission; em.rateOverTime = breathing ? 160f : 0f;
        if (IsWater) UpdateWater(breathing, dt);
        if (breathSrc != null) breathSrc.volume = Mathf.MoveTowards(breathSrc.volume, breathing ? volume : 0f, dt * 3f);
        headLight.intensity = (breathing ? 6f : 3f) + Mathf.PerlinNoise(Time.time * 6f, 1f);

        // body follows the head's path like a snake
        if ((trail[0] - pos).sqrMagnitude > 0.04f) { trail.Insert(0, pos); if (trail.Count > 800) trail.RemoveAt(trail.Count - 1); }
        float spacing = 1.1f * size;
        int ti = 0; float acc = 0f;
        for (int i = 0; i < segs.Count; i++)
        {
            float need = (i + 1) * spacing;
            while (ti < trail.Count - 1 && acc + Vector3.Distance(trail[ti], trail[ti + 1]) < need) { acc += Vector3.Distance(trail[ti], trail[ti + 1]); ti++; }
            Vector3 p = trail[Mathf.Min(ti + 1, trail.Count - 1)];
            float wave = Mathf.Sin(Time.time * 4f - i * 0.5f) * 0.3f * size;
            segs[i].position = p + Vector3.up * wave;
        }
    }

    // ---------------- water dragon ----------------
    void BuildWaterEffects(Material waterMat)
    {
        float p = waterJetPower;
        // 1) the jet core: a powerful, nearly straight HYDRO BLAST of foaming water that bursts into mist
        //    (big soft blobs, not thin streaks - so it reads as a dragon breath, not a trickle)
        var bm = breath.main;
        bm.startSpeed = new ParticleSystem.MinMaxCurve(34f * size, 44f * size);
        bm.startSize = new ParticleSystem.MinMaxCurve(0.7f * size * p, 1.5f * size * p);
        bm.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.85f);
        bm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        bm.gravityModifier = 0.08f;                                  // almost no droop
        bm.maxParticles = 4000;
        var bs = breath.shape; bs.angle = 7f; bs.radius = 0.25f * size * p;
        var br = breath.GetComponent<ParticleSystemRenderer>();
        br.renderMode = ParticleSystemRenderMode.Billboard;
        var bsol = breath.sizeOverLifetime; bsol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 3.2f));
        var bcol = breath.colorOverLifetime;
        var bg = new Gradient();
        bg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.Lerp(waterColor, Color.white, 0.5f), 0.3f), new GradientColorKey(waterColor, 1f) },
                   new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.75f, 0.4f), new GradientAlphaKey(0f, 1f) });
        bcol.color = new ParticleSystem.MinMaxGradient(bg);
        var bn = breath.noise; bn.enabled = true; bn.strength = 1.5f * size; bn.frequency = 0.6f;      // churning, swirling water
        var bc = breath.collision; bc.enabled = true; bc.type = ParticleSystemCollisionType.World; bc.bounce = 0.15f; bc.lifetimeLoss = 0.6f;
        bc.sendCollisionMessages = false;

        // shock-wave rings that shoot out of the mouth along the blast
        rings = MakeFire("Water Rings", head, new Vector3(0, -0.1f, 1.8f), PipelineUtil.MakeParticleMaterial(false, RingTex()), 1f, 0f);
        var rgm = rings.main;
        rgm.startSpeed = new ParticleSystem.MinMaxCurve(20f * size, 26f * size);
        rgm.startSize = new ParticleSystem.MinMaxCurve(1.2f * size * p, 1.6f * size * p);
        rgm.startLifetime = 0.7f; rgm.gravityModifier = 0f;
        rgm.startColor = new Color(0.85f, 0.95f, 1f, 0.9f);
        var rgs = rings.shape; rgs.shapeType = ParticleSystemShapeType.Cone; rgs.angle = 0f; rgs.radius = 0.01f; rgs.rotation = Vector3.zero;
        var rgsol = rings.sizeOverLifetime; rgsol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 3.5f));
        var rgr = rings.GetComponent<ParticleSystemRenderer>(); rgr.alignment = ParticleSystemRenderSpace.Velocity;   // ring faces along the blast

        // splash droplets wherever the jet hits the ground / track (collision sub-emitter)
        var hit = MakeFire("Jet Impact Splash", breath.transform, Vector3.zero, waterMat, 0.35f * size, 0f);
        var hm = hit.main; hm.loop = false; hm.playOnAwake = false;
        hm.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f); hm.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        hm.gravityModifier = 1.2f;
        var hs = hit.shape; hs.shapeType = ParticleSystemShapeType.Hemisphere; hs.radius = 0.3f;
        var hb = hit.emission; hb.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)6) });
        var sub = breath.subEmitters; sub.enabled = true;
        sub.AddSubEmitter(hit, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing);
        hit.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // only fires when the jet hits something

        // 2) spray: fine droplets fanning out around the jet
        spray = MakeFire("Water Spray", head, new Vector3(0, -0.1f, 1.8f), waterMat, 0.25f * size, 0f);
        var sm = spray.main;
        sm.startSpeed = new ParticleSystem.MinMaxCurve(10f * size, 20f * size);
        sm.startSize = new ParticleSystem.MinMaxCurve(0.1f * size, 0.3f * size);
        sm.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        sm.gravityModifier = 0.35f;
        var ss = spray.shape; ss.shapeType = ParticleSystemShapeType.Cone; ss.angle = 14f; ss.radius = 0.3f; ss.rotation = Vector3.zero;
        var sr = spray.GetComponent<ParticleSystemRenderer>(); sr.renderMode = ParticleSystemRenderMode.Stretch; sr.velocityScale = 0.04f;

        // 3) mouth mist: a soft cloud puffing out while it attacks
        mouthMist = MakeFire("Mouth Mist", head, new Vector3(0, 0, 1.8f), waterMat, 1.6f * size, 0f);
        var mm = mouthMist.main;
        mm.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f); mm.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
        mm.startColor = new Color(0.9f, 0.96f, 1f, 0.3f); mm.gravityModifier = -0.02f;
        var ms = mouthMist.shape; ms.shapeType = ParticleSystemShapeType.Cone; ms.angle = 35f; ms.rotation = Vector3.zero;

        // 4) sparkle trail: fine droplets drifting off the WHOLE dragon (not dripping from one spot)
        if (rainTrail)
        {
            var rain = MakeFire("Sparkle Trail", head, Vector3.zero, waterMat, 0.15f * size, 30f);
            var rm = rain.main; rm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f); rm.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.8f);
            rm.gravityModifier = 0.15f;
            var rs = rain.shape; rs.shapeType = ParticleSystemShapeType.Sphere; rs.radius = 3.5f * size;
            MakeFire("Mist Trail", head, new Vector3(0, 0, -2f * size), waterMat, 1.4f * size, 20f);
        }

        // 5) aura: water orbs circling the head
        if (waterAura)
        {
            var aura = MakeFire("Water Aura", head, Vector3.zero, waterMat, 0.5f * size, 40f);
            var am = aura.main; am.simulationSpace = ParticleSystemSimulationSpace.Local;
            am.startSpeed = 0f; am.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f); am.gravityModifier = 0f;
            // a ring around the dragon's neck (around its flying direction) that spins like a whirlpool
            var ash = aura.shape; ash.shapeType = ParticleSystemShapeType.Circle; ash.radius = 3f * size; ash.rotation = Vector3.zero;
            var vel = aura.velocityOverLifetime; vel.enabled = true;
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(2.5f);
        }
    }

    void UpdateWater(bool breathing, float dt)
    {
        // the jet gushes in pulses, like a real water cannon
        float gush = 0.75f + 0.25f * Mathf.Sin(Time.time * 14f);
        var em = breath.emission; em.rateOverTime = breathing ? 260f * waterJetPower * gush : 0f;
        if (spray != null) { var e = spray.emission; e.rateOverTime = breathing ? 140f * waterJetPower : 0f; }
        if (mouthMist != null) { var e = mouthMist.emission; e.rateOverTime = breathing ? 18f : 0f; }
        if (rings != null) { var e = rings.emission; e.rateOverTime = breathing ? 7f : 0f; }
        // aim the blast straight from the mouth at the riders
        if (breathing && (cart != null || battleMode))
        {
            Vector3 from = breath.transform.position, to = battleMode ? battleLookAt : cart.transform.position + Vector3.up * 1.2f;
            if ((to - from).sqrMagnitude > 1f)
            {
                Quaternion aim = Quaternion.LookRotation(to - from);
                breath.transform.rotation = aim;
                if (spray != null) spray.transform.rotation = aim;
                if (rings != null) { rings.transform.position = from; rings.transform.rotation = aim; }
            }
        }
        headLight.color = Color.Lerp(waterColor, Color.white, Mathf.PerlinNoise(Time.time * 2f, 3f) * 0.4f);   // shimmering

        // water splashes onto the cart while the dragon attacks
        if (breathing && splashOnCart && cart != null && !battleMode)
        {
            cartSplashTimer -= dt;
            if (cartSplashTimer <= 0f)
            {
                cartSplashTimer = 0.3f;
                FxFactory.CreateSplash(cart.transform.position + Vector3.up * 1.2f, 0.6f * waterJetPower);
                // water hits the camera too, when it is close to the cart
                var cam = Camera.main;
                if (cam != null && Vector3.Distance(cam.transform.position, cart.transform.position) < 25f) LensDrops.Splash(4);
            }
        }
    }

    // ---------------- helpers ----------------
    static GameObject Shape(PrimitiveType type, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale, Material m)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localEulerAngles = euler; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    static Material MakeMat(Color c, Color emission)
    {
        var m = PipelineUtil.MakeLit(c, 0.5f, 0.3f);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission);
        return m;
    }

    ParticleSystem MakeFire(string name, Transform parent, Vector3 local, Material mat, float s, float rate)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f * s, 1.6f * s);
        main.startColor = IsWater ? new ParticleSystem.MinMaxGradient(waterColor, new Color(0.9f, 0.97f, 1f, 0.8f))
                                  : new ParticleSystem.MinMaxGradient(fireColor, new Color(1f, 0.85f, 0.35f));
        main.gravityModifier = IsWater ? 0.35f : -0.2f;                // water drips down, fire rises
        main.maxParticles = 1500;
        var em = ps.emission; em.rateOverTime = rate;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f * s;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        if (IsWater)
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(waterColor, 0.5f), new GradientColorKey(waterColor * 0.8f, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.1f), new GradientAlphaKey(0f, 1f) });
        else
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(fireColor, 0.4f), new GradientColorKey(new Color(0.25f, 0.03f, 0f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, IsWater ? AnimationCurve.Linear(0f, 0.6f, 1f, 1.5f)     // mist spreads out
                                                              : AnimationCurve.Linear(0f, 1f, 1f, 0.1f));    // flames shrink
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        ps.Play();
        return ps;
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

    static Texture2D ring;
    static Texture2D RingTex()
    {
        if (ring != null) return ring;
        const int N = 64;
        ring = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.78f) / 0.16f); a *= a;
                ring.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        ring.Apply();
        return ring;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.6f);
        Vector3 c = transform.position + Vector3.up * flightHeight;
        Vector3 prev = c + new Vector3(flightRadius, 0, 0);
        for (int i = 1; i <= 64; i++)
        {
            float a = i * Mathf.PI * 2f / 64f;
            Vector3 p = c + new Vector3(Mathf.Cos(a) * flightRadius, 0f, Mathf.Sin(a * 2f) * flightRadius * 0.6f);
            Gizmos.DrawLine(prev, p); prev = p;
        }
    }
}
