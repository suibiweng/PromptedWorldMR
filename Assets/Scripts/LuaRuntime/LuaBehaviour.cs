using System;
using MoonSharp.Interpreter;
using UnityEngine;
using LuaProxies; // keep if your proxies live in this namespace

[DisallowMultipleComponent]
public class LuaBehaviour : MonoBehaviour
{
    [Header("Script Source")]
    [TextArea(6, 30)] public string inlineScript;
    public TextAsset scriptAsset;
    public bool runOnStart = true;

    [Header("Run Control")]
    [Tooltip("If false, update() will not call into Lua. Use StartRun()/StopRun()/ToggleRun().")]
    public bool runEnabled = true;

    [Tooltip("When stopping, snap back to the position captured at the moment the script began running.")]
    public bool resetPositionOnStop = true;

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
            // Compile and bind, but do not auto-start yet; we’ll control start below
            LoadScript(src, callStart: false);

            if (runOnStart)
                StartRun();
        }
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

    public void Trigger()
    {
        if (!runEnabled) return;
        SafeCall(_fnOnTrigger);
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
            Debug.LogWarning($"[Lua] Empty script on {name}");
            return;
        }

        _currentLuaPreview = luaText ?? string.Empty;

        try
        {
            CompileBind(luaText);

            if (callStart)
            {
                // ensure we’re in a running state
                runEnabled = true;
                CaptureRunStartPose();
                SafeCall(_fnStart);
            }
        }
        catch (SyntaxErrorException ex)
        {
            Debug.LogError($"[Lua] Syntax error on '{name}': {ex.DecoratedMessage}");
            _fnStart = _fnUpdate = _fnOnCollision = _fnOnTrigger = _fnOnStopOptional = null;
        }
        catch (ScriptRuntimeException ex)
        {
            Debug.LogError($"[Lua] Runtime error on '{name}': {ex.DecoratedMessage}");
            _fnStart = _fnUpdate = _fnOnCollision = _fnOnTrigger = _fnOnStopOptional = null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Lua] General error on '{name}': {ex.Message}");
            _fnStart = _fnUpdate = _fnOnCollision = _fnOnTrigger = _fnOnStopOptional = null;
        }
    }

    // ===== Run control API =====

    /// <summary>Enable/disable running. When turning on, captures the run-start position and calls Lua start(). When turning off, snaps back to the captured position.</summary>
    public void SetRunning(bool on)
    {
        if (on)
        {
            if (!runEnabled)
            {
                runEnabled = true;
                CaptureRunStartPose();
                SafeCall(_fnStart);
            }
        }
        else
        {
            if (runEnabled)
            {
                runEnabled = false;
                // optional user hook
                SafeCall(_fnOnStopOptional);
                if (resetPositionOnStop && _hasRunStartPose)
                    transform.position = _runStartPosition;
            }
        }
    }

    public void StartRun() => SetRunning(true);
    public void StopRun()  => SetRunning(false);
    public void ToggleRun() => SetRunning(!runEnabled);

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

        // Globals: helpers + DOTween bridge
        _script.Globals["dotween"] = new LuaDOTween();
        _script.Globals["log"] = (Action<string>)((s) => Debug.Log($"[Lua] {s}"));
        _script.Globals["warn"] = (Action<string>)((s) => Debug.LogWarning($"[Lua] {s}"));
        _script.Globals["error"] = (Action<string>)((s) => Debug.LogError($"[Lua] {s}"));
        _script.Globals["time_seconds"] = (Func<double>)(() => (double)Time.timeAsDouble);
        _script.Globals["find_go"] = (Func<string, GameObjectProxy>)(name =>
        {
            var go = GameObject.Find(name);
            return go ? new GameObjectProxy(go) : null;
        });

        // Build 'self' table with proxies for THIS GameObject
        _self = DynValue.NewTable(_script).Table;

        var goProxy = new GameObjectProxy(gameObject);
        _self["gameObject"] = UserData.Create(goProxy);
        _self["transform"]  = UserData.Create(goProxy.GetTransformProxy());

        var rbProxy = goProxy.GetRigidbodyProxy();           if (rbProxy != null)       _self["rigidbody"] = UserData.Create(rbProxy);
        var psProxy = goProxy.GetParticleSystemProxy();      if (psProxy != null)       _self["particle"]  = UserData.Create(psProxy);
        var audioProxy = goProxy.GetAudioSourceProxy();      if (audioProxy != null)    _self["audio"]     = UserData.Create(audioProxy);
        var animatorProxy = goProxy.GetAnimatorProxy();      if (animatorProxy != null) _self["animator"]  = UserData.Create(animatorProxy);

        _script.Globals["self"] = _self;

        // Compile & run chunk (defines start/update/etc.)
        _script.DoString(src ?? string.Empty);

        // Cache functions
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
}
