// LensDrops.cs — realistic WATER DROPS on the camera lens.
//   * every drop BENDS the picture behind it like a real drop of water (not a flat 2D picture),
//     with a dark outline, a bright shine and a slightly blurry inside, so you can see it on sand and sky,
//   * big drops, small drops and a fine spray; some drops slowly SLIDE DOWN and stretch,
//   * the wind blows them sideways when the cart is fast; they fade away after a few seconds.
// The oasis waterfall, the ponds (Splash Zones) and the Water Dragon call it automatically.
//
// HOW TO USE: nothing to do - it adds itself to the Main Camera when you press Play.
// TEST: press  L  while playing to throw water on the camera any time.
// If the drops look GREY / BLACK: tick "Simple Drops" on Main Camera > Lens Drops (or turn on Opaque Texture).
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(500)]
public class LensDrops : MonoBehaviour
{
    [Tooltip("The LensDrop shader (found automatically)")] public Shader shader;
    [Tooltip("How much each drop bends the picture")] [Range(0f, 0.25f)] public float refraction = 0.1f;
    [Tooltip("How easy the drops are to see (outline + shine)")] [Range(0.3f, 2f)] public float visibility = 1.2f;
    [Tooltip("Bigger / smaller drops")] [Range(0.5f, 2f)] public float dropSize = 1.2f;
    [Tooltip("Seconds a drop stays on the lens")] public Vector2 life = new Vector2(2.2f, 4.5f);
    [Tooltip("Tick this if the drops look grey/black (your camera has no Opaque Texture). Draws milky drops instead.")]
    public bool simpleDrops = false;
    [Tooltip("Press L in Play mode to test the drops")] public bool testKey = true;

    class Drop { public Transform t; public Material m; public Vector2 pos; public float size, age, life, slide, stretch; }
    readonly List<Drop> drops = new List<Drop>();
    readonly Stack<Drop> pool = new Stack<Drop>();
    Camera cam;
    Mesh quad;
    int layer;
    static LensDrops instance;
    CoasterCart cart;

    // add itself to the camera as soon as Play starts (so the L test key works right away)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAdd() { var c = FindCamera(); if (c != null && c.GetComponent<LensDrops>() == null) c.gameObject.AddComponent<LensDrops>(); }

    static Camera FindCamera()
    {
        if (Camera.main != null) return Camera.main;
        var rc = FindAnyObjectByType<RideCamera>();
        if (rc != null && rc.GetComponent<Camera>() != null) return rc.GetComponent<Camera>();
        return FindAnyObjectByType<Camera>();
    }

    /// <summary>The camera that shows the ride right now (null if none).</summary>
    public static Camera Current => instance != null && instance.cam != null ? instance.cam : FindCamera();

    /// <summary>Splashes 'amount' drops onto the camera (adds the component if needed).</summary>
    public static void Splash(int amount, float bigness = 1f)
    {
        var cam = FindCamera();
        if (cam == null) { Debug.LogWarning("[Lens Drops] No camera found (is your camera tagged 'MainCamera'?)."); return; }
        if (instance == null || instance.cam != cam)
        {
            instance = cam.GetComponent<LensDrops>();
            if (instance == null) instance = cam.gameObject.AddComponent<LensDrops>();
        }
        instance.Add(amount, bigness);
    }

