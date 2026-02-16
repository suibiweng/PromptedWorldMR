// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

namespace BigBlit.ActivePack
{
    public delegate void ClickEvent(IClickable clickable);

    /// <summary>
    /// Interface for all clickable objects.
    /// </summary>
    public interface IClickable
    {
        event ClickEvent clickEvent;
        event ClickEvent longClickEvent;
    }
}
