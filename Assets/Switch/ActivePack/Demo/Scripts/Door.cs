using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BigBlit.ActivePack;

namespace BigBlit.ActivePack.Buttons
{
    public class Door : ActiveBehaviour, IValueable
    {
        [SerializeField] bool _isOpen;

        public float Value => _isOpen ? 1.0f : 0.0f;

        public event ValueChangeEvent valueChangeEvent;

        protected override void OnValidate() {
            base.OnValidate();
            valueChangeEvent?.Invoke(this);
        }

        public bool IsOpen {
            get => _isOpen;
            set {
                if (_isOpen != value) {
                    _isOpen = value;
                    valueChangeEvent?.Invoke(this);
                }
            }
        }
    }
}