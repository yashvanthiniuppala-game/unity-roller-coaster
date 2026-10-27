// RiderBoarding.cs — the riders WAIT on the station platform, WALK to the cart and CLIMB IN before the ride starts.
// After the ride they climb out, walk back to the platform, wave ... and board again for the next ride.
//   * the cart waits in the station until every rider is sitting, then it leaves by itself,
//   * uses Mixamo animations (walking + waving/idle) - the sitting animation you already have is used once seated.
//
// HOW TO USE (for EACH rider, e.g. Dora and the monkey):
//   1. Get two animations from mixamo.com for that character: "Walking" (tick IN PLACE!) and "Waving" (or "Standing Idle").
//      Download as FBX for Unity, drag into Unity, Rig tab = Humanoid > Apply, Animation tab = Loop Time > Apply.
//   2. Put the rider in its seat as you have it now (sitting). Select the rider > Add Component > Rider Boarding.
//   3. Drag the walking clip into "Walk Clip" and the waving clip into "Wait Clip" (the clips are inside the FBX files - the triangle icon).
//   4. Second rider: set "Order" = 1 so it gets in after the first one. Press Play.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[DefaultExecutionOrder(300)]          // runs after the ride cameras, so the boarding shot can take over
public class RiderBoarding : MonoBehaviour
{
    [Header("Animations (from Mixamo)")]
    [Tooltip("Walking animation - download it with 'In Place' ticked")] public AnimationClip walkClip;
    [Tooltip("Waving / standing idle while waiting on the platform")] public AnimationClip waitClip;
    [Tooltip("Speed of the walk animation (match the feet to the ground)")] public float walkAnimSpeed = 1f;
    [Tooltip("Sitting animation used in the cart (e.g. the clip inside 'Sitting Idle' / 'Sitting Clap'). Empty = the rider's own Animator Controller")]
    public AnimationClip sitClip;

    [Header("Where they wait")]
    [Tooltip("BEST: an empty object placed ON the station platform where the riders should stand. Leave empty to find the platform automatically")]
    public Transform waitSpot;
    [Tooltip("Find the platform side by itself (the side where the floor is closest to the cart)")] public bool autoSide = true;
    [Tooltip("Which side of the cart the platform is on: +1 = right, -1 = left (used when Auto Side is off)")] public float side = 1f;
    [Tooltip("How far from the cart they wait (metres)")] public float waitDistance = 4f;
    [Tooltip("0 = first to get in, 1 = second ...")] public int order = 0;
    [Tooltip("Platform height. Leave 'Auto' ticked to find the floor by itself")] public bool autoFloor = true;
    public float floorHeight = -0.55f;

    [Header("Camera")]
    [Tooltip("Show the boarding from the platform (only the first rider's setting is used)")] public bool boardingCamera = true;

    [Header("Timing")]
    public float waitOnPlatform = 2.5f;
    public float walkSpeed = 1.3f;
    [Tooltip("Seconds to climb into / out of the cart")] public float climbTime = 1.8f;
    [Tooltip("How high they step up over the side of the cart (0 = slide straight in)")] public float stepHeight = 0.3f;
    [Tooltip("Climb out after the ride and board again every ride")] public bool boardEveryRide = true;

    enum State { Waiting, WalkingIn, Climbing, Seated, ClimbingOut, WalkingOut }
    State state = State.Waiting;
    CoasterCart cart;
    Animator anim;
    PlayableGraph graph; AnimationClip playing; AnimationClipPlayable clipPlayable;
    Vector3 seatPos; Quaternion seatRot;
    Vector3 standPos, doorPos;
    float timer;
    static readonly List<RiderBoarding> all = new List<RiderBoarding>();
    static float dispatchTimer = -1f;

    void Awake()
    {
        cart = GetComponentInParent<CoasterCart>();
        if (cart == null) { Debug.LogWarning("[Rider Boarding] The rider must be inside the Coaster Cart (a child of it)."); enabled = false; return; }
        cart.waitInStation = 9999f;            // the cart now waits until everybody is in
        all.Add(this);
    }

    void OnDestroy() { all.Remove(this); if (graph.IsValid()) graph.Destroy(); }

