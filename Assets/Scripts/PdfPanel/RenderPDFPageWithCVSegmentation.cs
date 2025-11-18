using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Paroxe.PdfRenderer;

// OpenCVForUnity
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;

// Avoid Rect name clashes
using CvRect = OpenCVForUnity.CoreModule.Rect;
using URect  = UnityEngine.Rect;

using TMPro;

public class RenderPDFPageWithCVSegmentation : MonoBehaviour
{
    [Header("PDF Source")]
    public string pdfUrl;
    public string password = null;
    public int pageIndex = 0;

    [Header("Prompt Prefab (TMP)")]
    [Tooltip("Assign a prefab that contains a TMP_InputField somewhere under it.")]
    public GameObject promptFieldPrefab;

    [Header("UI (assign in Scene)")]
    public RawImage pageImage;           // Texture target
    public RectTransform overlayParent;  // Keep centered in Scene (do NOT change anchors/pivot at runtime)
    public Button paragraphButtonPrefab; // Optional (else built-in)

    [Header("Button Look & Feel")]
    public Color buttonColor = new Color(0, 0.6f, 1, 0.18f);
    public float labelPadding = 8f;
    public float minButtonWidth = 220f;
    public float minButtonHeight = 44f;
    public int previewLabelChars = 80;

    [Header("Overlay Behavior")]
    public bool buttonsCoverBlocks = true;      // overlay covers whole block
    public bool buttonLabelFullParagraph = true;
    public bool transparentButton = true;

    [Header("Render Size")]
    public int targetWidth = 1600;
    public int maxHeight = 0; // 0 = no limit

    [Header("CV Segmentation")]
    public int adaptiveBlockSize = 35;  // odd
    public int adaptiveC = 12;
    public Vector2Int morphCloseKernel = new Vector2Int(17, 5);
    public Vector2Int verticalDilateKernel = new Vector2Int(7, 42);
    public float minBlockArea = 4000f;
    public float maxAspectRatio = 28f;
    public int mergeTolerance = 12;
    public int extraVerticalMerge = 26;
    [Range(0.02f, 0.5f)] public float columnSplitFrac = 0.12f;

    [Header("Extraction")]
    public int blockPad = 8;
    public int fallbackPadExtra = 10;

    [Header("Coordinate / Ordering")]
    public bool deviceYOriginTopLeft = true;

    [Header("Outputs")]
    [TextArea(5, 20)] public string fullText = "";
    [TextArea(3, 10)] public string selectedParagraphText = "";

    // -------- Side Panel (single, reused) --------
    [Header("Side Panel (Expansion)")]
    public RectTransform sidePanelHost;     // assign a rect outside the page (e.g., right column)
    public Vector2 panelSize = new Vector2(520, 720);
    public bool showPanelOnClick = true;
    public string panelTitle = "Paragraph";

    [Header("Pagination UI (optional)")]
    public Button prevPageButton;            // assign if you want a "Previous" button
    public Button nextPageButton;            // assign if you want a "Next" button
    public Component pageNumberInput;        // InputField or TMP_InputField
    public Text pageCountLabel;              // e.g., " / 12"
    public bool autoRunOnPageChange = true;  // when page changes, re-run pipeline

    public bool loadOnStart = true;

    // internals
    Texture2D _tex;
    readonly List<GameObject> _spawned = new();
    PDFDocument _doc;
    PDFPage _page;
    Vector2 _pagePts;
    int _devW, _devH;

    // side panel internals
    GameObject _panelGO;
    RectTransform _panelRT;
    CanvasGroup _panelCg;

    // header
    Component _panelTitleText;
    Button _copyBtn, _closeBtn;

    // prompt row
    RectTransform _promptRowRT;
    TMP_InputField _panelPromptTMP;  // TMP input field from prefab
    Button _submitBtn;

    // body
    Component _panelBodyText;        // will be TMP_Text
    RectTransform _contentRT;
    RectTransform _bodyRT;

    // pagination + pipeline state
    string _currentUrl = null;
    int _pageCount = 0;
    Coroutine _pipelineCo = null;
    bool _isLoading = false;

    void Start()
    {
        if (!pageImage || !overlayParent)
        {
            Debug.LogError("[CVSeg] Assign pageImage & overlayParent.");
            return;
        }

        // Wire pagination UI if assigned
        if (prevPageButton) prevPageButton.onClick.AddListener(PrevPage);
        if (nextPageButton) nextPageButton.onClick.AddListener(NextPage);
        if (pageNumberInput) UIX.WireOnEndEdit(pageNumberInput, OnPageNumberEntered);

        if (loadOnStart)
        {
            _currentUrl = pdfUrl;
            StartPipeline(true);  // force fresh load
        }
    }

    // -------------------- Pagination public API --------------------
    public void LoadPDF(string url) { LoadPDF(url, 0); }

