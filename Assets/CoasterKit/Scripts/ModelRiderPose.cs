// ModelRiderPose.cs — poses the Blender Sunny model in her seat, every frame, by turning her bones
// directly (no Animator / Humanoid settings needed):
//   * sits with her thighs forward and her lower legs hanging down,
//   * holds the lap bar, waves in the station, throws her arms up on drops and fast parts,
//   * leans into the turns and looks around on the lift hill.
// Cart Makeover adds this for you.
using System.Collections.Generic;
using UnityEngine;

public class ModelRiderPose : MonoBehaviour
{
    public CoasterCart cart;
    [Tooltip("Arms go up above this speed (m/s)")] public float armsUpSpeed = 17f;
    [Range(0f, 2f)] public float leanAmount = 1f;

    Transform hips, spine, head;
    Transform[] upLeg = new Transform[2], loLeg = new Transform[2], foot = new Transform[2], toes = new Transform[2];
    Transform[] upArm = new Transform[2], loArm = new Transform[2], hand = new Transform[2];
    readonly Dictionary<Transform, Quaternion> rest = new Dictionary<Transform, Quaternion>();
    float arms, lean, prevYaw;
    Quaternion frame = Quaternion.identity;   // her body frame (x right, y up, z forward) relative to this object
    Quaternion Body => transform.rotation * frame;

    /// <summary>Finds a bone by name (also "mixamorig:Hips" style), or through a Humanoid Animator.</summary>
    public static Transform FindBone(Transform root, string name, HumanBodyBones humanBone)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name || t.name.EndsWith(":" + name) || t.name.EndsWith("_" + name)) return t;
        var a = root.GetComponentInChildren<Animator>();
        if (a != null && a.avatar != null && a.avatar.isHuman) return a.GetBoneTransform(humanBone);
        return null;
    }

    void Start()
    {
        if (cart == null) cart = GetComponentInParent<CoasterCart>();
        foreach (var a in GetComponentsInChildren<Animator>()) a.enabled = false;
        hips = FindBone(transform, "Hips", HumanBodyBones.Hips);
        spine = FindBone(transform, "Spine", HumanBodyBones.Spine);
        head = FindBone(transform, "Head", HumanBodyBones.Head);
        string[] side = { "Left", "Right" };
        HumanBodyBones[,] hb =
        {
            { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand },
            { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand },
        };
        for (int s = 0; s < 2; s++)
        {
            upLeg[s] = FindBone(transform, side[s] + "UpperLeg", hb[s, 0]);
            loLeg[s] = FindBone(transform, side[s] + "LowerLeg", hb[s, 1]);
            foot[s] = FindBone(transform, side[s] + "Foot", hb[s, 2]);
            toes[s] = FindBone(transform, side[s] + "Toes", hb[s, 3]);
            upArm[s] = FindBone(transform, side[s] + "UpperArm", hb[s, 4]);
            loArm[s] = FindBone(transform, side[s] + "LowerArm", hb[s, 5]);
            hand[s] = FindBone(transform, side[s] + "Hand", hb[s, 6]);
        }
        foreach (var t in GetComponentsInChildren<Transform>(true)) rest[t] = t.localRotation;
        if (head != null && upArm[0] != null && upArm[1] != null)
        {
            Vector3 up = head.position - hips.position, right = upArm[1].position - upArm[0].position;
            frame = Quaternion.Inverse(transform.rotation) * Quaternion.LookRotation(Vector3.Cross(right, up), up);
        }
        if (cart != null) prevYaw = cart.transform.eulerAngles.y;
    }

    void LateUpdate()
    {
        if (hips == null) return;
        foreach (var kv in rest) if (kv.Key != null) kv.Key.localRotation = kv.Value;   // back to the T-pose

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        bool station = cart != null && cart.state == CoasterCart.RideState.WaitingInStation;
        bool riding = cart != null && cart.state == CoasterCart.RideState.Riding;
        bool thrill = riding && !cart.onLift && !cart.braking && (cart.speed > armsUpSpeed || cart.gForce < 0.6f);
        arms = Mathf.MoveTowards(arms, thrill ? 1f : 0f, dt * (thrill ? 4f : 1.5f));
        if (cart != null)
        {
            float yaw = cart.transform.eulerAngles.y;
            float d = Mathf.DeltaAngle(prevYaw, yaw); prevYaw = yaw;
            if (Mathf.Abs(d) > 20f || cart.transform.up.y < 0.3f) d = 0f;
            lean = Mathf.Lerp(lean, Mathf.Clamp(-d / dt * cart.speed * 0.012f, -25f, 25f) * leanAmount, dt * 4f);
        }

        // directions are given in the RIDER's space: x = her right, y = up, z = forward
        for (int s = 0; s < 2; s++)
        {
            Aim(upLeg[s], loLeg[s], new Vector3(0f, -0.06f, 1f));          // thighs forward along the seat
            Aim(loLeg[s], foot[s], new Vector3(0f, -1f, 0.12f));           // lower legs hang down
            Aim(foot[s], toes[s], new Vector3(0f, -0.2f, 1f));
        }
        if (spine != null) spine.rotation = Quaternion.AngleAxis(lean, Body * Vector3.forward) * spine.rotation;
        if (head != null)
        {
            float look = cart != null && cart.onLift ? Mathf.Sin(Time.time * 0.9f) : 0f;
            head.rotation = Quaternion.AngleAxis(look * 35f, Body * Vector3.up) * Quaternion.AngleAxis(-arms * 8f, Body * Vector3.right) * head.rotation;
        }
        for (int s = 0; s < 2; s++)
        {
            float sx = s == 0 ? -1f : 1f;                                    // left arm on her left (-x)
            float a = (station && s == 1) ? 1f : arms;                       // right arm waves in the station
            Vector3 barU = new Vector3(sx * 0.30f, -0.50f, 1f), barL = new Vector3(sx * 0.05f, -0.30f, 1f);
            Vector3 upU = new Vector3(sx * 0.45f, 1f, 0.05f), upL = new Vector3(sx * 0.20f, 1f, 0.15f);
            if (station && s == 1) upL += new Vector3(Mathf.Sin(Time.time * 8f) * 0.6f, 0f, 0f);   // wave!
            Aim(upArm[s], loArm[s], Vector3.Slerp(barU.normalized, upU.normalized, a));
            Aim(loArm[s], hand[s], Vector3.Slerp(barL.normalized, upL.normalized, a));
        }
    }

    void Aim(Transform bone, Transform child, Vector3 dirRiderSpace)
    {
        if (bone == null || child == null) return;
        Vector3 cur = child.position - bone.position;
        if (cur.sqrMagnitude < 1e-8f) return;
        Vector3 want = Body * dirRiderSpace;
        bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
    }
}
