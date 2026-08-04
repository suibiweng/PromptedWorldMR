using UnityEngine;
using UnityEngine.UI;
using MoonSharp.Interpreter;
using System;
using System.Collections.Generic;

namespace LuaProxies
{
    // ============================================================
    // NamePolicy: enforce ALL CAPS and spaces -> underscores
    // ============================================================
    internal static class NamePolicy
    {
        public static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "OBJECT";
            s = s.Trim().Replace(' ', '_');
            while (s.Contains("__")) s = s.Replace("__", "_");
            return s.ToUpperInvariant();
        }
    }

    // ------------------------------------------------------------
    // Vector3Proxy: supports component-wise edits that write back.
    // ------------------------------------------------------------
    [MoonSharpUserData]
    public class Vector3Proxy
    {
        private double _x, _y, _z;
        private System.Action<Vector3> _onWrite; // optional write-through

        public Vector3Proxy() { }
        public Vector3Proxy(Vector3 v) { _x = v.x; _y = v.y; _z = v.z; }
        public Vector3Proxy(Vector3 v, System.Action<Vector3> onWrite)
        {
            _x = v.x; _y = v.y; _z = v.z; _onWrite = onWrite;
        }

        public double x
        {
            get => _x;
            set { _x = value; _onWrite?.Invoke(ToVector3()); }
        }

        public double y
        {
            get => _y;
            set { _y = value; _onWrite?.Invoke(ToVector3()); }
        }

        public double z
        {
            get => _z;
            set { _z = value; _onWrite?.Invoke(ToVector3()); }
        }

        public Vector3 ToVector3() => new Vector3((float)_x, (float)_y, (float)_z);

        // Convenience to set all at once from Lua via numbers
        public void Set(double nx, double ny, double nz)
        {
            _x = nx; _y = ny; _z = nz;
            _onWrite?.Invoke(ToVector3());
        }
    }

    // ------------------------------------------------------------
    // TransformProxy: property-style access + helper methods
    // ------------------------------------------------------------
    [MoonSharpUserData]
    public class TransformProxy
    {
        internal readonly Transform _transform;

        public TransformProxy(Transform t)
        {
            _transform = t;
        }

        // ---- Basics ----
        public string name
        {
            get => _transform.name;
            set => _transform.name = NamePolicy.Normalize(value); // UPDATED
        }

        public GameObjectProxy gameObject => new GameObjectProxy(_transform.gameObject);

        public TransformProxy parent
        {
            get => _transform.parent != null ? new TransformProxy(_transform.parent) : null;
            set => _transform.parent = value != null ? value._transform : null;
        }

        // ---- Vector properties ----
        public object position
        {
            get => new Vector3Proxy(_transform.position, v => _transform.position = v);
            set => _transform.position = CoerceToVector3(value, _transform.position);
        }

        public object localPosition
        {
            get => new Vector3Proxy(_transform.localPosition, v => _transform.localPosition = v);
            set => _transform.localPosition = CoerceToVector3(value, _transform.localPosition);
        }

        public object localScale
        {
            get => new Vector3Proxy(_transform.localScale, v => _transform.localScale = v);
            set => _transform.localScale = CoerceToVector3(value, _transform.localScale);
        }

        public object eulerAngles
        {
            get => new Vector3Proxy(_transform.eulerAngles, v => _transform.eulerAngles = v);
            set => _transform.eulerAngles = CoerceToVector3(value, _transform.eulerAngles);
        }

        // Common directional vectors
        public object forward
        {
            get => new Vector3Proxy(_transform.forward, v => _transform.forward = v);
            set => _transform.forward = CoerceToVector3(value, _transform.forward);
        }

        public object up
        {
            get => new Vector3Proxy(_transform.up, v => _transform.up = v);
            set => _transform.up = CoerceToVector3(value, _transform.up);
        }

        public object right
        {
            get => new Vector3Proxy(_transform.right, v => _transform.right = v);
            set => _transform.right = CoerceToVector3(value, _transform.right);
        }

        // ---- Convenience methods ----

        public void Translate(object delta) =>
            _transform.Translate(CoerceToVector3(delta, Vector3.zero), Space.Self);

        public void TranslateWorld(object delta) =>
            _transform.Translate(CoerceToVector3(delta, Vector3.zero), Space.World);

        public void Rotate(object eulerDelta) =>
            _transform.Rotate(CoerceToVector3(eulerDelta, Vector3.zero), Space.Self);

        public void RotateWorld(object eulerDelta) =>
            _transform.Rotate(CoerceToVector3(eulerDelta, Vector3.zero), Space.World);

        public void LookAt(GameObjectProxy target)
        {
            if (target == null) return;
            _transform.LookAt(target._gameObject.transform);
        }

        public void LookAt(TransformProxy target)
        {
            if (target == null) return;
            _transform.LookAt(target._transform);
        }

        public void LookAt(object worldPoint)
        {
            var p = CoerceToVector3(worldPoint, _transform.position + _transform.forward);
            _transform.LookAt(p);
        }

        // ---- Internal coercion helper ----
        private static Vector3 CoerceToVector3(object any, Vector3 fallback)
        {
            if (any == null) return fallback;

            if (any is Vector3 v3) return v3;
            if (any is Vector3Proxy vp) return vp.ToVector3();

            if (any is DynValue dv)
            {
                if (dv.Type == DataType.UserData && dv.UserData != null)
                {
                    var obj = dv.UserData.Object;
                    if (obj is Vector3Proxy vpu) return vpu.ToVector3();
                    if (obj is Vector3 v32) return v32;
                }

                if (dv.Type == DataType.Table)
                    return TableToVector3(dv.Table, fallback);
            }

            if (any is Table tb)
                return TableToVector3(tb, fallback);

            if (any is string s)
            {
                var parts = s.Split(',');
                if (parts.Length >= 3 &&
                    float.TryParse(parts[0], out var sx) &&
                    float.TryParse(parts[1], out var sy) &&
                    float.TryParse(parts[2], out var sz))
                {
                    return new Vector3(sx, sy, sz);
                }
            }

            return fallback;
        }

        private static Vector3 TableToVector3(Table t, Vector3 fallback)
        {
            if (t == null) return fallback;

            float Read(string name, int idx)
            {
                var dv = t.Get(name);
                if (dv.IsNil()) dv = t.Get(idx);
                if (dv.IsNil()) return 0f;
                try { return Convert.ToSingle(dv.ToObject()); }
                catch { return 0f; }
            }

            return new Vector3(Read("x", 1), Read("y", 2), Read("z", 3));
        }
    }

    // ------------------------------------------------------------
    // GameObjectProxy
    // ------------------------------------------------------------
    [MoonSharpUserData]
    public class GameObjectProxy
    {
        public GameObject _gameObject;
        public GameObjectProxy(GameObject gameObject) => _gameObject = gameObject;

        public string GetName() => _gameObject.name;

        public void SetName(string name)  // UPDATED
        {
            if (_gameObject == null) return;
            _gameObject.name = NamePolicy.Normalize(name);
        }

        public string GetTag() => _gameObject.tag;
        public bool IsActive() => _gameObject.activeSelf;
        public void SetActive(bool active) => _gameObject.SetActive(active);
        public TransformProxy transform => GetTransformProxy();

        public TransformProxy GetTransformProxy() =>
            (_gameObject != null && _gameObject.transform != null)
                ? new TransformProxy(_gameObject.transform)
                : null;

        public bool HasRigidbody() =>
            _gameObject != null && _gameObject.TryGetComponent<Rigidbody>(out _);

        public RigidbodyProxy GetRigidbodyProxy()
        {
            if (_gameObject != null && _gameObject.TryGetComponent<Rigidbody>(out var rb))
                return new RigidbodyProxy(rb);
            return null;
        }

        public AudioSourceProxy GetAudioSourceProxy()
        {
            if (_gameObject != null && _gameObject.TryGetComponent<AudioSource>(out var src))
                return new AudioSourceProxy(src);
            return null;
        }

        public AnimatorProxy GetAnimatorProxy()
        {
            if (_gameObject != null && _gameObject.TryGetComponent<Animator>(out var anim))
                return new AnimatorProxy(anim);
            return null;
        }

        public bool HasParticleSystem()
        {
            if (_gameObject == null) return false;
            if (_gameObject.TryGetComponent<ParticleSystem>(out _)) return true;
            return _gameObject.GetComponentInChildren<ParticleSystem>(true) != null;
        }

        public ParticleSystemProxy GetParticleSystemProxy()
        {
            if (_gameObject == null) return null;

            if (_gameObject.TryGetComponent<ParticleSystem>(out var ps))
                return new ParticleSystemProxy(ps);

            var psChild = _gameObject.GetComponentInChildren<ParticleSystem>(true);
            return psChild != null ? new ParticleSystemProxy(psChild) : null;
        }

        public ParticleSystemProxy[] GetParticleSystemProxiesInChildren(bool includeInactive = true)
        {
            if (_gameObject == null) return System.Array.Empty<ParticleSystemProxy>();
            var systems = _gameObject.GetComponentsInChildren<ParticleSystem>(includeInactive);
            var proxies = new ParticleSystemProxy[systems.Length];
            for (int i = 0; i < systems.Length; i++)
                proxies[i] = new ParticleSystemProxy(systems[i]);
            return proxies;
        }

        public ProgramableObjectProxy GetProgramableObjectProxy()
        {
            if (_gameObject == null) return null;
            var po = _gameObject.GetComponentInParent<ProgramableObject>();
            return po != null ? new ProgramableObjectProxy(po) : null;
        }

        public ProgramableObjectProxy GetProgrammableObjectProxy() => GetProgramableObjectProxy();

        public void SetColor(float r, float g, float b, float a = 1f)
        {
            var po = GetProgramableObjectProxy();
            if (po != null) po.SetColor(r, g, b, a);
        }

        public void SetColorHex(string hex)
        {
            var po = GetProgramableObjectProxy();
            if (po != null) po.SetColorHex(hex);
        }
    }

    // ------------------------------------------------------------
    // SceneLookupProxy
    // ------------------------------------------------------------
    [MoonSharpUserData]
    public class SceneLookupProxy
    {
        private readonly global::PromptedWorldManager _manager;
        private readonly Transform _host;

        public SceneLookupProxy(global::PromptedWorldManager manager, Transform host)
        {
            _manager = manager;
            _host = host;
        }

        public GameObjectProxy FindObject(string reference) => Find(reference);

        public GameObjectProxy Find(string reference)
        {
            var go = ResolveGameObject(reference);
            return go != null ? new GameObjectProxy(go) : null;
        }

        public ProgramableObjectProxy FindProgramableObject(string reference)
        {
            var po = ResolveProgramableObject(reference);
            return po != null ? new ProgramableObjectProxy(po) : null;
        }

        public ProgramableObjectProxy FindProgrammableObject(string reference) => FindProgramableObject(reference);

        public GameObjectProxy FindVirtualObject(string reference)
        {
            var po = ResolveProgramableObject(reference, realFilter: false);
            return po != null ? new GameObjectProxy(po.gameObject) : null;
        }

        public GameObjectProxy FindRealObject(string reference)
        {
            var po = ResolveProgramableObject(reference, realFilter: true);
            return po != null ? new GameObjectProxy(po.gameObject) : null;
        }

        public bool Exists(string reference) => ResolveGameObject(reference) != null;

        public GameObjectProxy GetSelectedObject()
        {
            var selected = _manager != null ? _manager.selectedObject : null;
            return selected != null ? new GameObjectProxy(selected) : null;
        }

        private GameObject ResolveGameObject(string reference)
        {
            var po = ResolveProgramableObject(reference);
            if (po != null)
                return po.gameObject;

            var iot = ResolveIOTObject(reference);
            if (iot != null)
                return iot.gameObject;

            return null;
        }

        private ProgramableObject ResolveProgramableObject(string reference, bool? realFilter = null)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return null;

            var candidates = CollectProgramableObjects(realFilter);
            ProgramableObject exact = null;
            ProgramableObject partial = null;
            bool partialAmbiguous = false;

            foreach (var po in candidates)
            {
                if (po == null)
                    continue;

                if (MatchesProgramableObject(po, reference, exactOnly: true))
                {
                    if (exact != null && exact != po)
                        return null;
                    exact = po;
                }
                else if (MatchesProgramableObject(po, reference, exactOnly: false))
                {
                    if (partial != null && partial != po)
                        partialAmbiguous = true;
                    partial = po;
                }
            }

            if (exact != null)
                return exact;

            return partialAmbiguous ? null : partial;
        }

        private List<ProgramableObject> CollectProgramableObjects(bool? realFilter)
        {
            var list = new List<ProgramableObject>();
            void Add(ProgramableObject po)
            {
                if (po == null || list.Contains(po))
                    return;
                if (realFilter.HasValue && po.isRealObject != realFilter.Value)
                    return;
                list.Add(po);
            }

            if (_manager != null)
            {
                foreach (var po in _manager.VirtualObjects)
                    Add(po);
                foreach (var po in _manager.RealObjects)
                    Add(po);
            }

            foreach (var po in UnityEngine.Object.FindObjectsByType<ProgramableObject>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                Add(po);
            }

            return list;
        }

        private IOTobject ResolveIOTObject(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return null;

            IOTobject exact = null;
            IOTobject partial = null;
            bool partialAmbiguous = false;

            foreach (var iot in UnityEngine.Object.FindObjectsByType<IOTobject>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (iot == null)
                    continue;

                if (MatchesAny(reference, exactOnly: true, iot.DeviceId, iot.DisplayName, iot.gameObject.name))
                {
                    if (exact != null && exact != iot)
                        return null;
                    exact = iot;
                }
                else if (MatchesAny(reference, exactOnly: false, iot.DeviceId, iot.DisplayName, iot.gameObject.name))
                {
                    if (partial != null && partial != iot)
                        partialAmbiguous = true;
                    partial = iot;
                }
            }

            if (exact != null)
                return exact;

            return partialAmbiguous ? null : partial;
        }

        private bool MatchesProgramableObject(ProgramableObject po, string reference, bool exactOnly)
        {
            string label = po.TextBox != null ? po.TextBox.text : "";
            string shapeName = po.shape != null ? po.shape.name : "";
            var iot = po.GetComponentInParent<IOTobject>();

            return MatchesAny(
                reference,
                exactOnly,
                po.id,
                po.gameObject.name,
                label,
                shapeName,
                iot != null ? iot.DeviceId : "",
                iot != null ? iot.DisplayName : ""
            );
        }

        private bool MatchesAny(string reference, bool exactOnly, params string[] terms)
        {
            string needle = NormalizeLookup(reference);
            if (string.IsNullOrEmpty(needle))
                return false;

            foreach (var term in terms)
            {
                string haystack = NormalizeLookup(term);
                if (string.IsNullOrEmpty(haystack))
                    continue;

                if (haystack == needle)
                    return true;

                if (!exactOnly && (haystack.Contains(needle) || needle.Contains(haystack)))
                    return true;
            }

            return false;
        }

        private string NormalizeLookup(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return value.Trim()
                .Replace(' ', '_')
                .Replace("-", "_")
                .ToUpperInvariant();
        }
    }

    // ------------------------------------------------------------
    // RigidbodyProxy
    // ------------------------------------------------------------
    [MoonSharpUserData]
