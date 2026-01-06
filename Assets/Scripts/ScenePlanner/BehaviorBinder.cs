using UnityEngine;
using System.Collections.Generic;

public class BehaviorBinder : MonoBehaviour
{
    [Header("Dependencies")]
    public OpenAILuaGenerator luaGenerator;

    public void BindBehaviors(
        ScenePlan plan,
        Dictionary<string, ProgramableObject> programableObjects
    )
    {
        if (luaGenerator == null || plan == null)
            return;

        var batch = luaGenerator.GenerateBatchForScenePlan(plan);
        if (batch == null || batch.lua_assignments == null)
        {
            Debug.LogWarning("[BehaviorBinder] No batch Lua returned");
            return;
        }

        foreach (var assignment in batch.lua_assignments)
        {
            foreach (var kv in programableObjects)
            {
                // Supports BOWLING_PINS → BOWLING_PINS_1, _2, ...
                if (!kv.Key.StartsWith(assignment.target_id))
                    continue;

                var po = kv.Value;
                if (po == null) continue;

                var lua = po.GetComponent<LuaBehaviour>() ??
                          po.gameObject.AddComponent<LuaBehaviour>();

                lua.LoadScript(assignment.lua);
                lua.StartRun();

                Debug.Log(
                    $"[BehaviorBinder] Batch Lua applied: {assignment.target_id} → {kv.Key}"
                );
            }
        }
    }
}
