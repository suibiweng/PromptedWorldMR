using System;
using MoonSharp.Interpreter;
using UnityEngine;
using LuaProxies; // delete if your proxies have no namespace

[DisallowMultipleComponent]
public class LuaBehaviour : MonoBehaviour
{
    [Header("Script Source")]
    [TextArea(6, 30)] public string inlineScript;
    public TextAsset scriptAsset;
    public bool runOnStart = true;

    Script _script;
    Table _self;
    DynValue _fnStart, _fnUpdate, _fnOnCollision, _fnOnTrigger;

    void Awake()
    {
        // Build VM and compile initial source
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
            catch (ScriptRuntimeException ex) { Debug.LogError($"[Lua] update() error on '{name}': {ex.DecoratedMessage}"); }
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (_fnOnCollision == null || _fnOnCollision.Type != DataType.Function) return;
        try
        {
            var colProxy = new CollisionProxy(collision);
            _script.Call(_fnOnCollision, _self, UserData.Create(colProxy));
        }
        catch (ScriptRuntimeException ex) { Debug.LogError($"[Lua] on_collision() error on '{name}': {ex.DecoratedMessage}"); }
    }

    void OnTriggerEnter(Collider other)
    {
        if (_fnOnTrigger == null || _fnOnTrigger.Type != DataType.Function) return;
        try
        {
            var otherProxy = new GameObjectProxy(other.gameObject);
            _script.Call(_fnOnTrigger, _self, UserData.Create(otherProxy));
        }
        catch (ScriptRuntimeException ex) { Debug.LogError($"[Lua] on_trigger() error on '{name}': {ex.DecoratedMessage}"); }
    }

    // Public helpers (nice for your generator)
    public void CallStart()    => SafeCall(_fnStart);
    public void CallTrigger()
    {
        if (_fnOnTrigger != null && _fnOnTrigger.Type == DataType.Function)
        {
            try { _script.Call(_fnOnTrigger, _self, DynValue.Nil); }
            catch (ScriptRuntimeException ex) { Debug.LogError($"[Lua] on_trigger() error on '{name}': {ex.DecoratedMessage}"); }
        }
    }

    public void LoadScript(string code, bool callStart = true)
    {
        CompileBind(code ?? string.Empty);
        if (callStart) SafeCall(_fnStart);
    }

    // ===== Core rebuild path =====
    void CompileBind(string src)
    {
        try
        {
            // Fresh VM
            _script = new Script(CoreModules.Preset_Default);

            // Register proxies ONCE per VM build (safe to call multiple times)
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

            // Helpers and DOTween bridge
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

            // Rebuild self **on this new VM**
            _self = DynValue.NewTable(_script).Table;
            var goProxy = new GameObjectProxy(gameObject);
            _self["gameObject"] = UserData.Create(goProxy);
            _self["transform"]  = UserData.Create(goProxy.GetTransformProxy());

            var rbProxy = goProxy.GetRigidbodyProxy();           if (rbProxy != null)     _self["rigidbody"] = UserData.Create(rbProxy);
            var psProxy = goProxy.GetParticleSystemProxy();      if (psProxy != null)     _self["particle"]  = UserData.Create(psProxy);
            var audioProxy = goProxy.GetAudioSourceProxy();      if (audioProxy != null)  _self["audio"]     = UserData.Create(audioProxy);
            var animatorProxy = goProxy.GetAnimatorProxy();      if (animatorProxy != null) _self["animator"] = UserData.Create(animatorProxy);

            _script.Globals["self"] = _self;

            // Compile + bind
            _script.DoString(src ?? string.Empty);

            _fnStart       = _script.Globals.Get("start");
            _fnUpdate      = _script.Globals.Get("update");
            _fnOnCollision = _script.Globals.Get("on_collision");
            _fnOnTrigger   = _script.Globals.Get("on_trigger");

            Debug.Log($"[Lua] Bound on '{name}': start={_fnStart?.Type}, update={_fnUpdate?.Type}, on_collision={_fnOnCollision?.Type}, on_trigger={_fnOnTrigger?.Type}");
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
        catch (ScriptRuntimeException ex) { Debug.LogError($"[Lua] Error on '{name}': {ex.DecoratedMessage}"); }
    }
}
