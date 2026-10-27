// CoasterEditors.cs — adds big buttons to the Inspector. Must stay inside a folder named "Editor".
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CoasterTrack))]
public class CoasterTrackEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var t = (CoasterTrack)target;
        EditorGUILayout.HelpBox(
            $"Track length: {t.Length:F0} m   Knots: {t.KnotCount}\n" +
            "Scene view colours: YELLOW = chain lift, MAGENTA = twist/bank, RED = brakes.", MessageType.Info);

        if (GUILayout.Button("Generate Starter Layout (replaces all knots!)", GUILayout.Height(28)))
        {
            if (EditorUtility.DisplayDialog("Generate starter layout",
                "This deletes the knots of this spline and creates a complete starter coaster. Continue?", "Yes", "Cancel"))
            {
                Undo.RecordObject(t.Container, "Starter layout");
                Undo.RecordObject(t, "Starter layout");
                t.GenerateStarterLayout();
                EditorUtility.SetDirty(t.Container);
                EditorUtility.SetDirty(t);
                var b = t.GetComponent<TrackBuilder>();
                if (b != null) b.Build();
                SceneView.RepaintAll();
            }
        }
        if (GUILayout.Button("Re-measure track")) { t.Build(); SceneView.RepaintAll(); }
        EditorGUILayout.Space();
        LoopTool(t);
        EditorGUILayout.Space();
        DrawDefaultInspector();
    }

    // ================= VERTICAL LOOP TOOL =================
    static bool loopFold = true;

    void LoopTool(CoasterTrack t)
    {
        loopFold = EditorGUILayout.Foldout(loopFold, "Add a Vertical Loop", true, EditorStyles.foldoutHeader);
        if (!loopFold) return;
        EditorGUILayout.HelpBox(
            "1. Pick a LOW knot (a valley) after a big drop, with long track on both sides.\n" +
            "2. The ORANGE outline in the Scene view shows where the loop will go.\n" +
            "3. Press 'Insert Vertical Loop'. Track mesh, fire rings and the ride check update by themselves.",
            MessageType.None);

        var so = serializedObject;
        so.Update();
        EditorGUILayout.PropertyField(so.FindProperty("newLoopAtKnot"), new GUIContent("At Knot (K number)"));
        EditorGUILayout.Slider(so.FindProperty("newLoopRadius"), 5f, 20f, new GUIContent("Radius (m)", "Half the width of the loop"));
        EditorGUILayout.Slider(so.FindProperty("newLoopHeightStretch"), 1f, 1.5f, new GUIContent("Height Stretch", "1 = round circle, 1.25 = taller 'teardrop' like real coasters"));
        EditorGUILayout.Slider(so.FindProperty("newLoopSideShift"), -8f, 8f, new GUIContent("Side Shift (m)", "The exit is moved this far sideways so the track doesn't hit itself. + = right, - = left"));
        EditorGUILayout.Slider(so.FindProperty("newLoopStraight"), 6f, 25f, new GUIContent("Straight Before/After (m)"));
        so.ApplyModifiedProperties();

        float top = CoasterTrack.LoopTopHeight(t.newLoopRadius, t.newLoopHeightStretch);
        string why = t.CanInsertLoop(t.newLoopAtKnot, t.newLoopStraight);
        if (why != null) EditorGUILayout.HelpBox(why, MessageType.Warning);
        else EditorGUILayout.HelpBox($"Loop will be {top:F0} m tall and {t.newLoopRadius * 2f:F0} m wide at K{t.newLoopAtKnot}.", MessageType.Info);

        using (new EditorGUI.DisabledScope(why != null))
        {
            if (GUILayout.Button("Insert Vertical Loop", GUILayout.Height(30))) DoInsert(t);
        }

        for (int i = 0; i < t.loops.Count; i++)
        {
            var z = t.loops[i];
            if (z == null) continue;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{z.label}   (K{z.startKnot}-K{z.endKnot}, {CoasterTrack.LoopTopHeight(z.radius, z.heightStretch):F0} m tall)");
            if (GUILayout.Button("Remove", GUILayout.Width(70))) { DoRemove(t, i); GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal();
        }
    }

    void OnSceneGUI()
    {
        var t = (CoasterTrack)target;
        if (!loopFold || t.CanInsertLoop(t.newLoopAtKnot, t.newLoopStraight) != null) return;
        var pts = t.PreviewLoop(t.newLoopAtKnot, t.newLoopRadius, t.newLoopHeightStretch, t.newLoopSideShift, t.newLoopStraight);
        if (pts.Count < 2) return;
        Handles.color = new Color(1f, 0.55f, 0f);
        Handles.DrawAAPolyLine(6f, pts.ToArray());
        Vector3 hi = pts[0];
        foreach (var p in pts) if (p.y > hi.y) hi = p;
        Handles.Label(hi + Vector3.up * 2f, "NEW LOOP (preview)");
    }

    static void DoInsert(CoasterTrack t)
    {
        Undo.RecordObject(t.Container, "Insert loop");
        Undo.RecordObject(t, "Insert loop");
        t.Build();
        float oldLen = t.Length;
        float atDist = t.KnotDistance(t.newLoopAtKnot);
        string err = t.InsertLoop(t.newLoopAtKnot, t.newLoopRadius, t.newLoopHeightStretch, t.newLoopSideShift, t.newLoopStraight);
        if (err != null) { EditorUtility.DisplayDialog("Can't add the loop here", err, "OK"); return; }
        float added = t.Length - oldLen;
        int moved = ShiftDistanceItems(atDist, added, -1f);
        Finish(t, "Loop added!", $"The track is {added:F0} m longer. {moved} object(s) that use 'Distance Along Track' (fire rings) were moved along with it.", t.loops[t.loops.Count - 1]);
    }

    static void DoRemove(CoasterTrack t, int i)
    {
        Undo.RecordObject(t.Container, "Remove loop");
        Undo.RecordObject(t, "Remove loop");
        t.Build();
        var z = t.loops[i];
        float a = t.KnotDistance(z.startKnot), b = t.KnotDistance(z.endKnot);
        float oldLen = t.Length;
        t.RemoveLoop(i);
        float removed = oldLen - t.Length;
        ShiftDistanceItems(b, -removed, a);
        Finish(t, "Loop removed", $"The track is {removed:F0} m shorter again.", null);
    }

    static void Finish(CoasterTrack t, string title, string msg, CoasterTrack.LoopZone z)
    {
        EditorUtility.SetDirty(t.Container);
        EditorUtility.SetDirty(t);
        var b = t.GetComponent<TrackBuilder>();
        if (b != null) { b.Build(); EditorUtility.SetDirty(b.gameObject); }

        // ride check with the cart's own settings
        var cart = Object.FindAnyObjectByType<CoasterCart>();
        float lift = cart ? cart.liftSpeed : 4f, fr = cart ? cart.rollingFriction : 0.015f, drag = cart ? cart.airDrag : 0.0006f;
        float watch = z != null ? t.LoopTopDistance(z) : -1f;
        var sim = CoasterRideSim.Run(t, lift, fr, drag, cart ? cart.dispatchSpeed : 3f, cart ? cart.brakeDeceleration : 5f, cart ? cart.minSpeed : 1.5f, watch);
        msg += $"\n\nRide check: {sim.rideTime:F0} s ride, top speed {sim.topSpeed * 3.6f:F0} km/h.";
        if (z != null)
        {
            float need = Mathf.Sqrt(Physics.gravity.magnitude * z.radius / z.heightStretch);
            msg += $"\nSpeed at the top of the loop: {sim.watchSpeed:F1} m/s (needs more than {need:F1} m/s).";
            if (sim.watchSpeed < need * 1.2f) msg += "\nWARNING: Too slow at the top! Use a smaller radius, a lower knot, or make the first hill higher.";
            else msg += "\nOK: Fast enough to go round.";
        }
        if (sim.stalled) msg += $"\nWARNING: The cart nearly stalls at {sim.stallAt:F0} m - lower the next hill after the loop.";
        if (sim.rideTime > 60f) msg += "\nWARNING: Ride is over 60 s - raise 'Lift Speed' on the cart (e.g. 6) or use a smaller loop.";
        if (sim.rideTime < 30f) msg += "\nWARNING: Ride is under 30 s - lower 'Lift Speed' on the cart.";
        msg += "\n\nSave the scene with Ctrl+S.";
        Debug.Log("[Coaster Kit] " + title + " " + msg.Replace("\n", " "));
        EditorUtility.DisplayDialog(title, msg, "OK");
        SceneView.RepaintAll();
    }

    /// <summary>Moves every component with a public 'distanceAlongTrack' field (e.g. FireRing) that sits after 'from'.
    /// If 'clampTo' >= 0, items between clampTo and from are put at clampTo (used when removing a loop).</summary>
    static int ShiftDistanceItems(float from, float delta, float clampTo)
    {
        int count = 0;
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (mb == null) continue;
            var fi = mb.GetType().GetField("distanceAlongTrack", BindingFlags.Public | BindingFlags.Instance);
            if (fi == null || fi.FieldType != typeof(float)) continue;
            float d = (float)fi.GetValue(mb);
            float nd = d;
            if (d > from) nd = d + delta;
            else if (clampTo >= 0f && d > clampTo) nd = clampTo;
            if (Mathf.Approximately(nd, d)) continue;
            Undo.RecordObject(mb, "Move along track");
            fi.SetValue(mb, nd);
            var snap = mb.GetType().GetMethod("Snap", BindingFlags.Public | BindingFlags.Instance, null, System.Type.EmptyTypes, null);
            if (snap != null) { Undo.RecordObject(mb.transform, "Move along track"); snap.Invoke(mb, null); }
            EditorUtility.SetDirty(mb);
            count++;
        }
        return count;
    }
}

[CustomEditor(typeof(TrackBuilder))]
public class TrackBuilderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var b = (TrackBuilder)target;
        EditorGUILayout.HelpBox("Press 'Build Track Mesh' every time you change the spline.", MessageType.Info);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Build Track Mesh", GUILayout.Height(28))) { b.Build(); EditorUtility.SetDirty(b.gameObject); }
        if (GUILayout.Button("Clear", GUILayout.Height(28), GUILayout.Width(70))) { b.Clear(); EditorUtility.SetDirty(b.gameObject); }
        GUILayout.EndHorizontal();
        EditorGUILayout.Space();
        DrawDefaultInspector();
    }
}


[CustomEditor(typeof(CoasterCart))]
public class CoasterCartEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var c = (CoasterCart)target;
        if (GUILayout.Button("Snap Cart Onto Track (station)", GUILayout.Height(28)))
        {
            Undo.RecordObject(c.transform, "Snap cart");
            c.SnapToTrack();
            EditorUtility.SetDirty(c.transform);
            SceneView.RepaintAll();
        }
        EditorGUILayout.Space();
        DrawDefaultInspector();
    }
}