    void Start()
    {
        if (cart == null) return;
        anim = GetComponentInChildren<Animator>();
        seatPos = transform.localPosition; seatRot = transform.localRotation;
        if (waitSpot != null)
        {
            // the riders stand exactly where you put the Wait Spot (on the platform)
            Vector3 w = cart.transform.InverseTransformPoint(waitSpot.position);
            side = Mathf.Sign(w.x - seatPos.x == 0f ? 1f : w.x - seatPos.x);
            doorPos = new Vector3(seatPos.x + side * 1.7f, w.y, seatPos.z);
            standPos = new Vector3(w.x, w.y, w.z - order * 1.3f);
            cart.RideFinished += OnRideFinished;
            GoWait();
            return;
        }
        if (autoSide)
        {
            platformOnly = true;
            bool okR = FindFloor(1f, out float yR), okL = FindFloor(-1f, out float yL);
            platformOnly = false;
            if (!okR && !okL) Debug.LogWarning("[Rider Boarding] Couldn't find the station platform (it may have no collider). Put an empty 'Wait Spot' on the platform and drag it into the Wait Spot slot.");
            // the platform is the side where the floor is HIGHER (closer to the cart); the other side is the ground below the track
            if (okR && okL) side = yR >= yL ? 1f : -1f;
            else if (okR) side = 1f; else if (okL) side = -1f;
        }
        float fy = floorHeight;
        if (autoFloor && FindFloor(side, out float found)) fy = found;
        doorPos = new Vector3(seatPos.x + side * 1.7f, fy, seatPos.z);
        standPos = new Vector3(side * waitDistance, fy, seatPos.z - 1.2f - order * 1.3f);
        cart.RideFinished += OnRideFinished;
        GoWait();
    }

    /// <summary>The FIRST floor below a point beside the cart (platform, or the ground if there is no platform), in the cart's space.</summary>
    bool platformOnly;
    bool FindFloor(float s, out float y)
    {
        y = floorHeight;
        Vector3 from = cart.transform.TransformPoint(new Vector3(s * waitDistance, 2.2f, seatPos.z));
        float best = float.MaxValue; bool found = false;
        foreach (var h in Physics.RaycastAll(from, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.GetComponentInParent<CoasterCart>() != null || h.collider.GetComponentInParent<CoasterTrainCar>() != null) continue;
            if (h.normal.y < 0.6f) continue;                     // walls / pillars are not floors
            if (platformOnly && h.collider is TerrainCollider) continue;
            if (h.distance < best) { best = h.distance; y = cart.transform.InverseTransformPoint(h.point).y; found = true; }
        }
        return found;
    }

    void OnRideFinished()
    {
        if (!boardEveryRide) { if (AllSeated()) dispatchTimer = 1.5f; return; }
        state = State.ClimbingOut; timer = 0f;
        Play(sitClip, 1f);
    }

    void GoWait()
    {
        state = State.Waiting; timer = waitOnPlatform + order * 1.6f;
        transform.localPosition = standPos;
        transform.localRotation = Quaternion.LookRotation(new Vector3(-side, 0f, 0.6f).normalized);   // looking at the cart
        Play(waitClip, 1f);
    }

    void Update()
    {
        if (cart == null) return;
        float dt = Time.deltaTime;
        bool inStation = cart.state == CoasterCart.RideState.WaitingInStation;

        switch (state)
        {
            case State.Waiting:
                if (!inStation) break;
                timer -= dt;
                if (timer <= 0f) { state = State.WalkingIn; Play(walkClip, walkAnimSpeed); }
                break;

            case State.WalkingIn:
                if (Walk(doorPos)) { state = State.Climbing; timer = 0f; Play(waitClip != null ? waitClip : walkClip, 1f); }
                break;

            case State.Climbing:
                timer += dt / Mathf.Max(0.5f, climbTime);
                // first half: step up onto the side of the cart (still standing), second half: sit down into the seat
                if (timer >= 0.45f && playing != sitClip) Play(sitClip, 1f);
                Climb(doorPos, seatPos, Quaternion.LookRotation(-side * Vector3.right), seatRot, timer);
                if (timer >= 1f)
                {
                    state = State.Seated;
                    transform.localPosition = seatPos; transform.localRotation = seatRot;
                    Play(sitClip, 1f);
                    if (AllSeated()) dispatchTimer = 1.2f;
                }
                break;

            case State.ClimbingOut:
                timer += dt / Mathf.Max(0.5f, climbTime);
                // stand up out of the seat, then step down onto the platform
                if (timer >= 0.45f && playing != waitClip) Play(waitClip != null ? waitClip : walkClip, 1f);
                Climb(seatPos, doorPos, seatRot, Quaternion.LookRotation(side * Vector3.right), timer);
                if (timer >= 1f) { state = State.WalkingOut; Play(walkClip, walkAnimSpeed); }
                break;

            case State.WalkingOut:
                if (Walk(standPos)) GoWait();
                break;
        }

        // the first rider in the list starts the ride when everybody is sitting
        if (all.Count > 0 && all[0] == this && dispatchTimer >= 0f && inStation)
        {
            dispatchTimer -= dt;
            if (dispatchTimer < 0f && AllSeated() && !FireworksFinale.Busy) cart.Dispatch();   // wait for the fireworks to finish
        }
    }

