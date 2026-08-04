using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

public enum IOTtype
{
    Plug = 0,
    other = 1,
    TV = 2,
    Speaker = 3,
    LightBulb = 4
}

[Serializable]
public class IoTDeviceInfo
{
    public string id;
    public string displayName;
    public string type;
    public List<string> aliases;
    public List<string> supportedCommands;
    public bool isOn;
}

public class IOTobject : MonoBehaviour
{
    public IOTtype iotType;

    [Header("Device Identity")]
    [SerializeField] private string deviceId = "DEVICE_ID";
    [SerializeField] private string displayName = "Device";

    [Header("Digital Twin Objects")]
    public GameObject[] digitalTwinCompentsControl;

    [Header("IFTTT URLs")]
    [SerializeField] private string onURL;
    [SerializeField] private string offURL;

    [Header("Physical Device")]
    [SerializeField] private bool enablePhysicalRequest = true;

    [Header("Hub TCP Server")]
    [SerializeField] private bool enableHubRequest = false;
    [SerializeField] private string hubBaseUrl;
    [Tooltip("Optional override. Leave empty to auto-use tv for TV, music for Speaker, or DeviceId for other types.")]
    [SerializeField] private string hubDevicePath;

    [Header("Mock Device Control")]
    [SerializeField] private bool enableMockControl = true;
    [SerializeField] private GameObject mockControlTarget;

    [Header("State")]
    [SerializeField] private bool isOn = true;

    private static readonly IReadOnlyList<string> PlugCommands = new List<string> { "ON", "OFF" };
    private static readonly IReadOnlyList<string> TVCommands = new List<string> { "ON", "OFF", "NEXT", "PREVIOUS", "VOLUME:0-1", "CHANNEL:n" };
    private static readonly IReadOnlyList<string> SpeakerCommands = new List<string> { "ON", "OFF", "NEXT", "VOLUME:0-1" };
    private static readonly IReadOnlyList<string> LightBulbCommands = new List<string>
    {
        "ON",
        "OFF",
        "TOGGLE",
        "RGB:r:g:b",
        "HSV:h:s:v",
        "BRIGHTNESS:0-100",
        "TEMPERATURE:k",
        "STATE",
        "DISCOVER",
        "IP",
        "IP:address"
    };
    private static readonly IReadOnlyList<string> NoCommands = new List<string>();

