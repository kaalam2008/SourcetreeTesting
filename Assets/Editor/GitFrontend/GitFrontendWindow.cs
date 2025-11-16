#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

/// <summary>
/// Unity Git Frontend:
/// - Show status
/// - Stage / Unstage
/// - Diff popup
/// - Commit
/// - Push
/// - Show current branch
/// - Remote & branch dropdowns
/// - Checkout & Pull
/// </summary>
public class GitFrontendWindow : EditorWindow
{
    private string repoRoot;
    private Vector2 scroll;
    private List<GitFileStatus> statusList = new List<GitFileStatus>();
    private string lastError;
    private bool autoDetectRepo = true;

    // Commit / Push fields
    private string commitMessage = "";
    private string pushRemote = "origin";
    private string pushBranch = ""; // default: current branch if empty

    // Branch / remote info
    private List<string> remotes = new List<string>();
    private List<string> localBranches = new List<string>();
    private int selectedRemoteIndex = 0;
    private int selectedBranchIndex = 0;
    private string currentBranch = "";

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

        if (!string.IsNullOrEmpty(repoRoot))
        {
            LoadGitMeta();
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

        DrawBranchAndRemoteArea();
        EditorGUILayout.Space();

        // Top buttons
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Refresh Status", GUILayout.Height(25)))
            {
                RefreshStatus();
            }

            if (GUILayout.Button("Stage All", GUILayout.Height(25)))
            {
                StageAll();
            }

            if (GUILayout.Button("Open System Git GUI", GUILayout.Height(25)))
            {
                GitRunner.RunDetached("gui", repoRoot);
            }
        }

        EditorGUILayout.Space();
        DrawCommitAndPushArea();
        EditorGUILayout.Space();

        if (!string.IsNullOrEmpty(lastError))
        {
            EditorGUILayout.HelpBox("Git message:\n" + lastError, MessageType.Error);
        }