    static bool AllSeated()
    {
        foreach (var r in all) if (r != null && r.isActiveAndEnabled && r.state != State.Seated) return false;
        return true;
    }

    /// <summary>Walks (in the cart's space) towards 'to'. Returns true when there.</summary>
    bool Walk(Vector3 to)
    {
        Vector3 p = transform.localPosition;
        Vector3 d = to - p; d.y = 0f;
        if (d.magnitude < 0.05f) { transform.localPosition = new Vector3(to.x, to.y, to.z); return true; }
        transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.LookRotation(d.normalized), Time.deltaTime * 8f);
        Vector3 step = Vector3.MoveTowards(new Vector3(p.x, to.y, p.z), to, walkSpeed * Time.deltaTime);
        transform.localPosition = step;
        return false;
    }

    /// <summary>Calm climb: step up to the edge of the cart, then lower into the seat (or the other way round). No jumping.</summary>
    void Climb(Vector3 from, Vector3 to, Quaternion r0, Quaternion r1, float t)
    {
        t = Mathf.Clamp01(t);
        Vector3 edge = new Vector3(Mathf.Lerp(from.x, to.x, 0.5f), Mathf.Max(from.y, to.y) + stepHeight, Mathf.Lerp(from.z, to.z, 0.5f));
        Vector3 pos;
        if (t < 0.45f) pos = Vector3.Lerp(from, edge, Mathf.SmoothStep(0f, 1f, t / 0.45f));
        else pos = Vector3.Lerp(edge, to, Mathf.SmoothStep(0f, 1f, (t - 0.45f) / 0.55f));
        transform.localPosition = pos;
        transform.localRotation = Quaternion.Slerp(r0, r1, Mathf.SmoothStep(0f, 1f, t));
    }

    /// <summary>Plays a clip on the rider (null = back to its own Animator, i.e. the sitting animation).</summary>
    void Play(AnimationClip clip, float speed = 1f)
    {
        if (anim == null) return;
        if (clip == playing && graph.IsValid()) return;
        if (graph.IsValid()) graph.Destroy();
        playing = clip;
        if (clip == null) return;
        anim.applyRootMotion = false;
        clipPlayable = AnimationPlayableUtilities.PlayClip(anim, clip, out graph);
        clipPlayable.SetSpeed(speed);
    }

    void LateUpdate()
    {
        if (cart != null && all.Count > 0 && all[0] == this && boardingCamera) CameraShot();
        // loop the walk / wait clip
        if (playing != null && graph.IsValid())
        {
            if (clipPlayable.IsValid() && clipPlayable.GetTime() > playing.length) clipPlayable.SetTime(clipPlayable.GetTime() % Mathf.Max(0.01f, playing.length));
        }
    }

    void OnDrawGizmosSelected()
    {
        if (waitSpot != null) { Gizmos.color = Color.green; Gizmos.DrawWireSphere(waitSpot.position, 0.5f); Gizmos.DrawLine(waitSpot.position, transform.position); }
    }

    // ------------------------------------------------------------------ the boarding shot
    Vector3 camPos; Quaternion camRot; bool camOn;
    void CameraShot()
    {
        var cam = Camera.main;
        if (cam == null) return;
        if (!Busy || cart.state != CoasterCart.RideState.WaitingInStation) { camOn = false; return; }
        Transform c = cart.transform;
        // from the platform side, a bit behind and above: the riders on the platform, their walk and the cart are all in the picture
        Vector3 riders = c.TransformPoint(new Vector3(side * waitDistance * 0.6f, 1f, seatPos.z - 1f));
        Vector3 pos = c.TransformPoint(new Vector3(side * (waitDistance + 7f), 3.5f, seatPos.z - 7f));
        Quaternion rot = Quaternion.LookRotation(riders - pos, Vector3.up);
        if (!camOn) { camPos = pos; camRot = rot; camOn = true; }
        float k = 1f - Mathf.Exp(-Time.deltaTime * 6f);
        camPos = Vector3.Lerp(camPos, pos, k); camRot = Quaternion.Slerp(camRot, rot, k);
        cam.transform.SetPositionAndRotation(camPos, camRot);
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 55f, k);
    }

    /// <summary>True while any rider is on the platform or getting in/out (the Ride Director uses this for a station shot).</summary>
    public static bool Busy { get { foreach (var r in all) if (r != null && r.isActiveAndEnabled && r.state != State.Seated) return true; return false; } }
    public static float Side { get { return all.Count > 0 && all[0] != null ? Mathf.Sign(all[0].side) : 1f; } }
}
