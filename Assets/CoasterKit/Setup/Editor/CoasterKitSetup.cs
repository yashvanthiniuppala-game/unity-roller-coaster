// CoasterKitSetup.cs — runs right after you import the Coaster Kit.
// It installs Unity's Splines package if it's missing (the coaster scripts need it),
// then tells you the next step. It compiles on its own, so it works even before Splines exists.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

[InitializeOnLoad]
static class CoasterKitSetup
{
    static AddRequest request;
    static string Key => "CoasterKit.Welcomed." + Application.dataPath;

    static CoasterKitSetup() { EditorApplication.delayCall += Check; }

    static bool HasSplines()
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            if (a.GetName().Name == "Unity.Splines") return true;
        try { return File.ReadAllText("Packages/manifest.json").Contains("\"com.unity.splines\""); }
        catch { return false; }
    }

    static void Check()
    {
        if (!HasSplines())
        {
            if (SessionState.GetBool("CoasterKit.AskedSplines", false)) return;
            SessionState.SetBool("CoasterKit.AskedSplines", true);
            if (EditorUtility.DisplayDialog("Coaster Kit",
                "The Coaster Kit needs Unity's free \"Splines\" package.\n\nInstall it now? (takes about a minute)", "Install", "Later"))
            {
                request = Client.Add("com.unity.splines");
                EditorApplication.update += WaitForInstall;
            }
            return;
        }
        if (!EditorPrefs.GetBool(Key, false))
        {
            EditorPrefs.SetBool(Key, true);
            EditorUtility.DisplayDialog("Coaster Kit is ready",
                "Next steps (top menu):\n\n1. Tools > Coaster Kit > 1. Open Desert Scene\n2. Tools > Coaster Kit > 2. Build Complete Coaster (one click)\n3. Press Play", "OK");
        }
    }

    static void WaitForInstall()
    {
        if (request == null || !request.IsCompleted) return;
        EditorApplication.update -= WaitForInstall;
        if (request.Status == StatusCode.Success)
            Debug.Log("[Coaster Kit] Splines installed. Unity will now compile the Coaster Kit; the Tools > Coaster Kit menu appears when it's done.");
        else
            Debug.LogError("[Coaster Kit] Could not install Splines: " + request.Error.message + "  (Install it by hand: Window > Package Manager > Unity Registry > Splines)");
    }
}