public class RigidbodyProxy
{
    private readonly Rigidbody _rb;
    public RigidbodyProxy(Rigidbody rb) => _rb = rb;

    private void EnsureDynamicIfPhysics()
    {
        if (_rb == null) return;
        if (_rb.isKinematic) _rb.isKinematic = false;
    }

    // -------- Gravity --------
    public void SetUseGravity(bool useGravity)
    {
        if (useGravity) EnsureDynamicIfPhysics();
        _rb.useGravity = useGravity;
    }

    public bool GetUseGravity() => _rb.useGravity;

    // -------- Kinematic --------
    public bool GetIsKinematic() => _rb.isKinematic;
    public void SetIsKinematic(bool k) => _rb.isKinematic = k;
    public void SetKinematic(bool k) => _rb.isKinematic = k;

    // -------- Force --------
    public void AddForce(Vector3 force)
    {
        EnsureDynamicIfPhysics();
        _rb.AddForce(force);
    }

    public void AddForce(Vector3 force, string mode)
    {
        EnsureDynamicIfPhysics();
        if (!Enum.TryParse(mode, true, out ForceMode fm)) fm = ForceMode.Force;
        _rb.AddForce(force, fm);
    }

    public void AddForce(float x, float y, float z)
    {
        EnsureDynamicIfPhysics();
        _rb.AddForce(new Vector3(x, y, z));
    }

