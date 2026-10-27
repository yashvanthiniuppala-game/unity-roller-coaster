// RiderAnimator.cs — makes the rider in the cart come alive:
//   * waves in the station, looks around on the lift hill,
//   * throws both arms up on drops / fast parts / air-time,
//   * leans into the turns and bobs over bumps.
// Works with (a) the kit's built-in "Captain Dune" rider made of simple shapes, or
// (b) ANY humanoid character model you import (set its Rig to Humanoid) — then the script
// also bends the legs so the character sits in the seat.
using UnityEngine;

public class RiderAnimator : MonoBehaviour
{
    public CoasterCart cart;

    [Header("Built-in rider parts (filled in by the builder)")]
    public Transform leftArm;
    public Transform rightArm;
    public Transform head;
    public Transform body;

    [Header("Imported humanoid model (optional)")]
    public Animator humanoid;
    [Tooltip("How far the thighs lift forward to sit (humanoid muscle value -1..1)")] [Range(-1, 1)] public float sitThighs = 0.75f;
    [Tooltip("How much the knees bend (humanoid muscle value -1..1)")] [Range(-1, 1)] public float bendKnees = -0.9f;

    [Header("Behaviour")]
    [Tooltip("Arms go up above this speed (m/s)")] public float armsUpSpeed = 17f;
    public float leanAmount = 1f;

    float arms, lean, bob, prevYaw;
    Vector3 armL0, armR0; Quaternion head0, body0;
    HumanPoseHandler poser; HumanPose pose;
    int mThighL = -1, mThighR = -1, mKneeL = -1, mKneeR = -1, mArmUpL = -1, mArmUpR = -1,
        mArmFwdL = -1, mArmFwdR = -1, mElbowL = -1, mElbowR = -1, mSpineSide = -1, mHeadNod = -1, mHeadTurn = -1;

    void Start()
    {
        if (cart == null) cart = GetComponentInParent<CoasterCart>();
        if (cart == null) cart = FindAnyObjectByType<CoasterCart>();
        if (leftArm) armL0 = leftArm.localEulerAngles;
        if (rightArm) armR0 = rightArm.localEulerAngles;
        if (head) head0 = head.localRotation;
        if (body) body0 = body.localRotation;
        if (cart) prevYaw = cart.transform.eulerAngles.y;

        if (humanoid != null && humanoid.avatar != null && humanoid.avatar.isHuman)
        {
            humanoid.applyRootMotion = false;
            poser = new HumanPoseHandler(humanoid.avatar, humanoid.transform);
            string[] names = HumanTrait.MuscleName;
            for (int i = 0; i < names.Length; i++)
            {
                switch (names[i])
                {
                    case "Left Upper Leg Front-Back": mThighL = i; break;
                    case "Right Upper Leg Front-Back": mThighR = i; break;
                    case "Left Lower Leg Stretch": mKneeL = i; break;
                    case "Right Lower Leg Stretch": mKneeR = i; break;
                    case "Left Arm Down-Up": mArmUpL = i; break;
                    case "Right Arm Down-Up": mArmUpR = i; break;
                    case "Left Arm Front-Back": mArmFwdL = i; break;
                    case "Right Arm Front-Back": mArmFwdR = i; break;
                    case "Left Forearm Stretch": mElbowL = i; break;
                    case "Right Forearm Stretch": mElbowR = i; break;
                    case "Spine Left-Right": mSpineSide = i; break;
                    case "Head Nod Down-Up": mHeadNod = i; break;
                    case "Head Turn Left-Right": mHeadTurn = i; break;
                }
            }
        }
    }

    void Update()
    {
        if (cart == null) return;
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        bool riding = cart.state == CoasterCart.RideState.Riding;
        bool thrill = riding && !cart.onLift && !cart.braking &&
                      (cart.speed > armsUpSpeed || cart.gForce < 0.6f);
        arms = Mathf.MoveTowards(arms, thrill ? 1f : 0f, dt * (thrill ? 4f : 1.5f));

        float yaw = cart.transform.eulerAngles.y;
        float dYaw = Mathf.DeltaAngle(prevYaw, yaw);
        if (Mathf.Abs(dYaw) > 20f || cart.transform.up.y < 0.3f) dYaw = 0f;   // loop / upside down: no leaning
        float yawRate = dYaw / dt;
        prevYaw = yaw;
        lean = Mathf.Lerp(lean, Mathf.Clamp(-yawRate * cart.speed * 0.012f, -25f, 25f) * leanAmount, dt * 4f);
        bob = Mathf.Lerp(bob, Mathf.Clamp((cart.gForce - 1f) * -0.06f, -0.08f, 0.08f), dt * 8f);
    }

    void LateUpdate()
    {
        if (cart == null) return;
        float t = Time.time;
        bool station = cart.state == CoasterCart.RideState.WaitingInStation;
        float wave = station ? Mathf.Sin(t * 8f) : 0f;
        float look = cart.onLift ? Mathf.Sin(t * 0.9f) : 0f;

        // ---- built-in rider ----
        if (leftArm)
            leftArm.localRotation = Quaternion.Euler(Mathf.LerpAngle(armL0.x, -170f, arms), armL0.y, armL0.z + Mathf.Lerp(0f, 18f, arms));
        if (rightArm)
        {
            float up = station ? 0.9f : arms;
            rightArm.localRotation = Quaternion.Euler(Mathf.LerpAngle(armR0.x, -170f, up), armR0.y, armR0.z - Mathf.Lerp(0f, 18f, up) + wave * 25f);
        }
        if (head) head.localRotation = head0 * Quaternion.Euler(-arms * 12f, look * 45f, 0f);
        if (body)
        {
            body.localRotation = body0 * Quaternion.Euler(0f, 0f, lean);
            body.localPosition = new Vector3(body.localPosition.x, body.localPosition.y + (bob - lastBob), body.localPosition.z);
            lastBob = bob;
        }

        // ---- imported humanoid ----
        if (poser != null)
        {
            poser.GetHumanPose(ref pose);
            var m = pose.muscles;
            Set(m, mThighL, sitThighs); Set(m, mThighR, sitThighs);
            Set(m, mKneeL, bendKnees); Set(m, mKneeR, bendKnees);
            float upR = station ? 0.9f : arms;
            Set(m, mArmUpL, Mathf.Lerp(-0.2f, 1f, arms)); Set(m, mArmUpR, Mathf.Lerp(-0.2f, 1f, upR));
            Set(m, mArmFwdL, Mathf.Lerp(0.6f, 0.1f, arms)); Set(m, mArmFwdR, Mathf.Lerp(0.6f, 0.1f, upR) + wave * 0.2f);
            Set(m, mElbowL, Mathf.Lerp(0.3f, 1f, arms)); Set(m, mElbowR, Mathf.Lerp(0.3f, 1f, upR));
            Set(m, mSpineSide, lean / 40f);
            Set(m, mHeadNod, -arms * 0.3f);
            Set(m, mHeadTurn, look * 0.6f);
            poser.SetHumanPose(ref pose);
        }
    }

    float lastBob;
    static void Set(float[] m, int i, float v) { if (i >= 0 && i < m.Length) m[i] = Mathf.Clamp(v, -1f, 1f); }

    void OnDestroy() { if (poser != null) poser.Dispose(); }
}
