using UnityEngine;
using UnityEngine.UI;
using MoonSharp.Interpreter;

namespace LuaProxies
{
    // ------------------------------------------------------------
    // Vector3Proxy: supports component-wise edits that write back.
    // Lua can do:
    //   local p = self.transform.position
    //   p.x = p.x + 1        -- writes back automatically
    //   self.transform.position = { x = 0, y = 1, z = 2 } -- whole assign
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
        private readonly Transform _transform;
        public TransformProxy(Transform transform) => _transform = transform;

        // Property-style access (lowercase to match typical Lua generations)
        public Vector3Proxy position
        {
            get => new Vector3Proxy(_transform.position, v => _transform.position = v);
            set
            {
                if (value != null) _transform.position = value.ToVector3();
            }
        }

        public Vector3Proxy localPosition
        {
            get => new Vector3Proxy(_transform.localPosition, v => _transform.localPosition = v);
            set
            {
                if (value != null) _transform.localPosition = value.ToVector3();
            }
        }

        public Vector3Proxy localScale
        {
            get => new Vector3Proxy(_transform.localScale, v => _transform.localScale = v);
            set
            {
                if (value != null) _transform.localScale = value.ToVector3();
            }
        }

        public Vector3Proxy eulerAngles
        {
            get => new Vector3Proxy(_transform.eulerAngles, v => _transform.eulerAngles = v);
            set
            {
                if (value != null) _transform.eulerAngles = value.ToVector3();
            }
        }

        // Helper methods (keep for compatibility with older prompts)
        public Vector3 GetPosition() => _transform.position;
        public void SetPosition(Vector3 position) => _transform.position = position;
        public void SetPosition(double x, double y, double z) =>
            _transform.position = new Vector3((float)x, (float)y, (float)z);

        public Vector3 GetRotation() => _transform.eulerAngles;
        public void SetRotation(Vector3 rotation) => _transform.eulerAngles = rotation;
        public void SetRotation(double x, double y, double z) =>
            _transform.eulerAngles = new Vector3((float)x, (float)y, (float)z);

        public Vector3 GetScale() => _transform.localScale;
        public void SetScale(Vector3 scale) => _transform.localScale = scale;
        public void SetScale(double x, double y, double z) =>
            _transform.localScale = new Vector3((float)x, (float)y, (float)z);

        public void Translate(Vector3 delta) => _transform.Translate(delta, Space.World);
        public void Translate(double x, double y, double z) =>
            _transform.Translate(new Vector3((float)x, (float)y, (float)z), Space.World);

        public void Rotate(Vector3 rotation) => _transform.Rotate(rotation);
        public void Rotate(double x, double y, double z) =>
            _transform.Rotate(new Vector3((float)x, (float)y, (float)z));
    }

    // ------------------------------------------------------------
    // GameObjectProxy
    // ------------------------------------------------------------
    [MoonSharpUserData]
    public class GameObjectProxy
    {
        private readonly GameObject _gameObject;
        public GameObjectProxy(GameObject gameObject) => _gameObject = gameObject;

        public string GetName() => _gameObject.name;
        public void SetName(string name) => _gameObject.name = name;

        public string GetTag() => _gameObject.tag;
        public bool IsActive() => _gameObject.activeSelf;
        public void SetActive(bool active) => _gameObject.SetActive(active);

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
    }

    // ------------------------------------------------------------
    // RigidbodyProxy
    // ------------------------------------------------------------
    [MoonSharpUserData]
    public class RigidbodyProxy
    {
        private readonly Rigidbody _rb;
        public RigidbodyProxy(Rigidbody rb) => _rb = rb;

        public void AddForce(Vector3 force) => _rb.AddForce(force);
        public void SetVelocity(Vector3 velocity) => _rb.linearVelocity = velocity;

        public void AddForce(float x, float y, float z) => _rb.AddForce(new Vector3(x, y, z));
        public void SetVelocity(float x, float y, float z) => _rb.linearVelocity = new Vector3(x, y, z);

        public void AddForce(Vector3 force, string mode)
        {
            if (!System.Enum.TryParse(mode, true, out ForceMode fm)) fm = ForceMode.Force;
            _rb.AddForce(force, fm);
        }

        public void AddForce(float x, float y, float z, string mode)
        {
            if (!System.Enum.TryParse(mode, true, out ForceMode fm)) fm = ForceMode.Force;
            _rb.AddForce(new Vector3(x, y, z), fm);
        }

        public Vector3 GetVelocity() => _rb.linearVelocity;
        public void SetUseGravity(bool useGravity) => _rb.useGravity = useGravity;
        public float GetMass() => _rb.mass;
        public void SetMass(float mass) => _rb.mass = mass;
        public void AddImpulse(float x, float y, float z) => _rb.AddForce(new Vector3(x, y, z), ForceMode.Impulse);
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
        public CollisionProxy(Collision collision) => _collision = collision;

        public GameObjectProxy GetGameObject() =>
            _collision != null && _collision.gameObject != null ? new GameObjectProxy(_collision.gameObject) : null;

        public Vector3 GetContactPoint() =>
            _collision != null && _collision.contacts.Length > 0 ? _collision.contacts[0].point : Vector3.zero;

        public Vector3 GetRelativeVelocity() => _collision != null ? _collision.relativeVelocity : Vector3.zero;

        public string GetName() => _collision != null && _collision.gameObject != null ? _collision.gameObject.name : null;

        public RigidbodyProxy GetRigidbodyProxy()
        {
            if (_collision != null && _collision.rigidbody != null)
                return new RigidbodyProxy(_collision.rigidbody);

            var go = _collision != null ? _collision.gameObject : null;
            if (go != null && go.TryGetComponent<Rigidbody>(out var rb))
                return new RigidbodyProxy(rb);

            return null;
        }
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
}
