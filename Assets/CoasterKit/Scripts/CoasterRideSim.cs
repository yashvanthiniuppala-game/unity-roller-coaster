// CoasterRideSim.cs — runs the SAME physics as CoasterCart, but instantly (no Play needed).
// Used by the one-click builder to size the track so the ride takes 30–60 s and never stalls.
using UnityEngine;

public static class CoasterRideSim
{
    public struct Result { public float rideTime, topSpeed, stallAt, watchSpeed; public bool stalled; }

    public static Result Run(CoasterTrack track, float liftSpeed, float rollingFriction, float airDrag,
                             float dispatchSpeed = 3f, float brakeDecel = 5f, float minSpeed = 1.5f, float watchDistance = -1f)
    {
        var r = new Result { stallAt = -1f };
        if (track == null) return r;
        track.Build();
        if (!track.IsBuilt) return r;
        float L = track.Length, ls = track.LiftStart, le = track.LiftEnd, bs = track.BrakeStart;
        float g = Physics.gravity.magnitude;
        float d = 0f, v = 0f, t = 0f, dt = 0.02f;
        while (d < L - 0.05f && t < 600f)
        {
            track.Sample(d, out _, out Vector3 f, out _);
            float a = Vector3.Dot(Physics.gravity, f) - rollingFriction * g - airDrag * v * Mathf.Abs(v);
            v += a * dt;
            bool lift = d >= ls && d < le;
            if (d < ls) v = Mathf.Max(v, dispatchSpeed);
            if (lift) v = Mathf.Max(v, liftSpeed);
            if (d >= bs)
            {
                float allowed = Mathf.Sqrt(2f * brakeDecel * Mathf.Max(0f, L - d)) + 0.4f;
                v = Mathf.Max(Mathf.Min(v, allowed), v - 3f * brakeDecel * dt);
                v = Mathf.Max(v, 0.6f);
            }
            else if (!lift && v < minSpeed)
            {
                if (!r.stalled) { r.stalled = true; r.stallAt = d; }
                v = minSpeed;
            }
            if (watchDistance >= 0f && d <= watchDistance && d + v * dt > watchDistance) r.watchSpeed = v;   // e.g. top of a loop
            d += v * dt; t += dt;
            r.topSpeed = Mathf.Max(r.topSpeed, v);
        }
        r.rideTime = t;
        return r;
    }
}
