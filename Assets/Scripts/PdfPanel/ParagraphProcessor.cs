using UnityEngine;

[CreateAssetMenu(fileName = "ParagraphProcessor", menuName = "PDF/Paragraph Processor", order = 0)]
public class ParagraphProcessor : ScriptableObject
{
    // Replace this with your LLM pipeline later.
    public string Process(string paragraph, string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return paragraph;
        // demo: prepend prompt and normalize whitespace
        return $"[prompt: {prompt}]\n\n{paragraph}";
    }
}
