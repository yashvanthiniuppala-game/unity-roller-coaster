// RiderScreams.cs — the riders SCREAM (and cheer) at the exciting moments of the ride:
//   * at the top of a hill when the cart tips over into a big drop,
//   * when they float out of their seats (airtime, low G-force),
//   * upside down in the loop / twist,
//   * a happy cheer when the ride is over.
// Each scream is a little different (random clip + random pitch), and it never screams non-stop.
//
// HOW TO USE: select "Coaster Cart" > Add Component > Rider Screams.
// Drag 2-4 scream sounds into "Screams" and (optional) a cheer / "woohoo" into "Cheers". Press Play.
// Free sounds: pixabay.com/sound-effects (search "roller coaster scream" / "woohoo") or freesound.org (CC0).
using UnityEngine;

[RequireComponent(typeof(CoasterCart))]
public class RiderScreams : MonoBehaviour
{
    [Tooltip("Scream sounds (one is picked at random each time)")] public AudioClip[] screams;
    [Tooltip("Optional: cheering / 'woohoo' at the end of the ride")] public AudioClip[] cheers;
    [Range(0f, 1f)] public float volume = 1f;

    [Header("When to scream")]
    [Tooltip("Scream on drops at least this steep (0.3 = gentle, 0.6 = steep)")] [Range(0.1f, 0.9f)] public float steepness = 0.35f;
    [Tooltip("Minimum speed for a scream (m/s)")] public float minSpeed = 6f;
    [Tooltip("Scream when the G-force falls below this (floating in the seat)")] public float airtimeG = 0.45f;
    public bool screamUpsideDown = true;
    [Tooltip("Seconds between screams")] public float cooldown = 5f;

    CoasterCart cart;
    AudioSource src;
    float wait;
    bool wasDropping, wasUpsideDown, wasAir;

    void Start()
    {
        cart = GetComponent<CoasterCart>();
        src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false; src.spatialBlend = 0.6f; src.minDistance = 8f; src.maxDistance = 200f;
        cart.RideFinished += () => { if (cheers != null && cheers.Length > 0) Say(cheers); };
    }

    void Update()
    {
        if (cart.state != CoasterCart.RideState.Riding) { wasDropping = wasUpsideDown = wasAir = false; return; }
        wait -= Time.deltaTime;

        Vector3 fwd = transform.forward, up = transform.up;
        bool dropping = !cart.onLift && !cart.braking && fwd.y < -steepness && cart.speed > minSpeed;
        bool upsideDown = up.y < -0.2f;
        bool air = !cart.onLift && cart.gForce < airtimeG && cart.speed > minSpeed;

        // scream at the START of each moment (not all the time)
        if ((dropping && !wasDropping) || (screamUpsideDown && upsideDown && !wasUpsideDown) || (air && !wasAir))
            if (wait <= 0f) Say(screams);

        wasDropping = dropping; wasUpsideDown = upsideDown; wasAir = air;
    }

    void Say(AudioClip[] list)
    {
        if (list == null || list.Length == 0 || src == null) return;
        var clip = list[Random.Range(0, list.Length)];
        if (clip == null) return;
        src.pitch = Random.Range(0.92f, 1.1f);
        src.PlayOneShot(clip, volume);
        wait = Mathf.Max(cooldown, clip.length * 0.8f);
    }
}
