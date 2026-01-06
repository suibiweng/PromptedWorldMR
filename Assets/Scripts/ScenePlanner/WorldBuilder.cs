using System.Collections.Generic;
using UnityEngine;
using PromptedWorld;

[DisallowMultipleComponent]
public class WorldBuilder : MonoBehaviour
{
    [Header("Dependencies")]
    public PromptedWorldManager promptedWorldManager;
    public LayoutRunner layoutRunner;
    public BehaviorBinder behaviorBinder;

    [Header("Layout")]
    public bool applyLayout = true;

    public void Build(ScenePlan plan)
    {
        Debug.Log("[WorldBuilder] Build() CALLED");

        if (promptedWorldManager == null || plan == null)
            return;

        List<Transform> spawned = new List<Transform>();

        foreach (var obj in plan.objects)
            SpawnObjects(obj, spawned);

        // 🔹 Layout integration (UNCHANGED)
        if (applyLayout && layoutRunner != null && spawned.Count > 0)
        {
            string prompt = plan.layout_prompt;

            if (string.IsNullOrWhiteSpace(prompt))
            {
                prompt =
                    "Arrange the objects in a clear, playable layout in front of the user, aligned to the floor.";
            }

            Debug.Log("[WorldBuilder] Applying layout:\n" + prompt);

            layoutRunner.GenerateLayoutForObjects(
                spawned.ToArray(),
                prompt
            );
        }

        // 🔹 ✅ FIX: adapt VirtualObjects → Dictionary<string, ProgramableObject>
        if (behaviorBinder != null)
        {
            Debug.Log("[WorldBuilder] Binding behaviors");

            Dictionary<string, ProgramableObject> programableObjectMap =
                new Dictionary<string, ProgramableObject>();

            foreach (var po in promptedWorldManager.VirtualObjects)
            {
                // Use GameObject name as ID (matches SpawnObjects naming)
                string id = po.gameObject.name;
                if (!programableObjectMap.ContainsKey(id))
                    programableObjectMap.Add(id, po);
            }

            behaviorBinder.BindBehaviors(plan, programableObjectMap);
        }
    }

    private void SpawnObjects(SceneObject obj, List<Transform> spawned)
    {
        int count = Mathf.Max(1, obj.count);
        int shapeCode = MapPrimitiveToShapeCode(obj.primitive);

        for (int i = 0; i < count; i++)
        {
            promptedWorldManager.CreateShape(shapeCode);

            GameObject container = promptedWorldManager.selectedObject;
            if (container == null) continue;
            if (container.GetComponent<ProgramableObject>() == null) continue;

            string name = count > 1 ? $"{obj.id}_{i + 1}" : obj.id;
            container.name = name;
            spawned.Add(container.transform);
        }
    }

    private static readonly Dictionary<string, int> PrimitiveMap =
        new Dictionary<string, int>
    {
        { "cube", PrimitiveFactory.SHAPE_CUBE },
        { "box", PrimitiveFactory.SHAPE_CUBE },
        { "sphere", PrimitiveFactory.SHAPE_SPHERE },
        { "capsule", PrimitiveFactory.SHAPE_CAPSULE },
        { "cylinder", PrimitiveFactory.SHAPE_CYLINDER },
        { "plane", PrimitiveFactory.SHAPE_PLANE },
        { "quad", PrimitiveFactory.SHAPE_QUAD }
    };

    private int MapPrimitiveToShapeCode(string primitive)
    {
        if (string.IsNullOrWhiteSpace(primitive))
            return PrimitiveFactory.SHAPE_CUBE;

        primitive = primitive.ToLowerInvariant();
        return PrimitiveMap.TryGetValue(primitive, out int code)
            ? code
            : PrimitiveFactory.SHAPE_CUBE;
    }
}
