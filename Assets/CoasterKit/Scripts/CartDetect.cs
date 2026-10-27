// CartDetect.cs — helper used by the effect zones (you don't add this to objects).
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

static class CartDetect
{
    public static bool IsCart(Collider c)
    {
        return c.GetComponentInParent<CoasterCart>() != null || c.GetComponentInParent<CoasterTrainCar>() != null;
    }
    public static void EnsureTrigger(GameObject go)
    {
        var col = go.GetComponent<Collider>();
        if (col == null) { var b = go.AddComponent<BoxCollider>(); b.size = new Vector3(6, 6, 6); col = b; }
        col.isTrigger = true;
    }
    public static void Play(AudioClip clip, Vector3 pos, float volume)
    {
        if (clip == null) return;
        // 2D-ish one-shot so it is always audible in the recording
        var go = new GameObject("OneShot " + clip.name);
        go.transform.position = pos;
        var a = go.AddComponent<AudioSource>();
        a.clip = clip; a.volume = volume; a.spatialBlend = 0.6f; a.minDistance = 15f; a.maxDistance = 300f;
        a.Play();
        Object.Destroy(go, clip.length + 0.1f);
    }
}
