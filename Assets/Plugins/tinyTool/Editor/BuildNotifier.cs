using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;

public class BuildNotifier : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    static string buildMessage;
    static DateTime buildTime;
    static bool buildFailed;

    public void OnPostprocessBuild(BuildReport report)
    {
        buildTime = DateTime.Now;
        buildFailed = report.summary.result != BuildResult.Succeeded;

        if (!buildFailed)
        {
            buildMessage =
                $"Build Succeeded\n" +
                $"Build Time: {report.summary.totalTime.TotalSeconds:F2}s";
        }
        else
        {
            buildMessage =
                $"Build Failed ({report.summary.result})";

            var errors = report.steps
                .SelectMany(s => s.messages)
                .Where(m => m.type == LogType.Error || m.type == LogType.Exception)
                .Select(m => m.content);

            if (errors.Any())
            {
                buildMessage += "\n\nErrors:\n" + string.Join("\n", errors);
            }
        }

        // Delay popup + sound until editor is safe
        EditorApplication.delayCall += ShowPopupWithSound;
    }

    static void ShowPopupWithSound()
    {
        PlayEditorSound(buildFailed);

        EditorUtility.DisplayDialog(
            "Build Finished",
            $"Finished At: {buildTime:yyyy-MM-dd HH:mm:ss}\n\n{buildMessage}",
            "OK"
        );
    }

    /// <summary>
    /// Plays Unity Editor internal system sounds
    /// </summary>
    static void PlayEditorSound(bool isError)
    {
        try
        {
            // Internal Unity editor sound utility
            var editorAssembly = typeof(EditorApplication).Assembly;
            var audioUtilType = editorAssembly.GetType("UnityEditor.AudioUtil");

            if (audioUtilType == null)
                return;

            MethodInfo playMethod = audioUtilType.GetMethod(
                isError ? "PlayEditorSound" : "PlayPreviewClip",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            );

            // Known built-in sound names
            if (isError)
            {
                // Error beep
                MethodInfo errorSound = audioUtilType.GetMethod(
                    "PlayEditorSound",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                );

                errorSound?.Invoke(null, new object[] { "console.error" });
            }
            else
            {
                MethodInfo okSound = audioUtilType.GetMethod(
                    "PlayEditorSound",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                );

                okSound?.Invoke(null, new object[] { "console.info" });
            }
        }
        catch
        {
            // Fallback (very safe)
            EditorApplication.Beep();
        }
    }
}
