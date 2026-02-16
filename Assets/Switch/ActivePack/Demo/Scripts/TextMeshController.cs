using BigBlit.ActivePack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
namespace BigBlit.ActivePack.Buttons
{
    [RequireComponent(typeof(TextMesh))]
    public class TextMeshController : MonoBehaviour
    {
        [SerializeField] string _text = "";
        [SerializeField] float _duration = 0.0f;

        private float _curDuration;
        bool _hasText;
        private bool _started;
        TextMesh _textMesh;

        private void Awake() {
            _textMesh = GetComponent<TextMesh>();
        }
        private void Start() {
            _started = true;
        }

        private void Update() {
            if (!_hasText)
                return;

            if (_curDuration >= _duration) {
                _hasText = false;
                _textMesh.text = "";
            }
            else
                _curDuration += Time.deltaTime;

        }

        public void OnChangeText(IPressable eventSource) {
            processEvent(eventSource as Component);
        }

        public void OnChangeText(IClickable eventSource) {
            processEvent(eventSource as Component);
        }

        public void OnChangeText(IToggleable eventSource) {
            processEvent(eventSource as Component);
        }

        void processEvent(Component component) {
            Assert.IsNotNull(component);

            if (!_started)
                return;

            if (_duration > Mathf.Epsilon)
                _hasText = true;
            else
                _hasText = false;

            _curDuration = 0.0f;

            if(_textMesh != null)
                _textMesh.text = component.name.Substring(10) + " " + _text;
        }
    }
}