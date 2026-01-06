using System;
using System.Collections.Generic;

[Serializable]
public class ScenePlan
{
    public int version = 1;
    public string title;
    public string scene_type;
    public string summary;

    // 🔹 NEW: scene-level layout intent (natural language)
    public string layout_prompt;

    public List<SceneObject> objects = new();
    public List<SceneSystem> systems = new();
    public List<SceneUI> ui = new();
}

[Serializable]
public class SceneObject
{
    public string id;
    public string primitive;
    public int count = 1;
    public string role;
    public bool interactive;
}

[Serializable]
public class SceneSystem
{
    public string type;
    public List<string> targets = new();
}

[Serializable]
public class SceneUI
{
    public string type;
    public string id;
}
