#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

/// <summary>
/// Simple Git UI inside Unity: shows git status in an EditorWindow.
/// Extend from here: add commit, diff, history, branches, etc.
/// </summary>
public class GitFrontendWindow : EditorWindow
{
    private string repoRoot;
    private Vector2 scroll;
    private List<GitFileStatus> statusList = new List<GitFileStatus>();
    private string lastError;
    private bool autoDetectRepo = true;

    [MenuItem("Tools/Git Frontend")]
    public static void ShowWindow()
    {
        var window = GetWindow<GitFrontendWindow>("Git");
        window.Show();
    }

    private void OnEnable()
    {
        if (autoDetectRepo || string.IsNullOrEmpty(repoRoot))
        {
            repoRoot = FindRepoRoot(Application.dataPath);
        }
    }

    private void OnGUI()
    {
        DrawHeader();

        if (string.IsNullOrEmpty(repoRoot))
        {
            EditorGUILayout.HelpBox("No .git folder found. Set repo root manually or place your project under Git.", MessageType.Warning);
            DrawRepoRootField();
            return;
        }

        DrawRepoRootField();
        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Refresh Status", GUILayout.Height(25)))
            {
                RefreshStatus();
            }

            if (GUILayout.Button("Open System Git GUI", GUILayout.Height(25)))
            {
                // Example: opens 'git gui' if installed
                GitRunner.RunDetached("gui", repoRoot);
            }
        }

        if (!string.IsNullOrEmpty(lastError))
        {
            EditorGUILayout.HelpBox("Git error:\n" + lastError, MessageType.Error);
        }

        EditorGUILayout.Space();
        DrawStatusList();
    }

    private void DrawHeader()
    {
        EditorGUILayout.LabelField("Unity Git Frontend", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("A simple in-editor UI for git status (extendable).", EditorStyles.miniLabel);
        EditorGUILayout.Space();
    }

    private void DrawRepoRootField()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Repo Root:", GUILayout.Width(80));
            repoRoot = EditorGUILayout.TextField(repoRoot ?? "");

            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                var selected = EditorUtility.OpenFolderPanel("Select Git Repo Root", repoRoot ?? Application.dataPath, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    repoRoot = selected;
                    autoDetectRepo = false;
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            autoDetectRepo = EditorGUILayout.ToggleLeft("Auto-detect from Assets/", autoDetectRepo, GUILayout.Width(200));
            if (autoDetectRepo && !string.IsNullOrEmpty(Application.dataPath))
            {
                if (GUILayout.Button("Re-Detect", GUILayout.Width(80)))
                {
                    repoRoot = FindRepoRoot(Application.dataPath);
                }
            }
        }
    }

    private void DrawStatusList()
    {
        EditorGUILayout.LabelField("Changed Files", EditorStyles.boldLabel);

        if (statusList == null || statusList.Count == 0)
        {
            EditorGUILayout.HelpBox("No changes (working tree clean) or status not loaded yet.", MessageType.Info);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);

        foreach (var s in statusList)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(s.Code, GUILayout.Width(30));
                GUILayout.Label(s.Path, GUILayout.ExpandWidth(true));

                if (GUILayout.Button("Diff", GUILayout.Width(50)))
                {
                    ShowFileDiffPopup(s.Path);
                }

                // You can later add Stage/Unstage buttons here:
                // if (GUILayout.Button("Stage", GUILayout.Width(60))) { ... }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void ShowFileDiffPopup(string filePath)
    {
        string output = GitRunner.Run("diff -- " + QuotePath(filePath), repoRoot, out string error);
        if (!string.IsNullOrEmpty(error))
        {
            UnityEngine.Debug.LogError("Git diff error: " + error);
        }

        DiffViewerWindow.ShowDiff(filePath, output);
    }

    private void RefreshStatus()
    {
        if (string.IsNullOrEmpty(repoRoot) || !Directory.Exists(Path.Combine(repoRoot, ".git")))
        {
            lastError = "Repo root is invalid or .git folder missing.";
            statusList.Clear();
            return;
        }

        string output = GitRunner.Run("status --porcelain", repoRoot, out string error);
        lastError = error;

        statusList = GitStatusParser.ParseStatus(output);
        Repaint();
    }

    /// <summary>
    /// Climb up directories until a .git folder is found.
    /// </summary>
    private static string FindRepoRoot(string startPath)
    {
        if (string.IsNullOrEmpty(startPath))
            return null;

        string dir = startPath;
        if (File.Exists(startPath))
            dir = Path.GetDirectoryName(startPath);

        while (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;

            string parent = Directory.GetParent(dir)?.FullName;
            if (string.IsNullOrEmpty(parent) || parent == dir)
                break;

            dir = parent;
        }

        return null;
    }

    private static string QuotePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        if (path.Contains(" "))
            return "\"" + path.Replace("\"", "\\\"") + "\"";
        return path;
    }
}

/// <summary>
/// Represents one line of `git status --porcelain`.
/// Example line: " M Assets/Scripts/MyScript.cs"
/// </summary>
public class GitFileStatus
{
    public string Code;  // e.g. "M", "A", "??", "D"
    public string Path;  // file path relative to repo root
}

/// <summary>
/// Parses porcelain status output from git.
/// </summary>
public static class GitStatusParser
{
    public static List<GitFileStatus> ParseStatus(string raw)
    {
        var list = new List<GitFileStatus>();
        if (string.IsNullOrEmpty(raw))
            return list;

        using (var reader = new StringReader(raw))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length < 3) continue;

                // porcelain format: XY<space>PATH
                string code = line.Substring(0, 2).Trim();
                string path = line.Substring(3).Trim();

                list.Add(new GitFileStatus
                {
                    Code = code,
                    Path = path
                });
            }
        }

        return list;
    }
}

/// <summary>
/// Low-level git runner. Wraps the git executable.
/// </summary>
public static class GitRunner
{
    public static string GitExecutableName
    {
        get
        {
#if UNITY_EDITOR_WIN
            return "git.exe";
#else
            return "git";
#endif
        }
    }

    /// <summary>
    /// Run a git command and capture stdout + stderr.
    /// </summary>
    public static string Run(string arguments, string workingDirectory, out string error)
    {
        error = null;

        var psi = new ProcessStartInfo
        {
            FileName = GitExecutableName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            using (var process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return output;
            }
        }
        catch (System.Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Run a git command without waiting for output (for things like git gui).
    /// </summary>
    public static void RunDetached(string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = GitExecutableName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = false,
            UseShellExecute = false
        };

        try
        {
            Process.Start(psi);
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError("Failed to run git command: " + ex.Message);
        }
    }
}

/// <summary>
/// Simple popup window to display text diff.
/// </summary>
public class DiffViewerWindow : EditorWindow
{
    private string titleText;
    private string diffText;
    private Vector2 scroll;

    public static void ShowDiff(string title, string diff)
    {
        var win = CreateInstance<DiffViewerWindow>();
        win.titleText = "Diff: " + title;
        win.diffText = string.IsNullOrEmpty(diff) ? "(no diff output)" : diff;
        win.titleContent = new GUIContent("Diff");
        win.ShowUtility();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField(titleText, EditorStyles.boldLabel);
        EditorGUILayout.Space();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.TextArea(diffText, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }
}
#endif
