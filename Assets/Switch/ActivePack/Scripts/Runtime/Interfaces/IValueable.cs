// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

namespace BigBlit.ActivePack
{
    public delegate void ValueChangeEvent(IValueable valueable);

    /// <summary>
    /// Interface for all objects that have position-aware element like buttons heads or levers handles.
    /// </summary>
    public interface IValueable
    {
        /// <summary> Gets actual position of the element.</summary>
        float Value { get; }

        /// <summary> Triggered each time when the position-aware element is changing position</summary>
        event ValueChangeEvent valueChangeEvent;
    }
}
