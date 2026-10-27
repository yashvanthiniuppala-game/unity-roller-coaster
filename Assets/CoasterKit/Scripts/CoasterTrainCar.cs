// CoasterTrainCar.cs — optional: extra cars that follow the lead cart (makes a train).
// Duplicate your cart, REMOVE CoasterCart + CartAudio from the copy, add this script,
// drag the lead cart into 'Leader' and set the gap (car 2 = 3 m, car 3 = 6 m ...).
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CoasterTrainCar : MonoBehaviour
{
    public CoasterCart leader;
    [Tooltip("Metres behind the lead cart")] public float gap = 3f;

    Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true; rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void FixedUpdate()
    {
        if (leader == null || leader.track == null || !leader.track.IsBuilt) return;
        var track = leader.track;
        leader.track.Sample(track.Wrap(leader.distance - gap), out Vector3 pos, out Vector3 fwd, out Vector3 up);
        rb.MovePosition(pos + up * leader.heightAboveTrack);
        rb.MoveRotation(Quaternion.LookRotation(fwd, up));
    }
}