    public void AddForce(float x, float y, float z, string mode)
    {
        EnsureDynamicIfPhysics();
        if (!Enum.TryParse(mode, true, out ForceMode fm)) fm = ForceMode.Force;
        _rb.AddForce(new Vector3(x, y, z), fm);
    }

    public void AddImpulse(float x, float y, float z)
    {
        EnsureDynamicIfPhysics();
        _rb.AddForce(new Vector3(x, y, z), ForceMode.Impulse);
    }

    // -------- Velocity --------
    public void SetVelocity(Vector3 v)
    {
        EnsureDynamicIfPhysics();
        SetRbVelocity(v);
    }

    public void SetVelocity(float x, float y, float z)
    {
        EnsureDynamicIfPhysics();
        SetRbVelocity(new Vector3(x, y, z));
    }

    public Vector3Proxy GetVelocity()
    {
        return new Vector3Proxy(GetRbVelocity());
    }

    // -------- Mass --------
    public float GetMass() => _rb.mass;
    public void SetMass(float m) => _rb.mass = m;

    private Vector3 GetRbVelocity()
    {
#if UNITY_6000_0_OR_NEWER
        return _rb.linearVelocity;
#else
        return _rb.velocity;
#endif
    }

    private void SetRbVelocity(Vector3 v)
    {
#if UNITY_6000_0_OR_NEWER
        _rb.linearVelocity = v;
#else
        _rb.velocity = v;
#endif
    }
}


    // ------------------------------------------------------------
    // AudioSourceProxy
    // ------------------------------------------------------------
    [MoonSharpUserData]
    public class AudioSourceProxy
    {
        private readonly AudioSource _src;
        public AudioSourceProxy(AudioSource src) => _src = src;

        public void Play() => _src.Play();
        public void Stop() => _src.Stop();
        public void Pause() => _src.Pause();
        public void SetVolume(float volume) => _src.volume = volume;
        public void SetLoop(bool loop) => _src.loop = loop;
    }

    // ------------------------------------------------------------
    // TextProxy
    // ------------------------------------------------------------
    [MoonSharpUserData]
    public class TextProxy
    {
        private readonly Text _text;
        public TextProxy(Text text) => _text = text;

        public void SetText(string text) => _text.text = text;
        public string GetText() => _text.text;
        public void SetColor(Color color) => _text.color = color;
    }

    // ------------------------------------------------------------
    // ButtonProxy
    // ------------------------------------------------------------
    [MoonSharpUserData]
    public class ButtonProxy
    {
        private readonly Button _btn;
        public ButtonProxy(Button button) => _btn = button;

        public void SetInteractable(bool state) => _btn.interactable = state;
        public bool IsInteractable() => _btn.interactable;
    }

    // ------------------------------------------------------------
    // CollisionProxy
    // ------------------------------------------------------------
    [MoonSharpUserData]

