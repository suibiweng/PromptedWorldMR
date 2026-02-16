// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using System;
using System.Collections;
using UnityEngine;

namespace BigBlit.ActivePack
{
    [AttributeUsage(AttributeTargets.Class)]
    public class BehaviourInfoAttribute : Attribute
    {
        private string _text;
        public string Text {
            get => _text;
            set => _text = value;
        }

        public BehaviourInfoAttribute(string text) {
            _text = text;
        }
    }
}