    public void LoadPDF(string url, int page)
    {
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogError("[CVSeg] LoadPDF called with empty url.");
            return;
        }
        _currentUrl = url;
        pageIndex = Mathf.Max(0, page);
        StartPipeline(true); // (re)download + (re)open doc
    }

    public void NextPage()
    {
        if (_isLoading || _doc == null) return;
        if (pageIndex >= _pageCount - 1) return;
        pageIndex++;
        if (autoRunOnPageChange) StartPipeline(false);
    }

    public void PrevPage()
    {
        if (_isLoading || _doc == null) return;
        if (pageIndex <= 0) return;
        pageIndex--;
        if (autoRunOnPageChange) StartPipeline(false);
    }

    public void GoToPage(int page)
    {
        if (_isLoading || _doc == null) return;
        int clamped = Mathf.Clamp(page, 0, Mathf.Max(0, _pageCount - 1));
        if (clamped == pageIndex) return;
        pageIndex = clamped;
        if (autoRunOnPageChange) StartPipeline(false);
    }

    void StartPipeline(bool reloadDoc)
    {
        if (_pipelineCo != null) StopCoroutine(_pipelineCo);
        _pipelineCo = StartCoroutine(RunPipeline(_currentUrl, reloadDoc));
    }

    void OnDestroy()
    {
        if (_tex) Destroy(_tex);
        if (_page != null) _page.Dispose();
        if (_doc  != null) _doc.Dispose();
        ClearSpawned();
        if (_panelGO) Destroy(_panelGO);
    }

    // -------------------- Main pipeline --------------------
    IEnumerator RunPipeline(string runUrl, bool reloadDoc)
    {
        if (string.IsNullOrEmpty(runUrl))
        {
            Debug.LogError("[CVSeg] pdfUrl is empty.");
            yield break;
        }

        _isLoading = true;

        // Clear UI that depends on old page
        HideSidePanel();
        selectedParagraphText = "";
        fullText = "";
        ClearSpawned();

        // Re/open document only if needed
        bool needNewDoc = reloadDoc || _doc == null || !string.Equals(runUrl, _currentUrl);
        if (needNewDoc)
        {
            // Dispose old doc/page
            if (_page != null) { _page.Dispose(); _page = null; }
            if (_doc != null)  { _doc.Dispose();  _doc  = null; }

            using (var req = UnityWebRequest.Get(runUrl))
            {
                yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                if (req.result != UnityWebRequest.Result.Success)
#else
                if (req.isHttpError || req.isNetworkError)
#endif
                {
                    Debug.LogError($"[CVSeg] Download fail: {req.error}");
                    _isLoading = false;
                    yield break;
                }

                var data = req.downloadHandler.data;
                _doc = new PDFDocument(data, password);
                if (!_doc.IsValid)
                {
                    Debug.LogError("[CVSeg] Invalid document or wrong password.");
                    _isLoading = false;
                    yield break;
                }

                _currentUrl = runUrl;
                _pageCount = Mathf.Max(0, _doc.GetPageCount());
            }
        }

        // Clamp page and open
        pageIndex = Mathf.Clamp(pageIndex, 0, Mathf.Max(0, _pageCount - 1));
        if (_page != null) { _page.Dispose(); _page = null; }
        _page = _doc.GetPage(pageIndex);
        _pagePts = _page.GetPageSize(1f);

        // Decide device rendering W/H
        _devW = Mathf.Max(64, targetWidth);
        _devH = Mathf.RoundToInt(_devW * (_pagePts.y / _pagePts.x));
        if (maxHeight > 0 && _devH > maxHeight)
        {
            _devH = maxHeight;
            _devW = Mathf.RoundToInt(_devH * (_pagePts.x / _pagePts.y));
        }

        // Render page bitmap
        using (var renderer = new PDFRenderer())
        {
            var tex = renderer.RenderPageToTexture(_page, _devW, _devH, null, new PDFRenderer.RenderSettings());
            if (!tex) { Debug.LogError("[CVSeg] Render failed."); _isLoading = false; yield break; }
            if (_tex) Destroy(_tex);
            _tex = tex;

            pageImage.texture = _tex;
            pageImage.rectTransform.sizeDelta = new Vector2(_devW, _devH);

            // Let layout settle, then mirror overlay once
            yield return AlignOverlayNextFrame();
            AlignOverlayToPage();
        }

        // Detect + merge blocks
        var blocks = DetectParagraphBlocks(_tex);
        blocks = MergeRects(blocks, mergeTolerance);
        blocks = MergeRectsVertically(blocks, extraVerticalMerge);

        float colGap = Mathf.Max(16f, (_devW * columnSplitFrac));
        var columns = Columnize(blocks, colGap);
        foreach (var col in columns)
            col.Sort((a, b) => a.yMin.CompareTo(b.yMin)); // device-space top -> down

        var ordered = new List<URect>();
        foreach (var col in columns) ordered.AddRange(col);

#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.LogWarning("[CVSeg] Bounded text extraction not available on WebGL runtime. Use OCR in WebGL.");
        UpdatePaginationUI();   // still update the UI so user sees page info
        _isLoading = false;
        yield break;
#else
        // Build buttons + extract text
        var paragraphs = new List<string>(ordered.Count);
        using (var textPage = new PDFTextPage(_page))
        {
            int maxChars = Mathf.Max(1024, textPage.CountChars() * 2);

            foreach (var r0 in ordered)
            {
                URect rPad = PadRectDevice(r0, blockPad, _devW, _devH);
                var pr = DeviceRectToPageRect(rPad, _devW, _devH, _pagePts, deviceYOriginTopLeft);
                var (left, right, top, bottom) = PageRectToLTRB(pr);

                string raw = textPage.GetBoundedText(left, top, right, bottom, maxChars);
                string para = NormalizeParagraph(raw);

                if (string.IsNullOrWhiteSpace(para) || para.Length < 12)
                {
                    var r2 = PadRectDevice(r0, blockPad + fallbackPadExtra, _devW, _devH);
                    var pr2 = DeviceRectToPageRect(r2, _devW, _devH, _pagePts, deviceYOriginTopLeft);
                    var (l2, r2x, t2, b2) = PageRectToLTRB(pr2);
                    string raw2 = textPage.GetBoundedText(l2, t2, r2x, b2, maxChars);
                    string para2 = NormalizeParagraph(raw2);
                    if (!string.IsNullOrWhiteSpace(para2)) para = para2;
                }

                if (string.IsNullOrWhiteSpace(para)) para = "(no text)";
                paragraphs.Add(para);

                // Button placement
                float bx = Mathf.Round(r0.xMin);
                float by = -Mathf.Round(r0.yMin);

                float bw = buttonsCoverBlocks ? Mathf.Round(r0.width)  : Mathf.Max(minButtonWidth,  r0.width);
                float bh = buttonsCoverBlocks ? Mathf.Round(r0.height) : Mathf.Max(minButtonHeight, r0.height);

                var btn = CreateButton(overlayParent, new Vector2(bw, bh), new Vector2(bx, by),
                                       transparentButton ? (new Color(buttonColor.r, buttonColor.g, buttonColor.b, Mathf.Clamp01(buttonColor.a))) : buttonColor);

                string label = buttonLabelFullParagraph ? para : FirstLine(para, previewLabelChars);
                SetAnyButtonLabel(btn, label ?? "");

                if (buttonsCoverBlocks)
                {
                    SizeLabelToFill(btn, bw, bh, labelPadding);
                    var brt = btn.GetComponent<RectTransform>();
                    brt.sizeDelta = new Vector2(bw, bh);
                }
                else
                {
                    AutoSizeButtonToLabel(btn, bw, labelPadding);
                }

                string captured = para;
                float capturedH = bh;
                btn.onClick.AddListener(() =>
                {
                    selectedParagraphText = captured;
                    if (showPanelOnClick && sidePanelHost)
                        ShowSidePanel(panelTitle, captured, capturedH);
                });

                _spawned.Add(btn.gameObject);
            }
        }

        fullText = string.Join("\n\n", paragraphs.Where(s => !string.IsNullOrWhiteSpace(s)));

        // Mirror once more after building
        AlignOverlayToPage();

        // Update UI (page count, input, buttons)
        UpdatePaginationUI();

        _isLoading = false;
#endif
    }

    // ---------- Keep overlayParent centered without touching anchors/pivot ----------
    IEnumerator AlignOverlayNextFrame()
    {
        yield return new WaitForEndOfFrame(); // let layout/content fitters settle
        AlignOverlayToPage();
    }

    void AlignOverlayToPage()
    {
        if (!overlayParent || !pageImage) return;

        var ov  = overlayParent;
        var img = pageImage.rectTransform;

        // We DO NOT change anchors/pivot or parent here.
        // We only mirror size/position so overlay stays centered with the page.
        ov.sizeDelta        = img.sizeDelta;
        ov.anchoredPosition = img.anchoredPosition;

        // Optional double-flush if a container recalculates on this frame.
        LayoutSafe.Flush(ov);
        ov.sizeDelta        = img.sizeDelta;
        ov.anchoredPosition = img.anchoredPosition;
    }

    // -------------------- Side panel --------------------
    void EnsurePanelBuilt()
    {
        if (_panelGO != null || sidePanelHost == null) return;

        // Root panel
        _panelGO = new GameObject("ParagraphPanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        _panelRT = _panelGO.GetComponent<RectTransform>();
        _panelGO.transform.SetParent(sidePanelHost, false);
        SetTopLeftAnchors(_panelRT);
        _panelRT.sizeDelta = panelSize;
        _panelRT.anchoredPosition = Vector2.zero;

        var bg = _panelGO.GetComponent<Image>();
        bg.color = new Color(1, 1, 1, 0.95f);

        _panelCg = _panelGO.GetComponent<CanvasGroup>();
        _panelCg.alpha = 0f;
        _panelGO.SetActive(false);

        // ---------- HEADER ----------
        var header = new GameObject("Header", typeof(RectTransform), typeof(Image));
        var hrt = header.GetComponent<RectTransform>();
        header.transform.SetParent(_panelRT, false);
        SetTopLeftAnchors(hrt);
        hrt.sizeDelta = new Vector2(panelSize.x, 48f);
        hrt.anchoredPosition = Vector2.zero;
        header.GetComponent<Image>().color = new Color(0.92f, 0.96f, 1f, 1f);

        // Title
        var titleGO = new GameObject("Title", typeof(RectTransform), typeof(Text));
        var trt = titleGO.GetComponent<RectTransform>();
        titleGO.transform.SetParent(header.transform, false);
        SetTopLeftAnchors(trt);
        trt.anchoredPosition = new Vector2(12, -8);
        trt.sizeDelta = new Vector2(panelSize.x - 140f, 32f);
        var titleText = titleGO.GetComponent<Text>();
        titleText.text = "Paragraph";
        titleText.fontSize = 20;
        titleText.fontStyle = FontStyle.Bold;
        titleText.alignment = TextAnchor.MiddleLeft;
        titleText.color = Color.black;
        _panelTitleText = titleText;

        // Copy button
        _copyBtn = UIX.CreateMiniButton(hrt, "Copy", new Vector2(panelSize.x - 128f, -8f), new Vector2(56f, 32f));
        _copyBtn.onClick.AddListener(() =>
        {
            var bodyStr = UIX.GetTextFromComponent(_panelBodyText);
            GUIUtility.systemCopyBuffer = bodyStr ?? "";
        });

        // Close button
        _closeBtn = UIX.CreateMiniButton(hrt, "Close", new Vector2(panelSize.x - 64f, -8f), new Vector2(56f, 32f));
        _closeBtn.onClick.AddListener(() => HideSidePanel());

        // ---------- SCROLL AREA ----------
        var scrollGO = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
        var srt = scrollGO.GetComponent<RectTransform>();
        scrollGO.transform.SetParent(_panelRT, false);
        SetTopLeftAnchors(srt);
        srt.anchoredPosition = new Vector2(0, -48f);
        srt.sizeDelta = new Vector2(panelSize.x, panelSize.y - 48f);
        scrollGO.GetComponent<Image>().color = Color.white;
        scrollGO.GetComponent<Mask>().showMaskGraphic = false;

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        var vrt = viewport.GetComponent<RectTransform>();
        viewport.transform.SetParent(scrollGO.transform, false);
        SetTopLeftAnchors(vrt);
        vrt.anchoredPosition = Vector2.zero;
        vrt.sizeDelta = srt.sizeDelta;

        var content = new GameObject("Content", typeof(RectTransform));
        _contentRT = content.GetComponent<RectTransform>();
        content.transform.SetParent(viewport.transform, false);
        SetTopLeftAnchors(_contentRT);
        _contentRT.anchoredPosition = Vector2.zero;
        _contentRT.sizeDelta = new Vector2(panelSize.x - 24f, panelSize.y - 72f);

        // ---------- PROMPT ROW ----------
        var promptRow = new GameObject("PromptRow", typeof(RectTransform), typeof(Image));
        _promptRowRT = promptRow.GetComponent<RectTransform>();
        promptRow.transform.SetParent(_contentRT, false);
        SetTopLeftAnchors(_promptRowRT);
        _promptRowRT.anchoredPosition = new Vector2(12f, -12f);
        _promptRowRT.sizeDelta = new Vector2(panelSize.x - 48f, 36f);
        promptRow.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.04f);

        // Prompt (TMP prefab)
        if (promptFieldPrefab != null)
        {
            GameObject promptGO = Instantiate(promptFieldPrefab, promptRow.transform);

            RectTransform prt = promptGO.GetComponent<RectTransform>();
            if (prt != null)
            {
                SetTopLeftAnchors(prt);
                prt.anchoredPosition = new Vector2(8f, -6f);
                prt.sizeDelta = new Vector2(
                    _promptRowRT.sizeDelta.x - 16f - 64f,
                    _promptRowRT.sizeDelta.y - 12f
                );
            }

            _panelPromptTMP = promptGO.GetComponentInChildren<TMP_InputField>(true);
            if (_panelPromptTMP == null)
            {
                Debug.LogError("promptFieldPrefab does NOT contain a TMP_InputField!");
            }
            else
            {
                _panelPromptTMP.text = "";
                if (_panelPromptTMP.placeholder != null)
                {
                    var p = _panelPromptTMP.placeholder.GetComponent<TMP_Text>();
                    if (p) p.text = "Type prompt…";
                }
            }
        }
        else
        {
            Debug.LogError("Assign promptFieldPrefab in Inspector!");
        }

        // Submit button
        _submitBtn = UIX.CreateMiniButton(_promptRowRT, "Submit",
            new Vector2(_promptRowRT.sizeDelta.x - 60f, -2f),
            new Vector2(56f, 32f));
        _submitBtn.onClick.AddListener(OnSubmitClicked);

        // ---------- BODY (TMP) ----------
        var bodyGO = new GameObject("Body", typeof(RectTransform), typeof(TextMeshProUGUI));
        _bodyRT = bodyGO.GetComponent<RectTransform>();
        bodyGO.transform.SetParent(_contentRT, false);
        SetTopLeftAnchors(_bodyRT);
        _bodyRT.anchoredPosition = new Vector2(12f, -12f - _promptRowRT.sizeDelta.y - 8f);
        _bodyRT.sizeDelta = new Vector2(panelSize.x - 48f, panelSize.y - 96f - _promptRowRT.sizeDelta.y);

        var bodyTMP = bodyGO.GetComponent<TextMeshProUGUI>();
        bodyTMP.fontSize = 16;
        bodyTMP.color = Color.black;
        bodyTMP.alignment = TextAlignmentOptions.TopLeft;
        bodyTMP.enableWordWrapping = true;
        _panelBodyText = bodyTMP;

        // ScrollRect wiring
        var scroll = scrollGO.GetComponent<ScrollRect>();
        scroll.viewport = vrt;
        scroll.content = _contentRT;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
    }

    void ShowSidePanel(string title, string body, float? matchHeight = null)
    {
        EnsurePanelBuilt();
        if (_panelGO == null) return;

        UIX.SetTextOnComponent(_panelTitleText, title ?? panelTitle);
        if (_panelBodyText is TMP_Text t) t.text = body ?? "";
        else UIX.SetTextOnComponent(_panelBodyText, body ?? "");

        // autosize body + content
        var preferred = GetPreferredHeight(_panelBodyText, body ?? "", _bodyRT.sizeDelta.x);
        _bodyRT.sizeDelta = new Vector2(_bodyRT.sizeDelta.x, preferred);
        _contentRT.sizeDelta = new Vector2(_contentRT.sizeDelta.x, preferred + _promptRowRT.sizeDelta.y + 36f);

        // optional: match panel height to the clicked block
        if (matchHeight.HasValue)
        {
            var h = Mathf.Clamp(matchHeight.Value + 64f, 240f, 2000f);
            _panelRT.sizeDelta = new Vector2(panelSize.x, h);
        }
        else
        {
            _panelRT.sizeDelta = panelSize;
        }

        _panelGO.SetActive(true);
        StopAllCoroutines();
        StartCoroutine(FadePanel(0f, 1f, 0.12f));
    }

    void HideSidePanel()
    {
        if (_panelGO == null) return;
        StopAllCoroutines();
        StartCoroutine(FadePanel(1f, 0f, 0.12f, () => _panelGO.SetActive(false)));
    }

    void OnSubmitClicked()
    {
        if (_panelPromptTMP == null)
        {
            Debug.LogError("TMP InputField is not assigned / not found in prefab.");
            return;
        }

        // Read from TMP input field
        string prompt = _panelPromptTMP.text ?? "";

        // Read current body (selected paragraph or processed output)
        string bodyNow = UIX.GetTextFromComponent(_panelBodyText) ?? "";
        string selectedParagraph = bodyNow;

        // Build context (prompt + selected paragraph + full page text)
        string context =
            "PROMPT:\n" + prompt + "\n\n" +
            "SELECTED PARAGRAPH:\n" + selectedParagraph + "\n\n" +
            "FULL PAGE TEXT (reference):\n" + fullText;

        // TODO: replace with your real OpenAI call later
        string processed = ProcessWithLLM_Placeholder(prompt, selectedParagraph, fullText, context);

        // Write result back to the panel body
        if (_panelBodyText is TMP_Text t) t.text = processed;
        else UIX.SetTextOnComponent(_panelBodyText, processed);

        // Resize after updating text
        var preferred = GetPreferredHeight(_panelBodyText, processed, _bodyRT.sizeDelta.x);
        _bodyRT.sizeDelta = new Vector2(_bodyRT.sizeDelta.x, preferred);
        _contentRT.sizeDelta = new Vector2(_contentRT.sizeDelta.x, preferred + _promptRowRT.sizeDelta.y + 36f);
    }

    [Header("LLM Processor")]
