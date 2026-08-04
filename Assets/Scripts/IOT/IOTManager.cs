using System.Collections.Generic;
using UnityEngine;

public enum IoTCommandResult
{
    Success,
    DeviceNotFound,
    UnsupportedCommand,
    NoStateChange,
    InvalidCommand
}

    public class IOTManager : MonoBehaviour
    {
        private Dictionary<string, IOTobject> devices =
            new Dictionary<string, IOTobject>(System.StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Vector3Int> lightBulbRgbState =
            new Dictionary<string, Vector3Int>(System.StringComparer.OrdinalIgnoreCase);

        [SerializeField] private bool physicalIoTEnabled = true;

    void Start()
    {
        CollectAllDevices();
    }

    void CollectAllDevices()
    {
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
        if (obj == null)
            return;

        string id = NormalizeDeviceId(obj.DeviceId);
        if (string.IsNullOrEmpty(id))
        {
            Debug.LogWarning($"[IOT] Rejecting device with empty ID on {obj.name}");
            return;
        }

        if (devices.TryGetValue(id, out var existing))
        {
            if (existing == obj)
                return;

            Debug.LogWarning(
                $"[IOT] Duplicate deviceId '{id}' on {obj.name}; already registered by {existing.name}."
            );
            return;
        }

        devices.Add(id, obj);

        Debug.Log($"[IOT] Registered {id}");
    }

    public void Unregister(IOTobject obj)
    {
        if (obj == null)
            return;

        string id = NormalizeDeviceId(obj.DeviceId);
        if (string.IsNullOrEmpty(id))
            return;

        if (devices.TryGetValue(id, out var existing) && existing == obj)
        {
            devices.Remove(id);
            Debug.Log($"[IOT] Unregistered {id}");
        }
    }

    // -------------------------
    // Commands
    // -------------------------
    public IoTCommandResult SendCommand(string id, string cmd)
    {
        string requestedId = id;
        id = ResolveDeviceId(id);
        cmd = NormalizeCommand(cmd);

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(cmd))
            return IoTCommandResult.InvalidCommand;

        if (!devices.TryGetValue(id, out var dev))
        {
            Debug.LogWarning($"[IOT] Device not found: {requestedId}");
            return IoTCommandResult.DeviceNotFound;
        }

        if (!dev.SupportsCommand(cmd))
        {
            Debug.LogWarning($"[IOT] Unsupported command '{cmd}' for device {id}");
            return IoTCommandResult.UnsupportedCommand;
        }

        return dev.ReceiveCommand(cmd, physicalIoTEnabled);
    }

    public IoTCommandResult TurnOn(string id)
    {
        return SendCommand(id, "ON");
    }

    public IoTCommandResult TurnOff(string id)
    {
        return SendCommand(id, "OFF");
    }

    public IoTCommandResult SetLightBulbRGB(string id, double red, double green, double blue)
    {
        string resolvedId = ResolveDeviceId(id);
        if (string.IsNullOrEmpty(resolvedId))
            return IoTCommandResult.DeviceNotFound;

        int r = ClampRgbChannel(red);
        int g = ClampRgbChannel(green);
        int b = ClampRgbChannel(blue);
        var nextRgb = new Vector3Int(r, g, b);

        if (lightBulbRgbState.TryGetValue(resolvedId, out var currentRgb) && currentRgb == nextRgb)
            return IoTCommandResult.NoStateChange;

        var result = SendCommand(resolvedId, $"RGB:{r}:{g}:{b}");

        if (result == IoTCommandResult.Success || result == IoTCommandResult.NoStateChange)
            lightBulbRgbState[resolvedId] = nextRgb;

        return result;
    }

    public IoTCommandResult SetLightBulbRed(string id, double red)
    {
        string resolvedId = ResolveDeviceId(id);
        if (string.IsNullOrEmpty(resolvedId))
            return IoTCommandResult.DeviceNotFound;

        Vector3Int rgb = GetCachedLightBulbRGB(resolvedId);
        return SetLightBulbRGB(resolvedId, red, rgb.y, rgb.z);
    }

    public IoTCommandResult SetLightBulbGreen(string id, double green)
    {
        string resolvedId = ResolveDeviceId(id);
        if (string.IsNullOrEmpty(resolvedId))
            return IoTCommandResult.DeviceNotFound;

        Vector3Int rgb = GetCachedLightBulbRGB(resolvedId);
        return SetLightBulbRGB(resolvedId, rgb.x, green, rgb.z);
    }

    public IoTCommandResult SetLightBulbBlue(string id, double blue)
    {
        string resolvedId = ResolveDeviceId(id);
        if (string.IsNullOrEmpty(resolvedId))
            return IoTCommandResult.DeviceNotFound;

        Vector3Int rgb = GetCachedLightBulbRGB(resolvedId);
        return SetLightBulbRGB(resolvedId, rgb.x, rgb.y, blue);
    }

    public Vector3Int GetCachedLightBulbRGB(string id)
    {
        string resolvedId = ResolveDeviceId(id);
        if (string.IsNullOrEmpty(resolvedId))
            resolvedId = NormalizeDeviceId(id);

        return !string.IsNullOrEmpty(resolvedId) && lightBulbRgbState.TryGetValue(resolvedId, out var rgb)
            ? rgb
            : Vector3Int.zero;
    }

    private int ClampRgbChannel(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0;

        return Mathf.Clamp(Mathf.RoundToInt((float)value), 0, 255);
    }

    public List<string> GetAllDeviceIDs()
    {
        var ids = new List<string>(devices.Keys);
        ids.Sort(System.StringComparer.OrdinalIgnoreCase);
        return ids;
    }

    public List<IoTDeviceInfo> GetAllDeviceInfo()
    {
        var ids = GetAllDeviceIDs();
        var info = new List<IoTDeviceInfo>();

        foreach (var id in ids)
        {
            if (!devices.TryGetValue(id, out var dev) || dev == null)
                continue;

            info.Add(new IoTDeviceInfo
            {
                id = id,
                displayName = dev.DisplayName,
                type = dev.DeviceType.ToString(),
                aliases = GetDeviceAliases(id, dev),
                supportedCommands = new List<string>(dev.SupportedCommands),
                isOn = dev.IsOn
            });
        }

        return info;
    }

    public string ResolveDeviceId(string reference)
    {
        string normalized = NormalizeDeviceId(reference);
        if (string.IsNullOrEmpty(normalized))
            return "";

        if (devices.ContainsKey(normalized))
            return normalized;

        var exactMatches = new List<string>();
        foreach (var kv in devices)
        {
            foreach (var alias in GetDeviceAliases(kv.Key, kv.Value))
            {
                if (NormalizeDeviceId(alias) == normalized)
                    exactMatches.Add(kv.Key);
            }
        }

        if (exactMatches.Count == 1)
            return exactMatches[0];

        if (exactMatches.Count > 1)
        {
            Debug.LogWarning($"[IOT] Ambiguous device reference '{reference}' matched {string.Join(", ", exactMatches)}");
            return "";
        }

        var partialMatches = new List<string>();
        foreach (var kv in devices)
        {
            foreach (var alias in GetDeviceAliases(kv.Key, kv.Value))
            {
                string normalizedAlias = NormalizeDeviceId(alias);
                if (normalizedAlias.Contains(normalized) || normalized.Contains(normalizedAlias))
                {
                    partialMatches.Add(kv.Key);
                    break;
                }
            }
        }

        if (partialMatches.Count == 1)
            return partialMatches[0];

        if (partialMatches.Count > 1)
            Debug.LogWarning($"[IOT] Ambiguous device reference '{reference}' matched {string.Join(", ", partialMatches)}");

        return "";
    }

    public bool TryGetDeviceInfo(string reference, out IoTDeviceInfo info)
    {
        info = null;
        string id = ResolveDeviceId(reference);
        if (string.IsNullOrEmpty(id) || !devices.TryGetValue(id, out var dev) || dev == null)
            return false;

        info = new IoTDeviceInfo
        {
            id = id,
            displayName = dev.DisplayName,
            type = dev.DeviceType.ToString(),
            aliases = GetDeviceAliases(id, dev),
            supportedCommands = new List<string>(dev.SupportedCommands),
            isOn = dev.IsOn
        };
        return true;
    }

    private List<string> GetDeviceAliases(string id, IOTobject dev)
    {
        var aliases = new List<string>();
        AddAlias(aliases, id);
        if (dev != null)
        {
            AddAlias(aliases, dev.DisplayName);
            AddAlias(aliases, dev.name);
            AddAlias(aliases, dev.DeviceType.ToString());
        }

        return aliases;
    }

    private void AddAlias(List<string> aliases, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return;

        alias = alias.Trim();
        foreach (var existing in aliases)
            if (string.Equals(existing, alias, System.StringComparison.OrdinalIgnoreCase))
                return;

        aliases.Add(alias);
    }

    public static string NormalizeDeviceId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "";

        string normalized = id.Trim().Replace(' ', '_').ToUpperInvariant();
        while (normalized.Contains("__"))
            normalized = normalized.Replace("__", "_");

        return normalized;
    }

    public static string NormalizeCommand(string cmd)
    {
        return string.IsNullOrWhiteSpace(cmd) ? "" : cmd.Trim().ToUpperInvariant();
    }

}
