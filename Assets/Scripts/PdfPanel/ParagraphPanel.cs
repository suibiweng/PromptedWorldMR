using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ParagraphPanel : MonoBehaviour
{
    // ===== Required by PanelLayoutFlow & Manager =====
    public Button ownerButton { get; set; }
    public float reservedHeight { get; set; } = 300f;

    public event Action<ParagraphPanel> onAnyChanged;
    public event Action<ParagraphPanel> onClose;
    public event Action<ParagraphPanel, string, string> onSubmit;

    // ===== Internal UI =====
    RectTransform _rt;
    Text _title;
    Text _body;
    Text _processed;
    InputField _prompt;
    Button _submitBtn;
    Button _closeBtn;

    bool _built = false;

    // ===== Public API (existing system expects these) =====

    public void SetContent(string title, string body)
    {
        if (_title) _title.text = title;
        if (_body) _body.text = body;
        FireChanged();
    }

    public void SetProcessed(string txt)
    {
        if (_processed) _processed.text = txt;
        FireChanged();
    }

    public void SetHeight(float h)
    {
        reservedHeight = Mathf.Max(120f, h);
        if (_rt) _rt.sizeDelta = new Vector2(_rt.sizeDelta.x, reservedHeight);
        FireChanged();
    }

    public void BuildIfNeeded(Vector2 size)
    {
        if (_built) return;
        EnsureCanvasAndEventSystem();
        BuildVisual(size);
        _built = true;
    }

    // ===== Visual build (simple) =====
    void BuildVisual(Vector2 size)
    {
        _rt = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();
        _rt.anchorMin = _rt.anchorMax = new Vector2(0, 1);
        _rt.pivot = new Vector2(0, 1);
        _rt.sizeDelta = new Vector2(size.x, reservedHeight);

        var bg = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
        bg.color = new Color(1, 1, 1, 0.95f);

        // ----- Title -----
        _title = CreateText("Title", "Paragraph", 18, FontStyle.Bold, new Vector2(10, -10), new Vector2(size.x - 20f, 28f));

        // ----- Body -----
        _body = CreateText("Body", "", 16, FontStyle.Normal, new Vector2(10, -45f), new Vector2(size.x - 20, reservedHeight - 140));
        _body.alignment = TextAnchor.UpperLeft;

        // ----- Prompt InputField -----
        var promptGO = new GameObject("Prompt", typeof(RectTransform), typeof(Image), typeof(InputField));
        promptGO.transform.SetParent(transform, false);

        var prt = promptGO.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = new Vector2(0, 1);
        prt.pivot = new Vector2(0, 1);
        prt.anchoredPosition = new Vector2(10f, -(reservedHeight - 85));
        prt.sizeDelta = new Vector2(size.x - 110, 32f);

        var img = promptGO.GetComponent<Image>();
        img.color = new Color(0.95f, 0.95f, 0.95f);

        _prompt = promptGO.GetComponent<InputField>();

        var txt = CreateText("Text", "", 14, FontStyle.Normal, new Vector2(6, -6), new Vector2(prt.sizeDelta.x - 12, 20), promptGO.transform);
        txt.alignment = TextAnchor.MiddleLeft;
        txt.raycastTarget = false;

        var placeholder = CreateText("Placeholder", "Type prompt…", 14, FontStyle.Italic, new Vector2(6, -6), new Vector2(prt.sizeDelta.x - 12, 20), promptGO.transform);
        placeholder.color = new Color(0,0,0,0.35f);
        placeholder.raycastTarget = false;

        _prompt.textComponent = txt;
        _prompt.placeholder = placeholder;
        _prompt.shouldActivateOnSelect = true;

        // ----- Submit -----
        _submitBtn = CreateButton("Submit", new Vector2(size.x - 95, -(reservedHeight - 85)), new Vector2(85, 32));
        _submitBtn.onClick.AddListener(OnSubmitClicked);

        // ----- Processed text -----
        _processed = CreateText("Processed", "", 14, FontStyle.Italic, new Vector2(10, -(reservedHeight - 40)), new Vector2(size.x - 20f, 40f));

        // ----- Close -----
        _closeBtn = CreateButton("Close", new Vector2(size.x - 95, -10), new Vector2(85, 28));
        _closeBtn.onClick.AddListener(() => onClose?.Invoke(this));

        FireChanged();
    }

    void OnSubmitClicked()
    {
        string prompt = _prompt ? _prompt.text : "";
        string body   = _body   ? _body.text : "";
        onSubmit?.Invoke(this, prompt, body);
    }

    void FireChanged() => onAnyChanged?.Invoke(this);

    // ===== UI helpers =====
    Text CreateText(string name, string text, int size, FontStyle style, Vector2 pos, Vector2 sizeDelta, Transform parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent ?? transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;

        var tx = go.GetComponent<Text>();
        tx.text = text;
        tx.fontStyle = style;
        Font builtin = null;
        try { builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch {}
        tx.font = builtin ?? Font.CreateDynamicFontFromOSFont("Arial", size);
        tx.fontSize = size;
        tx.color = Color.black;
        tx.alignment = TextAnchor.UpperLeft;

        return tx;
    }

    Button CreateButton(string label, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        var img = go.GetComponent<Image>();
        img.color = new Color(0.85f, 0.9f, 1f);

        var t = CreateText("Label", label, 14, FontStyle.Bold, new Vector2(6, -6), new Vector2(size.x - 12, size.y - 12), go.transform);
        t.alignment = TextAnchor.MiddleCenter;

        return go.GetComponent<Button>();
    }

    // ===== Canvas + EventSystem =====
    void EnsureCanvasAndEventSystem()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            var c = new GameObject("ParagraphPanelCanvas", typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasScaler));
            c.transform.SetParent(null);
            canvas = c.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        if (EventSystem.current == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }
    }
}
