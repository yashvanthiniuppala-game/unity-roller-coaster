// CartAudio.cs  —  put on the cart. Sounds change with the cart's speed.
using UnityEngine;

[RequireComponent(typeof(CoasterCart))]
public class CartAudio : MonoBehaviour
{
    [Tooltip("Looping wheel rumble")] public AudioClip rollingLoop;
    [Tooltip("Looping wind noise (louder when fast)")] public AudioClip windLoop;
    [Tooltip("Looping click-clack of the chain lift")] public AudioClip chainLoop;
    [Tooltip("Played when the ride starts")] public AudioClip stationBell;
    [Tooltip("Played at the top of the lift hill (riders scream!)")] public AudioClip dropSound;
    [Range(0, 1)] public float masterVolume = 1f;
    public float maxSpeedForSound = 30f;

    CoasterCart cart;
    AudioSource rolling, wind, chain;
    bool wasOnLift;

    void Start()
    {
        cart = GetComponent<CoasterCart>();
        rolling = MakeLoop(rollingLoop);
        wind = MakeLoop(windLoop);
        chain = MakeLoop(chainLoop);
        cart.RideStarted += () => OneShot(stationBell, 0.8f);
    }

    AudioSource MakeLoop(AudioClip clip)
    {
        if (clip == null) return null;
        var a = gameObject.AddComponent<AudioSource>();
        a.clip = clip; a.loop = true; a.volume = 0f; a.spatialBlend = 0.3f; a.playOnAwake = false;
        a.Play();
        return a;
    }

    void OneShot(AudioClip clip, float vol)
    {
        if (clip == null) return;
        var a = gameObject.AddComponent<AudioSource>();
        a.spatialBlend = 0f; a.PlayOneShot(clip, vol * masterVolume);
        Destroy(a, clip.length + 0.1f);
    }

    void Update()
    {
        float s = Mathf.Clamp01(Mathf.Abs(cart.speed) / maxSpeedForSound);
        bool moving = cart.state == CoasterCart.RideState.Riding;
        if (rolling != null) { rolling.volume = moving ? Mathf.Lerp(0.15f, 0.9f, s) * masterVolume : 0f; rolling.pitch = Mathf.Lerp(0.7f, 1.6f, s); }
        if (wind != null) { wind.volume = moving ? s * s * masterVolume : 0f; wind.pitch = Mathf.Lerp(0.8f, 1.3f, s); }
        if (chain != null) chain.volume = cart.onLift && moving ? 0.8f * masterVolume : 0f;

        if (wasOnLift && !cart.onLift && moving) OneShot(dropSound, 1f);
        wasOnLift = cart.onLift;
    }
}
