using System;
using System.Reflection;
using UnityEngine;

/**
 * Auto-finds MRUK's environment depth texture each frame and exposes an RFloat RT.
 * No manual assignment needed. Works across SDK versions by reflecting for Texture fields/properties
 * that contain "Depth" in their names.
 */
public class EnvironmentDepthAutoBinder : MonoBehaviour
{
    [Header("Optional explicit reference (leave null to auto-find)")]
    public Component environmentDepthManager; // e.g., a class like Meta.XR.EnvironmentDepthManager

    [Header("Output (assign this to DepthSegmentationOverlay.depthRT or let it auto-find)")]
    public RenderTexture OutputRFloatRT;      // meters in R channel

    [Header("Debug")]
    public bool logOnceOnConnect = true;

    Texture _sourceDepthTex;
    string _sourceMemberName;
    bool _logged;

    void Awake()
    {
        // Try to auto-find a depth manager component in the scene if not assigned
        if (!environmentDepthManager)
        {
#if UNITY_2023_1_OR_NEWER
            var allBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var allBehaviours = FindObjectsOfType<MonoBehaviour>(true);
#endif
            foreach (var b in allBehaviours)
            {
                var t = b.GetType();
                if (t.Name.IndexOf("EnvironmentDepth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("DepthManager", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (t.Namespace != null && t.Namespace.Contains("Meta") && t.Name.Contains("Depth")))
                {
                    environmentDepthManager = b;
                    break;
                }
            }
        }
    }

    void LateUpdate()
    {
        if (!environmentDepthManager) return;

        // Locate a Texture property/field that contains "Depth"
        if (_sourceDepthTex == null)
        {
            if (!TryResolveDepthTexture(environmentDepthManager, out _sourceDepthTex, out _sourceMemberName))
                return;

            if (logOnceOnConnect && !_logged && _sourceDepthTex)
            {
                Debug.Log($"[DepthBinder] Depth source: {environmentDepthManager.GetType().Name}.{_sourceMemberName} -> " +
                          $"{_sourceDepthTex.width}x{_sourceDepthTex.height} ({_sourceDepthTex.GetType().Name})");
                _logged = true;
            }
        }

        if (_sourceDepthTex == null) return;

        // Ensure RFloat RT matches source size
        EnsureOutputRT(_sourceDepthTex.width, _sourceDepthTex.height);

        // Straight blit: assumes meters in R already. If not, swap to a conversion shader.
        Graphics.Blit(_sourceDepthTex, OutputRFloatRT);
    }

    bool TryResolveDepthTexture(Component mgr, out Texture tex, out string memberName)
    {
        tex = null; memberName = "";
        var type = mgr.GetType();

        // Properties
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var p in props)
        {
            if (p.PropertyType != typeof(Texture) && !p.PropertyType.IsSubclassOf(typeof(Texture))) continue;
            if (p.GetIndexParameters().Length > 0) continue;
            if (LooksLikeDepth(p.Name))
            {
                try
                {
                    var v = p.GetValue(mgr, null) as Texture;
                    if (v != null && v.width > 0 && v.height > 0)
                    {
                        tex = v; memberName = p.Name; return true;
                    }
                }
                catch { }
            }
        }

        // Fields
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var f in fields)
        {
            if (!typeof(Texture).IsAssignableFrom(f.FieldType)) continue;
            if (LooksLikeDepth(f.Name))
            {
                try
                {
                    var v = f.GetValue(mgr) as Texture;
                    if (v != null && v.width > 0 && v.height > 0)
                    {
                        tex = v; memberName = f.Name; return true;
                    }
                }
                catch { }
            }
        }
        return false;

        static bool LooksLikeDepth(string n)
        {
            n = n.ToLowerInvariant();
            return n.Contains("depth") && (n.Contains("tex") || n.Contains("texture") || n.Contains("rt"));
        }
    }

    void EnsureOutputRT(int w, int h)
    {
        if (OutputRFloatRT && OutputRFloatRT.width == w && OutputRFloatRT.height == h) return;

        if (OutputRFloatRT)
        {
            OutputRFloatRT.Release();
            Destroy(OutputRFloatRT);
        }
        OutputRFloatRT = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
        {
            name = "EnvDepth_RFloat",
            filterMode = FilterMode.Point
        };
        OutputRFloatRT.Create();
    }
}
