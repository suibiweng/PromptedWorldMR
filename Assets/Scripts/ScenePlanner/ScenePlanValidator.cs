using UnityEngine;

public static class ScenePlanValidator
{
    public static bool Validate(ScenePlan plan)
    {
        if (plan == null)
        {
            Debug.LogError("[ScenePlanValidator] Plan is null.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(plan.title))
            plan.title = "UNTITLED_SCENE";

        if (plan.objects == null || plan.objects.Count == 0)
        {
            Debug.LogError("[ScenePlanValidator] No objects defined.");
            return false;
        }

        foreach (var o in plan.objects)
        {
            if (string.IsNullOrWhiteSpace(o.id))
                Debug.LogWarning("[ScenePlanValidator] Object missing id.");

            if (string.IsNullOrWhiteSpace(o.primitive))
                o.primitive = "box";

            if (o.count <= 0)
                o.count = 1;
        }

        // systems / ui are optional — no hard failure
        return true;
    }
}