public OpenAIParagraphProcessor openAIProcessor;


string ProcessWithLLM_Placeholder(string prompt, string selectedParagraph, string full, string combinedContext)
{
    // --- Auto-assign strictly via FindFirstObjectByType ---
    if (openAIProcessor == null)
    {
        // If you want to include inactive objects too, use the overload with the enum in newer Unity:
        // openAIProcessor = FindFirstObjectByType<OpenAIParagraphProcessor>(FindObjectsInactive.Include);
        openAIProcessor = FindFirstObjectByType<OpenAIParagraphProcessor>();
    }

    if (openAIProcessor == null)
    {
        return "[Error] OpenAIParagraphProcessor not found in the scene.";
    }

    // Lock UI during request
    if (_submitBtn) _submitBtn.interactable = false;
    if (_panelPromptTMP) _panelPromptTMP.interactable = false;

    // Build corpus from full page text
    var corpus = new List<string>();
    if (!string.IsNullOrEmpty(full))
    {
        corpus = full
            .Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList();
    }

    // Locate selected paragraph index (fallback to 0)
    int selectedIndex = 0;
    if (!string.IsNullOrEmpty(selectedParagraph) && corpus.Count > 0)
    {
        int idx = corpus.FindIndex(p => string.Equals(p, selectedParagraph, StringComparison.Ordinal));
        selectedIndex = Mathf.Clamp(idx < 0 ? 0 : idx, 0, Mathf.Max(0, corpus.Count - 1));
    }

    // Fire async; update panel on completion
    openAIProcessor.ProcessParagraphWithCorpus(
        corpus,
        selectedIndex,
        prompt ?? string.Empty,
        onDone: (result) =>
        {
            UIX.SetTextOnComponent(_panelBodyText, result ?? "");
            if (_submitBtn) _submitBtn.interactable = true;
            if (_panelPromptTMP) _panelPromptTMP.interactable = true;

            if (_bodyRT != null && _panelBodyText != null)
            {
                var preferred = GetPreferredHeight(_panelBodyText, result ?? "", _bodyRT.sizeDelta.x);
                _bodyRT.sizeDelta = new Vector2(_bodyRT.sizeDelta.x, preferred);
                if (_contentRT != null && _promptRowRT != null)
                    _contentRT.sizeDelta = new Vector2(_contentRT.sizeDelta.x, preferred + _promptRowRT.sizeDelta.y + 36f);
            }
        },
        onError: (err) =>
        {
            UIX.SetTextOnComponent(_panelBodyText, "[Error]\n" + (err ?? "Unknown error"));
            if (_submitBtn) _submitBtn.interactable = true;
            if (_panelPromptTMP) _panelPromptTMP.interactable = true;

            if (_bodyRT != null && _panelBodyText != null)
            {
                var preferred = GetPreferredHeight(_panelBodyText, UIX.GetTextFromComponent(_panelBodyText), _bodyRT.sizeDelta.x);
                _bodyRT.sizeDelta = new Vector2(_bodyRT.sizeDelta.x, preferred);
                if (_contentRT != null && _promptRowRT != null)
                    _contentRT.sizeDelta = new Vector2(_contentRT.sizeDelta.x, preferred + _promptRowRT.sizeDelta.y + 36f);
            }
        }
    );

    // Immediate status while async runs
    return "[Processing with LLM…]";
}



    IEnumerator FadePanel(float a, float b, float t, Action onDone = null)
    {
        float el = 0f;
        while (el < t)
        {
            el += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01(el / t));
            _panelCg.alpha = Mathf.Lerp(a, b, k);
            yield return null;
        }
        _panelCg.alpha = b;
        onDone?.Invoke();
    }

    void UpdatePaginationUI()
    {
        // Label like " / 12"
        if (pageCountLabel)
        {
            pageCountLabel.text = (_pageCount > 0) ? $" / {_pageCount}" : " / 0";
        }

        // Fill input with 1-based page number for users
        if (pageNumberInput)
        {
            UIX.SetInputText(pageNumberInput, (_pageCount > 0) ? (pageIndex + 1).ToString() : "0");
        }

        // Enable/disable prev/next
        bool canPrev = (_doc != null && pageIndex > 0);
        bool canNext = (_doc != null && pageIndex < _pageCount - 1);

        if (prevPageButton) prevPageButton.interactable = canPrev && !_isLoading;
        if (nextPageButton) nextPageButton.interactable = canNext && !_isLoading;
    }

    void OnPageNumberEntered(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText)) return;
        if (!int.TryParse(userText.Trim(), out var oneBased)) return;
        if (_pageCount <= 0) return;

        // Convert to 0-based
        int target = Mathf.Clamp(oneBased - 1, 0, _pageCount - 1);
        GoToPage(target);
    }

    // -------------------- CV segmentation --------------------
    List<URect> DetectParagraphBlocks(Texture2D srcTex)
    {
        var img = new Mat(srcTex.height, srcTex.width, CvType.CV_8UC3);
        Utils.texture2DToMat(srcTex, img);

        var gray = new Mat();
        Imgproc.cvtColor(img, gray, Imgproc.COLOR_RGB2GRAY);

        var bin = new Mat();
        int blk = Mathf.Max(3, adaptiveBlockSize | 1);
        Imgproc.adaptiveThreshold(gray, bin, 255, Imgproc.ADAPTIVE_THRESH_MEAN_C,
                                  Imgproc.THRESH_BINARY_INV, blk, adaptiveC);

        var kClose = Imgproc.getStructuringElement(Imgproc.MORPH_RECT,
                        new Size(Mathf.Max(1, morphCloseKernel.x), Mathf.Max(1, morphCloseKernel.y)));
        var closed = new Mat();
        Imgproc.morphologyEx(bin, closed, Imgproc.MORPH_CLOSE, kClose);

        var kVD = Imgproc.getStructuringElement(Imgproc.MORPH_RECT,
                        new Size(Mathf.Max(1, verticalDilateKernel.x), Mathf.Max(1, verticalDilateKernel.y)));
        var vDilated = new Mat();
        Imgproc.dilate(closed, vDilated, kVD);

        var contours = new List<MatOfPoint>();
        var hierarchy = new Mat();
        Imgproc.findContours(vDilated, contours, hierarchy, Imgproc.RETR_EXTERNAL, Imgproc.CHAIN_APPROX_SIMPLE);

        var rects = new List<URect>();
        foreach (var c in contours)
        {
            CvRect cr = Imgproc.boundingRect(c);
            if (cr.area() < minBlockArea) continue;
            float ar = (float)cr.width / Mathf.Max(1, cr.height);
            if (ar > maxAspectRatio) continue;
            rects.Add(UIX.ToURect(cr));
        }

        img.Dispose(); gray.Dispose(); bin.Dispose(); closed.Dispose(); vDilated.Dispose();
        kClose.Dispose(); kVD.Dispose(); hierarchy.Dispose();
        foreach (var c in contours) c.Dispose();

        rects = rects.OrderBy(r => r.xMin).ThenBy(r => r.yMin).ToList();
        return rects;
    }

    List<URect> MergeRects(List<URect> src, int tol)
    {
        if (src.Count <= 1) return src;
        var list = new List<URect>(src);
        bool merged;
        do
        {
            merged = false;
            for (int i = 0; i < list.Count && !merged; i++)
            {
                for (int j = i + 1; j < list.Count && !merged; j++)
                {
                    if (UIX.NearOrOverlap(list[i], list[j], tol))
                    {
                        var u = UIX.Union(list[i], list[j]);
                        list.RemoveAt(j); list.RemoveAt(i);
                        list.Add(u);
                        merged = true;
                    }
                }
            }
        } while (merged);
        return list;
    }

    List<URect> MergeRectsVertically(List<URect> src, int extraGap)
    {
        if (src.Count <= 1 || extraGap <= 0) return src;
        var list = new List<URect>(src);
        bool merged;
        do
        {
            merged = false;
            for (int i = 0; i < list.Count && !merged; i++)
            {
                for (int j = i + 1; j < list.Count && !merged; j++)
                {
                    var a = list[i]; var b = list[j];
                    bool xOverlap = a.xMin <= b.xMax && b.xMin <= a.xMax;
                    bool yNear = Mathf.Abs(a.yMax - b.yMin) <= extraGap || Mathf.Abs(b.yMax - a.yMin) <= extraGap;
                    if (xOverlap && yNear)
                    {
                        var u = UIX.Union(a, b);
                        list.RemoveAt(j); list.RemoveAt(i);
                        list.Add(u);
                        merged = true;
                    }
                }
            }
        } while (merged);
        return list;
    }

    List<List<URect>> Columnize(List<URect> rects, float gap)
    {
        var outCols = new List<List<URect>>();
        if (rects.Count == 0) return outCols;

        rects = rects.OrderBy(r => r.xMin).ThenBy(r => r.yMin).ToList();

        var curr = new List<URect> { rects[0] };
        float lastX = rects[0].xMin;

        for (int i = 1; i < rects.Count; i++)
        {
            float dx = Mathf.Abs(rects[i].xMin - lastX);
            if (dx > gap) { outCols.Add(curr); curr = new List<URect>(); }
            curr.Add(rects[i]);
            lastX = rects[i].xMin;
        }
        if (curr.Count > 0) outCols.Add(curr);
        return outCols;
    }

    // -------------------- Text helpers --------------------
    static (float left, float right, float top, float bottom) PageRectToLTRB(URect pageRect)
    {
        float left = pageRect.xMin;
        float right = pageRect.xMax;
        float top = pageRect.yMax;
        float bottom = pageRect.yMin;
        return (left, right, top, bottom);
    }

    static URect DeviceRectToPageRect(URect rDev, int devW, int devH, Vector2 pagePts, bool deviceTopLeft)
    {
        float x0 = (rDev.xMin / devW) * pagePts.x;
        float x1 = (rDev.xMax / devW) * pagePts.x;

        float yTopPx    = rDev.yMin;
        float yBottomPx = rDev.yMax;

        float yTopPts    = (deviceTopLeft ? (1f - (yTopPx    / devH)) : (yTopPx    / devH)) * pagePts.y;
        float yBottomPts = (deviceTopLeft ? (1f - (yBottomPx / devH)) : (yBottomPx / devH)) * pagePts.y;

        float yMin = Mathf.Min(yTopPts, yBottomPts);
        float yMax = Mathf.Max(yTopPts, yBottomPts);
        return URect.MinMaxRect(x0, yMin, x1, yMax);
    }

    static URect PadRectDevice(URect r, int pad, int devW, int devH)
    {
        float x0 = Mathf.Max(0, r.xMin - pad);
        float y0 = Mathf.Max(0, r.yMin - pad);
        float x1 = Mathf.Min(devW, r.xMax + pad);
        float y1 = Mathf.Min(devH, r.yMax + pad);
        return URect.MinMaxRect(x0, y0, x1, y1);
    }

    static string FirstLine(string s, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(s)) return "(no text)";
        var idx = s.IndexOfAny(new[] { '\n', '\r' });
        string line = idx >= 0 ? s.Substring(0, idx) : s;
        line = Regex.Replace(line, @"\s+", " ").Trim();
        if (line.Length > maxChars) line = line.Substring(0, maxChars - 1) + "…";
        return line;
    }

    static string NormalizeParagraph(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\0", "");
        s = s.Replace("\r\n", "\n").Replace("\r", "\n");
        s = Regex.Replace(s, @"[ \t]+", " ");
        s = Regex.Replace(s, @"(?<!\n)\n(?!\n)", " ");
        s = Regex.Replace(s, @"(\n\s*){3,}", "\n\n");
        return s.Trim();
    }

    // -------------------- UI helpers --------------------
    static void SetTopLeftAnchors(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
    }

    // (kept for completeness; not used for overlay anymore)
    void MatchOverlayTopLeft(RectTransform overlay, RectTransform image, float w, float h)
    {
        if (overlay.parent != image) overlay.SetParent(image, false);
        SetTopLeftAnchors(overlay);
        overlay.anchoredPosition = Vector2.zero;
        overlay.sizeDelta = new Vector2(w, h);

        SetTopLeftAnchors(image);
        image.anchoredPosition = Vector2.zero;
        image.sizeDelta = new Vector2(w, h);
    }

    Button CreateButton(RectTransform parent, Vector2 size, Vector2 anchoredPos, Color bg)
    {
        Button btn;
        if (paragraphButtonPrefab != null)
        {
            btn = Instantiate(paragraphButtonPrefab, parent);
            var rt = btn.GetComponent<RectTransform>();
            SetTopLeftAnchors(rt);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            var img = btn.GetComponent<Image>();
            if (img != null)
            {
                var c = bg;
                if (transparentButton) c.a = Mathf.Min(c.a, 0.2f);
                img.color = c;
            }
        }
        else
        {
            var go = new GameObject("ParagraphButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            SetTopLeftAnchors(rt);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            var img = go.GetComponent<Image>();
            var c = bg; if (transparentButton) c.a = Mathf.Min(c.a, 0.2f);
            img.color = c;

            btn = go.GetComponent<Button>();

            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGO.transform.SetParent(go.transform, false);
            var lrt = labelGO.GetComponent<RectTransform>();
            SetTopLeftAnchors(lrt);
            lrt.anchoredPosition = new Vector2(labelPadding, -labelPadding);
            lrt.sizeDelta = new Vector2(size.x - labelPadding * 2f, Mathf.Max(0, size.y - labelPadding * 2f));

            var txt = labelGO.GetComponent<Text>();
            txt.alignment = TextAnchor.UpperLeft;
            txt.raycastTarget = false;
            txt.color = Color.black;
            Font builtin = null;
            try { builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            txt.font = builtin ?? Font.CreateDynamicFontFromOSFont("Arial", 16);
            txt.fontSize = 16;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
        }
        return btn;
    }

    void SetAnyButtonLabel(Button btn, string label)
    {
        label ??= "";
        var text = btn.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.text = label;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return;
        }

        // TMP fallback
        foreach (var c in btn.GetComponentsInChildren<Component>(true))
        {
            var t = c.GetType();
            if (t.Name == "TMP_Text" || t.Name == "TextMeshProUGUI" || t.Name == "TextMeshPro")
            {
                UIX.EnsureTMPFontAssigned(c);
                var prop = t.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanWrite) { prop.SetValue(c, label, null); }
                return;
            }
        }
    }

    float AutoSizeButtonToLabel(Button btn, float fixedWidth, float pad)
    {
        var txt = btn.GetComponentInChildren<Text>(true);
        if (txt != null)
        {
            var lrt = txt.GetComponent<RectTransform>();
            SetTopLeftAnchors(lrt);
            lrt.anchoredPosition = new Vector2(pad, -pad);
            lrt.sizeDelta = new Vector2(fixedWidth - pad * 2f, 0);
            LayoutSafe.Flush(lrt);
            float prefH = Mathf.Ceil(txt.preferredHeight);
            var brt = btn.GetComponent<RectTransform>();
            float h = Mathf.Max(minButtonHeight, prefH + pad * 2f);
            brt.sizeDelta = new Vector2(fixedWidth, h);
            return h;
        }

        // TMP reflection path
        foreach (var c in btn.GetComponentsInChildren<Component>(true))
        {
            var t = c.GetType();
            if (t.Name == "TMP_Text" || t.Name == "TextMeshProUGUI" || t.Name == "TextMeshPro")
            {
                UIX.EnsureTMPFontAssigned(c);

                var lrt = (c as Component).GetComponent<RectTransform>();
                SetTopLeftAnchors(lrt);
                lrt.anchoredPosition = new Vector2(pad, -pad);
                lrt.sizeDelta = new Vector2(fixedWidth - pad * 2f, 0);

                float prefH = 0f;
                try
                {
                    var getPref = t.GetMethod("GetPreferredValues", new[] { typeof(string), typeof(float), typeof(float) });
                    var textProp = t.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
                    string val = textProp != null ? ((string)textProp.GetValue(c, null) ?? "") : "";
                    if (getPref != null)
                    {
                        var v = getPref.Invoke(c, new object[] { val, fixedWidth - pad * 2f, 0f });
                        if (v is Vector2 v2) prefH = v2.y;
                    }
                }
                catch { }

                var brt = btn.GetComponent<RectTransform>();
                float h = Mathf.Max(minButtonHeight, Mathf.Ceil(prefH) + pad * 2f);
                brt.sizeDelta = new Vector2(fixedWidth, h);
                return h;
            }
        }
        return btn.GetComponent<RectTransform>().sizeDelta.y;
    }

    float GetPreferredHeight(object textOrTMP, string value, float width)
    {
        if (textOrTMP == null) return 0f;
        value ??= "";

        if (textOrTMP is Text uText)
        {
            var saved = uText.text;
            uText.text = value;
            var lrt = uText.GetComponent<RectTransform>();
            var savedSize = lrt.sizeDelta;
            lrt.sizeDelta = new Vector2(width, 0f);
            LayoutSafe.Flush(lrt);
            float h = Mathf.Ceil(uText.preferredHeight);
            uText.text = saved;
            lrt.sizeDelta = savedSize;
            return Mathf.Max(40f, h);
        }

        // TMP reflection path
        var t = textOrTMP.GetType();
        UIX.EnsureTMPFontAssigned(textOrTMP as Component);

        try
        {
            var getPref = t.GetMethod("GetPreferredValues", new[] { typeof(string), typeof(float), typeof(float) });
            if (getPref != null)
            {
                var v = getPref.Invoke(textOrTMP, new object[] { value, width, 0f });
                if (v is Vector2 v2) return Mathf.Max(40f, Mathf.Ceil(v2.y));
            }
        }
        catch { }
        return 200f;
    }

    float SizeLabelToFill(Button btn, float bw, float bh, float pad)
    {
        var txt = btn.GetComponentInChildren<Text>(true);
        if (txt != null)
        {
            var lrt = txt.GetComponent<RectTransform>();
            SetTopLeftAnchors(lrt);
            lrt.anchoredPosition = new Vector2(pad, -pad);
            lrt.sizeDelta = new Vector2(Mathf.Max(0, bw - pad * 2f), Mathf.Max(0, bh - pad * 2f));
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            LayoutSafe.Flush(lrt);
            return bh;
        }
        foreach (var c in btn.GetComponentsInChildren<Component>(true))
        {
            var tt = c.GetType();
            if (tt.Name == "TMP_Text" || tt.Name == "TextMeshProUGUI" || tt.Name == "TextMeshPro")
            {
                UIX.EnsureTMPFontAssigned(c);

                var lrt = (c as Component).GetComponent<RectTransform>();
                SetTopLeftAnchors(lrt);
                lrt.anchoredPosition = new Vector2(pad, -pad);
                lrt.sizeDelta = new Vector2(Mathf.Max(0, bw - pad * 2f), Mathf.Max(0, bh - pad * 2f));
                LayoutSafe.Flush(lrt);
                return bh;
            }
        }
        return bh;
    }

    void ClearSpawned()
    {
        foreach (var go in _spawned) if (go) Destroy(go);
        _spawned.Clear();
    }
}

