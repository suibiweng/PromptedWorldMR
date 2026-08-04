using Meta.WitAi.Dictation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DictationInputFieldBridge : MonoBehaviour
{
    [Header("Voice")]
    [SerializeField] private DictationService _dictation;

    [Header("Button")]
    [SerializeField] private Button _activateButton;
    [SerializeField] private bool _autoHookActivateButton = true;

    [Header("Input Target")]
    [SerializeField] private TMP_InputField _tmpInputField;

    [Header("Behavior")]
    [SerializeField] private bool _updateOnPartialTranscription = true;
    [SerializeField] private bool _replaceExistingText = true;
    [SerializeField] private bool _deactivateAfterFullTranscription = true;
    [SerializeField] private string _idlePlaceholderText = "Type here...";
    [SerializeField] private string _listeningPlaceholderText = "Listening...";

    private void Reset()
    {
        if (_activateButton == null)
        {
            _activateButton = GetComponent<Button>();
        }

        if (_tmpInputField == null)
        {
            _tmpInputField = GetComponent<TMP_InputField>();
        }
    }

    private void OnEnable()
    {
        if (_autoHookActivateButton && _activateButton != null)
        {
            _activateButton.onClick.AddListener(StartDictationFromButton);
        }

        if (_dictation == null)
        {
            return;
        }

        _dictation.DictationEvents.OnPartialTranscription.AddListener(OnPartialTranscription);
        _dictation.DictationEvents.OnFullTranscription.AddListener(OnFullTranscription);

        SetPlaceholderText(_idlePlaceholderText);
    }

    private void OnDisable()
    {
        if (_autoHookActivateButton && _activateButton != null)
        {
            _activateButton.onClick.RemoveListener(StartDictationFromButton);
        }

        if (_dictation == null)
        {
            return;
        }

        _dictation.DictationEvents.OnPartialTranscription.RemoveListener(OnPartialTranscription);
        _dictation.DictationEvents.OnFullTranscription.RemoveListener(OnFullTranscription);
    }

    // Use this on your UI Button onClick.
    public void StartDictationFromButton()
    {
        if (_dictation == null || _dictation.MicActive)
        {
            return;
        }

        SetPlaceholderText(_listeningPlaceholderText);
        _dictation.Activate();
    }

    // Optional alternative if you want one button for start/stop.
    public void ToggleDictationFromButton()
    {
        if (_dictation == null)
        {
            return;
        }

        if (_dictation.MicActive)
        {
            _dictation.Deactivate();
            SetPlaceholderText(_idlePlaceholderText);
        }
        else
        {
            SetPlaceholderText(_listeningPlaceholderText);
            _dictation.Activate();
        }
    }

    public void StopDictationFromButton()
    {
        if (_dictation == null || !_dictation.MicActive)
        {
            return;
        }

        _dictation.Deactivate();
        SetPlaceholderText(_idlePlaceholderText);
    }

    public void SetDictationService(DictationService dictationService)
    {
        _dictation = dictationService;
    }

    public void SetTmpInputField(TMP_InputField tmpInputField)
    {
        _tmpInputField = tmpInputField;
    }

    public void SetActivateButton(Button activateButton)
    {
        if (_activateButton == activateButton)
        {
            return;
        }

        if (_autoHookActivateButton && isActiveAndEnabled && _activateButton != null)
        {
            _activateButton.onClick.RemoveListener(StartDictationFromButton);
        }

        _activateButton = activateButton;

        if (_autoHookActivateButton && isActiveAndEnabled && _activateButton != null)
        {
            _activateButton.onClick.AddListener(StartDictationFromButton);
        }
    }

    private void OnPartialTranscription(string transcription)
    {
        if (!_updateOnPartialTranscription)
        {
            return;
        }

        ApplyTranscription(transcription);
    }

    private void OnFullTranscription(string transcription)
    {
        ApplyTranscription(transcription);

        if (_deactivateAfterFullTranscription && _dictation != null && _dictation.MicActive)
        {
            _dictation.Deactivate();
        }

        SetPlaceholderText(_idlePlaceholderText);
    }

    private void ApplyTranscription(string transcription)
    {
        if (string.IsNullOrWhiteSpace(transcription))
        {
            return;
        }

        string finalText = transcription;
        if (!_replaceExistingText)
        {
            string current = GetCurrentText();
            if (!string.IsNullOrWhiteSpace(current))
            {
                finalText = current + " " + transcription;
            }
        }

        SetTargetText(finalText);
    }

    private string GetCurrentText()
    {
        if (_tmpInputField != null)
        {
            return _tmpInputField.text;
        }

        return string.Empty;
    }

    private void SetTargetText(string text)
    {
        if (_tmpInputField != null)
        {
            _tmpInputField.SetTextWithoutNotify(text);
            _tmpInputField.caretPosition = _tmpInputField.text.Length;
        }
    }

    private void SetPlaceholderText(string text)
    {
        if (_tmpInputField == null || _tmpInputField.placeholder == null)
        {
            return;
        }

        if (_tmpInputField.placeholder is TMP_Text tmpPlaceholder)
        {
            tmpPlaceholder.text = text;
            return;
        }

        if (_tmpInputField.placeholder is Text uiTextPlaceholder)
        {
            uiTextPlaceholder.text = text;
        }
    }
}
