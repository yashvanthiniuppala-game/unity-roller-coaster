// FireJet.cs  —  CPS 499 HW#2 Roller Coaster Kit
// Trigger zones that react when the cart drives through them.
// How to use any of them:
//   1. Create an empty GameObject where the effect should happen.
//   2. Add a Box Collider, tick "Is Trigger", make the box big enough for the cart to pass through.
//   3. Add one of these scripts and fill in the sound / settings.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A fire that always burns (with flickering light + crackle loop) and shoots a big
/// flame burst when the cart comes close. Put it on a torch, bonfire or 'fire cannon'.
/// Add a trigger collider around it for the burst (added automatically if missing).
/// </summary>
public class FireJet : MonoBehaviour
{
    public float flameSize = 1f;
    [Tooltip("Optional: your own looping fire Particle System prefab")] public ParticleSystem customFirePrefab;
    public AudioClip fireLoop;
    public AudioClip burstSound;
    [Range(0, 1)] public float volume = 0.8f;
    public float burstMultiplier = 3f;
    public float burstDuration = 1.2f;
    public bool addLight = true;

    ParticleSystem fire;
    Light flicker;
    float burstTimer;
    float baseRate, baseSpeed;

    void Reset() { CartDetect.EnsureTrigger(gameObject); var b = GetComponent<BoxCollider>(); if (b != null) b.size = new Vector3(10, 10, 10); }

    void Start()
    {
        CartDetect.EnsureTrigger(gameObject);
        if (customFirePrefab != null) { fire = Instantiate(customFirePrefab, transform); fire.transform.localPosition = Vector3.zero; fire.Play(); }
        else fire = FxFactory.CreateFire(transform, transform.position, flameSize);
        baseRate = fire.emission.rateOverTime.constant;
        baseSpeed = fire.main.startSpeedMultiplier;

        if (addLight)
        {
            var lg = new GameObject("Fire Light");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = Vector3.up * flameSize;
            flicker = lg.AddComponent<Light>();
            flicker.type = LightType.Point; flicker.color = new Color(1f, 0.55f, 0.2f); flicker.range = 10f * flameSize;
        }
        if (fireLoop != null)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.clip = fireLoop; a.loop = true; a.volume = volume * 0.6f; a.spatialBlend = 1f; a.minDistance = 4f; a.maxDistance = 60f;
            a.Play();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!CartDetect.IsCart(other)) return;
        burstTimer = burstDuration;
        CartDetect.Play(burstSound, transform.position, volume);
    }

    void Update()
    {
        if (fire == null) return;
        burstTimer -= Time.deltaTime;
        float k = burstTimer > 0f ? burstMultiplier : 1f;
        var e = fire.emission; e.rateOverTime = baseRate * k;
        var m = fire.main; m.startSpeedMultiplier = Mathf.Lerp(m.startSpeedMultiplier, baseSpeed * k, 10f * Time.deltaTime);
        if (flicker != null) flicker.intensity = (1.5f + Mathf.PerlinNoise(Time.time * 8f, 0f) * 1.5f) * (burstTimer > 0f ? 2.5f : 1f);
    }
}
