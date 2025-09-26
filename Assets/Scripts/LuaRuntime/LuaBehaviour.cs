using System;
using MoonSharp.Interpreter;
using UnityEngine;
using LuaProxies; // your proxy namespace
using MoonSharp.Interpreter.Interop;


/// <summary>
/// Runtime-only Lua behaviour for Quest/IL2CPP.
/// Attach to a GameObject. Provide script via Inline or TextAsset,
/// or call LoadScript(string code) at runtime.
/// 
/// Lua lifecycle (all optional):
///   start(self)
///   update(self, dt)
///   on_collision(self, collision)    -- CollisionProxy
///   on_trigger(self, otherGO)        -- GameObjectProxy
/// 
/// self table exposes:
///   self.gameObject  -> GameObjectProxy
///   self.transform   -> TransformProxy
///   self.rigidbody   -> RigidbodyProxy (if found)
///   self.particle    -> ParticleSystemProxy (first on GO/children)
///   self.audio       -> AudioSourceProxy (if found)
///   self.animator    -> AnimatorProxy (if found)
/// </summary>
[DisallowMultipleComponent]
public class LuaBehaviour : MonoBehaviour
{
    [Header("Script Source")]
    [TextArea(6, 30)] public string inlineScript;
    public TextAsset scriptAsset;
    public bool runOnStart = true;

    // MoonSharp state
    Script _script;
    Table _self;
    DynValue _fnStart, _fnUpdate, _fnOnCollision, _fnOnTrigger;

    void Awake()
    {
        // Create VM (safe preset) and register proxies
        _script = new Script(CoreModules.Preset_Default);
        UserData.RegistrationPolicy = InteropRegistrationPolicy.Default;

        UserData.RegisterType<GameObjectProxy>();
        UserData.RegisterType<TransformProxy>();
        UserData.RegisterType<RigidbodyProxy>();
        UserData.RegisterType<ParticleSystemProxy>();
        UserData.RegisterType<AudioSourceProxy>();
        UserData.RegisterType<AnimatorProxy>();
        UserData.RegisterType<TextProxy>();
        UserData.RegisterType<ButtonProxy>();
        UserData.RegisterType<CollisionProxy>();

        // Small stdlib helpers
        _script.Globals["log"] = (Action<string>)((s) => Debug.Log($"[Lua] {s}"));
        _script.Globals["warn"] = (Action<string>)((s) => Debug.LogWarning($"[Lua] {s}"));
        _script.Globals["error"] = (Action<string>)((s) => Debug.LogError($"[Lua] {s}"));
        _script.Globals["time_seconds"] = (Func<double>)(() => (double)Time.timeAsDouble);
        _script.Globals["find_go"] = (Func<string, GameObjectProxy>)(name =>
        {
            var go = GameObject.Find(name);
            return go ? new GameObjectProxy(go) : null;
        });

        // Build self table with your proxies
        _self = DynValue.NewTable(_script).Table;
        var goProxy = new GameObjectProxy(gameObject);
        _self["gameObject"] = UserData.Create(goProxy);
        _self["transform"]  = UserData.Create(goProxy.GetTransformProxy()); // always exists on GO

        var rbProxy = goProxy.GetRigidbodyProxy(); if (rbProxy != null) _self["rigidbody"] = UserData.Create(rbProxy); // :contentReference[oaicite:4]{index=4}
        var psProxy = goProxy.GetParticleSystemProxy(); if (psProxy != null) _self["particle"] = UserData.Create(psProxy); // :contentReference[oaicite:5]{index=5}
        var audioProxy = goProxy.GetAudioSourceProxy(); if (audioProxy != null) _self["audio"] = UserData.Create(audioProxy); // :contentReference[oaicite:6]{index=6}
        var animatorProxy = goProxy.GetAnimatorProxy(); if (animatorProxy != null) _self["animator"] = UserData.Create(animatorProxy); // :contentReference[oaicite:7]{index=7}

        _script.Globals["self"] = _self;

        // Load initial script from asset or inline
        string src = scriptAsset != null ? scriptAsset.text : (inlineScript ?? string.Empty);
        CompileBind(src);
    }

