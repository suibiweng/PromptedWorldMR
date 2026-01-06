using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ScenePlannerUI : MonoBehaviour
{
    [Header("Planner")]
    public ScenePlanner planner;

    [Header("UI")]
    public TMP_InputField descriptionInput;
    public Button generateButton;
    public TMP_Text statusText;
    public TMP_Text outputText;
    public ScrollRect outputScroll;

    void Awake()
    {
        generateButton.onClick.AddListener(OnGenerateClicked);
        SetStatus("READY");
    }

    void OnGenerateClicked()
    {
        if (planner == null)
        {
            SetStatus("NO SCENE PLANNER");
            return;
        }

        if (string.IsNullOrWhiteSpace(descriptionInput.text))
        {
            SetStatus("ENTER A DESCRIPTION");
            return;
        }

        outputText.text = "";
        planner.userDescription = descriptionInput.text;
        planner.GenerateScenePlan();

        SetStatus("GENERATING...");
        InvokeRepeating(nameof(PollPlanner), 0.2f, 0.2f);
    }

    void PollPlanner()
    {
        if (!string.IsNullOrEmpty(planner.rawScenePlanJson))
        {
            CancelInvoke(nameof(PollPlanner));

            outputText.text = planner.rawScenePlanJson;
            outputScroll.verticalNormalizedPosition = 1f;

            SetStatus("DONE");
        }
    }

    void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
    }
}
