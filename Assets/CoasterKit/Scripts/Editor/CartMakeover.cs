// CartMakeover.cs — puts the Blender models on your coaster:
//   * the realistic car (CoasterCar.fbx) replaces the box-shaped cart,
//   * Sunny (Sunny.fbx) sits in a front seat and is posed by ModelRiderPose
//     (holds the lap bar, waves in the station, arms up on the drops, leans into turns).
// The physics cart (CoasterCart, Rigidbody, collider) is NOT changed - only what you see.
//
// The models are turned the right way AUTOMATICALLY (it measures the car and Sunny's skeleton),
// so it doesn't matter how Unity's FBX import settings rotated them.
//
// HOW TO USE: select "Coaster Cart" > Add Component > Cart Makeover.
// Drag CoasterCar and Sunny from the Project panel into the slots, press "Apply Makeover".
using System.Collections.Generic;
using UnityEngine;

public class CartMakeover : MonoBehaviour
{
    [Header("New car body")]
    [Tooltip("Drag CoasterCar.fbx here")] public GameObject cartModel;
    [Tooltip("Only if the nose still points backwards after Apply")] public bool turnCartAround = false;
    public Vector3 cartOffset = Vector3.zero;

    [Header("New rider")]
    [Tooltip("Drag Sunny.fbx here")] public GameObject riderModel;
    [Tooltip("Only if she still faces backwards after Apply")] public bool turnRiderAround = false;
    [Tooltip("Where her hips sit in the cart (x right, y up, z forward). Other front seat: change the sign of X.")]
    public Vector3 hipSeat = new Vector3(-0.28f, 0.58f, 0.3f);
    [Range(0.5f, 1.5f)] public float riderScale = 1f;

    [SerializeField, HideInInspector] GameObject cartInstance, riderInstance;

    static readonly string[] OldParts = { "Body", "Nose", "Stripe", "Seat_Front", "Back_Front", "Seat_Rear", "Back_Rear", "LapBar_Front", "LapBar_Rear", "Wheel", "Rider", "Head" };

    // the realistic car's size in the cart's own space (x right, y up, z forward), measured in Blender
    static readonly Vector3 CarMin = new Vector3(-0.847f, -0.74f, -1.495f);
    static readonly Vector3 CarMax = new Vector3(0.847f, 1.287f, 1.578f);

