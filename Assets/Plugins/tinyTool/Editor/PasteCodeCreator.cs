#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using System.IO;
using System.Text.RegularExpressions;

public static class PasteCodeCreator
{
    // Cmd+V (macOS) / Ctrl+V (Windows)
    [MenuItem("Assets/Paste Code %#v", false, 2000)]
    public static void PasteCode()
    {
        string clipboard = EditorGUIUtility.systemCopyBuffer;

        if (string.IsNullOrWhiteSpace(clipboard))
        {
            EditorUtility.DisplayDialog("Paste Code", "Clipboard is empty.", "OK");
            return;
        }

        string folderPath = GetSelectedFolderPath();
        if (string.IsNullOrEmpty(folderPath))
        {
            EditorUtility.DisplayDialog(
                "Paste Code",
                "Please select a folder in the Project window.",
                "OK"
            );
            return;
        }

        // ===============================
        // Detection order (IMPORTANT)
        // ===============================

        // 1️⃣ Compute Shader
        if (IsComputeShader(clipboard))
        {
            CreateFileWithConflictHandling(
                folderPath,
                ExtractComputeName(clipboard),
                ".compute",
                clipboard
            );
            return;
        }

        // 2️⃣ C#
        if (IsCSharpCode(clipboard))
        {
            string className = ExtractClassName(clipboard);
            if (string.IsNullOrEmpty(className))
            {
                className = "NewScript";
            }

            CreateFileWithConflictHandling(
                folderPath,
                className,
                ".cs",
                clipboard
            );
            return;
        }

        // 3️⃣ Shader (STRICT)
        if (IsShaderCode(clipboard))
        {
            CreateFileWithConflictHandling(
                folderPath,
                GetSafeShaderFileName(clipboard),
                ".shader",
                clipboard
            );
            return;
        }

        // 4️⃣ Fallback TXT
        CreateFileWithConflictHandling(
            folderPath,
            "PastedText",
            ".txt",
            clipboard
        );
    }

    // ==================================================
    // File creation + conflict handling
    // ==================================================
    static void CreateFileWithConflictHandling(
        string folderPath,
        string baseName,
        string extension,
        string content
    )
    {
        string targetPath = Path.Combine(folderPath, baseName + extension);

        if (File.Exists(targetPath))
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "File Already Exists",
                $"\"{baseName}{extension}\" already exists.\n\nWhat would you like to do?",
                "Overwrite",
                "Cancel",
                "Create New"
            );

            // Overwrite
            if (choice == 0)
            {
                WriteFile(targetPath, content);
                return;
            }

            // Create New (auto-number)
            if (choice == 2)
            {
                string newPath = GenerateNumberedPath(folderPath, baseName, extension);
                WriteFile(newPath, content);
                return;
            }

            // Cancel
            return;
        }

        WriteFile(targetPath, content);
    }

    static void WriteFile(string path, string content)
    {
        File.WriteAllText(path, content);
        AssetDatabase.Refresh();
        Debug.Log($"Created file: {path}");
    }

    static string GenerateNumberedPath(string folder, string baseName, string extension)
    {
        int index = 1;
        string path;

        do
        {
            path = Path.Combine(folder, $"{baseName}{index}{extension}");
            index++;
        }
        while (File.Exists(path));

        return path;
    }

    // ==================================================
    // C# detection (STRICTER)
    // ==================================================
    static bool IsCSharpCode(string text)
    {
        return
            Regex.IsMatch(text, @"\bclass\s+[A-Za-z_][A-Za-z0-9_]*") ||
            text.Contains("using UnityEngine") ||
            text.Contains("using UnityEditor");
    }

    static string ExtractClassName(string code)
    {
        Match match = Regex.Match(
            code,
            @"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)"
        );

        return match.Success ? match.Groups[1].Value : null;
    }

    // ==================================================
    // Shader detection (FIXED)
    // ==================================================
    static bool IsShaderCode(string text)
    {
        // Must contain a real Shader declaration at line start
        return Regex.IsMatch(
            text,
            @"^\s*Shader\s+""[^""]+""",
            RegexOptions.Multiline
        );
    }

    static string GetSafeShaderFileName(string code)
    {
        Match match = Regex.Match(
            code,
            @"Shader\s+""([^""]+)"""
        );

        if (!match.Success)
            return "NewShader";

        return match.Groups[1].Value.Replace("/", "_");
    }

    // ==================================================
    // Compute shader detection
    // ==================================================
    static bool IsComputeShader(string text)
    {
        return Regex.IsMatch(
            text,
            @"^\s*#pragma\s+kernel\s+",
            RegexOptions.Multiline
        );
    }

    static string ExtractComputeName(string text)
    {
        Match match = Regex.Match(
            text,
            @"#pragma\s+kernel\s+([A-Za-z_][A-Za-z0-9_]*)"
        );

        return match.Success ? match.Groups[1].Value : "NewCompute";
    }

    // ==================================================
    // Utilities
    // ==================================================
    static string GetSelectedFolderPath()
    {
        Object obj = Selection.activeObject;

        if (obj == null)
            return "Assets";

        string path = AssetDatabase.GetAssetPath(obj);

        if (File.Exists(path))
            return Path.GetDirectoryName(path);

        return path;
    }
}

#endif
