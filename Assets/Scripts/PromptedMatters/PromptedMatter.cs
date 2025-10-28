using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class PromptedMatter : MonoBehaviour
{
    [Header("Identity / Ontology (Optional)")]
    public ObjectProfile objectProfile;

    [Header("Freeform Context")]
    [TextArea(1, 3)] public string objectHint = "This is a cup.";

    [Header("Flags")]
    public bool isRealObject = true;

    [Header("Lua Runtime (Optional)")]
    public LuaBehaviour luaBehaviour;

    [Header("Particles")]
    public ParticleSystem meshParticleSystem;

    [Header("Particle Material (Auto-Assign)")]
    public bool autoAssignParticleMaterial = true;
    public bool overrideExistingParticleMaterial = false;
    public Material particleMaterial;
    public string particleMaterialResourcePath = "Materials/PM_Particle_Default";

    [Header("Previous Generated State (Saved)")]
    [TextArea(6, 20)] public string lastLuaCode;
    [TextArea(6, 20)] public string lastParticleJson;

    // ================== NEW: Visual color target ==================
    [Header("Visual (Optional)")]
    [Tooltip("If set, changeColor() will tint this renderer's material. If null, we try the first Renderer under this object.")]
    public Renderer shapeRenderer;

    // ================== NEW: Proximity Touch like ProgramableObject ==================
    [Header("Proximity Touch")]
    [Tooltip("Left hand transform (e.g., XR rig hand). If empty, you can set at runtime.")]
    public Transform userLeftHand;
    [Tooltip("Right hand transform (e.g., XR rig hand). If empty, you can set at runtime.")]
    public Transform userRightHand;
    [Range(0.01f, 0.5f)] public float TouchingDistance = 0.1f;
    [Tooltip("True while either hand is within TouchingDistance of this object's position.")]
    public bool isTouching;

    [Header("Proximity Touch Events")]
    public UnityEvent onProximityEnter;
    public UnityEvent onProximityExit;

    private bool _prevTouching;

    // =============================================================
    private Material _cachedResolvedParticleMat;

    private void Awake()
    {
        // Try to find a renderer automatically if none assigned
        if (!shapeRenderer)
            shapeRenderer = GetComponentInChildren<Renderer>(includeInactive: true);
    }

    private void Update()
    {
        ProximityTouchingDetection();
    }

    // ================== Public Context for LLM ==================
    public string GetLLMContext()
    {
        string profile = objectProfile != null ? objectProfile.ToLLMContext() : "object_profile:{}";
        string freeform = string.IsNullOrWhiteSpace(objectHint) ? "" : $"object_context:\"{EscapeQuotes(objectHint)}\"";
        return string.IsNullOrEmpty(freeform) ? profile : profile + "\n" + freeform;
    }

    public string GetPreviousStateContext()
    {
        bool hasLua = !string.IsNullOrEmpty(lastLuaCode);
        bool hasParticle = !string.IsNullOrEmpty(lastParticleJson);
        if (!hasLua && !hasParticle) return "previous_state:{}";

        string luaBlock = hasLua ? $"<previous_lua_begin>\n{lastLuaCode}\n<previous_lua_end>" : "";
        string particleBlock = hasParticle ? $"<previous_particle_json_begin>\n{lastParticleJson}\n<previous_particle_json_end>" : "";
        return $"previous_state:\n{luaBlock}\n{particleBlock}";
    }

    private string EscapeQuotes(string s) => s.Replace("\"", "\\\"");

    // ================== Lua / Particles Apply ==================
    public void ApplyLua(string luaCode)
    {
        if (string.IsNullOrEmpty(luaCode)) return;
        if (luaBehaviour == null) { Debug.Log("[PromptedMatter] Lua requested but no LuaBehaviour assigned."); return; }
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
            default: return;

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
                else
                {
                    target = CreateChildParticle("ExtraParticleSystem");
                }
                break;
        }

        if (target == null) return;

        TryAssignParticleMaterial(target, overrideExistingParticleMaterial);
        ParticleSystemApplier.Apply(target, profile);

        target.Clear(true);
        target.Play(true);
    }

    public void RememberLast(string luaCode, ParticleProfile particle)
    {
        lastLuaCode = string.IsNullOrEmpty(luaCode) ? lastLuaCode : luaCode;
        lastParticleJson = (particle != null) ? JsonUtility.ToJson(particle) : lastParticleJson;
    }

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

        TryAssignParticleMaterial(ps, true);
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

        if (particleMaterial != null) return _cachedResolvedParticleMat = particleMaterial;

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
            if (runtimeMat.HasProperty("_Color"))     runtimeMat.SetColor("_Color", Color.white);
            _cachedResolvedParticleMat = runtimeMat;
            return _cachedResolvedParticleMat;
        }
        return null;
    }

    // ================== NEW: Change Color API ==================
    /// <summary>Change the object's color (tints the assigned shapeRenderer, or the first found renderer).</summary>
    public void changeColor(Color color)
    {
        var r = shapeRenderer ? shapeRenderer : GetComponentInChildren<Renderer>(includeInactive: true);
        if (!r) return;

        // Use sharedMaterial so instances in scene each get their own runtime material copy
        var mat = r.material;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
    }

    /// <summary>Convenience: set color from hex like "#FFAA00" or "FFAA00".</summary>
    public void changeColorHex(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out var c)) changeColor(c);
    }

    // ================== NEW: Proximity Touch Detection ==================
    private void ProximityTouchingDetection()
    {
        var selfPos = transform.position;

        bool leftClose = userLeftHand != null &&
                         Vector3.Distance(userLeftHand.position, selfPos) < TouchingDistance;
        bool rightClose = userRightHand != null &&
                          Vector3.Distance(userRightHand.position, selfPos) < TouchingDistance;

        isTouching = leftClose || rightClose;

        if (isTouching && !_prevTouching) onProximityEnter?.Invoke();
        if (!isTouching && _prevTouching) onProximityExit?.Invoke();

        _prevTouching = isTouching;
    }
}
