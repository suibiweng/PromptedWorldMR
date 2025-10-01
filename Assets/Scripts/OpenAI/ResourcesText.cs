// Assets/Scripts/ResourcesText.cs
using UnityEngine;

public static class ResourcesText
{
    /// <summary>
    /// Loads a text file from a Resources path without extension.
    /// Example: Load("LLM/base_prompt") -> Assets/Resources/LLM/base_prompt.txt
    /// </summary>
    public static string Load(string resourcesPathNoExt)
    {
        var ta = Resources.Load<TextAsset>(resourcesPathNoExt);
        return ta ? ta.text : null;
    }
}
