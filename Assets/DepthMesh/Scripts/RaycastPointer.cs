using UnityEngine;
using Unity.XR.CoreUtils; // for XROrigin (Building Blocks / XRI rigs)

[DisallowMultipleComponent]
public class RaycastPointer : MonoBehaviour
{
    [Header("Resolved at runtime if left empty")]
    public Transform rayOrigin;

    [Header("Visuals")]
    public bool drawLaser = true;
    public float maxDistance = 8f;
    public LayerMask hitMask = ~0;

    LineRenderer lr;

    public Transform RayOrigin => rayOrigin;

    void Awake()
    {
        if (!rayOrigin) rayOrigin = FindRayOrigin();
        if (drawLaser)
        {
            lr = gameObject.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.widthMultiplier = 0.004f;
            lr.useWorldSpace = true;
            lr.material = new Material(Shader.Find("Unlit/Color"));

            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }
    }

    void Update()
    {
        if (!rayOrigin || !drawLaser || !lr) return;

        var start = rayOrigin.position;
        var dir   = rayOrigin.forward;

        if (Physics.Raycast(start, dir, out var hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore))
        {
            lr.SetPosition(0, start);
            lr.SetPosition(1, hit.point);
        }
        else
        {
            lr.SetPosition(0, start);
            lr.SetPosition(1, start + dir * maxDistance);
        }
    }

    Transform FindRayOrigin()
    {
        // 1) XR Origin (Action-based) — Building Blocks default
        var xro = FindObjectOfType<XROrigin>(true);
        if (xro)
        {
            // Try common right-hand aim transform names under the right-hand controller
            var right = xro.transform.Find("Camera Offset/RightHand Controller/Pointer Pose");
            if (!right) right = xro.transform.Find("Camera Offset/RightHand Controller/Aim Pose");
            if (!right) right = xro.transform.Find("Camera Offset/RightHand Controller");
            if (right) return right;
        }

        // 2) OVRCameraRig (Oculus Integration style rigs)
        var ovrRig = GameObject.Find("OVRCameraRig");
        if (ovrRig)
        {
            var r = ovrRig.transform.Find("RightHandAnchor");
            if (!r) r = ovrRig.transform.Find("RightControllerAnchor");
            if (r) return r;
        }

        // 3) Fallbacks: common names you might have in your hierarchy
        string[] candidates =
        {
            "RightHand/Pointer Pose",
            "RightHand/Aim Pose",
            "RightHand",
            "RightController/Pointer Pose",
            "RightController/Aim Pose",
            "RightController"
        };
        foreach (var path in candidates)
        {
            var t = GameObject.Find(path);
            if (t) return t.transform;
        }

        // 4) Last resort: main camera (not ideal, but at least gives a forward)
        if (Camera.main) return Camera.main.transform;

        Debug.LogWarning("RaycastPointer: Could not auto-find a ray origin. Assign one in the Inspector.");
        return null;
    }
}
