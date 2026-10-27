// CartMakeover.cs — puts the Blender models on your coaster:
//   * the "Desert Serpent" cart body (DesertSerpentCart.fbx) replaces the box-shaped cart,
//   * Sunny (Sunny.fbx, a Humanoid) sits in the front seat and is animated by RiderAnimator
//     (sits, waves in the station, arms up on the drops, leans into the turns).
// The physics cart (CoasterCart, Rigidbody, collider) is NOT changed - only what you see.
//
// HOW TO USE: select "Coaster Cart" > Add Component > Cart Makeover.
// Drag DesertSerpentCart and Sunny from the Project panel into the two slots, press "Apply Makeover".
// Facing the wrong way? Tick "Turn ... Around" and press Apply again.  "Undo Makeover" brings the old look back.
using UnityEngine;

public class CartMakeover : MonoBehaviour
{
    [Header("New cart body")]
    [Tooltip("Drag CoasterCar.fbx (realistic) or DesertSerpentCart.fbx here")] public GameObject cartModel;
    public bool turnCartAround = false;
    public Vector3 cartOffset = Vector3.zero;

    [Header("New rider (Rig = Humanoid)")]
    [Tooltip("Drag Sunny.fbx here")] public GameObject riderModel;
    public bool turnRiderAround = false;
    [Tooltip("Where her hips sit in the cart (x right, y up, z forward). Realistic car: front-left seat = (-0.28, 0.58, 0.3), front-right = (0.28, 0.58, 0.3)")] public Vector3 hipSeat = new Vector3(-0.28f, 0.58f, 0.3f);
    [Range(0.5f, 1.5f)] public float riderScale = 1f;

    [SerializeField, HideInInspector] GameObject cartInstance, riderInstance;

    static readonly string[] OldParts = { "Body", "Nose", "Stripe", "Seat_Front", "Back_Front", "Seat_Rear", "Back_Rear", "LapBar_Front", "LapBar_Rear", "Wheel", "Rider", "Head" };

    [ContextMenu("Apply Makeover")]
    public void Apply()
    {
        Remove(ref cartInstance);
        Remove(ref riderInstance);

        if (cartModel != null)
        {
            cartInstance = Spawn(cartModel, "Serpent Cart (model)");
            cartInstance.transform.localPosition = cartOffset;
            cartInstance.transform.localRotation = Quaternion.Euler(0f, turnCartAround ? 180f : 0f, 0f);
            foreach (var c in cartInstance.GetComponentsInChildren<Collider>()) DestroyNow(c);   // the cart's own box collider does the physics
            // switch off the old box-shaped parts
            foreach (Transform t in transform)
                foreach (var n in OldParts)
                    if (t.name == n) Hide(t.gameObject, true);
        }

        if (riderModel != null)
        {
            riderInstance = Spawn(riderModel, "Sunny (model rider)");
            riderInstance.transform.localRotation = Quaternion.Euler(0f, turnRiderAround ? 180f : 0f, 0f);
            riderInstance.transform.localScale = Vector3.one * riderScale;
            foreach (var c in riderInstance.GetComponentsInChildren<Collider>()) DestroyNow(c);

            var anim = riderInstance.GetComponentInChildren<Animator>();
            bool human = anim != null && anim.avatar != null && anim.avatar.isHuman;
            Vector3 seatLocal = turnCartAround ? new Vector3(-hipSeat.x, hipSeat.y, -hipSeat.z) : hipSeat;   // seats turn with the cart
            Vector3 seat = transform.TransformPoint(seatLocal);
            Transform hips = human ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips != null) riderInstance.transform.position += seat - hips.position;
            else riderInstance.transform.position = seat;

            // hide every other rider (Captain Dune, the shape-built Sunny, ...)
            foreach (var ra in GetComponentsInChildren<RiderAnimator>(true))
                if (!ra.transform.IsChildOf(riderInstance.transform)) Hide(ra.gameObject, true);
            var shapeSunny = GetComponent("SunnyRider") as Behaviour;
            if (shapeSunny != null) shapeSunny.enabled = false;

            var r = riderInstance.GetComponent<RiderAnimator>();
            if (r == null) r = riderInstance.AddComponent<RiderAnimator>();
            r.cart = GetComponent<CoasterCart>();
            if (human)
            {
                r.humanoid = anim;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            else
            {
                r.body = riderInstance.transform;
                Debug.LogWarning("[Cart Makeover] The rider is not Humanoid, so she can't sit. Click Sunny.fbx > Rig tab > Animation Type: Humanoid > Apply, then press Apply Makeover again.");
            }
        }
        MarkDirty();
    }

    [ContextMenu("Undo Makeover")]
    public void Revert()
    {
        Remove(ref cartInstance);
        Remove(ref riderInstance);
        foreach (Transform t in transform)
            foreach (var n in OldParts)
                if (t.name == n) Hide(t.gameObject, false);
        // bring back the first rider we find
        foreach (var ra in GetComponentsInChildren<RiderAnimator>(true)) { Hide(ra.gameObject, false); break; }
        MarkDirty();
    }

    // ------------------------------------------------------------------ helpers
    GameObject Spawn(GameObject prefab, string name)
    {
        GameObject go = null;
#if UNITY_EDITOR
        if (!Application.isPlaying) go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, transform);
#endif
        if (go == null) go = Instantiate(prefab, transform);
        go.name = name;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Cart makeover");
#endif
        return go;
    }

    void Hide(GameObject go, bool hide)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.Undo.RecordObject(go, "Cart makeover");
#endif
        go.SetActive(!hide);
    }

    static void Remove(ref GameObject go)
    {
        if (go == null) return;
        if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        go = null;
    }

    static void DestroyNow(Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

    void MarkDirty()
    {
#if UNITY_EDITOR
        if (Application.isPlaying) return;
        UnityEditor.EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(CartMakeover))]
public class CartMakeoverEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        var m = (CartMakeover)target;
        UnityEditor.EditorGUILayout.HelpBox("1. Drag DesertSerpentCart and Sunny (from the Project panel) into the slots.\n2. Press Apply Makeover.\nOnly the look changes - the ride physics stay the same.", UnityEditor.MessageType.Info);
        DrawDefaultInspector();
        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Apply Makeover", GUILayout.Height(30))) m.Apply();
        if (GUILayout.Button("Undo Makeover", GUILayout.Height(30), GUILayout.Width(120))) m.Revert();
        GUILayout.EndHorizontal();
    }
}
#endif
