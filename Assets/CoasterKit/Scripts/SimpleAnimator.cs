// SimpleAnimator.cs  —  quick code animation for decorations
// (spinning windmill, bobbing skull, swinging sign, pulsing crystal ...).
// For the grade, ALSO make at least one real Animation Clip with the Animation window
// (see the guide) — this script is a handy extra, not a replacement.
using UnityEngine;

public class SimpleAnimator : MonoBehaviour
{
    public enum Kind { Rotate, Bob, Swing, Pulse }
    public Kind kind = Kind.Rotate;
    [Tooltip("Rotate: degrees/sec   Bob: metres   Swing: degrees   Pulse: scale amount")] public float amount = 45f;
    [Tooltip("Cycles per second (not used by Rotate)")] public float speed = 0.5f;
    public Vector3 axis = Vector3.up;
    public float phase = 0f;

    Vector3 startPos, startScale; Quaternion startRot;

    void Start() { startPos = transform.localPosition; startRot = transform.localRotation; startScale = transform.localScale; }

    void Update()
    {
        float w = Mathf.Sin((Time.time * speed + phase) * Mathf.PI * 2f);
        switch (kind)
        {
            case Kind.Rotate: transform.Rotate(axis.normalized, amount * Time.deltaTime, Space.Self); break;
            case Kind.Bob: transform.localPosition = startPos + axis.normalized * w * amount; break;
            case Kind.Swing: transform.localRotation = startRot * Quaternion.AngleAxis(w * amount, axis.normalized); break;
            case Kind.Pulse: transform.localScale = startScale * (1f + w * amount); break;
        }
    }
}
