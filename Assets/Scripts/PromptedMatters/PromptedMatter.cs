// PromptedMatter.cs
// Unified generative MR object that can accept LLM-generated Lua + particle behaviors.
// Stores previous generated Lua and particle JSON so the LLM can "continue editing" next time.

using UnityEngine;

[DisallowMultipleComponent]
public class PromptedMatter : MonoBehaviour
{
    [Header("Identity / Ontology (Optional)")]
    public ObjectProfile objectProfile;

    [Header("Freeform Context")]
    [TextArea(1, 3)]
    public string objectHint = "This is a cup.";

    [Header("Flags")]
    public bool isRealObject = true;

    [Header("Lua Runtime (Optional)")]
    public LuaBehaviour luaBehaviour;

    [Header("Particles")]
    public ParticleSystem meshParticleSystem;

    [Space(8)]
    [Header("Particle Material (Auto-Assign)")]
    public bool autoAssignParticleMaterial = true;
    public bool overrideExistingParticleMaterial = false;
    public Material particleMaterial;
    public string particleMaterialResourcePath = "Materials/PM_Particle_Default";

    // ---------- NEW: previous-state memory ----------
    [Header("Previous Generated State (Saved)")]
    [TextArea(6, 20)] public string lastLuaCode;          // raw lua last applied
    [TextArea(6, 20)] public string lastParticleJson;     // raw JSON (ParticleProfile) last applied
    // ------------------------------------------------

    private Material _cachedResolvedParticleMat;

    // Context sent to LLM
    public string GetLLMContext()
    {
        string profile = objectProfile != null ? objectProfile.ToLLMContext() : "object_profile:{}";
        string freeform = string.IsNullOrWhiteSpace(objectHint) ? "" : $"object_context:\"{EscapeQuotes(objectHint)}\"";
        return string.IsNullOrEmpty(freeform) ? profile : profile + "\n" + freeform;
    }

    // ---------- NEW: previous-state context block ----------
    public string GetPreviousStateContext()
    {
        bool hasLua = !string.IsNullOrEmpty(lastLuaCode);
        bool hasParticle = !string.IsNullOrEmpty(lastParticleJson);

        if (!hasLua && !hasParticle) return "previous_state:{}";

        // Wrap in clear sentinels so the model treats it as reference material
        string luaBlock = hasLua ? $"<previous_lua_begin>\n{lastLuaCode}\n<previous_lua_end>" : "";
        string particleBlock = hasParticle ? $"<previous_particle_json_begin>\n{lastParticleJson}\n<previous_particle_json_end>" : "";

        return $"previous_state:\n{luaBlock}\n{particleBlock}";
    }
    // -------------------------------------------------------

    private string EscapeQuotes(string s) => s.Replace("\"", "\\\"");

    public void ApplyLua(string luaCode)
    {
        if (string.IsNullOrEmpty(luaCode)) return;
        if (luaBehaviour == null)
        {
            Debug.Log("[PromptedMatter] Lua requested but no LuaBehaviour assigned.");
            return;
        }
        luaBehaviour.LoadScript(luaCode, true);
        luaBehaviour.StartRun();
    }

    public void ApplyParticles(ParticleProfile profile)
    {
        if (profile == null) return;

        ParticleSystem target = null;
        switch (profile.add_mode)
        {
            case "none":
            default:
                return;

            case "replace_mesh":
                target = ResolveMeshParticleSystem();
                if (target == null) target = CreateChildParticle("MeshParticleSystem");
                break;

            case "append":
                if (profile.target != null && profile.target.StartsWith("named:"))
                {
                    string name = profile.target.Substring("named:".Length);
                    target = FindChildParticle(name) ?? CreateChildParticle(name);
                }
                else if (profile.target == "mesh")
                {
                    target = CreateChildParticle("ExtraParticleSystem");
                }
                else
                {
                    target = CreateChildParticle("ExtraParticleSystem");
                }
                break;
        }

        if (target == null) return;

        // Ensure material assignment
        TryAssignParticleMaterial(target, /*force=*/overrideExistingParticleMaterial);

        ParticleSystemApplier.Apply(target, profile);

        target.Clear(true);
        target.Play(true);
    }

    // ---------- NEW: remember last applied state ----------
    public void RememberLast(string luaCode, ParticleProfile particle)
    {
        lastLuaCode = string.IsNullOrEmpty(luaCode) ? lastLuaCode : luaCode;
        lastParticleJson = (particle != null) ? JsonUtility.ToJson(particle) : lastParticleJson;
    }
    // ------------------------------------------------------

    // Particle helpers
    private ParticleSystem ResolveMeshParticleSystem()
    {
        if (meshParticleSystem) return meshParticleSystem;
        var found = FindChildParticle("MeshParticleSystem");
        if (found) meshParticleSystem = found;
        return meshParticleSystem;
    }

    private ParticleSystem FindChildParticle(string childName)
    {
        var t = transform.Find(childName);
        if (!t) return null;
        return t.GetComponent<ParticleSystem>();
    }

    private ParticleSystem CreateChildParticle(string childName)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);

        var ps = go.AddComponent<ParticleSystem>();
        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;

        TryAssignParticleMaterial(ps, /*force=*/true);
        return ps;
    }

    private void TryAssignParticleMaterial(ParticleSystem ps, bool force)
    {
        if (!autoAssignParticleMaterial || ps == null) return;
        var pr = ps.GetComponent<ParticleSystemRenderer>();
        if (pr == null) return;

        if (!force && pr.sharedMaterial != null) return;

        var mat = ResolveDefaultParticleMaterial();
        if (mat != null) pr.sharedMaterial = mat;
    }

    private Material ResolveDefaultParticleMaterial()
    {
        if (_cachedResolvedParticleMat != null) return _cachedResolvedParticleMat;

        if (particleMaterial != null)
            return _cachedResolvedParticleMat = particleMaterial;

        if (!string.IsNullOrWhiteSpace(particleMaterialResourcePath))
        {
            var resMat = Resources.Load<Material>(particleMaterialResourcePath);
            if (resMat != null) return _cachedResolvedParticleMat = resMat;
        }

        Shader shader =
            Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
            Shader.Find("HDRP/Unlit") ??
            Shader.Find("Particles/Standard Unlit") ??
            Shader.Find("Legacy Shaders/Particles/Alpha Blended");

        if (shader != null)
        {
            var runtimeMat = new Material(shader);
            if (runtimeMat.HasProperty("_Surface")) runtimeMat.SetFloat("_Surface", 1f);
            if (runtimeMat.HasProperty("_ZWrite")) runtimeMat.SetFloat("_ZWrite", 0f);
            if (runtimeMat.HasProperty("_BaseColor")) runtimeMat.SetColor("_BaseColor", Color.white);
            if (runtimeMat.HasProperty("_Color")) runtimeMat.SetColor("_Color", Color.white);
            _cachedResolvedParticleMat = runtimeMat;
            return _cachedResolvedParticleMat;
        }

        return null;
    }
}
