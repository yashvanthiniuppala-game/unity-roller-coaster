// FadeLight.cs — used by the explosion flash (added automatically).
using UnityEngine;

/// <summary>Fades a light out quickly (used by the explosion flash).</summary>
public class FadeLight : MonoBehaviour
{
    public float duration = 0.6f;
    Light l; float start, t;
    void Awake() { l = GetComponent<Light>(); start = l != null ? l.intensity : 0f; }
    void Update()
    {
        if (l == null) return;
        t += Time.deltaTime;
        l.intensity = Mathf.Lerp(start, 0f, t / duration);
        if (t >= duration) l.enabled = false;
    }
}
