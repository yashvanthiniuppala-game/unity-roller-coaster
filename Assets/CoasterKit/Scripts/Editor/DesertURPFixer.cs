// DesertURPFixer.cs — one-click fix for the professor's Desert package in a URP project (Unity 6.5).
// Menu: Tools > Coaster Kit > Fix Desert Pack for URP
// * converts every material under Assets/Desert to URP/Lit (keeps textures, normal maps,
//   cut-out leaves, and makes the 'Standard (Double Sided)' plants/tents double-sided),
// * turns the old FX/Water4 materials into a clear, shiny URP water,
// * turns fire.mat into an additive URP particle material,
// * gives the terrain the URP terrain material,
// * removes the "Missing (Mono Script)" components left by the old Water4 prefabs.
// Must stay inside a folder named "Editor".
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class DesertURPFixer
{
    const string DesertFolder = "Assets/Desert";
    const string TerrainMatPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/TerrainLit.mat";

    [MenuItem("Tools/Coaster Kit/Fix Desert Pack for URP")]
    public static void Fix()
    {
        if (!PipelineUtil.IsSRP)
        {
            EditorUtility.DisplayDialog("Coaster Kit",
                "This project uses the Built-In Render Pipeline, so the Desert materials already work. Nothing to fix.", "OK");
            return;
        }
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Shader particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (lit == null)
        {
            EditorUtility.DisplayDialog("Coaster Kit", "Could not find the URP Lit shader. Is the Universal RP package installed?", "OK");
            return;
        }

        int converted = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { DesertFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) continue;
            string sn = m.shader != null ? m.shader.name : "";
            if (sn.StartsWith("Universal Render Pipeline")) continue;

            // --- read the old values BEFORE switching shader ---
            Texture main = Tex(m, "_MainTex");
            Vector2 tiling = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offset = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;
            Color col = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            Texture bump = Tex(m, "_BumpMap");
            float gloss = Num(m, "_Glossiness", 0.2f);
            float metal = Num(m, "_Metallic", 0f);
            float cutoff = Num(m, "_Cutoff", 0.5f);
            bool cutout = m.IsKeywordEnabled("_ALPHATEST_ON") || Mathf.Approximately(Num(m, "_Mode", 0f), 1f);
            bool twoSided = sn.Contains("Double");
            bool water = sn.StartsWith("FX/") || path.Contains("/Water/");
            bool fire = sn.Contains("Particle") || m.name.ToLower() == "fire";

            Undo.RecordObject(m, "Convert Desert material to URP");

            if (fire && particles != null)
            {
                m.shader = particles;
                if (main != null) m.SetTexture("_BaseMap", main);
                m.SetColor("_BaseColor", Color.white);
                PipelineUtil.SetTransparent(m, true);
            }
            else
            {
                m.shader = lit;
                if (water)
                {
                    m.SetColor("_BaseColor", new Color(0.22f, 0.52f, 0.62f, 0.65f));
                    m.SetFloat("_Smoothness", 0.95f);
                    m.SetFloat("_Metallic", 0f);
                    PipelineUtil.SetTransparent(m, false);
                }
                else
                {
                    if (main != null) { m.SetTexture("_BaseMap", main); m.SetTextureScale("_BaseMap", tiling); m.SetTextureOffset("_BaseMap", offset); }
                    m.SetColor("_BaseColor", col);
                    if (bump != null) { m.SetTexture("_BumpMap", bump); m.EnableKeyword("_NORMALMAP"); }
                    m.SetFloat("_Smoothness", gloss);
                    m.SetFloat("_Metallic", metal);
                    if (cutout)
                    {
                        m.SetFloat("_AlphaClip", 1f);
                        m.SetFloat("_Cutoff", cutoff);
                        m.EnableKeyword("_ALPHATEST_ON");
                        m.SetOverrideTag("RenderType", "TransparentCutout");
                        m.renderQueue = (int)RenderQueue.AlphaTest;
                    }
                    if (twoSided) m.SetFloat("_Cull", 0f);   // 0 = render both sides
                }
            }
            EditorUtility.SetDirty(m);
            converted++;
        }
        AssetDatabase.SaveAssets();

        // --- terrain ---
        int terrains = 0;
        // (if the URP terrain material isn't found, null = "use the pipeline's default terrain material")
        var terrainMat = AssetDatabase.LoadAssetAtPath<Material>(TerrainMatPath);
        {
            foreach (var t in Terrain.activeTerrains)
            {
                if (t == null) continue;
                Undo.RecordObject(t, "URP terrain material");
                t.materialTemplate = terrainMat;
                EditorUtility.SetDirty(t);
                terrains++;
            }
        }

        // --- missing scripts left by the old Water4 prefabs ---
        int removed = 0;
        Scene scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(tr.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);

        string msg = $"Converted {converted} Desert materials to URP.\nTerrains updated: {terrains}.\nMissing scripts removed: {removed}.\n\nSave the scene (Ctrl+S).";
        Debug.Log("[Coaster Kit] " + msg.Replace("\n", " "));
        EditorUtility.DisplayDialog("Coaster Kit", msg, "OK");
    }

    static Texture Tex(Material m, string p) { return m.HasProperty(p) ? m.GetTexture(p) : null; }
    static float Num(Material m, string p, float fallback) { return m.HasProperty(p) ? m.GetFloat(p) : fallback; }
}
