// FxFactory.cs  —  CPS 499 HW#2 Roller Coaster Kit
// Builds particle effects (fire, smoke, splash, explosion, sparks) from code, so the
// effect scripts work even before you design your own particle systems.
// If you make your own Particle System prefab, drag it into the effect script's
// "Custom ... Prefab" slot and it will be used instead.
using UnityEngine;

public static class FxFactory
{
    static Texture2D softTex, smokeTex;
    static Material additive, alpha;

    // ---------------- materials ----------------
    public static Material MakeLitMaterial(Color c)
    {
        var m = new Material(PipelineUtil.LitShader());
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        return m;
    }

    static Texture2D SoftTexture(bool noisy)
    {
        const int N = 64;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r); a *= a;
                if (noisy) a *= Mathf.Lerp(0.55f, 1f, Mathf.PerlinNoise(x * 0.15f, y * 0.15f));
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        t.Apply();
        return t;
    }

    static Material ParticleMaterial(bool add)
    {
        if (add && additive != null) return additive;
        if (!add && alpha != null) return alpha;
        if (softTex == null) softTex = SoftTexture(false);
        if (smokeTex == null) smokeTex = SoftTexture(true);
        var m = PipelineUtil.MakeParticleMaterial(add, add ? softTex : smokeTex);
        if (add) additive = m; else alpha = m;
        return m;
    }

    static ParticleSystem NewSystem(string name, Transform parent, Vector3 pos, bool additiveBlend)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = ParticleMaterial(additiveBlend);
        return ps;
    }

    static ParticleSystem.MinMaxGradient Fade(Color a, Color b)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0f, 1f) });
        return new ParticleSystem.MinMaxGradient(g);
    }

    static void Burst(ParticleSystem ps, int count)
    {
        var e = ps.emission;
        e.rateOverTime = 0f;
        e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
    }

    // ---------------- effects ----------------

    /// <summary>Looping flames (use for torches, fire jets, burning barrels).</summary>
    public static ParticleSystem CreateFire(Transform parent, Vector3 pos, float size = 1f)
    {
        var ps = NewSystem("Fire", parent, pos, true);
        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * size, 3f * size);
        main.startSize = new ParticleSystem.MinMaxCurve(0.6f * size, 1.3f * size);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.1f), new Color(1f, 0.85f, 0.3f));
        main.maxParticles = 400;
        var e = ps.emission; e.rateOverTime = 60f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 0.25f * size;
        sh.rotation = new Vector3(-90f, 0f, 0f); // point up
        var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(new Color(1f, 0.8f, 0.3f), new Color(0.8f, 0.1f, 0.02f));
        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f));
        var n = ps.noise; n.enabled = true; n.strength = 0.6f; n.frequency = 1.5f;
        ps.Play();
        return ps;
    }

    /// <summary>One-shot water splash (white spray + ring).</summary>
    public static ParticleSystem CreateSplash(Vector3 pos, float size = 1f)
    {
        // Bigger 'size' = wider and fuller splash; the height only grows a little (sqrt),
        // so a big splash doesn't shoot water into the sky.
        float up = Mathf.Sqrt(Mathf.Max(0.1f, size));
        var ps = NewSystem("Splash", null, pos, false);
        var main = ps.main;
        main.loop = false; main.duration = 0.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f * up, 9.5f * up);
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f * size, 1.0f * size);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.9f, 1f, 0.9f), new Color(1f, 1f, 1f, 1f));
        main.gravityModifier = 1.2f;
        main.maxParticles = 3000;
        main.stopAction = ParticleSystemStopAction.Destroy;
        Burst(ps, (int)(140 * size));
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 1.3f * size;
        sh.rotation = new Vector3(-90f, 0f, 0f);
        var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, new Color(0.7f, 0.85f, 1f));
        ps.Play();

        // side "curtain": a wide, low sheet of water thrown out to both sides
        var wide = NewSystem("Splash Curtain", ps.transform, pos, false);
        var wm = wide.main;
        wm.loop = false; wm.duration = 0.4f;
        wm.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
        wm.startSpeed = new ParticleSystem.MinMaxCurve(5f * up, 10f * up);
        wm.startSize = new ParticleSystem.MinMaxCurve(0.4f * size, 1.2f * size);
        wm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.93f, 1f, 0.8f), Color.white);
        wm.gravityModifier = 1.4f;
        wm.maxParticles = 3000;
        Burst(wide, (int)(110 * size));
        var wsh = wide.shape; wsh.shapeType = ParticleSystemShapeType.Cone; wsh.angle = 70f; wsh.radius = 0.8f * size;
        wsh.rotation = new Vector3(-90f, 0f, 0f);
        var wcol = wide.colorOverLifetime; wcol.enabled = true; wcol.color = Fade(Color.white, new Color(0.7f, 0.85f, 1f));
        wide.Play();

        // soft mist that hangs in the air for a moment
        var mist = NewSystem("Splash Mist", ps.transform, pos + Vector3.up * size, false);
        var mm = mist.main;
        mm.loop = false; mm.duration = 0.3f;
        mm.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        mm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        mm.startSize = new ParticleSystem.MinMaxCurve(2f * size, 4f * size);
        mm.startColor = new Color(1f, 1f, 1f, 0.25f);
        mm.gravityModifier = -0.02f;
        Burst(mist, (int)(12 * size));
        var msh = mist.shape; msh.shapeType = ParticleSystemShapeType.Sphere; msh.radius = 1.5f * size;
        var mcol = mist.colorOverLifetime; mcol.enabled = true; mcol.color = Fade(Color.white, Color.white);
        mist.Play();
        return ps;
    }

    /// <summary>One-shot explosion: fireball + smoke + sparks. Destroys itself.</summary>
    public static GameObject CreateExplosion(Vector3 pos, float size = 1f)
    {
        var root = new GameObject("Explosion");
        root.transform.position = pos;
        Object.Destroy(root, 6f);

        // fireball
        var fire = NewSystem("Fireball", root.transform, pos, true);
        var m = fire.main; m.loop = false; m.duration = 0.3f;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(4f * size, 12f * size);
        m.startSize = new ParticleSystem.MinMaxCurve(2f * size, 4.5f * size);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.1f), new Color(1f, 0.9f, 0.4f));
        Burst(fire, 80);
        var sh = fire.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.8f * size;
        var col = fire.colorOverLifetime; col.enabled = true; col.color = Fade(new Color(1f, 0.9f, 0.5f), new Color(0.6f, 0.05f, 0f));
        var sol = fire.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.5f, 1, 1.4f));
        fire.Play();

        // smoke
        var smoke = NewSystem("Smoke", root.transform, pos, false);
        m = smoke.main; m.loop = false; m.duration = 0.5f;
        m.startDelay = 0.1f;
        m.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * size, 5f * size);
        m.startSize = new ParticleSystem.MinMaxCurve(3f * size, 6f * size);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.15f, 0.15f, 0.15f, 0.8f), new Color(0.35f, 0.32f, 0.3f, 0.8f));
        m.gravityModifier = -0.15f;
        Burst(smoke, 40);
        sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 1.5f * size;
        sh.rotation = new Vector3(-90f, 0f, 0f);
        col = smoke.colorOverLifetime; col.enabled = true; col.color = Fade(new Color(0.3f, 0.3f, 0.3f), new Color(0.6f, 0.6f, 0.6f));
        smoke.Play();

        // sparks / debris
        var sparks = NewSystem("Sparks", root.transform, pos, true);
        m = sparks.main; m.loop = false; m.duration = 0.2f;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(10f * size, 25f * size);
        m.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.25f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.3f), Color.white);
        m.gravityModifier = 1f;
        Burst(sparks, 120);
        sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.5f;
        var r = sparks.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 1f;
        sparks.Play();

        // light flash
        var lightGo = new GameObject("Flash");
        lightGo.transform.SetParent(root.transform, false);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point; light.color = new Color(1f, 0.6f, 0.25f);
        light.range = 40f * size; light.intensity = 8f;
        lightGo.AddComponent<FadeLight>();
        return root;
    }
}