    /// <summary>Splash only if the camera is within 'maxDistance' of the water (no drops on a far-away drone shot).</summary>
    public static void SplashIfClose(Vector3 water, float maxDistance, int amount, float bigness = 1f)
    {
        var cam = FindCamera();
        if (cam != null && Vector3.Distance(cam.transform.position, water) <= maxDistance) Splash(amount, bigness);
    }

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (instance == null) instance = this;
        if (shader == null) shader = Shader.Find("Coaster/LensDrop");
        if (cam != null)
        {
            // the drops need a copy of the picture to bend ("Opaque Texture") - switch it on for this camera
            // (done by name, so this script works even if your scripts can't see the URP package directly)
            bool ok = false;
            var data = cam.GetComponent("UniversalAdditionalCameraData");
            var prop = data != null ? data.GetType().GetProperty("requiresColorTexture") : null;
            if (prop != null && prop.CanWrite) { prop.SetValue(data, true); ok = true; }
            if (!ok && !simpleDrops)
                Debug.Log("[Lens Drops] Could not switch on 'Opaque Texture' for the camera. If the drops look grey, tick 'Simple Drops' on Main Camera > Lens Drops.");
            // put the drops on a layer this camera really draws
            layer = gameObject.layer;
            if ((cam.cullingMask & (1 << layer)) == 0)
                for (int i = 0; i < 32; i++) if ((cam.cullingMask & (1 << i)) != 0) { layer = i; break; }
        }
        quad = MakeQuad();
        cart = FindAnyObjectByType<CoasterCart>();
    }

    void Update()
    {
        if (testKey && TestKey()) { Add(30, 1.1f); Debug.Log("[Lens Drops] Test splash (L key)."); }
    }

    public void Add(int amount, float bigness)
    {
        if (cam == null) return;
        if (shader == null) { Debug.LogWarning("[Lens Drops] LensDrop shader not found - put LensDrop.shader (from the zip) in your project."); return; }
        if (!shader.isSupported) Debug.LogWarning("[Lens Drops] The LensDrop shader has an error - click LensDrop.shader in the Project panel and read the Inspector.");
        for (int i = 0; i < amount; i++)
        {
            // a mix of a few big drops and many small ones
            float r = Random.value;
            float size = (r < 0.18f ? Random.Range(0.08f, 0.14f) : r < 0.6f ? Random.Range(0.04f, 0.07f) : Random.Range(0.015f, 0.03f)) * bigness * dropSize;
            var d = pool.Count > 0 ? pool.Pop() : NewDrop();
            d.t.gameObject.SetActive(true);
            d.pos = new Vector2(Random.Range(0.03f, 0.97f), Random.Range(0.08f, 0.97f));
            d.size = size; d.age = 0f; d.life = Random.Range(life.x, life.y) * (0.7f + size * 5f);
            d.slide = size > 0.04f && Random.value < 0.6f ? Random.Range(0.02f, 0.08f) : 0f;   // big drops slide down
            d.stretch = 1f;
            d.m.SetFloat("_Refraction", refraction * (0.5f + size * 6f));
            d.m.SetFloat("_Visibility", visibility);
            d.m.SetFloat("_SceneOK", simpleDrops ? 0f : 1f);
            drops.Add(d);
        }
    }

    Drop NewDrop()
    {
        var go = new GameObject("Lens Drop");
        go.layer = layer;
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        var m = new Material(shader) { renderQueue = 3900 };      // drawn after everything else in the world
        mr.sharedMaterial = m;
        return new Drop { t = go.transform, m = m };
    }

    void LateUpdate()
    {
        if (cam == null || drops.Count == 0) return;
        float dt = Time.deltaTime;
        float wind = cart != null && cart.state == CoasterCart.RideState.Riding ? Mathf.Clamp01(cart.speed / 30f) : 0f;
        float z = cam.nearClipPlane + 0.02f;
        float h = 2f * z * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), w = h * cam.aspect;

        for (int i = drops.Count - 1; i >= 0; i--)
        {
            var d = drops[i];
            d.age += dt;
            // sliding drops speed up and stretch; the wind pushes everything sideways a little
            if (d.slide > 0f) { d.pos.y -= d.slide * dt * (1f + d.age); d.stretch = Mathf.Min(1.8f, d.stretch + dt * 0.4f); }
            d.pos.x += wind * dt * 0.05f * (d.pos.x - 0.5f) * 4f;                        // blown towards the edges
            float fadeIn = Mathf.Clamp01(d.age / 0.12f), fadeOut = Mathf.Clamp01((d.life - d.age) / 0.8f);
            d.m.SetFloat("_Alpha", fadeIn * fadeOut);
            d.m.SetFloat("_Stretch", d.stretch);
            float s = d.size * h * (0.85f + 0.15f * fadeIn);
            d.t.localPosition = new Vector3((d.pos.x - 0.5f) * w, (d.pos.y - 0.5f) * h, z);
            d.t.localRotation = Quaternion.identity;
            d.t.localScale = new Vector3(s, s * d.stretch, 1f);
            if (d.age >= d.life || d.pos.y < -0.1f)
            {
                d.t.gameObject.SetActive(false);
                drops.RemoveAt(i);
                pool.Push(d);
            }
        }
    }

    static bool TestKey()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        return kb != null && kb.lKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.L);
#else
        return false;
#endif
    }

    static Mesh MakeQuad()
    {
        var m = new Mesh { name = "Lens Drop Quad" };
        m.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
        m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
        m.SetTriangles(new List<int> { 0, 2, 1, 0, 3, 2 }, 0);
        m.RecalculateBounds();
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);   // never culled
        return m;
    }
}