        EditorGUILayout.Space();
        DrawStatusList();
    }

    private void DrawHeader()
    {
        EditorGUILayout.LabelField("Unity Git Frontend", EditorStyles.boldLabel);

        string branchText = string.IsNullOrEmpty(currentBranch)
            ? "Branch: (unknown)"
            : "Branch: " + currentBranch;

        EditorGUILayout.LabelField(branchText, EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Status • Stage • Commit • Push • Branch / Remote", EditorStyles.miniLabel);
        EditorGUILayout.Space();
    }

    private void DrawRepoRootField()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Repo Root:", GUILayout.Width(80));
            string oldRoot = repoRoot;
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

            // If repo root changed manually, reload meta
            if (oldRoot != repoRoot && !string.IsNullOrEmpty(repoRoot))
            {
                LoadGitMeta();
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
                    LoadGitMeta();
                }
            }

            if (GUILayout.Button("Reload Git Info", GUILayout.Width(120)))
            {
                LoadGitMeta();
            }
        }
    }

    private void DrawBranchAndRemoteArea()
    {
        EditorGUILayout.LabelField("Branch & Remote", EditorStyles.boldLabel);

        // Remote dropdown
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Remote:", GUILayout.Width(60));

            if (remotes != null && remotes.Count > 0)
            {
                int newIndex = EditorGUILayout.Popup(selectedRemoteIndex, remotes.ToArray());
                if (newIndex != selectedRemoteIndex)
                {
                    selectedRemoteIndex = newIndex;
                    pushRemote = remotes[selectedRemoteIndex];
                }
            }
            else
            {
                EditorGUILayout.LabelField("(no remotes)", GUILayout.Width(120));
            }

            // Also show editable field
            pushRemote = EditorGUILayout.TextField(pushRemote);
        }

        // Branch dropdown
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Branch:", GUILayout.Width(60));

            if (localBranches != null && localBranches.Count > 0)
            {
                int newIndex = EditorGUILayout.Popup(selectedBranchIndex, localBranches.ToArray());
                if (newIndex != selectedBranchIndex)
                {
                    selectedBranchIndex = newIndex;
                    pushBranch = localBranches[selectedBranchIndex];
                }
            }
            else
            {
                EditorGUILayout.LabelField("(no local branches)", GUILayout.Width(140));
            }

            // Also show editable field
            pushBranch = EditorGUILayout.TextField(pushBranch);
        }

        // Checkout & Pull
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Checkout Branch", GUILayout.Width(140)))
            {
                CheckoutSelectedBranch();
            }

            if (GUILayout.Button("Pull", GUILayout.Width(80)))
            {
                Pull();
            }
        }
    }

    private void DrawCommitAndPushArea()
    {
        EditorGUILayout.LabelField("Commit & Push", EditorStyles.boldLabel);

        // Commit row
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Message:", GUILayout.Width(60));
            commitMessage = EditorGUILayout.TextField(commitMessage);

            if (GUILayout.Button("Commit", GUILayout.Width(80)))
            {
                Commit();
            }

            if (GUILayout.Button("Commit & Push", GUILayout.Width(120)))
            {
                if (Commit())
                {
                    Push();
                }
            }
        }

        // Info about where push will go
        EditorGUILayout.LabelField(
            $"Push target: {(string.IsNullOrEmpty(pushRemote) ? "(default remote)" : pushRemote)} " +
            $"{(string.IsNullOrEmpty(pushBranch) ? "(tracking branch)" : pushBranch)}",
            EditorStyles.miniLabel
        );
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

                // Diff
                if (GUILayout.Button("Diff", GUILayout.Width(50)))
                {
                    ShowFileDiffPopup(s.Path);
                }

                // Stage / Unstage
                if (IsStaged(s))
                {
                    if (GUILayout.Button("Unstage", GUILayout.Width(70)))
                    {
                        UnstageFile(s.Path);
                        RefreshStatus();
                    }
                }
                else
                {
                    if (GUILayout.Button("Stage", GUILayout.Width(60)))
                    {
                        StageFile(s.Path);
                        RefreshStatus();
                    }
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    #region Git Operations

    private void ShowFileDiffPopup(string filePath)
    {
        string output = GitRunner.Run("diff -- " + QuotePath(filePath), repoRoot, out string error);

        if (!string.IsNullOrEmpty(error) && !GitRunner.IsBenignLineEndingWarning(error))
        {
            UnityEngine.Debug.LogError("Git diff error: " + error);
            lastError = error;
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

        if (!GitRunner.IsBenignLineEndingWarning(error))
            lastError = error;
        else
            lastError = null;

        statusList = GitStatusParser.ParseStatus(output);
        Repaint();
    }

    private void StageFile(string path)
    {
        GitRunner.Run("add -- " + QuotePath(path), repoRoot, out string error);
        if (!string.IsNullOrEmpty(error) && !GitRunner.IsBenignLineEndingWarning(error))
        {
            UnityEngine.Debug.LogError("Git add error: " + error);
            lastError = error;
        }
    }

    private void UnstageFile(string path)
    {
        GitRunner.Run("restore --staged -- " + QuotePath(path), repoRoot, out string error);
        if (!string.IsNullOrEmpty(error) && !GitRunner.IsBenignLineEndingWarning(error))
        {
            UnityEngine.Debug.LogError("Git restore --staged error: " + error);
            lastError = error;
        }
    }

    private void StageAll()
    {
        GitRunner.Run("add -A", repoRoot, out string error);
        if (!string.IsNullOrEmpty(error) && !GitRunner.IsBenignLineEndingWarning(error))
        {
            UnityEngine.Debug.LogError("Git add -A error: " + error);
            lastError = error;
        }
        RefreshStatus();
    }

    private bool Commit()
    {
        if (string.IsNullOrWhiteSpace(commitMessage))
        {
            EditorUtility.DisplayDialog("Commit", "Commit message is empty.", "OK");
            return false;
        }

        string msgArg = "-m " + QuoteCommitMessage(commitMessage);
        int exitCode;
        string output = GitRunner.Run("commit " + msgArg, repoRoot, out string error, out exitCode);

        if (exitCode != 0 && !GitRunner.IsBenignLineEndingWarning(error))
        {
            UnityEngine.Debug.LogError("Git commit error: " + error);
            lastError = error;
            return false;
        }

        if (!string.IsNullOrEmpty(output))
        {
            UnityEngine.Debug.Log("Git commit output:\n" + output);
        }

        commitMessage = "";
        RefreshStatus();
        LoadGitMeta(); // branch may not change, but good to refresh
        return true;
    }

    private void Push()
    {
        string args;
        if (!string.IsNullOrEmpty(pushRemote) && !string.IsNullOrEmpty(pushBranch))
        {
            args = "push " + pushRemote + " " + pushBranch;
        }
        else if (!string.IsNullOrEmpty(pushRemote) && string.IsNullOrEmpty(pushBranch))
        {
            args = "push " + pushRemote;
        }
        else
        {
            args = "push";
        }

        int exitCode;
        string output = GitRunner.Run(args, repoRoot, out string error, out exitCode);

        if (exitCode != 0 && !GitRunner.IsBenignLineEndingWarning(error))
        {
            UnityEngine.Debug.LogError("Git push error: " + error);
            lastError = error;
        }
        else
        {
            lastError = null;

            if (!string.IsNullOrEmpty(error) && !GitRunner.IsBenignLineEndingWarning(error))
            {
                UnityEngine.Debug.Log("Git push info (stderr):\n" + error);
            }
        }

        if (!string.IsNullOrEmpty(output))
        {
            UnityEngine.Debug.Log("Git push output (stdout):\n" + output);
        }
    }

    private void Pull()
    {
        string args;

        // Use explicit remote + branch if available, else let git decide
        if (!string.IsNullOrEmpty(pushRemote) && !string.IsNullOrEmpty(pushBranch))
        {
            args = "pull " + pushRemote + " " + pushBranch;
        }
        else if (!string.IsNullOrEmpty(pushRemote) && string.IsNullOrEmpty(pushBranch))
        {
            args = "pull " + pushRemote;
        }
        else
        {
            args = "pull";
        }

        int exitCode;
        string output = GitRunner.Run(args, repoRoot, out string error, out exitCode);

        if (exitCode != 0 && !GitRunner.IsBenignLineEndingWarning(error))
        {
            UnityEngine.Debug.LogError("Git pull error: " + error);
            lastError = error;
        }
        else
        {
            lastError = null;

            if (!string.IsNullOrEmpty(error) && !GitRunner.IsBenignLineEndingWarning(error))
            {
                UnityEngine.Debug.Log("Git pull info (stderr):\n" + error);
            }
        }

        if (!string.IsNullOrEmpty(output))
        {
            UnityEngine.Debug.Log("Git pull output (stdout):\n" + output);
        }

        RefreshStatus();
        LoadGitMeta();
    }

    private void CheckoutSelectedBranch()
    {
        if (localBranches == null || localBranches.Count == 0)
        {
            EditorUtility.DisplayDialog("Checkout", "No local branches found.", "OK");
            return;
        }

        string branchName = localBranches[Mathf.Clamp(selectedBranchIndex, 0, localBranches.Count - 1)];
        int exitCode;
        string output = GitRunner.Run("checkout " + QuotePath(branchName), repoRoot, out string error, out exitCode);

        if (exitCode != 0 && !GitRunner.IsBenignLineEndingWarning(error))
        {
            UnityEngine.Debug.LogError("Git checkout error: " + error);
            lastError = error;
        }
        else
        {
            lastError = null;
        }

        if (!string.IsNullOrEmpty(output))
        {
            UnityEngine.Debug.Log("Git checkout output:\n" + output);
        }

        RefreshStatus();
        LoadGitMeta();
    }

    #endregion

    #region Git Meta Helpers

    private void LoadGitMeta()
    {
        if (string.IsNullOrEmpty(repoRoot) || !Directory.Exists(Path.Combine(repoRoot, ".git")))
        {
            currentBranch = "";
            remotes = new List<string>();
            localBranches = new List<string>();
            return;
        }

        remotes = GitRunner.GetRemotes(repoRoot);
        localBranches = GitRunner.GetLocalBranches(repoRoot);
        currentBranch = GitRunner.GetCurrentBranch(repoRoot);

        if (remotes == null) remotes = new List<string>();
        if (localBranches == null) localBranches = new List<string>();

        if (remotes.Count == 0)
        {
            remotes.Add("origin");
        }

        // Ensure we have some branch in list if currentBranch is known
        if (localBranches.Count == 0 && !string.IsNullOrEmpty(currentBranch))
        {
            localBranches.Add(currentBranch);
        }

        // Select remote index based on pushRemote
        selectedRemoteIndex = Mathf.Clamp(remotes.IndexOf(pushRemote), 0, remotes.Count - 1);
        pushRemote = remotes[selectedRemoteIndex];

        // Select branch index based on current branch (or pushBranch if set)
        if (!string.IsNullOrEmpty(pushBranch))
        {
            selectedBranchIndex = localBranches.IndexOf(pushBranch);
        }
        if (selectedBranchIndex < 0 && !string.IsNullOrEmpty(currentBranch))
        {
            selectedBranchIndex = localBranches.IndexOf(currentBranch);
        }
        if (selectedBranchIndex < 0) selectedBranchIndex = 0;

        if (localBranches.Count > 0)
        {
            selectedBranchIndex = Mathf.Clamp(selectedBranchIndex, 0, localBranches.Count - 1);
            // If pushBranch is empty, use the selected branch
            if (string.IsNullOrEmpty(pushBranch))
                pushBranch = localBranches[selectedBranchIndex];
        }
        else
        {
            pushBranch = "";
        }
    }

    private static bool IsStaged(GitFileStatus s)
    {
        // staged if X is not space and not '?' (i.e., something in the index)
        return !string.IsNullOrEmpty(s.X) && s.X != " " && s.X != "?";
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

    private static string QuoteCommitMessage(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return "\"\"";
        msg = msg.Replace("\"", "\\\"");
        return "\"" + msg + "\"";
    }

    #endregion
}

/// <summary>
/// Represents one line of `git status --porcelain`.
/// X = index status, Y = working tree status
/// </summary>
public class GitFileStatus
{
    public string X;    // index status
    public string Y;    // work tree status
    public string Path; // file path relative to repo root

    public string Code => (X + Y).Trim();
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
                char x = line[0];
                char y = line[1];
                string path = line.Substring(3).Trim();

                list.Add(new GitFileStatus
                {
                    X = x.ToString(),
                    Y = y.ToString(),
                    Path = path
                });
            }
        }

        return list;
    }
}

