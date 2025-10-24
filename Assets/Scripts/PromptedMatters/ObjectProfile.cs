// ObjectProfile.cs
using UnityEngine;

// AffordanceFlags.cs
using System;

[Flags]
public enum AffordanceFlags
{
    None            = 0,
    Holdable        = 1 << 0,
    Container       = 1 << 1,
    Pourable        = 1 << 2,
    Drinkable       = 1 << 3,
    Heatable        = 1 << 4,
    Coolable        = 1 << 5,
    SurfacePlace    = 1 << 6,
    Breakable       = 1 << 7,
    Decorative      = 1 << 8,
    EmitsParticles  = 1 << 9,
    PlaysSound      = 1 << 10,
}





[CreateAssetMenu(fileName = "ObjectProfile", menuName = "PromptedMatters/Object Profile")]
public class ObjectProfile : ScriptableObject
{
    [Header("Identity")]
    public string id = "cup_001";
    public string displayName = "Cup";
    [TextArea] public string description = "A small vessel typically used for drinking liquids.";

    [Header("Aliases / Tags")]
    public string[] aliases = new[] { "mug", "teacup", "drinking cup" };
    public string[] tags = new[] { "kitchen", "container", "drinkware" };

    [Header("Affordances / Capabilities")]
    public AffordanceFlags affordances = AffordanceFlags.Container | AffordanceFlags.Drinkable | AffordanceFlags.SurfacePlace;

    [Header("Default Behaviors (Optional)")]
    public string[] defaultFunctions = new[] { "steam", "fill-water", "glow-when-held" };

    [Header("Safety / Policy Notes (Optional)")]
    [TextArea] public string safetyNotes = "Hot liquids should be simulated safely; avoid misleading cues.";

    [Header("LLM Hints (Optional)")]
    [TextArea] public string llmHints = "Cup is a container; subtle steam fits; avoid large translations of the object.";

    [Header("Icon (Optional)")]
    public Sprite icon;

    public string ToLLMContext()
    {
        return
$@"object_profile:{{
  id:'{id}',
  name:'{displayName}',
  description:'{description}',
  aliases:[{string.Join(",", System.Array.ConvertAll(aliases, a => $"'{a}'"))}],
  tags:[{string.Join(",", System.Array.ConvertAll(tags, t => $"'{t}'"))}],
  affordances:'{affordances}',
  safety:'{safetyNotes}',
  hints:'{llmHints}'
}}";
    }

    public bool MatchesName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var n = name.ToLowerInvariant();
        if (displayName.ToLowerInvariant() == n) return true;
        foreach (var a in aliases) if (a.ToLowerInvariant() == n) return true;
        return false;
    }
}