/* =========================
 * Helpers (UIX + LayoutSafe)
 * ========================= */
static class UIX
{
    public static Component AddTextOrTMP(GameObject host, string text, int fontSize, FontStyle style, TextAnchor align)
    {
        // Try TMP first
        var tmpType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        if (tmpType != null)
        {
            var comp = host.AddComponent(tmpType);
            EnsureTMPFontAssigned(comp);

            var prText = tmpType.GetProperty("text");
            prText?.SetValue(comp, text ?? "", null);

            var alProp = tmpType.GetProperty("alignment");
            var enumAlign = Type.GetType("TMPro.TextAlignmentOptions, Unity.TextMeshPro");
            if (alProp != null && enumAlign != null)
            {
                object alignVal = Enum.Parse(enumAlign, "TopLeft");
                if (align == TextAnchor.MiddleLeft) alignVal = Enum.Parse(enumAlign, "Left");
                if (align == TextAnchor.UpperLeft)  alignVal = Enum.Parse(enumAlign, "TopLeft");
                alProp.SetValue(comp, alignVal, null);
            }

            var fsProp = tmpType.GetProperty("fontSize");
            fsProp?.SetValue(comp, (float)fontSize, null);

            var colProp = tmpType.GetProperty("color");
            colProp?.SetValue(comp, Color.black, null);

            return comp as Component;
        }

        // Fallback: legacy Text
        var uText = host.AddComponent<Text>();
        uText.text = text ?? "";
        uText.fontStyle = style;
        Font builtin = null;
        try { builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        uText.font = builtin ?? Font.CreateDynamicFontFromOSFont("Arial", fontSize);
        uText.fontSize = fontSize;
        uText.color = Color.black;
        uText.alignment = align;
        uText.horizontalOverflow = HorizontalWrapMode.Wrap;
        uText.verticalOverflow = VerticalWrapMode.Overflow;
        return uText;
    }

    public static void SetTextOnComponent(object textOrTMP, string value)
    {
        if (textOrTMP == null) return;
        if (textOrTMP is Text ut) { ut.text = value ?? ""; return; }

        var t = textOrTMP.GetType();
        var prop = t.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
        prop?.SetValue(textOrTMP, value ?? "", null);
    }

    public static string GetTextFromComponent(object textOrTMP)
    {
        if (textOrTMP == null) return "";
        if (textOrTMP is Text ut) return ut.text ?? "";

        var t = textOrTMP.GetType();
        var prop = t.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
        return prop != null ? ((string)prop.GetValue(textOrTMP, null) ?? "") : "";
    }

    public static Button CreateMiniButton(RectTransform parent, string label, Vector2 anchoredPos, Vector2 size)
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

        var txtGO = new GameObject("Label", typeof(RectTransform));
        txtGO.transform.SetParent(go.transform, false);
        var trt = txtGO.GetComponent<RectTransform>();
        trt.anchorMin = trt.anchorMax = new Vector2(0, 1);
        trt.pivot = new Vector2(0, 1);
        trt.anchoredPosition = new Vector2(6f, -6f);
        trt.sizeDelta = new Vector2(size.x - 12f, size.y - 12f);
        AddTextOrTMP(txtGO, label, 14, FontStyle.Bold, TextAnchor.MiddleCenter);

        return b;
    }