/// <summary>
/// Low-level git runner. Wraps the git executable and basic git info helpers.
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
    /// (Convenience overload ignoring exit code)
    /// </summary>
    public static string Run(string arguments, string workingDirectory, out string error)
    {
        int exitCode;
        return Run(arguments, workingDirectory, out error, out exitCode);
    }

    /// <summary>
    /// Run a git command and capture stdout + stderr + exitCode.
    /// </summary>
    public static string Run(string arguments, string workingDirectory, out string error, out int exitCode)
    {
        error = null;
        exitCode = -1;

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
                exitCode = process.ExitCode;
                return output;
            }
        }
        catch (System.Exception ex)
        {
            error = ex.Message;
            exitCode = -1;
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

    /// <summary>
    /// Returns true if the error string is just a line-ending warning (CRLF/LF).
    /// </summary>
    public static bool IsBenignLineEndingWarning(string error)
    {
        if (string.IsNullOrEmpty(error))
            return false;

        if (error.Contains("LF will be replaced by CRLF"))
            return true;

        if (error.Contains("CRLF will be replaced by LF"))
            return true;

        if (error.Contains("the file will have its original line endings"))
            return true;

        return false;
    }

    // --- Git info helpers ---

    public static List<string> GetRemotes(string repoRoot)
    {
        string raw = Run("remote", repoRoot, out var err);
        var result = new List<string>();

        if (!string.IsNullOrEmpty(raw))
        {
            using (var reader = new StringReader(raw))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        result.Add(trimmed);
                }
            }
        }

        return result;
    }

    public static List<string> GetLocalBranches(string repoRoot)
    {
        // --format to get just the short names
        string raw = Run("branch --format=\"%(refname:short)\"", repoRoot, out var err);
        var result = new List<string>();

        if (!string.IsNullOrEmpty(raw))
        {
            using (var reader = new StringReader(raw))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim().Trim('*').Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        result.Add(trimmed);
                }
            }
        }

        return result;
    }

    public static string GetCurrentBranch(string repoRoot)
    {
        string raw = Run("rev-parse --abbrev-ref HEAD", repoRoot, out var err);
        return raw?.Trim();
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
