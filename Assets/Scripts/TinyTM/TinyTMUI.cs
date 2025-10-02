using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace TinyTM
{
    public class TinyTMUI : MonoBehaviour
    {
        public TinyTMManager manager;
        public TMP_Dropdown classDropdown;
        public Button captureButton;
        public Button saveButton;
        public Button clearButton;
        public TMP_Text statusText;

        void Awake()
        {
            if (captureButton) captureButton.onClick.AddListener(OnCapture);
            if (saveButton) saveButton.onClick.AddListener(manager.SaveDataset);
            if (clearButton) clearButton.onClick.AddListener(manager.ClearSamples);
            RefreshDropdown();
        }

        void Update()
        {
            if (!manager || statusText == null) return;
            statusText.text =
                $"Label: {manager.currentLabel}\nState: {manager.currentState}\nConf: {manager.currentConfidence:0.00}\nSamples: {manager.totalSamples}";
        }

        void OnCapture()
        {
            if (!manager || classDropdown == null) return;
            manager.AddSampleByIndex(classDropdown.value);
        }

        void RefreshDropdown()
        {
            if (!classDropdown || manager == null) return;
            classDropdown.ClearOptions();
            classDropdown.AddOptions(manager.classLabels);
        }
    }
}