    [ContextMenu("Apply Makeover")]
    public void Apply()
    {
        Remove(ref cartInstance);
        Remove(ref riderInstance);

        if (cartModel != null)
        {
            cartInstance = Spawn(cartModel, "Coaster Car (model)");
            cartInstance.transform.localPosition = cartOffset;
            foreach (var c in cartInstance.GetComponentsInChildren<Collider>()) DestroyNow(c);
            AlignCar(cartInstance.transform);
            foreach (Transform t in transform)
                foreach (var n in OldParts)
                    if (t.name == n) Hide(t.gameObject, true);
        }

        if (riderModel != null)
        {
            riderInstance = Spawn(riderModel, "Sunny (model rider)");
            foreach (var c in riderInstance.GetComponentsInChildren<Collider>()) DestroyNow(c);
            foreach (var a in riderInstance.GetComponentsInChildren<Animator>()) a.enabled = false;   // ModelRiderPose poses her
            foreach (var ra in riderInstance.GetComponentsInChildren<RiderAnimator>()) DestroyNow(ra);
            riderInstance.transform.localScale *= riderScale;

            var hips = ModelRiderPose.FindBone(riderInstance.transform, "Hips", HumanBodyBones.Hips);
            var head = ModelRiderPose.FindBone(riderInstance.transform, "Head", HumanBodyBones.Head);
            var lArm = ModelRiderPose.FindBone(riderInstance.transform, "LeftUpperArm", HumanBodyBones.LeftUpperArm);
            var rArm = ModelRiderPose.FindBone(riderInstance.transform, "RightUpperArm", HumanBodyBones.RightUpperArm);
            if (hips != null && head != null && lArm != null && rArm != null)
            {
                // turn her so that her own up / forward match the cart's up / forward
                Vector3 up = head.position - hips.position;
                Vector3 right = rArm.position - lArm.position;
                Vector3 fwd = Vector3.Cross(right, up);
                Quaternion current = Quaternion.LookRotation(fwd, up);
                Quaternion want = transform.rotation * Quaternion.Euler(0f, turnRiderAround ? 180f : 0f, 0f);
                riderInstance.transform.rotation = want * Quaternion.Inverse(current) * riderInstance.transform.rotation;
                // and put her hips on the seat
                riderInstance.transform.position += transform.TransformPoint(hipSeat) - hips.position;
            }
            else
            {
                riderInstance.transform.localPosition = hipSeat;
                Debug.LogWarning("[Cart Makeover] Couldn't find the rider's Hips/Head/arm bones, so she can't be posed.");
            }

            // hide every other rider (Captain Dune, the shape-built Sunny, ...)
            foreach (var ra in GetComponentsInChildren<RiderAnimator>(true)) Hide(ra.gameObject, true);
            var shapeSunny = GetComponent("SunnyRider") as Behaviour;
            if (shapeSunny != null) shapeSunny.enabled = false;

            var pose = riderInstance.GetComponent<ModelRiderPose>();
            if (pose == null) pose = riderInstance.AddComponent<ModelRiderPose>();
            pose.cart = GetComponent<CoasterCart>();
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
        foreach (var ra in GetComponentsInChildren<RiderAnimator>(true)) { Hide(ra.gameObject, false); break; }
        MarkDirty();
    }

    // ------------------------------------------------------------------ car alignment
    /// <summary>Tries all 24 right-angle orientations and keeps the one whose size matches the car,
    /// then uses the red rear reflectors to tell the front from the back.</summary>
    void AlignCar(Transform car)
    {
        Quaternion baseRot = car.localRotation;
        var tried = new List<Quaternion>();
        float best = float.MaxValue; Quaternion bestRot = baseRot;
        for (int x = 0; x < 4; x++)
            for (int y = 0; y < 4; y++)
                for (int z = 0; z < 4; z++)
                {
                    Quaternion r = Quaternion.Euler(x * 90f, y * 90f, z * 90f);
                    bool seen = false;
                    foreach (var q in tried) if (Quaternion.Angle(q, r) < 1f) { seen = true; break; }
                    if (seen) continue;
                    tried.Add(r);
                    car.localRotation = r * baseRot;
                    if (!LocalBounds(car, out Bounds b)) continue;
                    float score = Diff(b.min, CarMin) + Diff(b.max, CarMax);
                    if (score < best) { best = score; bestRot = car.localRotation; }
                }
        car.localRotation = bestRot;

        // the reflectors are at the BACK: if they ended up in front, turn the car round
        if (RearMarker(car, out Vector3 rear) && rear.z > 0f)
            car.localRotation = Quaternion.Euler(0f, 180f, 0f) * car.localRotation;
        if (turnCartAround)
            car.localRotation = Quaternion.Euler(0f, 180f, 0f) * car.localRotation;

        // safety: wrong import scale (e.g. 100x) -> fix it
        if (LocalBounds(car, out Bounds fb))
        {
            float k = (CarMax.z - CarMin.z) / Mathf.Max(fb.size.z, 1e-4f);
            if (k < 0.8f || k > 1.25f) car.localScale *= k;
        }
    }

    static float Diff(Vector3 a, Vector3 b) { return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z); }

    bool LocalBounds(Transform root, out Bounds b)
    {
        b = new Bounds(); bool any = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            Mesh m = null;
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null) m = mf.sharedMesh;
            var sm = r as SkinnedMeshRenderer;
            if (sm != null) m = sm.sharedMesh;
            if (m == null) continue;
            Bounds mb = m.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = transform.InverseTransformPoint(r.transform.TransformPoint(c));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }
        return any;
    }

    bool RearMarker(Transform root, out Vector3 local)
    {
        local = Vector3.zero;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length && i < mf.sharedMesh.subMeshCount; i++)
            {
                if (mats[i] == null || !mats[i].name.Contains("Reflector")) continue;
                Vector3 c = mf.sharedMesh.GetSubMesh(i).bounds.center;
                local = transform.InverseTransformPoint(r.transform.TransformPoint(c));
                return true;
            }
        }
        return false;
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
        go.transform.localRotation = prefab.transform.localRotation;
        go.transform.localScale = prefab.transform.localScale;
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
        UnityEditor.EditorGUILayout.HelpBox("1. Drag CoasterCar and Sunny (from the Project panel) into the slots.\n2. Press Apply Makeover - the models are turned the right way automatically.\nOnly the look changes - the ride physics stay the same.", UnityEditor.MessageType.Info);
        DrawDefaultInspector();
        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Apply Makeover", GUILayout.Height(30))) m.Apply();
        if (GUILayout.Button("Undo Makeover", GUILayout.Height(30), GUILayout.Width(120))) m.Revert();
        GUILayout.EndHorizontal();
    }
}
#endif
