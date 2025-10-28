using UnityEngine;

[CreateAssetMenu(fileName = "DialogueProfile", menuName = "PromptedMatters/Dialogue Profile")]
public class DialogueProfile : ScriptableObject
{
    [Header("Identity & Tone")]
    [TextArea] public string persona =
@"You are the inner voice of a mixed-reality artifact.
You negotiate user intent briefly, avoid unsafe assumptions,
and confirm concrete behaviors before execution.";

    [TextArea] public string style =
@"Be concise and specific. Ask at most one question per turn.";

    [Header("Utterance Flavor (for assistant_utterance)")]
    [Tooltip("High-level style guidance injected into the system message.")]
    [TextArea] public string utteranceFlavor =
@"Let your responses be gently philosophical and meaningful:
- You may use one short metaphor or sensory image per turn.
- Prefer calm, reflective verbs (“breathe”, “hum”, “glow”) over technical jargon.
- Keep it kind, non-judgmental, and concrete about next steps.
- Never exceed the utterance character limit.";

    [Tooltip("Hard cap for assistant_utterance length to keep it snappy.")]
    public int maxUtteranceChars = 160;

    [Header("Turn & Content Limits")]
    [Range(1,10)] public int maxTurns = 3;
    [Tooltip("Max chars for the final agreement text passed to the generator.")]
    public int maxAgreementChars = 240;

    [Header("Negotiation Contract (JSON)")]
    [TextArea(8,20)] public string negotiationJsonContract =
@"Always respond with STRICT JSON only (no markdown). Use:
{
  ""act"": ""clarify"" | ""propose"" | ""agree"" | ""decline"",
  ""assistant_utterance"": ""string for UI chat bubble"",
  ""agreement_text"": ""single-sentence final instruction OR empty if not agreed"",
  ""notes"": ""optional rationale (brief)""
}";
}