public class CollisionProxy
{
    private readonly Collision _collision;
    public CollisionProxy(Collision c) => _collision = c;

    public GameObjectProxy GetGameObject()
        => new GameObjectProxy(_collision.gameObject);

    public Vector3Proxy GetContactPoint()
        => new Vector3Proxy(_collision.contacts.Length > 0 
            ? _collision.contacts[0].point 
            : Vector3.zero);

    public Vector3Proxy GetContactNormal()
        => new Vector3Proxy(_collision.contacts.Length > 0
            ? _collision.contacts[0].normal
            : Vector3.up);

    public int GetContactCount()
        => _collision.contacts?.Length ?? 0;

    // ⭐ MISSING METHOD ADDED
    public Vector3Proxy GetContactPointAt(int i)
    {
        if (_collision.contacts == null ||
            i < 0 || i >= _collision.contacts.Length)
            return new Vector3Proxy(Vector3.zero);

        return new Vector3Proxy(_collision.contacts[i].point);
    }

    public Vector3Proxy GetRelativeVelocity()
        => new Vector3Proxy(_collision.relativeVelocity);

    public RigidbodyProxy GetRigidbodyProxy()
        => _collision.rigidbody != null
            ? new RigidbodyProxy(_collision.rigidbody)
            : null;

    public GameObjectProxy GetRootGameObject()
        => new GameObjectProxy(_collision.transform.root.gameObject);

