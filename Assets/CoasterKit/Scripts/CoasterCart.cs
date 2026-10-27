// CoasterCart.cs  —  CPS 499 HW#2 Roller Coaster Kit
// The cart is moved by PHYSICS along the spline:
//   * a chain lift pulls it up the first hill at a constant speed,
//   * after the top, ONLY GRAVITY (Physics.gravity) speeds it up / slows it down,
//   * rolling friction and air drag take energy away (like a real coaster),
//   * brakes stop it in the station, it waits, then it goes again.
// The Rigidbody is kinematic so it can never "fall off" the track, but it still
// triggers OnTriggerEnter on splash / fire / explosion zones and can push things.
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CoasterCart : MonoBehaviour
{
    public enum RideState { WaitingInStation, Riding }

    [Header("Track")]
    public CoasterTrack track;
    [Tooltip("How high the cart's pivot sits above the rails (metres)")] public float heightAboveTrack = 0.55f;

    [Header("Gravity & energy loss")]
    [Tooltip("Rolling friction coefficient (real coasters ≈ 0.01–0.03)")] public float rollingFriction = 0.015f;
    [Tooltip("Air drag: deceleration = airDrag × speed²")] public float airDrag = 0.0006f;
    [Tooltip("If the cart would stall it is kept at this speed (and a warning is printed)")] public float minSpeed = 1.5f;

    [Header("Station & lift hill")]
    [Tooltip("Speed the station tyres push the cart to the lift (m/s)")] public float dispatchSpeed = 3f;
    [Tooltip("Chain lift speed (m/s). Slower lift = longer ride.")] public float liftSpeed = 4f;
    [Tooltip("Brake strength (m/s²)")] public float brakeDeceleration = 5f;
    [Tooltip("Seconds the cart waits in the station before each ride")] public float waitInStation = 3f;
    public bool repeatRide = true;

    [Header("Live values (read only)")]
    public RideState state = RideState.WaitingInStation;
    public float distance;       // metres from the station
    public float speed;          // m/s along the track
    public float rideTime;       // seconds of the current ride
    public float lastRideTime;   // seconds of the last full ride
    public int ridesCompleted;
    public bool onLift, braking;
    public float gForce = 1f;    // vertical G felt by riders

    public System.Action RideStarted, RideFinished;

    Rigidbody rb;
    float waitTimer;
    bool stallWarned;
    Vector3 lastVel;

    public float Height => transform.position.y;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        if (GetComponent<Collider>() == null)
        {
            var box = gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(1.6f, 1.2f, 2.6f);
            box.center = new Vector3(0, 0.4f, 0);
        }
    }

    void Start()
    {
        if (track == null) track = FindAnyObjectByType<CoasterTrack>();
        if (track == null) { Debug.LogError("CoasterCart: drag your track (the object with CoasterTrack) into the 'Track' slot."); enabled = false; return; }
        track.Build();
        Debug.Log($"Coaster track length: {track.Length:F0} m  (lift {track.LiftStart:F0}–{track.LiftEnd:F0} m, brakes from {track.BrakeStart:F0} m)");
        distance = 0f; speed = 0f;
        waitTimer = waitInStation;
        PlaceOnTrack(true);
    }

    void FixedUpdate()
    {
        if (track == null || !track.IsBuilt) return;
        float dt = Time.fixedDeltaTime;

        if (state == RideState.WaitingInStation)
        {
            speed = 0f;
            waitTimer -= dt;
            if (waitTimer <= 0f)
            {
                state = RideState.Riding;
                rideTime = 0f; stallWarned = false;
                RideStarted?.Invoke();
            }
            PlaceOnTrack(false);
            return;
        }

        rideTime += dt;
        track.Sample(distance, out _, out Vector3 fwd, out Vector3 up);

        float liftStart = track.LiftStart, liftEnd = track.LiftEnd, brakeStart = track.BrakeStart;
        onLift = distance >= liftStart && distance < liftEnd;
        braking = distance >= brakeStart;

        // 1) GRAVITY: the part of Physics.gravity that points along the track
        float accel = Vector3.Dot(Physics.gravity, fwd);
        // 2) FRICTION: proportional to the normal force (use |g| as an approximation)
        accel -= rollingFriction * Physics.gravity.magnitude * Mathf.Sign(speed);
        // 3) AIR DRAG
        accel -= airDrag * speed * Mathf.Abs(speed);
        speed += accel * dt;

        if (distance < liftStart) speed = Mathf.Max(speed, dispatchSpeed);   // station tyres
        if (onLift) speed = Mathf.Max(speed, liftSpeed);                     // chain lift (+ anti-rollback)

        if (braking)
        {
            float remaining = Mathf.Max(0f, track.Length - distance);
            float allowed = Mathf.Sqrt(2f * brakeDeceleration * remaining) + 0.4f;
            // squeeze the speed down smoothly (max ~3x brake strength) so riders aren't jolted
            speed = Mathf.Max(Mathf.Min(speed, allowed), speed - 3f * brakeDeceleration * dt);
            speed = Mathf.Max(speed, 0.6f);
        }
        else if (!onLift && speed < minSpeed)
        {
            if (!stallWarned) { Debug.LogWarning($"Cart nearly stalled at {distance:F0} m. Make the first hill higher, the next hill lower, or reduce friction."); stallWarned = true; }
            speed = minSpeed;
        }

        distance += speed * dt;

        if (distance >= track.Length - 0.05f)   // back in the station
        {
            distance = 0f; speed = 0f;
            lastRideTime = rideTime; ridesCompleted++;
            Debug.Log($"Ride {ridesCompleted} finished in {lastRideTime:F1} s (homework needs 30–60 s)");
            RideFinished?.Invoke();
            state = RideState.WaitingInStation;
            waitTimer = repeatRide ? waitInStation : float.PositiveInfinity;
        }

        PlaceOnTrack(false);
    }

    void PlaceOnTrack(bool teleport)
    {
        track.Sample(distance, out Vector3 pos, out Vector3 fwd, out Vector3 up);
        Vector3 target = pos + up * heightAboveTrack;
        Quaternion rot = Quaternion.LookRotation(fwd, up);

        // vertical G force felt by the rider (1 = normal, >1 pressed into seat, <0 air-time)
        Vector3 vel = fwd * speed;
        Vector3 acc = (vel - lastVel) / Mathf.Max(Time.fixedDeltaTime, 1e-4f);
        lastVel = vel;
        float g = Physics.gravity.magnitude;
        gForce = Mathf.Lerp(gForce, Vector3.Dot(acc - Physics.gravity, up) / g, 0.2f);

        if (teleport) { transform.SetPositionAndRotation(target, rot); rb.position = target; rb.rotation = rot; }
        else { rb.MovePosition(target); rb.MoveRotation(rot); }
    }

    /// <summary>Puts the cart exactly on the rails at the station (knot 0), facing along the track.
    /// Works in Edit mode too - use it after you move knots or add a loop.</summary>
    [ContextMenu("Snap Cart Onto Track")]
    public void SnapToTrack()
    {
        if (track == null) track = FindAnyObjectByType<CoasterTrack>();
        if (track == null) { Debug.LogWarning("CoasterCart: no track found."); return; }
        track.Build();
        if (!track.IsBuilt) return;
        track.Sample(0f, out Vector3 pos, out Vector3 fwd, out Vector3 up);
        transform.SetPositionAndRotation(pos + up * heightAboveTrack, Quaternion.LookRotation(fwd, up));
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb != null && Application.isPlaying) { rb.position = transform.position; rb.rotation = transform.rotation; }
    }

    /// <summary>Starts the ride immediately (e.g. from a button).</summary>
    public void Dispatch() { if (state == RideState.WaitingInStation) waitTimer = 0f; }
}
