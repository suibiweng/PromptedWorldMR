using System;
using UnityEngine;
using UnityEngine.UI;

public class ParagraphPanel : MonoBehaviour
{
    // ===== Public API used by other scripts =====
    public Button ownerButton { get; set; }            // set by manager/renderer
    public float reservedHeight { get; set; } = 300f;  // layout hint for external layouters

    // Events
    public event Action<ParagraphPanel> onAnyChanged;
    public event Action<ParagraphPanel> onClose;
    // (self, prompt, bodyShownInPanel)
    public event Action<ParagraphPanel, string, string> onSubmit;

    // --- Public helpers other code may call ---
    public void SetTitle(string title)
    {
        if (_title) _title.text = title ?? "Paragraph";
        FireChanged();
    }

    public void SetBody(string body)
    {
        if (_body) _body.text = body ?? "";
        FireChanged();
    }

    public void SetProcessed(string txt)
    {
        if (_processed) _processed.text = txt ?? "";
        FireChanged();
    }

    /// <summary>Externally force panel height and re-layout internal areas.</summary>
    public void SetHeight(float h)
    {
        reservedHeight = Mathf.Max(120f, h);

        if (_rt) _rt.sizeDelta = new Vector2(_rt.sizeDelta.x, reservedHeight);

        if (_bodyRT && _footerRT)
        {
            // Body takes most of the height; footer sits below it
            _bodyRT.sizeDelta = new Vector2(_bodyRT.sizeDelta.x, Mathf.Max(80f, reservedHeight - 140f));
            _footerRT.anchoredPosition = new Vector2(_footerRT.anchoredPosition.x, -(48f + _bodyRT.sizeDelta.y + 6f));
        }
        FireChanged();
    }

    /// <summary>Shorthand setter used by some callers.</summary>
    public void SetContent(string title, string body)
    {
        SetTitle(title);
        SetBody(body);
    }

    /// <summary>Raise the submit event with (prompt, body) values currently in the panel.</summary>
    public void OnSubmit()
    {
        var prompt = _prompt ? _prompt.text : "";
        var body   = _body   ? _body.text   : "";
        onSubmit?.Invoke(this, prompt, body);
    }

    /// <summary>Ensure visuals are built exactly once. Call with desired size.</summary>
    public void BuildIfNeeded(Vector2 size)
    {
        if (_built) return;
        InitVisual(size);
        _built = true;
    }

    // ===== Internal UI refs =====
    RectTransform _rt;
    RectTransform _bodyRT, _footerRT;
    Text _title, _body, _processed;
    InputField _prompt;
    Button _copyBtn, _closeBtn, _submitBtn;
    bool _built = false;