    public string GetRootName()
        => _collision.transform.root.name;
}


    // ------------------------------------------------------------
    // ParticleSystemProxy
    // ------------------------------------------------------------
    [MoonSharpUserData]
    public class ParticleSystemProxy
    {
        private readonly ParticleSystem _ps;
        public ParticleSystemProxy(ParticleSystem ps) => _ps = ps;

        public void Play() => _ps.Play();
        public void Stop() => _ps.Stop();
        public bool IsPlaying() => _ps.isPlaying;
        public void SetLooping(bool loop)
        {
            var main = _ps.main;
            main.loop = loop;
        }
    }

    // ------------------------------------------------------------
    // AnimatorProxy
    // ------------------------------------------------------------
    [MoonSharpUserData]
    public class AnimatorProxy
    {
        private readonly Animator _anim;
        public AnimatorProxy(Animator anim) => _anim = anim;

        public void Play(string stateName) => _anim.Play(stateName);
        public void SetBool(string name, bool value) => _anim.SetBool(name, value);
        public void SetTrigger(string name) => _anim.SetTrigger(name);
    }

    // =========================
    // ProgramableObjectProxy
    // =========================
    [MoonSharpUserData]
    public class ProgramableObjectProxy
    {
        private readonly ProgramableObject _po;

        public ProgramableObjectProxy(ProgramableObject po)
        {
            _po = po;
        }

        // ---- Identity / flags ----
        public string GetId() => _po != null ? _po.id : "";
        public bool GetIsRealObject() => _po != null && _po.isRealObject;

        // ---- Label / visuals ----
        public void SetLabel(string label)
        {
            if (_po != null) _po.setLabel(label);
        }

        // RGBA 0..1
        public void SetColor(float r, float g, float b, float a = 1f)
        {
            if (_po != null) _po.changeColor(new UnityEngine.Color(r, g, b, a));
        }

        public void ChangeColor(float r, float g, float b, float a = 1f) => SetColor(r, g, b, a);

        public void SetColorHex(string hex)
        {
            if (_po == null || string.IsNullOrWhiteSpace(hex)) return;
            if (!hex.StartsWith("#", StringComparison.Ordinal)) hex = "#" + hex;
            if (ColorUtility.TryParseHtmlString(hex, out var color)) _po.changeColor(color);
        }

        public GameObjectProxy GetShape()
        {
            var shapeObject = _po != null ? _po.GetLuaShapeObject() : null;
            return shapeObject != null ? new GameObjectProxy(shapeObject) : null;
        }

        public TransformProxy GetShapeTransform()
        {
            var shapeTransform = _po != null ? _po.GetLuaShapeTransform() : null;
            return shapeTransform != null ? new TransformProxy(shapeTransform) : null;
        }

        public void SetOutline(bool on)
        {
            if (_po != null) _po.SetOutlineVisible(on);
        }

        public void OutlineOn() => SetOutline(true);
        public void OutlineOff() => SetOutline(false);

        // ---- Highlight controls (sticky latch) ----
        public void ToggleLatchedHighlight()
        {
            if (_po == null) return;
            _po.SetLatchedHighlight(!_po.highlightLatched);
        }

        public void SetLatchedHighlight(bool on)
        {
            if (_po == null) return;
            _po.SetLatchedHighlight(on);
        }

        public void ClearLatchedHighlight()
        {
            if (_po == null) return;
            _po.ClearLatchedHighlight();
        }

        public void RefreshHighlight()
        {
            if (_po == null) return;
            _po.SetLatchedHighlight(_po.highlightLatched);
        }

        public bool GetHighlightOnHover() => _po != null && _po.highlightOnHover;
        public void SetHighlightOnHover(bool enable)
        {
            if (_po == null) return;
            _po.highlightOnHover = enable;
            _po.SetLatchedHighlight(_po.highlightLatched);
        }

        public bool GetStickyHighlightEnabled() => _po != null && _po.stickyHighlightEnabled;
        public void SetStickyHighlightEnabled(bool enable)
        {
            if (_po == null) return;
            _po.stickyHighlightEnabled = enable;
            _po.SetLatchedHighlight(_po.highlightLatched);
        }

        public bool GetIsTouching() => _po != null && _po.isToching;
        public bool IsTouching() => GetIsTouching();
        public float GetTouchDistance() => _po != null ? _po.Touchingdistance : 0f;
        public void SetTouchDistance(float d)
        {
            if (_po == null) return;
            _po.Touchingdistance = Mathf.Max(0.001f, d);
        }

        public float GetUserHeadDistance() => _po != null ? _po.GetUserHeadDistance() : float.PositiveInfinity;
        public bool IsUserClose(float distance = 1f) => _po != null && _po.IsUserClose(distance);
        public bool GetIsUserClose(float distance = 1f) => IsUserClose(distance);

        public bool GetIsSelected() => _po != null && _po._selected;
        public bool IsSelected() => GetIsSelected();
        public bool GetIsHovering() => _po != null && _po._hovering;
        public bool IsHovering() => GetIsHovering();

        public void SetImage(Texture tex)
        {
            if (_po != null) _po.setImage(tex);
        }
    }

    [MoonSharpUserData]
    public class UserAnchorProxy
    {
        private readonly global::PromptedWorldManager _manager;
        private readonly Transform _target;

        public UserAnchorProxy(global::PromptedWorldManager manager, Transform target)
        {
            _manager = manager;
            _target = target;
        }

        public bool HasHead() => _manager != null && _manager.userHead != null;
        public bool HasLeftHand() => _manager != null && _manager.userLeftHand != null;
        public bool HasRightHand() => _manager != null && _manager.userRightHand != null;
        public bool HasHands() => HasLeftHand() && HasRightHand();

        public Vector3Proxy GetHeadPosition()
        {
            return HasHead()
                ? new Vector3Proxy(_manager.userHead.position)
                : new Vector3Proxy(Vector3.zero);
        }

        public Vector3Proxy GetLeftHandPosition()
        {
            return HasLeftHand()
                ? new Vector3Proxy(_manager.userLeftHand.position)
                : new Vector3Proxy(Vector3.zero);
        }

        public Vector3Proxy GetRightHandPosition()
        {
            return HasRightHand()
                ? new Vector3Proxy(_manager.userRightHand.position)
                : new Vector3Proxy(Vector3.zero);
        }

        public float GetHeadDistanceToThisObject()
        {
            return HasHead() && _target != null
                ? Vector3.Distance(_manager.userHead.position, _target.position)
                : float.PositiveInfinity;
        }

        public float GetLeftHandDistanceToThisObject()
        {
            return HasLeftHand() && _target != null
                ? Vector3.Distance(_manager.userLeftHand.position, _target.position)
                : float.PositiveInfinity;
        }

        public float GetRightHandDistanceToThisObject()
        {
            return HasRightHand() && _target != null
                ? Vector3.Distance(_manager.userRightHand.position, _target.position)
                : float.PositiveInfinity;
        }

        public float GetNearestHandDistanceToThisObject()
        {
            return Mathf.Min(GetLeftHandDistanceToThisObject(), GetRightHandDistanceToThisObject());
        }

        public float GetHandDistance()
        {
            return HasHands()
                ? Vector3.Distance(_manager.userLeftHand.position, _manager.userRightHand.position)
                : float.PositiveInfinity;
        }

        public bool IsHandsClose(float distance = 0.12f)
        {
            return GetHandDistance() <= Mathf.Max(0.001f, distance);
        }

        public bool AreHandsClose(float distance = 0.12f) => IsHandsClose(distance);

        public bool IsHeadCloseToThisObject(float distance = 1f)
        {
            return GetHeadDistanceToThisObject() <= Mathf.Max(0.001f, distance);
        }

        public bool IsLeftHandCloseToThisObject(float distance = 0.25f)
        {
            return GetLeftHandDistanceToThisObject() <= Mathf.Max(0.001f, distance);
        }

        public bool IsRightHandCloseToThisObject(float distance = 0.25f)
        {
            return GetRightHandDistanceToThisObject() <= Mathf.Max(0.001f, distance);
        }

        public bool IsAnyHandCloseToThisObject(float distance = 0.25f)
        {
            return GetNearestHandDistanceToThisObject() <= Mathf.Max(0.001f, distance);
        }

        public bool IsClose(float distance = 1f) => IsHeadCloseToThisObject(distance);

        public float MapHeadDistanceToThisObject(float nearDistance, float farDistance, float nearValue, float farValue)
        {
            return MapRange(GetHeadDistanceToThisObject(), nearDistance, farDistance, nearValue, farValue);
        }

        public float MapNearestHandDistanceToThisObject(float nearDistance, float farDistance, float nearValue, float farValue)
        {
            return MapRange(GetNearestHandDistanceToThisObject(), nearDistance, farDistance, nearValue, farValue);
        }

        public float MapHandDistance(float nearDistance, float farDistance, float nearValue, float farValue)
        {
            return MapRange(GetHandDistance(), nearDistance, farDistance, nearValue, farValue);
        }

        private static float MapRange(float value, float inMin, float inMax, float outMin, float outMax)
        {
            if (float.IsInfinity(value) || float.IsNaN(value))
                return outMin;

            float t = Mathf.InverseLerp(inMin, inMax, value);
            return Mathf.Lerp(outMin, outMax, t);
        }
    }

    [MoonSharpUserData]
    public class PromptedMatterProxy
    {
        private readonly PromptedMatter _pm;
        public PromptedMatterProxy(PromptedMatter pm) { _pm = pm; }

        public string GetName() => _pm ? _pm.name : "";
        public string GetHint() => _pm ? _pm.objectHint : "";

        public void SetColor(float r, float g, float b, float a = 1f)
        {
            if (_pm == null) return;
            _pm.changeColor(new Color(r, g, b, a));
        }
        public void SetColorHex(string hex)
        {
            if (_pm == null) return;
            _pm.changeColorHex(hex);
        }

        public bool GetIsTouching() => _pm != null && _pm.isTouching;
        public float GetTouchDistance() => _pm != null ? _pm.TouchingDistance : 0f;
        public void SetTouchDistance(float d) { if (_pm == null) return; _pm.TouchingDistance = Mathf.Max(0.001f, d); }

        public bool HasMeshParticles()
        {
            return _pm != null && _pm.meshParticleSystem != null;
        }
        public void PlayMeshParticles()
        {
            if (_pm?.meshParticleSystem != null) _pm.meshParticleSystem.Play(true);
        }
        public void StopMeshParticles()
        {
            if (_pm?.meshParticleSystem != null) _pm.meshParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
    [MoonSharpUserData]
    public class TouchpadInputProxy
    {
        private readonly TouchpadInputState _state;

        public TouchpadInputProxy(TouchpadInputState state)
        {
            _state = state;
        }

        // Normalized coordinates [0,1]
        public float x
        {
            get { return _state != null ? _state.normalizedPosition.x : 0f; }
        }

        public float y
        {
            get { return _state != null ? _state.normalizedPosition.y : 0f; }
        }

        // Simple state flags
        public bool is_inside
        {
            get { return _state != null && _state.isInside; }
        }

        public bool pressed
        {
            get { return _state != null && _state.isPressed; }
        }

        public bool dragging
        {
            get { return _state != null && _state.isDragging; }
        }

        public string phase
        {
            get { return _state != null ? _state.phase.ToString() : "None"; }
        }
    }




    [MoonSharpUserData]
public class IoTProxy
{
    IOTManager manager;

    public IoTProxy(IOTManager m)
    {
        manager = m;
    }

    public bool On(string id)
    {
        if (manager == null)
            return false;

        var result = manager.TurnOn(id);
        return result == IoTCommandResult.Success || result == IoTCommandResult.NoStateChange;
    }

    public bool Off(string id)
    {
        if (manager == null)
            return false;

        var result = manager.TurnOff(id);
        return result == IoTCommandResult.Success || result == IoTCommandResult.NoStateChange;
    }

    public string Send(string id, string cmd)
    {
        return manager != null
            ? manager.SendCommand(id, cmd).ToString()
            : IoTCommandResult.DeviceNotFound.ToString();
    }

    public LightBulbProxy LightBulb(string id)
    {
        return new LightBulbProxy(manager, id);
    }

    public LightBulbProxy Lightbuld(string id)
    {
        return LightBulb(id);
    }

    public string SetRGB(string id, double red, double green, double blue)
    {
        return manager != null
            ? manager.SetLightBulbRGB(id, red, green, blue).ToString()
            : IoTCommandResult.DeviceNotFound.ToString();
    }

    public string setRGB(string id, double red, double green, double blue) => SetRGB(id, red, green, blue);

    public string SetRed(string id, double red)
    {
        return manager != null
            ? manager.SetLightBulbRed(id, red).ToString()
            : IoTCommandResult.DeviceNotFound.ToString();
    }

    public string setRed(string id, double red) => SetRed(id, red);

    public string SetGreen(string id, double green)
    {
        return manager != null
            ? manager.SetLightBulbGreen(id, green).ToString()
            : IoTCommandResult.DeviceNotFound.ToString();
    }

    public string setGreen(string id, double green) => SetGreen(id, green);

    public string SetBlue(string id, double blue)
    {
        return manager != null
            ? manager.SetLightBulbBlue(id, blue).ToString()
            : IoTCommandResult.DeviceNotFound.ToString();
    }

    public string setBlue(string id, double blue) => SetBlue(id, blue);
}

[MoonSharpUserData]
public class LightBulbProxy
{
    private readonly IOTManager manager;
    private readonly string id;

    public LightBulbProxy(IOTManager manager, string id)
    {
        this.manager = manager;
        this.id = id;
    }

    public string SetRGB(double red, double green, double blue)
    {
        return manager != null
            ? manager.SetLightBulbRGB(id, red, green, blue).ToString()
            : IoTCommandResult.DeviceNotFound.ToString();
    }

    public string setRGB(double red, double green, double blue) => SetRGB(red, green, blue);

    public string SetRed(double red)
    {
        return manager != null
            ? manager.SetLightBulbRed(id, red).ToString()
            : IoTCommandResult.DeviceNotFound.ToString();
    }

    public string setRed(double red) => SetRed(red);

    public string SetGreen(double green)
    {
        return manager != null
            ? manager.SetLightBulbGreen(id, green).ToString()
            : IoTCommandResult.DeviceNotFound.ToString();
    }

    public string setGreen(double green) => SetGreen(green);

    public string SetBlue(double blue)
    {
        return manager != null
            ? manager.SetLightBulbBlue(id, blue).ToString()
            : IoTCommandResult.DeviceNotFound.ToString();
    }

    public string setBlue(double blue) => SetBlue(blue);
}


[MoonSharpUserData]

public class PokeButtonProxy
{
    private readonly PokeButton _btn;

    public PokeButtonProxy(PokeButton btn)
    {
        _btn = btn;
    }

    public bool is_pressed => _btn != null && _btn.IsPressed;
    public bool pressed_this_frame => _btn != null && _btn.WasPressedThisFrame;
    public bool released_this_frame => _btn != null && _btn.WasReleasedThisFrame;
    public bool clicked_this_frame => _btn != null && _btn.WasClickedThisFrame;

    public bool toggle => _btn != null && _btn.ToggleState;
    public bool toggle_mode => _btn != null && _btn.ToggleMode;

    public bool IsPressed()
    {
        return _btn != null && _btn.IsPressed;
    }

    public bool WasPressed()
    {
        return _btn != null && _btn.WasPressedThisFrame;
    }

    public bool WasReleased()
    {
        return _btn != null && _btn.WasReleasedThisFrame;
    }

    public bool WasClicked()
    {
        return _btn != null && _btn.WasClickedThisFrame;
    }

    public bool GetToggleState()
    {
        return _btn != null && _btn.ToggleState;
    }

    public void SetToggleMode(bool enabled)
    {
        if (_btn != null)
            _btn.SetToggleMode(enabled);
    }
}




    [MoonSharpUserData]
    public class CustomCollisionProxy
    {
        private readonly GameObject _self;
        private readonly GameObject _other;

        public CustomCollisionProxy(GameObject self, GameObject other)
        {
            _self = self;
            _other = other;
        }

        // -----------------------------
        // Basic identity
        // -----------------------------
        public GameObjectProxy GetGameObject()
            => _other != null ? new GameObjectProxy(_other) : null;

        public GameObjectProxy GetRootGameObject()
            => _other != null
                ? new GameObjectProxy(_other.transform.root.gameObject)
                : null;

        public string GetRootName()
            => _other != null
                ? _other.transform.root.name
                : "";

        public string GetRuntimeName()
            => _other != null ? _other.name : "";

        public string GetObjectId()
        {
            var iot = GetIOTObject();
            if (iot != null)
                return iot.DeviceId;

            var po = GetProgramableObject();
            if (po != null && !string.IsNullOrWhiteSpace(po.id))
                return po.id;

            return GetRootName();
        }

        public string GetDisplayLabel()
        {
            var iot = GetIOTObject();
            if (iot != null)
                return iot.DisplayName;

            var po = GetProgramableObject();
            if (po != null && po.TextBox != null && !string.IsNullOrWhiteSpace(po.TextBox.text))
                return po.TextBox.text.Trim();

            if (po != null && po.shape != null && !string.IsNullOrWhiteSpace(po.shape.name))
                return po.shape.name.Trim();

            return GetRootName();
        }

        public string GetIdentityText()
        {
            return $"{GetObjectId()} {GetDisplayLabel()} {GetRuntimeName()} {GetRootName()}";
        }

        public bool Matches(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return false;

            string needle = NormalizeIdentity(query);
            if (string.IsNullOrEmpty(needle))
                return false;

            return IdentityMatches(GetObjectId(), needle) ||
                   IdentityMatches(GetDisplayLabel(), needle) ||
                   IdentityMatches(GetIOTDeviceId(), needle) ||
                   IdentityMatches(GetIOTDisplayName(), needle) ||
                   IdentityMatches(GetRuntimeName(), needle) ||
                   IdentityMatches(GetRootName(), needle);
        }

        public string GetIOTDeviceId()
        {
            var iot = GetIOTObject();
            return iot != null ? iot.DeviceId : "";
        }

        public string GetIOTDisplayName()
        {
            var iot = GetIOTObject();
            return iot != null ? iot.DisplayName : "";
        }

        // -----------------------------
        // Physics-like data (not available)
        // Return safe defaults
        // -----------------------------
        public Vector3Proxy GetContactPoint()
            => new Vector3Proxy(Vector3.zero);

        public Vector3Proxy GetContactNormal()
            => new Vector3Proxy(Vector3.up);

        public int GetContactCount()
            => 0;

        public Vector3Proxy GetContactPointAt(int i)
            => new Vector3Proxy(Vector3.zero);

        public Vector3Proxy GetRelativeVelocity()
            => new Vector3Proxy(Vector3.zero);

        // -----------------------------
        // Rigidbody (optional)
        // -----------------------------
        public RigidbodyProxy GetRigidbodyProxy()
        {
            if (_other != null &&
                _other.TryGetComponent<Rigidbody>(out var rb))
            {
                return new RigidbodyProxy(rb);
            }

            return null;
        }

        private ProgramableObject GetProgramableObject()
        {
            if (_other == null)
                return null;

            return _other.GetComponentInParent<ProgramableObject>();
        }

        private IOTobject GetIOTObject()
        {
            if (_other == null)
                return null;

            return _other.GetComponentInParent<IOTobject>();
        }

        private static bool IdentityMatches(string value, string normalizedNeedle)
        {
            string normalizedValue = NormalizeIdentity(value);
            return !string.IsNullOrEmpty(normalizedValue) &&
                   (normalizedValue == normalizedNeedle ||
                    normalizedValue.StartsWith(normalizedNeedle + "_") ||
                    normalizedValue.Contains(normalizedNeedle));
        }

        private static string NormalizeIdentity(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? ""
                : value.Trim().Replace(' ', '_').ToUpperInvariant();
        }
    }




}
