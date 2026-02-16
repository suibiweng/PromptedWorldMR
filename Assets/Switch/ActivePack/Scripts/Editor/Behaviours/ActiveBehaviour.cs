// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using System.Collections;
using UnityEngine;
using UnityEditor;
using BigBlit.ActivePack;

namespace BigBlit.ActivePackEditor
{
    [CanEditMultipleObjects()]
    [CustomEditor(typeof(ActiveBehaviour), true)]
    public class ActiveBehaviourEditor : Editor
    {
        string _infoText = null;

        protected virtual void OnEnable() {
            if (target == null)
                return;
            var attributes = target.GetType().GetCustomAttributes(true);
            foreach(var attrib in attributes) {
                if (!(attrib is BehaviourInfoAttribute infoAttribute))
                    continue;
                _infoText = infoAttribute.Text;
            }
        }

        protected virtual void OnDisable() {
            _infoText = null;
        }

        public sealed override void OnInspectorGUI() {
            if (_infoText != null)
                EditorGUILayout.HelpBox(_infoText, MessageType.Info);

            DrawInspectorGUI();

        }

        protected virtual void DrawInspectorGUI() {
            base.OnInspectorGUI();
        }
    }
}
