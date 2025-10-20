using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using UnityEngine;
using LuaProxies; // keep if your proxies live in this namespace

/// <summary>
/// Runtime Lua behaviour with lazy, safe-guarded proxy binding.
/// - Eagerly binds gameObject & transform.
/// - Lazily exposes self.rigidbody, self.audio, self.particles, self.animator, self.collider, etc.
///   If missing, adds a safe default where sensible (see EnsureComponent<T>() usage).
/// - start(self), update(self, dt), on_trigger(self, other), on_collision(self, col), on_stop(self) supported.
/// </summary>
[DisallowMultipleComponent]
public class LuaBehaviour : MonoBehaviour
{
    [Header("Script Source")]
    [TextArea(6, 30)] public string inlineScript;
    public TextAsset scriptAsset;
    public bool runOnStart = true;

    [Header("Run Control")]
    [Tooltip("If false, Update() will not call into Lua. Use StartRun()/StopRun()/ToggleRun().")]
    public bool runEnabled = true;

    [Tooltip("When stopping, snap back to the position captured at the moment the script began running.")]
    public bool resetPositionOnStop = true;

    [Header("Auto-Add Defaults (used by lazy proxy binding)")]
    public bool addRigidbodyIfMissing = true;
    public bool rbKinematicDefault = true;
    public bool rbUseGravityDefault = false;
    public float rbMassDefault = 1f;

    public bool addAudioSourceIfMissing = true;
    public bool addParticleSystemIfMissing = true;
    public bool addAnimatorIfMissing = false; // harmless but often unnecessary
    public bool addBoxColliderIfMissing = false; // collider shapes matter; default off

    [Header("Current Lua (preview)")]
    [SerializeField, TextArea(6, 30)] private string _currentLuaPreview;
    public string CurrentLua => _currentLuaPreview;

    // MoonSharp VM + bindings
    private Script _script;
    private Table _self;
    private DynValue _fnStart, _fnUpdate, _fnOnTrigger, _fnOnCollision, _fnOnStopOptional;

    // Run-session state
    private bool _hasRunStartPose;
    private Vector3 _runStartPosition;

    // Cache of bound proxies by key (e.g., "rigidbody","audio",...)
    private readonly Dictionary<string, DynValue> _proxyCache = new Dictionary<string, DynValue>();

    void Awake()
    {
        // Safe: okay to call multiple times; registers all public userdata types in loaded assemblies.
        UserData.RegisterAssembly();
    }

    void Start()
    {
        var src = !string.IsNullOrEmpty(inlineScript) ? inlineScript :
                  (scriptAsset != null ? scriptAsset.text : "");

        if (!string.IsNullOrEmpty(src))
        {
            LoadScript(src, callStart: runOnStart);
        }
    }

    public void StartRun()
    {
        runEnabled = true;
        CaptureRunStartPose();
        SafeCall(_fnStart);
    }

    public void StopRun()
    {
        runEnabled = false;
        SafeCall(_fnOnStopOptional);
        if (resetPositionOnStop) ResetToRunStartPosition();
    }

    public void ToggleRun()
    {
        if (runEnabled) StopRun();
        else StartRun();
    }

