// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

namespace BigBlit.ActivePack
{
    public delegate void PressEvent(IPressable pressable);

    /// <summary>
    /// Interface for all pressable objects.
    /// </summary>
    public interface IPressable
    {
        event PressEvent pressedEvent;
        event PressEvent normalEvent;

        bool IsPressed { get; }

        void Press();
        void Normal();

        // Time elapsed since the last press event
        float PressTime { get; }
    }
}