    public static void EnsureTMPFontAssigned(Component maybeTMP)
    {
        if (maybeTMP == null) return;
        var t = maybeTMP.GetType();
        if (!(t.Name == "TMP_Text" || t.Name == "TextMeshProUGUI" || t.Name == "TextMeshPro")) return;

        try
        {
            var fontProp = t.GetProperty("font", BindingFlags.Public | BindingFlags.Instance);
            if (fontProp == null) return;

            var current = fontProp.GetValue(maybeTMP, null);
            if (current != null) return;

            var settingsType = Type.GetType("TMPro.TMP_Settings, Unity.TextMeshPro");
            var fontAssetType = Type.GetType("TMPro.TMP_FontAsset, Unity.TextMeshPro");
            if (settingsType != null && fontAssetType != null)
            {
                var defProp = settingsType.GetProperty("defaultFontAsset", BindingFlags.Public | BindingFlags.Static);
                var def = defProp != null ? defProp.GetValue(null, null) : null;

                if (def != null && fontAssetType.IsInstanceOfType(def))
                {
                    fontProp.SetValue(maybeTMP, def, null);
                    var colProp = t.GetProperty("color", BindingFlags.Public | BindingFlags.Instance);
                    colProp?.SetValue(maybeTMP, Color.black, null);
                }
            }
        }
        catch { }
    }

