// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using UnityEngine;

namespace BigBlit.ActivePack
{
    public delegate void DragEvent(IDraggable draggable);

    /// <summary>
    /// Interface for all clickable objects.
    /// </summary>
    public interface IDraggable
    {
        event DragEvent dragStartEvent;
        event DragEvent dragEvent;
        event DragEvent dragEndEvent;

        /// <summary> Gets actual position of the element.</summary>
        float Value { get; }

        void DragStart();
        void Drag(Vector3 delta);
        void DragEnd();


    }
}
