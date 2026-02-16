using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public enum IOTtype
{
    Plug,
    other
}

public class IOTobject : MonoBehaviour
{
    public IOTtype iotType;

    [Header("Auto ID")]
    public string deviceId;

    [Header("Digital Twin Objects")]
    public GameObject[] digitalTwinCompentsControl;

    [Header("IFTTT URLs")]
    public string onURL =
        "https://maker.ifttt.com/trigger/1Plug_On/with/key/OqfDCmRSoLEHVebFewQEc";

    public string offURL =
        "https://maker.ifttt.com/trigger/A1Plug_off/with/key/OqfDCmRSoLEHVebFewQEc";

    public bool isOn = true;

    void Awake()
    {
        deviceId = gameObject.name + "_" + GetInstanceID();
    }

    public void reciveSingnal(string cmd)
    {
        if (iotType != IOTtype.Plug) return;

        if (cmd == "ON")
            SetState(true);
        else if (cmd == "OFF")
            SetState(false);
    }

    void SetState(bool on)
    {
        isOn = on;

        Debug.Log($"[IOT] {deviceId} -> {(on ? "ON" : "OFF")}");

        UpdateVisual();

        // 🔥 REAL IOT CALL
        StartCoroutine(SendIFTTT(on));
    }

    void UpdateVisual()
    {
        foreach (var obj in digitalTwinCompentsControl)
            if (obj)
                obj.SetActive(isOn);
    }

    IEnumerator SendIFTTT(bool turnOn)
    {
        string url = turnOn ? onURL : offURL;

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
                Debug.Log("[IOT] IFTTT Success");
            else
                Debug.LogWarning("[IOT] IFTTT Failed: " + www.error);
        }
    }
}
