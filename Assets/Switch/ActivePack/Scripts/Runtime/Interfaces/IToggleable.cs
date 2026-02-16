// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

namespace BigBlit.ActivePack
{
    public delegate void ToggleEvent(IToggleable toggleable);

    /// <summary>
    /// Interface for all toggleable objects.
    /// </summary>
    public interface IToggleable
    {
        event ToggleEvent toggleOnEvent;
        event ToggleEvent toggleOffEvent;

        bool IsToggledOn { get; }

        void ToggleOn();
        void ToggleOff();
        void Toggle();
    }
}
