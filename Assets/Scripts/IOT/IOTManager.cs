using System.Collections.Generic;
using UnityEngine;

public class IOTManager : MonoBehaviour
{
    private Dictionary<string, IOTobject> devices =
        new Dictionary<string, IOTobject>();

    // -------------------------
    // Awake = auto collect
    // -------------------------
    void Awake()
    {
        CollectAllDevices();
    }

    void CollectAllDevices()
    {
        devices.Clear();

        IOTobject[] found =
            FindObjectsOfType<IOTobject>(true);

        foreach (var dev in found)
        {
            Register(dev);
        }

        Debug.Log($"[IOT] Collected {devices.Count} devices");
    }

    // -------------------------
    // Manual register (for runtime spawn)
    // -------------------------
    public void Register(IOTobject obj)
    {
        if (obj == null || string.IsNullOrEmpty(obj.deviceId))
            return;

        if (devices.ContainsKey(obj.deviceId))
        {
            Debug.LogWarning(
                $"[IOT] Duplicate deviceId: {obj.deviceId}"
            );
            return;
        }

        devices.Add(obj.deviceId, obj);

        Debug.Log($"[IOT] Registered {obj.deviceId}");
    }

    // -------------------------
    // Commands
    // -------------------------
    public void SendCommand(string id, string cmd)
    {
        if (!devices.TryGetValue(id, out var dev))
        {
            Debug.LogWarning($"[IOT] Device not found: {id}");
            return;
        }

        dev.reciveSingnal(cmd);
    }

    public void TurnOn(string id)
    {
        SendCommand(id, "ON");
    }

    public void TurnOff(string id)
    {
        SendCommand(id, "OFF");
    }

    public List<string> GetAllDeviceIDs()
{
    return new List<string>(devices.Keys);
}

}