    // ===== Build UI (call via BuildIfNeeded) =====
    public void InitVisual(Vector2 size)
    {
        reservedHeight = size.y > 0 ? size.y : reservedHeight;

        // Root background
        var img = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
        img.color = new Color(1, 1, 1, 0.95f);

        _rt = GetComponent<RectTransform>();
        if (_rt == null) _rt = gameObject.AddComponent<RectTransform>();
        _rt.anchorMin = _rt.anchorMax = new Vector2(0, 1); // top-left anchored
        _rt.pivot = new Vector2(0, 1);
        _rt.sizeDelta = new Vector2(size.x, reservedHeight);

        // ----- Header -----
        var header = new GameObject("Header", typeof(RectTransform), typeof(Image));
        header.transform.SetParent(transform, false);
        var headerRT = header.GetComponent<RectTransform>();
        headerRT.anchorMin = headerRT.anchorMax = new Vector2(0, 1);
        headerRT.pivot = new Vector2(0, 1);
        headerRT.sizeDelta = new Vector2(size.x, 44f);
        headerRT.anchoredPosition = Vector2.zero;
        header.GetComponent<Image>().color = new Color(0.92f, 0.96f, 1f, 1f);

        _title = CreateText(header.transform, "Title", "Paragraph", 18, FontStyle.Bold);
        var trt = _title.GetComponent<RectTransform>();
        trt.anchoredPosition = new Vector2(10f, -8f);
        trt.sizeDelta = new Vector2(size.x - 140f, 32f);

        _copyBtn = CreateMiniButton(headerRT, "Copy", new Vector2(size.x - 128f, -6f), new Vector2(56f, 30f));
        _copyBtn.onClick.AddListener(() =>
        {
            GUIUtility.systemCopyBuffer = _body != null ? _body.text : "";
        });

        _closeBtn = CreateMiniButton(headerRT, "Close", new Vector2(size.x - 66f, -6f), new Vector2(56f, 30f));
        _closeBtn.onClick.AddListener(() => { onClose?.Invoke(this); });

        // ----- Body -----
        var bodyGO = new GameObject("Body", typeof(RectTransform));
        bodyGO.transform.SetParent(transform, false);
        _bodyRT = bodyGO.GetComponent<RectTransform>();
        _bodyRT.anchorMin = _bodyRT.anchorMax = new Vector2(0, 1);
        _bodyRT.pivot = new Vector2(0, 1);
        _bodyRT.anchoredPosition = new Vector2(10f, -48f);
        _bodyRT.sizeDelta = new Vector2(size.x - 20f, Mathf.Max(80f, reservedHeight - 140f));

        _body = CreateText(bodyGO.transform, "BodyText", "", 16, FontStyle.Normal);
        var brt = _body.GetComponent<RectTransform>();
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = _bodyRT.sizeDelta;
        _body.alignment = TextAnchor.UpperLeft;
        _body.horizontalOverflow = HorizontalWrapMode.Wrap;
        _body.verticalOverflow   = VerticalWrapMode.Overflow;

        // ----- Footer (Prompt + Submit + Processed) -----
        var footer = new GameObject("Footer", typeof(RectTransform));
        footer.transform.SetParent(transform, false);
        _footerRT = footer.GetComponent<RectTransform>();
        _footerRT.anchorMin = _footerRT.anchorMax = new Vector2(0, 1);
        _footerRT.pivot = new Vector2(0, 1);
        _footerRT.anchoredPosition = new Vector2(10f, -(48f + _bodyRT.sizeDelta.y + 6f));
        _footerRT.sizeDelta = new Vector2(size.x - 20f, 80f);

        // Prompt input
        var promptGO = new GameObject("Prompt", typeof(RectTransform), typeof(Image), typeof(InputField));
        promptGO.transform.SetParent(_footerRT, false);
        var prt = promptGO.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = new Vector2(0, 1);
        prt.pivot = new Vector2(0, 1);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(_footerRT.sizeDelta.x - 90f, 30f);
        promptGO.GetComponent<Image>().color = new Color(0.95f, 0.95f, 0.95f, 1f);

        _prompt = promptGO.GetComponent<InputField>();
        _prompt.textComponent = CreateText(promptGO.transform, "Text", "", 14, FontStyle.Normal, forInput: true);
        var tRect = _prompt.textComponent.GetComponent<RectTransform>();
        tRect.anchorMin = tRect.anchorMax = new Vector2(0, 1);
        tRect.pivot = new Vector2(0, 1);
        tRect.anchoredPosition = new Vector2(6f, -6f);
        tRect.sizeDelta = new Vector2(prt.sizeDelta.x - 12f, prt.sizeDelta.y - 12f);

        // Submit
        _submitBtn = CreateMiniButton(_footerRT, "Submit", new Vector2(_footerRT.sizeDelta.x - 80f, 0f), new Vector2(80f, 30f));
        _submitBtn.onClick.AddListener(OnSubmit);

        // Processed output
        _processed = CreateText(_footerRT, "Processed", "", 14, FontStyle.Italic);
        var prt2 = _processed.GetComponent<RectTransform>();
        prt2.anchoredPosition = new Vector2(0, -36f);
        prt2.sizeDelta = new Vector2(_footerRT.sizeDelta.x, 40f);
        _processed.horizontalOverflow = HorizontalWrapMode.Wrap;
        _processed.verticalOverflow   = VerticalWrapMode.Overflow;

        FireChanged();
    }

    void FireChanged() => onAnyChanged?.Invoke(this);

    // ===== Small UI helpers (legacy Text only; no TMP) =====
    Text CreateText(Transform parent, string name, string content, int size, FontStyle style, bool forInput = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);

        var tx = go.GetComponent<Text>();
        tx.text = content;
        tx.fontStyle = style;

        // Safe built-in font (new Unity versions deprecated Arial builtin)
        Font builtin = null;
        try { builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        tx.font = builtin ?? Font.CreateDynamicFontFromOSFont("Arial", size);

        tx.fontSize = size;
        tx.color = Color.black;
        tx.alignment = forInput ? TextAnchor.MiddleLeft : TextAnchor.UpperLeft;
        tx.horizontalOverflow = HorizontalWrapMode.Wrap;
        tx.verticalOverflow   = VerticalWrapMode.Overflow;
        return tx;
    }

    Button CreateMiniButton(RectTransform parent, string label, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        var img = go.GetComponent<Image>();
        img.color = new Color(0.85f, 0.9f, 1f, 1f);

        var b = go.GetComponent<Button>();

        var txtGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
        txtGO.transform.SetParent(go.transform, false);
        var trt = txtGO.GetComponent<RectTransform>();
        trt.anchorMin = trt.anchorMax = new Vector2(0, 1);
        trt.pivot = new Vector2(0, 1);
        trt.anchoredPosition = new Vector2(6f, -6f);
        trt.sizeDelta = new Vector2(size.x - 12f, size.y - 12f);

        var t = txtGO.GetComponent<Text>();
        Font builtin = null;
        try { builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        t.font = builtin ?? Font.CreateDynamicFontFromOSFont("Arial", 14);
        t.text = label;
        t.fontSize = 14;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(0.1f, 0.1f, 0.2f, 1f);

        return b;
    }
}