    void Start()
    {
        if (runOnStart) SafeCall(_fnStart);
    }

    void Update()
    {
        if (_fnUpdate != null && _fnUpdate.Type == DataType.Function)
        {
            try { _script.Call(_fnUpdate, _self, DynValue.NewNumber(Time.deltaTime)); }
            catch (ScriptRuntimeException ex)
            {
                Debug.LogError($"[Lua] update() error on '{name}': {ex.DecoratedMessage}");
            }
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (_fnOnCollision == null || _fnOnCollision.Type != DataType.Function) return;
        try
        {
            var colProxy = new CollisionProxy(collision); // GetName/GetContactPoint/GetRigidbodyProxy etc. :contentReference[oaicite:8]{index=8}
            _script.Call(_fnOnCollision, _self, UserData.Create(colProxy));
        }
        catch (ScriptRuntimeException ex)
        {
            Debug.LogError($"[Lua] on_collision() error on '{name}': {ex.DecoratedMessage}");
        }
    }

    void OnTriggerEnter(Collider other)
    {
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

    // --- Public runtime API ---

    /// <summary>Replace the current script at runtime (e.g., from network/UI) and (optionally) call start().</summary>
    public void LoadScript(string code, bool callStart = true)
    {
        CompileBind(code ?? string.Empty);
        if (callStart) SafeCall(_fnStart);
    }

    // --- Internals ---

    void CompileBind(string src)
    {
        try
        {
            // Recreate a clean script state but keep the same self table & helpers
            _script = new Script(CoreModules.Preset_Default);
            UserData.RegistrationPolicy = InteropRegistrationPolicy.Default;

            // re-register proxies & helpers
            UserData.RegisterType<GameObjectProxy>();
            UserData.RegisterType<TransformProxy>();
            UserData.RegisterType<RigidbodyProxy>();
            UserData.RegisterType<ParticleSystemProxy>();
            UserData.RegisterType<AudioSourceProxy>();
            UserData.RegisterType<AnimatorProxy>();
            UserData.RegisterType<TextProxy>();
            UserData.RegisterType<ButtonProxy>();
            UserData.RegisterType<CollisionProxy>();

            _script.Globals["log"] = (Action<string>)((s) => Debug.Log($"[Lua] {s}"));
            _script.Globals["warn"] = (Action<string>)((s) => Debug.LogWarning($"[Lua] {s}"));
            _script.Globals["error"] = (Action<string>)((s) => Debug.LogError($"[Lua] {s}"));
            _script.Globals["time_seconds"] = (Func<double>)(() => (double)Time.timeAsDouble);
            _script.Globals["find_go"] = (Func<string, GameObjectProxy>)(name =>
            {
                var go = GameObject.Find(name);
                return go ? new GameObjectProxy(go) : null;
            });

            _script.Globals["self"] = _self; // reuse existing proxies bound in Awake()

            _script.DoString(src);

            _fnStart       = _script.Globals.Get("start");
            _fnUpdate      = _script.Globals.Get("update");
            _fnOnCollision = _script.Globals.Get("on_collision");
            _fnOnTrigger   = _script.Globals.Get("on_trigger");
        }
        catch (SyntaxErrorException ex)
        {
            Debug.LogError($"[Lua] Syntax error in '{name}': {ex.DecoratedMessage}");
            _fnStart = _fnUpdate = _fnOnCollision = _fnOnTrigger = null;
        }
        catch (ScriptRuntimeException ex)
        {
            Debug.LogError($"[Lua] Runtime init error in '{name}': {ex.DecoratedMessage}");
            _fnStart = _fnUpdate = _fnOnCollision = _fnOnTrigger = null;
        }
    }

    void SafeCall(DynValue fn)
    {
        if (fn == null || fn.Type != DataType.Function) return;
        try { _script.Call(fn, _self); }
        catch (ScriptRuntimeException ex)
        {
            Debug.LogError($"[Lua] Error on '{name}': {ex.DecoratedMessage}");
        }
    }
}