    public string DeviceId => IOTManager.NormalizeDeviceId(deviceId);
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) || displayName.Trim() == "Device" ? gameObject.name : displayName.Trim();
    public IOTtype DeviceType => iotType;
    public bool IsOn => isOn;
    public bool PhysicalRequestEnabled => enablePhysicalRequest;
    public IReadOnlyList<string> SupportedCommands
    {
        get
        {
            switch (iotType)
            {
                case IOTtype.Plug:
                    return PlugCommands;
                case IOTtype.TV:
                    return TVCommands;
                case IOTtype.Speaker:
                    return SpeakerCommands;
                case IOTtype.LightBulb:
                    return LightBulbCommands;
                case IOTtype.other:
                    return PlugCommands;
                default:
                    return NoCommands;
            }
        }
    }

    private void OnEnable()
    {
        var manager = FindAnyObjectByType<IOTManager>();
        manager?.Register(this);
        RefreshStateOutputs(isOn);
    }

    private void OnDisable()
    {
        var manager = FindAnyObjectByType<IOTManager>();
        manager?.Unregister(this);
    }

    public IoTCommandResult reciveSingnal(string cmd)
    {
        return ReceiveCommand(cmd);
    }

    public IoTCommandResult ReceiveCommand(string cmd, bool physicalIoTEnabled = true)
    {
        cmd = IOTManager.NormalizeCommand(cmd);

        if (string.IsNullOrEmpty(cmd))
            return IoTCommandResult.InvalidCommand;

        if (!SupportsCommand(cmd))
            return IoTCommandResult.UnsupportedCommand;

        if (cmd == "ON")
            return TrySetState(true, physicalIoTEnabled) ? IoTCommandResult.Success : IoTCommandResult.NoStateChange;

        if (cmd == "OFF")
            return TrySetState(false, physicalIoTEnabled) ? IoTCommandResult.Success : IoTCommandResult.NoStateChange;

        if (cmd == "TOGGLE")
        {
            isOn = !isOn;
            RefreshStateOutputs(isOn);
            bool physicalHandled = TrySendPhysicalCommand(cmd, physicalIoTEnabled);
            return physicalHandled || enableMockControl ? IoTCommandResult.Success : IoTCommandResult.UnsupportedCommand;
        }

        if (cmd == "NEXT")
        {
            bool mockHandled = InvokeMockCommand("Next");
            bool physicalHandled = TrySendPhysicalCommand(cmd, physicalIoTEnabled);
            return mockHandled || physicalHandled ? IoTCommandResult.Success : IoTCommandResult.UnsupportedCommand;
        }

        if (cmd == "PREVIOUS")
        {
            bool mockHandled = InvokeMockCommand("Previous");
            bool physicalHandled = TrySendPhysicalCommand(cmd, physicalIoTEnabled);
            return mockHandled || physicalHandled ? IoTCommandResult.Success : IoTCommandResult.UnsupportedCommand;
        }

        if (TryParseCommandValue(cmd, "VOLUME", out float volume))
        {
            volume = Mathf.Clamp01(volume);
            string normalizedVolumeCommand = "VOLUME:" + volume.ToString("0.###", CultureInfo.InvariantCulture);
            bool mockHandled = SetMockVolume(volume);
            bool physicalHandled = TrySendPhysicalCommand(normalizedVolumeCommand, physicalIoTEnabled);
            return mockHandled || physicalHandled ? IoTCommandResult.Success : IoTCommandResult.UnsupportedCommand;
        }

        if (TryParseCommandInt(cmd, "CHANNEL", out int channel))
        {
            bool mockHandled = InvokeMockCommand("ChangeChanel", channel);
            bool physicalHandled = TrySendPhysicalCommand("CHANNEL:" + channel, physicalIoTEnabled);
            return mockHandled || physicalHandled ? IoTCommandResult.Success : IoTCommandResult.UnsupportedCommand;
        }

        if (TryParseCommandIntTriple(cmd, "RGB", out int red, out int green, out int blue))
        {
            red = Mathf.Clamp(red, 0, 255);
            green = Mathf.Clamp(green, 0, 255);
            blue = Mathf.Clamp(blue, 0, 255);
            string normalizedRgbCommand = $"RGB:{red}:{green}:{blue}";
            bool mockHandled = SetMockColor(new Color32((byte)red, (byte)green, (byte)blue, 255));
            bool physicalHandled = TrySendPhysicalCommand(normalizedRgbCommand, physicalIoTEnabled);
            return mockHandled || physicalHandled ? IoTCommandResult.Success : IoTCommandResult.UnsupportedCommand;
        }

        if (TryParseCommandIntTriple(cmd, "HSV", out int hue, out int saturation, out int value))
        {
            hue = Mathf.Clamp(hue, 0, 360);
            saturation = Mathf.Clamp(saturation, 0, 100);
            value = Mathf.Clamp(value, 0, 100);
            string normalizedHsvCommand = $"HSV:{hue}:{saturation}:{value}";
            Color color = Color.HSVToRGB(hue / 360f, saturation / 100f, value / 100f);
            bool mockHandled = SetMockColor(color);
            bool physicalHandled = TrySendPhysicalCommand(normalizedHsvCommand, physicalIoTEnabled);
            return mockHandled || physicalHandled ? IoTCommandResult.Success : IoTCommandResult.UnsupportedCommand;
        }

        if (TryParseCommandInt(cmd, "BRIGHTNESS", out int brightness))
        {
            brightness = Mathf.Clamp(brightness, 0, 100);
            string normalizedBrightnessCommand = "BRIGHTNESS:" + brightness.ToString(CultureInfo.InvariantCulture);
            bool mockHandled = SetMockBrightness(brightness);
            bool physicalHandled = TrySendPhysicalCommand(normalizedBrightnessCommand, physicalIoTEnabled);
            return mockHandled || physicalHandled ? IoTCommandResult.Success : IoTCommandResult.UnsupportedCommand;
        }

        if (TryParseCommandInt(cmd, "TEMPERATURE", out int kelvin))
        {
            kelvin = Mathf.Max(1, kelvin);
            bool physicalHandled = TrySendPhysicalCommand("TEMPERATURE:" + kelvin.ToString(CultureInfo.InvariantCulture), physicalIoTEnabled);
            return physicalHandled ? IoTCommandResult.Success : IoTCommandResult.UnsupportedCommand;
        }

        if (cmd == "STATE" || cmd == "DISCOVER" || cmd == "IP" || TryParseCommandText(cmd, "IP", out _))
        {
            bool physicalHandled = TrySendPhysicalCommand(cmd, physicalIoTEnabled);
            return physicalHandled ? IoTCommandResult.Success : IoTCommandResult.UnsupportedCommand;
        }

        return IoTCommandResult.InvalidCommand;
    }

    public void SetState(bool on)
    {
        TrySetState(on);
    }

    public bool TrySetState(bool on)
    {
        return TrySetState(on, true);
    }

    public bool TrySetState(bool on, bool physicalIoTEnabled)
    {
        if (isOn == on)
        {
            RefreshStateOutputs(on);
            TrySendPhysicalCommand(on ? "ON" : "OFF", physicalIoTEnabled);
            return false;
        }

        isOn = on;

        Debug.Log($"[IOT] {DeviceId} -> {(on ? "ON" : "OFF")}");

        RefreshStateOutputs(on);

        TrySendPhysicalCommand(on ? "ON" : "OFF", physicalIoTEnabled);
        return true;
    }

    public bool SupportsCommand(string cmd)
    {
        cmd = IOTManager.NormalizeCommand(cmd);
        foreach (var supported in SupportedCommands)
        {
            if (supported == cmd)
                return true;
        }

        if ((iotType == IOTtype.TV || iotType == IOTtype.Speaker) &&
            (CommandHasValue(cmd, "VOLUME")))
            return true;

        if (iotType == IOTtype.TV && CommandHasValue(cmd, "CHANNEL"))
            return true;

        if (iotType == IOTtype.LightBulb)
        {
            if (cmd == "TOGGLE" || cmd == "STATE" || cmd == "DISCOVER" || cmd == "IP")
                return true;

            if (CommandHasIntTriple(cmd, "RGB") ||
                CommandHasIntTriple(cmd, "HSV") ||
                CommandHasValue(cmd, "BRIGHTNESS") ||
                CommandHasValue(cmd, "TEMPERATURE") ||
                CommandHasText(cmd, "IP"))
                return true;
        }

        return false;
    }

    private void RefreshStateOutputs(bool on)
    {
        UpdateVisual(on);
        ApplyMockState(on);
    }

    void UpdateVisual()
    {
        UpdateVisual(isOn);
    }

    void UpdateVisual(bool on)
    {
        if (digitalTwinCompentsControl == null)
            return;

        foreach (var obj in digitalTwinCompentsControl)
            if (obj)
                obj.SetActive(on);
    }

    private bool TrySendPhysicalCommand(string cmd, bool physicalIoTEnabled)
    {
        if (!physicalIoTEnabled || !enablePhysicalRequest)
            return false;

        cmd = IOTManager.NormalizeCommand(cmd);
        bool sent = false;

        if (iotType == IOTtype.Plug && (cmd == "ON" || cmd == "OFF"))
        {
            string url = cmd == "ON" ? onURL : offURL;
            if (!string.IsNullOrWhiteSpace(url))
            {
                StartCoroutine(SendHttpRequest(url, "GET", "IFTTT"));
                sent = true;
            }
        }

        if (TryBuildHubCommandUrl(cmd, out string hubUrl))
        {
            string method = iotType == IOTtype.LightBulb ? "GET" : "POST";
            string label = iotType == IOTtype.LightBulb ? "LightBulb" : "Hub";
            Debug.Log($"[IOT] Sending {DeviceId} {cmd} -> {hubUrl}");
            StartCoroutine(SendHttpRequest(hubUrl, method, label));
            sent = true;
        }

        if (!sent && iotType == IOTtype.Plug && (cmd == "ON" || cmd == "OFF"))
            Debug.LogWarning($"[IOT] Missing physical URL for {DeviceId} {cmd}; updated digital twin/mock only.");
        else if (!sent)
            Debug.LogWarning($"[IOT] No physical request sent for {DeviceId} {cmd}; check Physical Device and Hub TCP Server settings.");

        return sent;
    }

    private bool TryBuildHubCommandUrl(string cmd, out string url)
    {
        url = "";

        if (!enableHubRequest || string.IsNullOrWhiteSpace(hubBaseUrl))
            return false;

        string devicePath = GetHubDevicePath();

        if (iotType == IOTtype.LightBulb)
        {
            string lightBulbEndpoint = BuildLightBulbEndpoint(cmd);
            if (string.IsNullOrWhiteSpace(lightBulbEndpoint))
                return false;

            url = JoinUrl(hubBaseUrl, devicePath, lightBulbEndpoint);
            return true;
        }

        if (string.IsNullOrWhiteSpace(devicePath))
            return false;

        string endpoint;
        if (cmd == "ON" || cmd == "OFF" || cmd == "NEXT" || cmd == "PREVIOUS")
        {
            endpoint = cmd.ToLowerInvariant();
        }
        else if (TryParseCommandValue(cmd, "VOLUME", out float volume))
        {
            endpoint = "volume/" + Mathf.Clamp01(volume).ToString("0.###", CultureInfo.InvariantCulture);
        }
        else if (TryParseCommandInt(cmd, "CHANNEL", out int channel))
        {
            endpoint = "channel/" + channel.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            return false;
        }

        url = JoinUrl(hubBaseUrl, devicePath, endpoint);
        return true;
    }

    private string GetHubDevicePath()
    {
        if (!string.IsNullOrWhiteSpace(hubDevicePath))
            return hubDevicePath.Trim().Trim('/');

        switch (iotType)
        {
            case IOTtype.TV:
                return "tv";
            case IOTtype.Speaker:
                return "music";
            case IOTtype.LightBulb:
                return "";
            default:
                return IOTManager.NormalizeDeviceId(DeviceId).ToLowerInvariant();
        }
    }

    private string BuildLightBulbEndpoint(string cmd)
    {
        if (cmd == "ON" || cmd == "OFF" || cmd == "TOGGLE" || cmd == "STATE" || cmd == "DISCOVER" || cmd == "IP")
            return cmd.ToLowerInvariant();

        if (TryParseCommandIntTriple(cmd, "RGB", out int red, out int green, out int blue))
            return $"rgb/{Mathf.Clamp(red, 0, 255)}/{Mathf.Clamp(green, 0, 255)}/{Mathf.Clamp(blue, 0, 255)}";

        if (TryParseCommandIntTriple(cmd, "HSV", out int hue, out int saturation, out int value))
            return $"hsv/{Mathf.Clamp(hue, 0, 360)}/{Mathf.Clamp(saturation, 0, 100)}/{Mathf.Clamp(value, 0, 100)}";

        if (TryParseCommandInt(cmd, "BRIGHTNESS", out int brightness))
            return "brightness/" + Mathf.Clamp(brightness, 0, 100).ToString(CultureInfo.InvariantCulture);

        if (TryParseCommandInt(cmd, "TEMPERATURE", out int kelvin))
            return "temperature/" + Mathf.Max(1, kelvin).ToString(CultureInfo.InvariantCulture);

        if (TryParseCommandText(cmd, "IP", out string address))
            return "ip/" + address;

        return "";
    }

    private string JoinUrl(string baseUrl, string devicePath, string endpoint)
    {
        string url = baseUrl.Trim().TrimEnd('/');
        string path = string.IsNullOrWhiteSpace(devicePath) ? "" : devicePath.Trim().Trim('/');
        string ep = string.IsNullOrWhiteSpace(endpoint) ? "" : endpoint.Trim().TrimStart('/');

        if (!string.IsNullOrEmpty(path))
            url += "/" + path;

        if (!string.IsNullOrEmpty(ep))
            url += "/" + ep;

        return url;
    }

    IEnumerator SendHttpRequest(string url, string method, string label)
    {
        using (UnityWebRequest www = new UnityWebRequest(url, method))
        {
            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                www.uploadHandler = new UploadHandlerRaw(new byte[0]);
                www.SetRequestHeader("Content-Type", "application/json");
            }

            www.downloadHandler = new DownloadHandlerBuffer();
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
                Debug.Log($"[IOT] {label} Success: {url}");
            else
                Debug.LogWarning($"[IOT] {label} Failed: {url} - {www.error}");
        }
    }

    private void ApplyMockState(bool on)
    {
        if (!enableMockControl)
            return;

        bool invoked =
            InvokeMockCommand(on ? "On" : "Off") ||
            InvokeMockCommand(on ? "TurnOn" : "TurnOff") ||
            InvokeMockCommand("SetOn", on) ||
            InvokeMockCommand("SetPower", on) ||
            InvokeMockCommand("SetActive", on);

        if (!invoked)
            ApplyMockActiveFallback(on);
    }

    private void ApplyMockActiveFallback(bool on)
    {
        if (mockControlTarget == null || mockControlTarget == gameObject)
            return;

        mockControlTarget.SetActive(on);
    }

    private bool SetMockVolume(float volume)
    {
        if (InvokeMockCommand("setVolume", volume) || InvokeMockCommand("SetVolume", volume))
            return true;

        var target = GetMockControlTarget();
        if (target == null)
            return false;

        foreach (var audioSource in target.GetComponentsInChildren<AudioSource>(true))
        {
            audioSource.volume = volume;
            return true;
        }

        return false;
    }

    private bool SetMockColor(Color color)
    {
        bool invoked =
            InvokeMockCommand("SetColor", color) ||
            InvokeMockCommand("ChangeColor", color);

        var target = GetMockControlTarget();
        if (target == null)
            return invoked;

        foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer != null && renderer.material != null)
            {
                renderer.material.color = color;
                invoked = true;
            }
        }

        return invoked;
    }

    private bool SetMockBrightness(int brightness)
    {
        float value = Mathf.Clamp01(brightness / 100f);
        bool invoked =
            InvokeMockCommand("SetBrightness", brightness) ||
            InvokeMockCommand("SetBrightness", value);

        var target = GetMockControlTarget();
        if (target == null)
            return invoked;

        foreach (var light in target.GetComponentsInChildren<Light>(true))
        {
            light.intensity = Mathf.Lerp(0f, 2f, value);
            invoked = true;
        }

        return invoked;
    }

    private bool InvokeMockCommand(string methodName)
    {
        return InvokeMockCommand(methodName, null);
    }

    private bool InvokeMockCommand(string methodName, object argument)
    {
        if (!enableMockControl)
            return false;

        var target = GetMockControlTarget();
        if (target == null)
            return false;

        foreach (var component in target.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || component == this)
                continue;

            var methods = component.GetType().GetMethods(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic
            );

            foreach (var method in methods)
            {
                if (!string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var parameters = method.GetParameters();
                if (argument == null && parameters.Length == 0)
                {
                    method.Invoke(component, null);
                    return true;
                }

                if (argument != null && parameters.Length == 1)
                {
                    object coerced = CoerceArgument(argument, parameters[0].ParameterType);
                    if (coerced == null)
                        continue;

                    method.Invoke(component, new[] { coerced });
                    return true;
                }
            }
        }

        return false;
    }

    private object CoerceArgument(object argument, Type targetType)
    {
        try
        {
            if (targetType == typeof(float))
                return Convert.ToSingle(argument);
            if (targetType == typeof(int))
                return Convert.ToInt32(argument);
            if (targetType == typeof(double))
                return Convert.ToDouble(argument);
            if (targetType == typeof(bool))
                return Convert.ToBoolean(argument);
            if (targetType == typeof(string))
                return argument.ToString();
            if (targetType.IsInstanceOfType(argument))
                return argument;
        }
        catch
        {
            return null;
        }

        return null;
    }

    private GameObject GetMockControlTarget()
    {
        return mockControlTarget != null ? mockControlTarget : gameObject;
    }

    private bool CommandHasValue(string cmd, string commandName)
    {
        return TryParseCommandValue(cmd, commandName, out _);
    }

    private bool CommandHasIntTriple(string cmd, string commandName)
    {
        return TryParseCommandIntTriple(cmd, commandName, out _, out _, out _);
    }

    private bool CommandHasText(string cmd, string commandName)
    {
        return TryParseCommandText(cmd, commandName, out _);
    }

    private bool TryParseCommandValue(string cmd, string commandName, out float value)
    {
        value = 0f;
        cmd = IOTManager.NormalizeCommand(cmd);
        commandName = IOTManager.NormalizeCommand(commandName);

        if (!cmd.StartsWith(commandName, StringComparison.Ordinal))
            return false;

        string raw = cmd.Length > commandName.Length
            ? cmd.Substring(commandName.Length).TrimStart(':', '_', '=', ' ')
            : "";

        return !string.IsNullOrWhiteSpace(raw) && float.TryParse(raw, out value);
    }

    private bool TryParseCommandIntTriple(string cmd, string commandName, out int a, out int b, out int c)
    {
        a = 0;
        b = 0;
        c = 0;

        cmd = IOTManager.NormalizeCommand(cmd);
        commandName = IOTManager.NormalizeCommand(commandName);

        if (!cmd.StartsWith(commandName, StringComparison.Ordinal))
            return false;

        string raw = cmd.Length > commandName.Length
            ? cmd.Substring(commandName.Length).TrimStart(':', '_', '=', ' ', '/')
            : "";

        string[] parts = raw.Split(new[] { ':', '_', '=', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3 &&
               int.TryParse(parts[0], out a) &&
               int.TryParse(parts[1], out b) &&
               int.TryParse(parts[2], out c);
    }

    private bool TryParseCommandText(string cmd, string commandName, out string value)
    {
        value = "";
        cmd = IOTManager.NormalizeCommand(cmd);
        commandName = IOTManager.NormalizeCommand(commandName);

        if (!cmd.StartsWith(commandName, StringComparison.Ordinal))
            return false;

        value = cmd.Length > commandName.Length
            ? cmd.Substring(commandName.Length).TrimStart(':', '_', '=', ' ', '/')
            : "";

        return !string.IsNullOrWhiteSpace(value);
    }

    private bool TryParseCommandInt(string cmd, string commandName, out int value)
    {
        value = 0;
        return TryParseCommandValue(cmd, commandName, out float floatValue) &&
               Mathf.Approximately(floatValue, Mathf.Round(floatValue)) &&
               int.TryParse(Mathf.RoundToInt(floatValue).ToString(), out value);
    }
}