    void Update()
    {
        if (!runEnabled) return;

        if (_script != null && _fnUpdate != null && _fnUpdate.Type == DataType.Function)
        {
            try
            {
                _script.Call(_fnUpdate, _self, DynValue.NewNumber(Time.deltaTime));
            }
            catch (ScriptRuntimeException ex)
            {
                Debug.LogError($"[Lua] update() error on '{name}': {ex.DecoratedMessage}");
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!runEnabled) return;
        if (_fnOnTrigger == null || _fnOnTrigger.Type != DataType.Function) return;

        try
        {
            var otherProxy = new GameObjectProxy(other.gameObject);
            _script.Call(_fnOnTrigger, _self, UserData.Create(otherProxy));
        }
        catch (ScriptRuntimeException ex)
        {
            Debug.LogError($"[Lua] on_trigger() error on '{name}': {ex.DecoratedMessage}");
        }
    }

    void OnCollisionEnter(Collision col)
    {
        if (!runEnabled) return;
        if (_fnOnCollision == null || _fnOnCollision.Type != DataType.Function) return;

        try
        {
            var colProxy = new CollisionProxy(col);
            _script.Call(_fnOnCollision, _self, UserData.Create(colProxy));
        }
        catch (ScriptRuntimeException ex)
        {
            Debug.LogError($"[Lua] on_collision() error on '{name}': {ex.DecoratedMessage}");
        }
    }

    /// <summary>
    /// Compiles & loads the Lua script and caches function handles.
    /// If callStart==true, also enables running, captures pose, and calls start(self).
    /// </summary>
    public void LoadScript(string luaText, bool callStart = true)
    {
        if (string.IsNullOrEmpty(luaText))
        {
            Debug.LogWarning($"[Lua] Empty script on '{name}'.");
            return;
        }

        CompileBind(luaText);

        if (callStart)
        {
            StartRun();
        }
    }

    public void ResetToRunStartPosition()
    {
        if (_hasRunStartPose)
            transform.position = _runStartPosition;
    }

    // ===== Internals =====

    private void CompileBind(string src)
    {
        // New VM with your proxy-based environment
        _script = new Script(CoreModules.Preset_Default);

        // Register proxy types (safe to call multiple times)
        UserData.RegisterType<Vector3Proxy>();
        UserData.RegisterType<GameObjectProxy>();
        UserData.RegisterType<TransformProxy>();
        UserData.RegisterType<RigidbodyProxy>();
        UserData.RegisterType<ParticleSystemProxy>();
        UserData.RegisterType<AudioSourceProxy>();
        UserData.RegisterType<AnimatorProxy>();
        UserData.RegisterType<TextProxy>();
        UserData.RegisterType<ButtonProxy>();
        UserData.RegisterType<CollisionProxy>();
        UserData.RegisterType<LuaDOTween>();
        UserData.RegisterType<ProgramableObjectProxy>();


        //------extra safty
        UserData.RegisterType<Vector3>();


        // Globals: helpers + DOTween bridge
        _script.Globals["dotween"] = new LuaDOTween();
        _script.Globals["log"] = (Action<string>)((s) => Debug.Log($"[Lua] {s}"));
        _script.Globals["warn"] = (Action<string>)((s) => Debug.LogWarning($"[Lua] {s}"));
        _script.Globals["error"] = (Action<string>)((s) => Debug.LogError($"[Lua] {s}"));

        // Create self table with lazy __index to auto-bind proxies on demand
        _self = new Table(_script);
        var mt = new Table(_script);
        mt.Set("__index", DynValue.NewCallback((ctx, args) =>
        {
            // args[0] = table, args[1] = key
            var key = args[1].CastToString();
            if (string.IsNullOrEmpty(key)) return DynValue.Nil;

            // Serve cache first
            if (_proxyCache.TryGetValue(key, out var cached))
                return cached;

            DynValue bound = DynValue.Nil;

            switch (key)
            {
                case "gameObject":
                    bound = UserData.Create(new GameObjectProxy(gameObject));
                    break;
                case "transform":
                    bound = UserData.Create(new TransformProxy(transform));
                    break;
                case "rigidbody":
                {
                    var rb = EnsureRigidbody();
                    if (rb != null) bound = UserData.Create(new RigidbodyProxy(rb));
                    break;
                }
                case "audio":
                case "audioSource":
                {
                    var au = EnsureComponent<AudioSource>(addIfMissing: addAudioSourceIfMissing, afterAdd: a => {
                        a.playOnAwake = false;
                    });
                    if (au != null) bound = UserData.Create(new AudioSourceProxy(au));
                    break;
                }
                case "particles":
                case "particleSystem":
                {
                    var ps = EnsureComponent<ParticleSystem>(addIfMissing: addParticleSystemIfMissing, afterAdd: p => {
                        var main = p.main; main.loop = false; // reasonable default
                        p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    });
                    if (ps != null) bound = UserData.Create(new ParticleSystemProxy(ps));
                    break;
                }
                case "animator":
                {
                    var an = EnsureComponent<Animator>(addIfMissing: addAnimatorIfMissing);
                    if (an != null) bound = UserData.Create(new AnimatorProxy(an));
                    break;
                }
                case "collider":
                {
                    // Only add if explicitly enabled; default off because shape matters.
                    var col = GetComponent<Collider>();
                    if (col == null && addBoxColliderIfMissing) col = gameObject.AddComponent<BoxCollider>();
                    if (col != null) bound = UserData.Create(new GameObjectProxy(col.gameObject)); // or ColliderProxy if you have one
                    break;
                }
                // UI/Text/Button are "resolve-only" by default (no auto-adds)
                case "text":
                {
                    var txt = GetComponentInChildren<UnityEngine.UI.Text>();
                    if (txt != null) bound = UserData.Create(new TextProxy(txt));
                    break;
                }
                case "button":
                    {
                        var btn = GetComponentInChildren<UnityEngine.UI.Button>();
                        if (btn != null) bound = UserData.Create(new ButtonProxy(btn));
                        break;
                    }
                case "programable":
                case "programmable":
                case "programableObject":
                {
    var po = GetComponent<ProgramableObject>();   // same GO as LuaBehaviour
    if (po != null)
        bound = UserData.Create(new ProgramableObjectProxy(po));
    break;
}

            }

            if (bound.IsNotNil())
                _proxyCache[key] = bound; // cache for future lookups

            return bound; // Nil if not bound; Lua can check 'if self.rigidbody then ... end'
        }));
        _self.MetaTable = mt;

        // Optionally eager-bind must-haves:
        _self["gameObject"] = UserData.Create(new GameObjectProxy(gameObject));
        _self["transform"]  = UserData.Create(new TransformProxy(transform));

        // Load source
        _script.DoString(src);

        // Cache function handles if present
        _fnStart       = _script.Globals.Get("start");
        _fnUpdate      = _script.Globals.Get("update");
        _fnOnTrigger   = _script.Globals.Get("on_trigger");
        _fnOnCollision = _script.Globals.Get("on_collision");
        _fnOnStopOptional = _script.Globals.Get("on_stop");

        // Keep preview synced
        _currentLuaPreview = src ?? string.Empty;

        Debug.Log($"[Lua] Bound on '{name}': start={_fnStart?.Type}, update={_fnUpdate?.Type}, on_trigger={_fnOnTrigger?.Type}, on_collision={_fnOnCollision?.Type}");
    }

    private void CaptureRunStartPose()
    {
        _runStartPosition = transform.position;
        _hasRunStartPose = true;
    }

    private void SafeCall(DynValue fn)
    {
        if (fn == null || fn.Type != DataType.Function) return;
        try { _script.Call(fn, _self); }
        catch (ScriptRuntimeException ex)
        {
            var msg = string.IsNullOrEmpty(ex.DecoratedMessage) ? ex.Message : ex.DecoratedMessage;
            Debug.LogError($"[Lua] Error on '{name}': {msg}");
        }
    }

    // ===== Ensure helpers =====

    /// <summary>
    /// Ensures there's a Rigidbody. If missing and allowed, adds one with safe defaults,
    /// then returns it. Also updates cache entry for 'rigidbody' if already requested.
    /// </summary>
    public Rigidbody EnsureRigidbody()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null && addRigidbodyIfMissing)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = rbKinematicDefault;
            rb.useGravity  = rbUseGravityDefault;
            rb.mass        = rbMassDefault;
        }
        return rb;
    }

    /// <summary>
    /// Generic ensure for other components.
    /// </summary>
    private T EnsureComponent<T>(bool addIfMissing, Action<T> afterAdd = null) where T : Component
    {
        var c = GetComponent<T>();
        if (c == null && addIfMissing)
        {
            c = gameObject.AddComponent<T>();
            afterAdd?.Invoke(c);
        }
        return c;
    }
}

static class DynValueExtensions
{
    public static bool IsNotNil(this DynValue dv)
        => dv != null && dv.Type != DataType.Nil && dv.Type != DataType.Void;
}




