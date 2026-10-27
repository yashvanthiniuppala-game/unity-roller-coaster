// RideCamera.cs  —  CPS 499 HW#2 Roller Coaster Kit
// Put this on the Main Camera. Keys (in Play mode):
//   C = next camera   1 = rider view   2 = chase view   3 = trackside (cinematic)   4 = overview
using UnityEngine;

public class RideCamera : MonoBehaviour
{
    public enum Mode { Onboard, Chase, Trackside, Overview }

    public CoasterCart cart;
    public Mode mode = Mode.Onboard;

    [Header("Onboard (rider's eyes)")]
    [Tooltip("Rider view position in the cart (x right, y up, z forward). Default = just behind and above the rider.")]
    public Vector3 onboardOffset = new Vector3(0f, 2.0f, -1.5f);
    public float onboardLookDown = 10f;

    [Header("Chase")]
    public float chaseDistance = 9f;
    public float chaseHeight = 3.5f;

    [Header("Trackside")]
    [Tooltip("Optional camera spots (empty GameObjects). Empty list = automatic cuts.")]
    public Transform[] tracksidePoints;
    public float autoCutEvery = 4f;

    [Header("Overview")]
    public float overviewDistance = 45f;
    public float overviewHeight = 30f;

    [Header("Cycle automatically (nice for the video)")]
    public bool autoCycle = false;
    public float autoCycleEvery = 8f;

    static RideCamera instance;
    float shakeAmount, shakeTime;
    Vector3 vel;
    Vector3 autoPoint; float autoTimer, cycleTimer, orbitAngle;
    Camera cam;
    float baseFov;

    public static void Shake(float amount, float duration)
    {
        if (instance == null) return;
        instance.shakeAmount = Mathf.Max(instance.shakeAmount, amount);
        instance.shakeTime = Mathf.Max(instance.shakeTime, duration);
    }

    void Awake() { instance = this; cam = GetComponent<Camera>(); if (cam != null) baseFov = cam.fieldOfView; }

    void Start()
    {
        if (cart == null) cart = FindAnyObjectByType<CoasterCart>();
        EnsureAudio();
    }

    /// <summary>Sound needs exactly one enabled AudioListener - put it on this camera.</summary>
    void EnsureAudio()
    {
        var mine = GetComponent<AudioListener>();
        if (mine == null) mine = gameObject.AddComponent<AudioListener>();
        mine.enabled = true;
        foreach (var l in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
            if (l != mine) l.enabled = false;
        AudioListener.pause = false;
        AudioListener.volume = 1f;
#if UNITY_EDITOR
        if (UnityEditor.EditorUtility.audioMasterMute)
            Debug.LogWarning("[Coaster Kit] Game view audio is MUTED. Click the speaker icon at the top-right of the Game tab to hear the ride.");
#endif
    }

    void Update()
    {
        if (CoasterInput.Pressed('c')) mode = (Mode)(((int)mode + 1) % 4);
        if (CoasterInput.Pressed('1')) mode = Mode.Onboard;
        if (CoasterInput.Pressed('2')) mode = Mode.Chase;
        if (CoasterInput.Pressed('3')) mode = Mode.Trackside;
        if (CoasterInput.Pressed('4')) mode = Mode.Overview;
        if (CoasterInput.Pressed(' ') && cart != null) cart.Dispatch();

        if (autoCycle)
        {
            cycleTimer += Time.deltaTime;
            if (cycleTimer > autoCycleEvery) { cycleTimer = 0f; mode = (Mode)(((int)mode + 1) % 4); autoTimer = 0f; }
        }
    }

    void LateUpdate()
    {
        if (cart == null) return;
        Transform c = cart.transform;
        float fov = baseFov;

        switch (mode)
        {
            case Mode.Onboard:
                transform.position = c.TransformPoint(onboardOffset);
                transform.rotation = c.rotation * Quaternion.Euler(onboardLookDown, 0f, 0f);
                fov = baseFov + Mathf.Clamp(cart.speed * 0.6f, 0f, 15f);   // speed feeling
                break;

            case Mode.Chase:
                Vector3 want = c.position - c.forward * chaseDistance + Vector3.up * chaseHeight;
                transform.position = Vector3.SmoothDamp(transform.position, want, ref vel, 0.15f);
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(c.position + c.forward * 4f - transform.position, Vector3.up), 10f * Time.deltaTime);
                break;

            case Mode.Trackside:
                Vector3 spot;
                if (tracksidePoints != null && tracksidePoints.Length > 0)
                {
                    Transform best = tracksidePoints[0];
                    foreach (var p in tracksidePoints)
                        if (p != null && (p.position - c.position).sqrMagnitude < (best.position - c.position).sqrMagnitude) best = p;
                    spot = best.position;
                }
                else
                {
                    autoTimer -= Time.deltaTime;
                    if (autoTimer <= 0f)
                    {
                        autoTimer = autoCutEvery;
                        float side = Random.value > 0.5f ? 1f : -1f;
                        autoPoint = c.position + c.forward * Mathf.Max(20f, cart.speed * 2f) + c.right * 12f * side;
                        autoPoint.y = Mathf.Max(autoPoint.y, c.position.y) + 3f;
                    }
                    spot = autoPoint;
                }
                transform.position = spot;
                transform.rotation = Quaternion.LookRotation(c.position - spot, Vector3.up);
                fov = Mathf.Clamp(Mathf.Lerp(60f, 25f, Vector3.Distance(spot, c.position) / 80f), 20f, 70f);
                break;

            case Mode.Overview:
                orbitAngle += 10f * Time.deltaTime;
                Vector3 o = Quaternion.Euler(0f, orbitAngle, 0f) * new Vector3(0f, 0f, -overviewDistance);
                Vector3 target = c.position + o + Vector3.up * overviewHeight;
                transform.position = Vector3.SmoothDamp(transform.position, target, ref vel, 0.5f);
                transform.rotation = Quaternion.LookRotation(c.position - transform.position, Vector3.up);
                break;
        }

        if (cam != null) cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, 5f * Time.deltaTime);

        if (shakeTime > 0f)
        {
            shakeTime -= Time.deltaTime;
            transform.position += Random.insideUnitSphere * shakeAmount * Mathf.Clamp01(shakeTime * 2f);
            if (shakeTime <= 0f) shakeAmount = 0f;
        }
    }
}