    // ===== Input helpers (support both legacy InputField and TMP_InputField) =====
    public static void SetInputText(Component input, string value)
    {
        if (!input) return;

        var t = input.GetType();
        // UnityEngine.UI.InputField
        if (t == typeof(UnityEngine.UI.InputField))
        {
            var f = input as UnityEngine.UI.InputField;
            f.text = value ?? "";
            return;
        }

        // TMPro.TMP_InputField (by reflection to avoid hard dependency here)
        var tmpType = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
        if (tmpType != null && tmpType.IsInstanceOfType(input))
        {
            var prop = tmpType.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
            prop?.SetValue(input, value ?? "", null);
            return;
        }
    }

    public static void WireOnEndEdit(Component input, Action<string> onEndEdit)
    {
        if (!input || onEndEdit == null) return;

        var t = input.GetType();
        // UnityEngine.UI.InputField
        if (t == typeof(UnityEngine.UI.InputField))
        {
            var f = input as UnityEngine.UI.InputField;
            f.onEndEdit.RemoveAllListeners();
            f.onEndEdit.AddListener(onEndEdit.Invoke);
            return;
        }

        // TMPro.TMP_InputField
        var tmpType = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
        if (tmpType != null && tmpType.IsInstanceOfType(input))
        {
            var evtProp = tmpType.GetProperty("onEndEdit", BindingFlags.Public | BindingFlags.Instance);
            var unityEventBase = evtProp?.GetValue(input, null);
            if (unityEventBase != null)
            {
                // onEndEdit is TMP_InputField.SubmitEvent (UnityEvent<string>)
                var removeAll = unityEventBase.GetType().GetMethod("RemoveAllListeners");
                removeAll?.Invoke(unityEventBase, null);

                var addListener = unityEventBase.GetType().GetMethod("AddListener");
                if (addListener != null)
                {
                    UnityEngine.Events.UnityAction<string> act = (s) => onEndEdit.Invoke(s);
                    addListener.Invoke(unityEventBase, new object[] { act });
                }
            }
        }
    }

    // Geometry helpers
    public static URect ToURect(CvRect r) => new URect(r.x, r.y, r.width, r.height);

    public static URect Union(URect a, URect b)
    {
        float xMin = Mathf.Min(a.xMin, b.xMin);
        float yMin = Mathf.Min(a.yMin, b.yMin);
        float xMax = Mathf.Max(a.xMax, b.xMax);
        float yMax = Mathf.Max(a.yMax, b.yMax);
        return URect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    public static bool NearOrOverlap(URect a, URect b, int tol)
    {
        var ea = new URect(a.xMin - tol, a.yMin - tol, a.width + 2 * tol, a.height + 2 * tol);
        var eb = new URect(b.xMin - tol, b.yMin - tol, b.width + 2 * tol, b.height + 2 * tol);
        return ea.Overlaps(eb) || eb.Overlaps(ea);
    }
}

static class LayoutSafe
{
    public static void Flush(RectTransform rt)
    {
        if (rt == null) return;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        Canvas.ForceUpdateCanvases();
    }
}
