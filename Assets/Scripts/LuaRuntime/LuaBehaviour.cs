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

    [Header("Current Lua (preview)")]
    [SerializeField, TextArea(6, 30)] private string _currentLuaPreview;
    public string CurrentLua => _currentLuaPreview;

    // MoonSharp state
    private Script _script;
    private DynValue _self;
    private DynValue _fnStart, _fnUpdate, _fnOnTrigger, _fnOnCollision;

    void Awake()
    {
        // Register proxies if needed
        UserData.RegisterAssembly();
    }

    void Start()
    {
        if (runOnStart)
        {
            var src = !string.IsNullOrEmpty(inlineScript) ? inlineScript :
                      (scriptAsset != null ? scriptAsset.text : "");
            if (!string.IsNullOrEmpty(src))
            {
                LoadScript(src);
                SafeCall(_fnStart);
            }
        }
    }

    void Update()
    {
        if (_script != null && _fnUpdate != null)
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
        SafeCall(_fnOnTrigger);
    }

    public void OnCollisionEnter(Collision col)
    {
        if (_fnOnCollision == null) return;
        try { _script.Call(_fnOnCollision, _self, DynValue.NewString(col.gameObject.name)); }
        catch (ScriptRuntimeException ex)
        {
            Debug.LogError($"[Lua] on_collision error on '{name}': {ex.DecoratedMessage}");
        }
    }

    public void LoadScript(string luaText)
    {
        if (string.IsNullOrEmpty(luaText))
        {
            Debug.LogWarning($"[Lua] Empty script on {name}");
            return;
        }

        try
        {
            _script = new Script(CoreModules.Preset_Complete);
            // Inject common userdata
            _script.Globals["gameObject"] = gameObject;
            _script.Globals["transform"]  = transform;

            // Optional: provide time helper
            _script.Globals["time_seconds"] = (Func<double>)(() => Time.timeAsDouble);

            // Bind DOTween or your tween bridge via LuaProxies
            var dotween = GetComponent<LuaDOTween>();
            if (dotween != null) _script.Globals["dotween"] = dotween;

            // Self table
            var selfTable = new Table(_script);
            selfTable["transform"] = transform;
            _self = DynValue.NewTable(selfTable);

            // Load
            _script.DoString(luaText);

            // Cache functions (required contract)
            _fnStart       = _script.Globals.Get("start");
            _fnUpdate      = _script.Globals.Get("update");
            _fnOnTrigger   = _script.Globals.Get("on_trigger");
            _fnOnCollision = _script.Globals.Get("on_collision");

            // Record last applied for Edit-in-Place
            __RecordLastAppliedLua(luaText);

            // Call start once after load
            SafeCall(_fnStart);
        }
        catch (SyntaxErrorException ex)
        {
            Debug.LogError($"[Lua] Syntax error on '{name}': {ex.DecoratedMessage}");
            _fnStart = _fnUpdate = _fnOnCollision = _fnOnTrigger = null;
        }
        catch (ScriptRuntimeException ex)
        {
            Debug.LogError($"[Lua] Runtime error on '{name}': {ex.DecoratedMessage}");
            _fnStart = _fnUpdate = _fnOnCollision = _fnOnTrigger = null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Lua] General error on '{name}': {ex.Message}");
            _fnStart = _fnUpdate = _fnOnCollision = _fnOnTrigger = null;
        }
    }

    // ===== ADD: keep a copy of applied text so the generator can send CURRENT_LUA =====
    private void __RecordLastAppliedLua(string luaText)
    {
        _currentLuaPreview = luaText ?? "";
    }

    private void SafeCall(DynValue fn)
    {
        if (fn == null || fn.Type != DataType.Function) return;
        try { _script.Call(fn, _self); }
        catch (ScriptRuntimeException ex)
        {
            Debug.LogError($"[Lua] Error on '{name}': {ex.DecoratedMessage}");
        }
    }
}
