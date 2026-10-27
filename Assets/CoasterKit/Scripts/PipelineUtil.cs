// PipelineUtil.cs — picks the right shaders for URP (Unity 6.5 default) or the Built-In pipeline.
using UnityEngine;
using UnityEngine.Rendering;

public static class PipelineUtil
{
    /// <summary>True when the project uses URP (or any Scriptable Render Pipeline).</summary>
    public static bool IsSRP => GraphicsSettings.currentRenderPipeline != null;

    public static Shader LitShader()
    {
        Shader s = IsSRP ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
        if (s == null) s = Shader.Find("Universal Render Pipeline/Lit");
        if (s == null) s = Shader.Find("Standard");
        return s;
    }

    /// <summary>Soft particle material: additive (fire, sparks) or alpha-blended (smoke, splash).</summary>
    public static Material MakeParticleMaterial(bool additive, Texture tex)
    {
        Material m;
        Shader urp = IsSRP ? Shader.Find("Universal Render Pipeline/Particles/Unlit") : null;
        if (urp != null)
        {
            m = new Material(urp);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            SetTransparent(m, additive);
            return m;
        }
        string[] names = additive
            ? new[] { "Legacy Shaders/Particles/Additive", "Particles/Additive", "Sprites/Default" }
            : new[] { "Legacy Shaders/Particles/Alpha Blended", "Particles/Alpha Blended", "Sprites/Default" };
        Shader s = null;
        foreach (var n in names) { s = Shader.Find(n); if (s != null) break; }
        m = new Material(s) { mainTexture = tex };
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        return m;
    }

    /// <summary>Switches a URP Lit / Particles material to transparent (alpha or additive).</summary>
    public static void SetTransparent(Material m, bool additive)
    {
        m.SetFloat("_Surface", 1f);                         // 0 = Opaque, 1 = Transparent
        m.SetFloat("_Blend", additive ? 2f : 0f);           // 0 = Alpha, 2 = Additive
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    /// <summary>Opaque lit material with colour, smoothness and metallic (works in URP and Built-In).</summary>
    public static Material MakeLit(Color c, float smoothness = 0.3f, float metallic = 0f)
    {
        var m = new Material(LitShader());
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        return m;
    }

    /// <summary>See-through shiny material (water). Works in URP and Built-In.</summary>
    public static Material MakeWater(Color c)
    {
        var m = MakeLit(c, 0.95f, 0f);
        if (IsSRP)
        {
            SetTransparent(m, false);
        }
        else
        {
            m.SetFloat("_Mode", 3f);   // Standard shader: Transparent
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        return m;
    }
}
