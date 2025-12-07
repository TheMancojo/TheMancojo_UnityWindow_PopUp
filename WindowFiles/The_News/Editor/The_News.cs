#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

public class The_News : EditorWindow
{
    private const string REPO = "TheMancojo/TheMancojo_UnityWindow_PopUp";
    private const string BRANCH = "main";
    private const string API_TREE = "https://api.github.com/repos/{0}/git/trees/{1}?recursive=1";
    private const string RAW_BASE = "https://raw.githubusercontent.com/{0}/{1}/"; // + path
    private const string PREF_TOKEN_KEY = "The_News.GitHubToken";
    private const string PREF_LAST_COMMIT_KEY = "The_News.LastCommit";
    private const string LOCAL_SCRIPT_PATH = "Assets/TheMancojo/Scripts/The_News/Editor/The_News.cs";
    private const string GITHUB_SCRIPT_PATH = "WindowFiles/The_News/Editor/The_News.cs";
    private static readonly Color32 BACK_COLOR = new Color32(0x00, 0x00, 0x00, 0xFF);

    private class Node
    {
        public string name;
        public string path;
        public Node parent;
        public Dictionary<string, Node> children = new Dictionary<string, Node>();
        public List<string> imagePaths = new List<string>();
        public bool hidden;
        public bool hasColor;
        public Color32 bgColor;
        public List<PostEntry> posts = new List<PostEntry>();
        public List<ButtonEntry> buttons = new List<ButtonEntry>();
    }

    private struct PostEntry
    {
        public int number;
        public string title;
        public List<string> imagePaths;
        public List<ButtonEntry> buttons;
    }

    private struct ButtonEntry
    {
        public string displayName;
        public string url;
        public int order;
    }

    private Node root = new Node { name = "root", path = "" };
    private bool isLoading;
    private Vector2 scroll;
    private List<string> breadcrumb = new List<string>();
    private Node current;
    private int siblingSelectedIndex = -1;
    private string lastApiError;
    private string githubToken;
    private bool showSettings;
    private string lastKnownCommit;
    private bool isCheckingForUpdates;
    private bool hasCodeUpdates;

    [InitializeOnLoadMethod]
    private static void OnProjectLoaded()
    {
        EditorApplication.delayCall += CheckForUpdatesAndAutoOpen;
    }

    private static async void CheckForUpdatesAndAutoOpen()
    {
        try
        {
            string lastCommit = EditorPrefs.GetString(PREF_LAST_COMMIT_KEY, "");
            string currentCommit = await GetLatestCommitSHA();
            
            if (!string.IsNullOrEmpty(currentCommit))
            {
                // Check if there are code updates
                bool hasUpdates = await CheckForCodeUpdates(currentCommit, lastCommit);
                
                // Open window if it's the first time (no stored commit) OR if something was updated
                if (string.IsNullOrEmpty(lastCommit) || currentCommit != lastCommit || hasUpdates)
                {
                    ShowWindow();
                }
                
                // Always update the stored commit
                EditorPrefs.SetString(PREF_LAST_COMMIT_KEY, currentCommit);
            }
        }
        catch (System.Exception e)
        {
            // Silently fail - don't bother user with network issues on project load
            UnityEngine.Debug.Log($"The_News auto-check failed: {e.Message}");
        }
    }

    private static async System.Threading.Tasks.Task<string> GetLatestCommitSHA()
    {
        try
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(5); // Quick timeout for auto-check
                
                // Use same headers as main window
                var env = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
                string token = EditorPrefs.GetString(PREF_TOKEN_KEY, string.IsNullOrEmpty(env) ? "" : env);
                
                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                    client.DefaultRequestHeaders.Add("User-Agent", "Unity-The_News");
                if (!string.IsNullOrEmpty(token?.Trim()))
                {
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token.Trim());
                }
                
                string url = $"https://api.github.com/repos/{REPO}/commits/{BRANCH}";
                var response = await client.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    // Simple SHA extraction from JSON
                    var shaMatch = System.Text.RegularExpressions.Regex.Match(json, "\"sha\"\\s*:\\s*\"([^\"]+)\"");
                    if (shaMatch.Success)
                    {
                        return shaMatch.Groups[1].Value;
                    }
                }
            }
        }
        catch
        {
            // Ignore network errors for auto-check
        }
        return null;
    }

    private static async System.Threading.Tasks.Task<bool> CheckForCodeUpdates(string currentCommit, string lastCommit)
    {
        if (string.IsNullOrEmpty(currentCommit) || currentCommit == lastCommit)
            return false;

        try
        {
            // Download GitHub version of the script
            string githubCode = await DownloadGitHubScript();
            if (string.IsNullOrEmpty(githubCode))
                return false;

            // Read local script
            string localCode = "";
            if (System.IO.File.Exists(LOCAL_SCRIPT_PATH))
            {
                localCode = System.IO.File.ReadAllText(LOCAL_SCRIPT_PATH);
            }

            // Compare scripts (ignore whitespace differences)
            string normalizedGithub = NormalizeCode(githubCode);
            string normalizedLocal = NormalizeCode(localCode);

            return normalizedGithub != normalizedLocal;
        }
        catch
        {
            return false;
        }
    }

    private static async System.Threading.Tasks.Task<string> DownloadGitHubScript()
    {
        try
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                
                var env = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
                string token = EditorPrefs.GetString(PREF_TOKEN_KEY, string.IsNullOrEmpty(env) ? "" : env);
                
                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                    client.DefaultRequestHeaders.Add("User-Agent", "Unity-The_News");
                if (!string.IsNullOrEmpty(token?.Trim()))
                {
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token.Trim());
                }
                
                string url = string.Format(RAW_BASE, REPO, BRANCH) + GITHUB_SCRIPT_PATH;
                var response = await client.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
            }
        }
        catch
        {
            // Ignore errors for code checking
        }
        return null;
    }

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrEmpty(code))
            return "";
            
        // Remove comments, normalize whitespace for comparison
        return code
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\t", "    ")
            .Trim();
    }

    private async void CheckForCodeUpdatesAsync()
    {
        if (isCheckingForUpdates) return;
        isCheckingForUpdates = true;

        try
        {
            string currentCommit = await GetLatestCommitSHA();
            if (!string.IsNullOrEmpty(currentCommit))
            {
                hasCodeUpdates = await CheckForCodeUpdates(currentCommit, lastKnownCommit);
                if (hasCodeUpdates)
                {
                    Debug.Log("Code updates available from GitHub");
                    Repaint();
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to check for code updates: {e.Message}");
        }
        finally
        {
            isCheckingForUpdates = false;
        }
    }

    private async void UpdateCodeFromGitHub()
    {
        try
        {
            string githubCode = await DownloadGitHubScript();
            if (string.IsNullOrEmpty(githubCode))
            {
                EditorUtility.DisplayDialog("Update Failed", "Could not download code from GitHub.", "OK");
                return;
            }

            // Create backup
            string backupPath = LOCAL_SCRIPT_PATH + ".backup." + System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            if (System.IO.File.Exists(LOCAL_SCRIPT_PATH))
            {
                System.IO.File.Copy(LOCAL_SCRIPT_PATH, backupPath);
            }

            // Write new code
            System.IO.File.WriteAllText(LOCAL_SCRIPT_PATH, githubCode);
            AssetDatabase.Refresh();

            hasCodeUpdates = false;
            Repaint();

            bool recompile = EditorUtility.DisplayDialog(
                "Code Updated", 
                $"The_News.cs has been updated from GitHub.\nBackup saved to: {backupPath}\n\nRecompile scripts now?", 
                "Recompile", "Later");

            if (recompile)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                EditorUtility.RequestScriptReload();
            }
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("Update Failed", $"Error updating code: {e.Message}", "OK");
        }
    }

    [MenuItem("The_Mancojo/The_News")]
    public static void ShowWindow()
    {
        var w = GetWindow<The_News>("The_News");
        w.minSize = new Vector2(400, 400);
        w.Show();
    }

    private void OnEnable()
    {
        // Load token from EditorPrefs or environment
        var env = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        githubToken = EditorPrefs.GetString(PREF_TOKEN_KEY, string.IsNullOrEmpty(env) ? "" : env);
        lastKnownCommit = EditorPrefs.GetString(PREF_LAST_COMMIT_KEY, "");
        if (!isLoading && root.children.Count == 0) Refresh();
        
        // Check for code updates
        CheckForCodeUpdatesAsync();
    }

    private void OnGUI()
    {
        // Fill full window with folder-specific background color if defined, else default
        Color32 back = BACK_COLOR;
        var bgNode = GetNodeByBreadcrumb() ?? root;
        var colNode = FindNearestColorNode(bgNode);
        if (colNode != null && colNode.hasColor) back = colNode.bgColor;
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), back);

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(100))) Refresh();
        
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Token", EditorStyles.toolbarButton, GUILayout.Width(60))) showSettings = !showSettings;
        EditorGUILayout.EndHorizontal();

        if (showSettings)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("GitHub Token (PAT)");
            githubToken = EditorGUILayout.PasswordField(githubToken);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save", GUILayout.Width(80)))
            {
                EditorPrefs.SetString(PREF_TOKEN_KEY, githubToken ?? "");
                ShowNotification(new GUIContent("Token saved"));
            }
            if (GUILayout.Button("Clear", GUILayout.Width(80)))
            {
                githubToken = "";
                EditorPrefs.DeleteKey(PREF_TOKEN_KEY);
                ShowNotification(new GUIContent("Token cleared"));
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Add a token to increase the request limit to 5000/hour. Create a PAT (no scopes) and paste it here.", MessageType.Info);
            EditorGUILayout.EndVertical();
        }

        // If code updates are available, show only the update interface
        if (hasCodeUpdates)
        {
            EditorGUILayout.Space(20);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Code Update Available", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("A newer version of The_News is available on GitHub.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(10);
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Update Now", GUILayout.Height(40), GUILayout.Width(120)))
            {
                UpdateCodeFromGitHub();
            }
            GUI.backgroundColor = Color.white;
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("Your current code will be backed up before updating.", MessageType.Info);
            EditorGUILayout.EndVertical();
            
            return; // Don't show the rest of the interface
        }

        // No breadcrumb display per request

        EditorGUILayout.Space(6);

        if (!string.IsNullOrEmpty(lastApiError))
        {
            EditorGUILayout.HelpBox("Could not download repository index.\nReason: " + lastApiError, MessageType.Error);
            if (GUILayout.Button("Retry", GUILayout.Width(100)))
            {
                Refresh();
                return;
            }
            EditorGUILayout.Space(6);
        }

        if (isLoading)
        {
            var r = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.ProgressBar(r, 0.5f, "Loading...");
            return;
        }

        current = GetNodeByBreadcrumb() ?? root;

        // Default to first top-level folder when nothing selected (skip "Header" and hidden folders)
        if ((current == null || current == root) && breadcrumb.Count == 0 && root.children.Count > 0)
        {
            var firstTopNonHeader = root.children
                .Where(kv => !kv.Key.Equals("Header", StringComparison.OrdinalIgnoreCase) && !kv.Value.hidden)
                .OrderBy(k => k.Key)
                .Select(k => k.Value)
                .FirstOrDefault();
            if (firstTopNonHeader != null)
                SetCurrentByPath(firstTopNonHeader.path);
        }

        // Use a scroll view for all content starting from navigation
        var prevContentColor = GUI.contentColor;
        if (!EditorGUIUtility.isProSkin) GUI.contentColor = Color.white;
        scroll = GUILayout.BeginScrollView(scroll, false, true, GUI.skin.horizontalScrollbar, GUI.skin.verticalScrollbar, GUIStyle.none);

        // Navigation rows will render their own headers per level

        // Multi-level navigation rows: for each ancestor level, show sibling selection
        int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 40f) / 140f));
        int depth = breadcrumb.Count;
        for (int level = 0; level < depth; level++)
        {
            Node parentNode = GetNodeAtDepth(level); // level 0 => root
            // Draw header for this folder (before its buttons) unless folder or Header is hidden by Hide.txt
            if (!parentNode.hidden && parentNode.children.TryGetValue("Header", out var headerAtLevel) && !headerAtLevel.hidden)
            {
                DrawHeaderContent(headerAtLevel);
                EditorGUILayout.Space(6);
            }
            var sorted = GetSortedChildren(parentNode);
            string[] displayNames = sorted.Select(s => s.displayName).ToArray();
            if (displayNames.Length == 0) continue;
            string selectedRaw = breadcrumb[level];
            string selectedDisplay = GetDisplayName(selectedRaw);
            int selIndex = Array.IndexOf(displayNames, selectedDisplay);
            int newIndex = GUILayout.SelectionGrid(Mathf.Max(0, selIndex), displayNames, columns);
            if (newIndex != selIndex && newIndex >= 0 && newIndex < displayNames.Length)
            {
                var target = sorted[newIndex].node;
                GUILayout.EndScrollView();
                GUI.contentColor = prevContentColor;
                SetCurrentByPath(target.path);
                // After navigation, stop drawing further rows this frame
                Repaint();
                return;
            }
        }

        // One more row for going deeper: show current's children as buttons (no selection)
        if (current.children.Count > 0)
        {
            // Header of current folder before its child buttons (disabled if current or Header is hidden by Hide.txt)
            if (!current.hidden && current.children.TryGetValue("Header", out var headerHere) && !headerHere.hidden)
            {
                DrawHeaderContent(headerHere);
                EditorGUILayout.Space(6);
            }
                var deeperSorted = GetSortedChildren(current);
                string[] deeperNames = deeperSorted.Select(s => s.displayName).ToArray();
                if (deeperNames.Length > 0)
                {
                    int clicked = GUILayout.SelectionGrid(-1, deeperNames, columns);
                    if (clicked >= 0 && clicked < deeperNames.Length)
                    {
                        var child = deeperSorted[clicked].node;
                        GUILayout.EndScrollView();
                        GUI.contentColor = prevContentColor;
                        SetCurrentByPath(child.path);
                        Repaint();
                        return;
                    }
                }
        }

        // Content in this folder (images and buttons mixed together)
        if (current.imagePaths.Count > 0 || current.buttons.Count > 0)
        {
            string raw = string.Format(RAW_BASE, REPO.Split('/')[0] + "/" + REPO.Split('/')[1], BRANCH);
            
            // Show images first
            foreach (var p in current.imagePaths)
            {
                string url = raw + p;
                Texture2D tex = GetCachedTexture(url);
                if (tex != null)
                {
                    float maxW = position.width - 40;
                    float aspect = (float)tex.height / Mathf.Max(1, tex.width);
                    Rect rect = GUILayoutUtility.GetRect(maxW, maxW * aspect);
                    GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
                    EditorGUILayout.Space(4);
                }
                else
                {
                    if (!IsDownloading(url)) QueueDownloadByPriority(url);
                    string reason = null;
                    texErrors.TryGetValue(url, out reason);
                    bool isQueued = downloadQueue.Contains(url);
                    string status = IsDownloading(url) ? "Downloading" : (isQueued ? "Waiting" : "Could not load");
                    MessageType msgType = IsDownloading(url) || isQueued ? MessageType.Info : MessageType.Warning;
                    EditorGUILayout.HelpBox($"{status}: {url}" + (string.IsNullOrEmpty(reason) ? "" : "\nReason: " + reason), msgType);
                }
            }
            
            // Show buttons right after images
            var sortedButtons = current.buttons.OrderBy(b => b.order).ThenBy(b => b.displayName).ToList();
            if (sortedButtons.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                foreach (var button in sortedButtons)
                {
                    if (GUILayout.Button(button.displayName, GUILayout.Height(30)))
                    {
                        Application.OpenURL(button.url);
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(4);
            }
        }

        // Posts in this folder (from Post.N subfolders) - descending order
        if (current.posts.Count > 0)
        {
            string raw = string.Format(RAW_BASE, REPO.Split('/')[0] + "/" + REPO.Split('/')[1], BRANCH);
            var sortedPosts = current.posts.OrderByDescending(p => p.number).ToList();
            foreach (var post in sortedPosts)
            {
                // Small separator for post
                GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
                EditorGUILayout.Space(4);
                
                // Show post images
                foreach (var p in post.imagePaths)
                {
                    string url = raw + p;
                    Texture2D tex = GetCachedTexture(url);
                    if (tex != null)
                    {
                        float maxW = position.width - 40;
                        float aspect = (float)tex.height / Mathf.Max(1, tex.width);
                        Rect rect = GUILayoutUtility.GetRect(maxW, maxW * aspect);
                        GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
                        EditorGUILayout.Space(4);
                    }
                    else
                    {
                        if (!IsDownloading(url)) QueueDownloadByPriority(url);
                        string reason = null;
                        texErrors.TryGetValue(url, out reason);
                        bool isQueued = downloadQueue.Contains(url);
                        string status = IsDownloading(url) ? "Descargando" : (isQueued ? "En espera" : "No se pudo cargar");
                        MessageType msgType = IsDownloading(url) || isQueued ? MessageType.Info : MessageType.Warning;
                        EditorGUILayout.HelpBox($"{status}: {url}" + (string.IsNullOrEmpty(reason) ? "" : "\nMotivo: " + reason), msgType);
                    }
                }
                
                // Show post buttons right after post images
                if (post.buttons != null && post.buttons.Count > 0)
                {
                    var sortedButtons = post.buttons.OrderBy(b => b.order).ThenBy(b => b.displayName).ToList();
                    EditorGUILayout.BeginHorizontal();
                    foreach (var button in sortedButtons)
                    {
                        if (GUILayout.Button(button.displayName, GUILayout.Height(30)))
                        {
                            Application.OpenURL(button.url);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(4);
                }
                
                EditorGUILayout.Space(8);
            }
        }

        GUILayout.EndScrollView();
        GUI.contentColor = prevContentColor;
    }

    private void Refresh()
    {
        isLoading = true;
        lastApiError = null;
        root = new Node { name = "root", path = "" };
        breadcrumb.Clear();
        Task.Run(async () =>
        {
            try
            {
                using (var client = new HttpClient())
                {
                    ApplyGitHubHeaders(client);
                    string url = string.Format(API_TREE, REPO, BRANCH);
                    var resp = await client.GetAsync(url);
                    if (!resp.IsSuccessStatusCode)
                    {
                        lastApiError = BuildRateLimitAwareError(resp);
                        Debug.LogError("GitHub API error: " + lastApiError);
                    }
                    else
                    {
                        string json = await resp.Content.ReadAsStringAsync();
                        ParseTree(json);
                        lastApiError = null;
                    }
                }
            }
            catch (Exception e)
            {
                lastApiError = e.Message;
                Debug.LogError("Refresh failed: " + e.Message);
            }
            finally
            {
                isLoading = false;
                EditorApplication.delayCall += Repaint;
            }
        });
    }

    private void ApplyGitHubHeaders(HttpClient client)
    {
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.Add("User-Agent", "Unity-The_News");
        if (!client.DefaultRequestHeaders.Contains("Accept"))
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        if (!client.DefaultRequestHeaders.Contains("X-GitHub-Api-Version"))
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        var token = string.IsNullOrWhiteSpace(githubToken) ? null : githubToken.Trim();
        if (!string.IsNullOrEmpty(token))
        {
            // Prefer Bearer; GitHub supports both Bearer and token
            if (client.DefaultRequestHeaders.Contains("Authorization"))
                client.DefaultRequestHeaders.Remove("Authorization");
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        }
    }

    private string BuildRateLimitAwareError(HttpResponseMessage resp)
    {
        string basic = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
        try
        {
            if ((int)resp.StatusCode == 403)
            {
                var remaining = resp.Headers.TryGetValues("X-RateLimit-Remaining", out var r) ? r.FirstOrDefault() : null;
                var reset = resp.Headers.TryGetValues("X-RateLimit-Reset", out var rs) ? rs.FirstOrDefault() : null;
                string extra = "";
                if (!string.IsNullOrEmpty(remaining)) extra += $" | Remaining: {remaining}";
                if (long.TryParse(reset, out var resetTs))
                {
                    var dt = DateTimeOffset.FromUnixTimeSeconds(resetTs).ToLocalTime();
                    extra += $" | Reset: {dt:HH:mm:ss}";
                }
                if (!string.IsNullOrEmpty(githubToken))
                    extra += " | Suggestion: check that the token is valid.";
                else
                    extra += " | Suggestion: add a token to increase the limit.";
                return basic + extra;
            }
        }
        catch { }
        return basic;
    }

    private void ParseTree(string json)
    {
        // very simple parse: find all paths from the JSON
        // "path":"folder/sub/file.png"
        var matches = System.Text.RegularExpressions.Regex.Matches(json, "\"path\"\\s*:\\s*\"([^\"]+)\"");
        var buttonFiles = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            string path = m.Groups[1].Value;
            
            // Skip any paths that start with WindowFiles
            if (path.StartsWith("WindowFiles/", StringComparison.OrdinalIgnoreCase))
                continue;
            
            string[] parts = path.Split('/');
            Node cur = root;
            for (int i = 0; i < parts.Length; i++)
            {
                bool isLast = i == parts.Length - 1;
                string part = parts[i];
                if (isLast)
                {
                    // Check if it's a button file first
                    if (IsButtonFile(part))
                    {
                        buttonFiles.Add(path);
                        continue;
                    }
                    // Mark folder as hidden only if it contains a text document named exactly "Hide.txt"
                    if (string.Equals(part, "Hide.txt", StringComparison.Ordinal))
                    {
                        cur.hidden = true;
                    }
                    else
                    {
                        // Folder background color: file name starts with "Color" and contains a HEX like #RRGGBB
                        // Example: "Color #113942.txt" or without extension in the name depending on repository
                        string fileNameNoExt = System.IO.Path.GetFileNameWithoutExtension(part);
                        if (TryParseColorFromName(fileNameNoExt, out var col))
                        {
                            cur.hasColor = true;
                            cur.bgColor = col;
                        }
                        else
                        {
                            string ext = System.IO.Path.GetExtension(part).ToLower();
                            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif")
                            {
                                cur.imagePaths.Add(path);
                            }
                        }
                    }
                }
                else
                {
                    if (!cur.children.TryGetValue(part, out var next))
                    {
                        next = new Node { name = part, path = (cur.path == "" ? part : cur.path + "/" + part), parent = cur };
                        cur.children[part] = next;
                    }
                    cur = next;
                }
            }
        }
        
        // Second pass: collect Post.N folders as posts and move their content to parent
        CollectPostFolders(root);
        // Third pass: process button files after posts are collected
        ProcessButtonFiles(buttonFiles);
        // Trigger repaint and ensure a default selection on main thread
        EditorApplication.delayCall += () =>
        {
            if (breadcrumb.Count == 0 && root.children.Count > 0)
            {
                var firstTopNonHeader = root.children
                    .Where(kv => !kv.Key.Equals("Header", StringComparison.OrdinalIgnoreCase) && !kv.Value.hidden)
                    .OrderBy(k => k.Key)
                    .Select(k => k.Value)
                    .FirstOrDefault();
                if (firstTopNonHeader != null)
                    SetCurrentByPath(firstTopNonHeader.path);
                else
                    Repaint();
            }
            else
            {
                Repaint();
            }
        };
    }

    private Node GetNodeByBreadcrumb()
    {
        Node cur = root;
        foreach (var b in breadcrumb)
        {
            if (!cur.children.TryGetValue(b, out var next)) return root;
            cur = next;
        }
        return cur;
    }

    private void SetCurrentByPath(string path)
    {
        // Clear any ongoing downloads to prioritize current page
        ClearDownloads();
        
        if (string.IsNullOrEmpty(path)) { breadcrumb.Clear(); current = root; return; }
        var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        breadcrumb.Clear();
        foreach (var p in parts) breadcrumb.Add(p);
        current = GetNodeByBreadcrumb();

        // Auto-open first subfolder if folder has no images (skip "Header")
        if (current != null && current.imagePaths.Count == 0 && current.children.Count > 0)
        {
            Node cur = current;
            // Keep going down the first child alphabetically until images or leaf, ignoring "Header"
            while (cur.imagePaths.Count == 0)
            {
                var next = cur.children
                    .Where(kv => !kv.Key.Equals("Header", StringComparison.OrdinalIgnoreCase) && !kv.Value.hidden)
                    .OrderBy(k => k.Key)
                    .Select(k => k.Value)
                    .FirstOrDefault();
                if (next == null) break;
                cur = next;
                if (cur.children.Count == 0 && cur.imagePaths.Count == 0) break;
            }
            if (cur != current)
            {
                breadcrumb.Clear();
                foreach (var p in cur.path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                    breadcrumb.Add(p);
                current = cur;
            }
        }
        Repaint();
    }

    private string GetTopLevelName(Node node)
    {
        if (node == null || node == root) return null;
        Node cur = node;
        while (cur.parent != null && cur.parent != root)
        {
            cur = cur.parent;
        }
        return cur.name;
    }

    private Node GetNodeAtDepth(int level)
    {
        // level 0 => root; level 1 => node at first segment; ...
        if (level <= 0) return root;
        Node cur = root;
        for (int i = 0; i < level; i++)
        {
            if (i >= breadcrumb.Count) break;
            if (!cur.children.TryGetValue(breadcrumb[i], out var next)) return root;
            cur = next;
        }
        return cur;
    }

    private Node FindNearestHeaderNode(Node start)
    {
        Node n = start;
        while (n != null)
        {
            if (n.children != null && n.children.TryGetValue("Header", out var header))
                return header;
            n = n.parent;
        }
        return null;
    }

    private Node FindNearestColorNode(Node start)
    {
        Node n = start;
        while (n != null)
        {
            if (n.hasColor) return n;
            n = n.parent;
        }
        return null;
    }

    private void DrawHeaderContent(Node headerNode)
    {
        if (headerNode == null) return;
        
        // Draw header images
        if (headerNode.imagePaths != null && headerNode.imagePaths.Count > 0)
        {
            string raw = string.Format(RAW_BASE, REPO.Split('/')[0] + "/" + REPO.Split('/')[1], BRANCH);
            foreach (var p in headerNode.imagePaths)
            {
                string url = raw + p;
                Texture2D tex = GetCachedTexture(url);
                if (tex != null)
                {
                    float maxW = position.width - 40;
                    float aspect = (float)tex.height / Mathf.Max(1, tex.width);
                    Rect rect = GUILayoutUtility.GetRect(maxW, maxW * aspect);
                    GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
                    EditorGUILayout.Space(4);
                }
                else
                {
                    if (!IsDownloading(url)) QueueDownloadByPriority(url);
                    string reason = null;
                    texErrors.TryGetValue(url, out reason);
                    bool isQueued = downloadQueue.Contains(url);
                    string status = IsDownloading(url) ? "Downloading" : (isQueued ? "Waiting" : "Could not load");
                    MessageType msgType = IsDownloading(url) || isQueued ? MessageType.Info : MessageType.Warning;
                    EditorGUILayout.HelpBox($"{status}: {url}" + (string.IsNullOrEmpty(reason) ? "" : "\nReason: " + reason), msgType);
                }
            }
        }
        
        // Draw header buttons right after images
        if (headerNode.buttons != null && headerNode.buttons.Count > 0)
        {
            var sortedButtons = headerNode.buttons.OrderBy(b => b.order).ThenBy(b => b.displayName).ToList();
            EditorGUILayout.BeginHorizontal();
            foreach (var button in sortedButtons)
            {
                if (GUILayout.Button(button.displayName, GUILayout.Height(30)))
                {
                    Application.OpenURL(button.url);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }
        
        // Draw header posts (from Post.N subfolders in Header) - descending order
        if (headerNode.posts != null && headerNode.posts.Count > 0)
        {
            string raw = string.Format(RAW_BASE, REPO.Split('/')[0] + "/" + REPO.Split('/')[1], BRANCH);
            var sortedPosts = headerNode.posts.OrderByDescending(p => p.number).ToList();
            foreach (var post in sortedPosts)
            {
                // Small separator for header post
                GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
                EditorGUILayout.Space(4);
                
                // Show post images
                foreach (var p in post.imagePaths)
                {
                    string url = raw + p;
                    Texture2D tex = GetCachedTexture(url);
                    if (tex != null)
                    {
                        float maxW = position.width - 40;
                        float aspect = (float)tex.height / Mathf.Max(1, tex.width);
                        Rect rect = GUILayoutUtility.GetRect(maxW, maxW * aspect);
                        GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
                        EditorGUILayout.Space(4);
                    }
                    else
                    {
                        if (!IsDownloading(url)) QueueDownloadByPriority(url);
                        string reason = null;
                        texErrors.TryGetValue(url, out reason);
                        bool isQueued = downloadQueue.Contains(url);
                        string status = IsDownloading(url) ? "Descargando" : (isQueued ? "En espera" : "No se pudo cargar");
                        MessageType msgType = IsDownloading(url) || isQueued ? MessageType.Info : MessageType.Warning;
                        EditorGUILayout.HelpBox($"{status}: {url}" + (string.IsNullOrEmpty(reason) ? "" : "\nMotivo: " + reason), msgType);
                    }
                }
                
                // Show post buttons right after post images
                if (post.buttons != null && post.buttons.Count > 0)
                {
                    var sortedButtons = post.buttons.OrderBy(b => b.order).ThenBy(b => b.displayName).ToList();
                    EditorGUILayout.BeginHorizontal();
                    foreach (var button in sortedButtons)
                    {
                        if (GUILayout.Button(button.displayName, GUILayout.Height(30)))
                        {
                            Application.OpenURL(button.url);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(4);
                }
                
                EditorGUILayout.Space(6);
            }
        }
    }

    private (int order, string displayName) GetOrderAndName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return (int.MaxValue, rawName);
        string trimmed = rawName.TrimStart();
        int dotIdx = trimmed.IndexOf('.');
        if (dotIdx > 0)
        {
            string prefix = trimmed.Substring(0, dotIdx);
            if (int.TryParse(prefix, out int num))
            {
                string rest = trimmed.Substring(dotIdx + 1).TrimStart();
                return (num, rest);
            }
        }
        return (int.MaxValue, rawName);
    }

    private string GetDisplayName(string rawName) => GetOrderAndName(rawName).displayName;

    private struct SortedEntry { public string displayName; public Node node; public int order; }

    private List<SortedEntry> GetSortedChildren(Node parent)
    {
        var list = new List<SortedEntry>();
        foreach (var kv in parent.children)
        {
            if (kv.Key.Equals("Header", StringComparison.OrdinalIgnoreCase)) continue;
            if (kv.Value.hidden) continue;
            if (IsPostFolder(kv.Key)) continue; // Skip Post.N folders from navigation
            var (order, display) = GetOrderAndName(kv.Key);
            list.Add(new SortedEntry { displayName = display, node = kv.Value, order = order });
        }
        return list
            .OrderBy(e => e.order)
            .ThenBy(e => e.displayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
    private bool TryParseColorFromName(string name, out Color32 color)
    {
        color = BACK_COLOR;
        if (string.IsNullOrEmpty(name)) return false;
        // Expect something like "Color #RRGGBB" optionally with extra words
        if (!name.StartsWith("Color", StringComparison.OrdinalIgnoreCase)) return false;
        int hashIdx = name.IndexOf('#');
        if (hashIdx < 0 || hashIdx + 7 > name.Length) return false;
        string hex = name.Substring(hashIdx + 1, 6);
        // Validate hex
        if (!System.Text.RegularExpressions.Regex.IsMatch(hex, "^[0-9A-Fa-f]{6}$")) return false;
        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
        color = new Color32(r, g, b, 0xFF);
        return true;
    }

    private bool IsPostFolder(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(name, @"^Post\.[0-9]+\b");
    }

    private bool IsButtonFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        string ext = System.IO.Path.GetExtension(fileName).ToLower();
        if (ext != ".txt") return false;
        
        string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
        
        // Check if it starts with "Button" (case-insensitive)
        if (nameNoExt.StartsWith("Button", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        
        // Check if it starts with a number followed by "Button" (e.g., "1.Button", "2. Button")
        var match = System.Text.RegularExpressions.Regex.Match(nameNoExt, @"^[0-9]+\s*\.\s*Button", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success;
    }

    private (string displayName, int order) ParseButtonName(string fileName)
    {
        string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
        
        // First, check if the whole filename starts with a number (e.g., "1.Button Discord")
        var (fileOrder, fileRest) = GetOrderAndName(nameNoExt);
        
        // If we found a number at the start, use it and process the rest
        if (fileOrder != int.MaxValue)
        {
            // Remove "Button" prefix from the rest
            string rest = fileRest.TrimStart();
            if (rest.StartsWith("Button", StringComparison.OrdinalIgnoreCase))
            {
                rest = rest.Substring(6).TrimStart();
            }
            
            // Remove any hex color if present
            var colorMatch = System.Text.RegularExpressions.Regex.Match(rest, @"#([0-9A-Fa-f]{6})\b");
            if (colorMatch.Success)
            {
                rest = rest.Replace(colorMatch.Value, "").Trim();
            }
            
            if (string.IsNullOrEmpty(rest)) rest = "Link";
            return (rest, fileOrder);
        }
        else
        {
            // No number at start, process normally (Button at beginning)
            if (!nameNoExt.StartsWith("Button", StringComparison.OrdinalIgnoreCase))
            {
                return ("Link", int.MaxValue);
            }
            
            string rest = nameNoExt.Substring(6).TrimStart();
            
            // Remove any hex color if present
            var colorMatch = System.Text.RegularExpressions.Regex.Match(rest, @"#([0-9A-Fa-f]{6})\b");
            if (colorMatch.Success)
            {
                rest = rest.Replace(colorMatch.Value, "").Trim();
            }
            
            // Check if there's a number after Button
            var (buttonOrder, displayName) = GetOrderAndName(rest);
            
            if (string.IsNullOrEmpty(displayName)) displayName = "Link";
            return (displayName, buttonOrder);
        }
    }

    private async void ProcessButtonFiles(List<string> buttonFiles)
    {
        foreach (string buttonPath in buttonFiles)
        {
            try
            {
                string[] parts = buttonPath.Split('/');
                string fileName = parts[parts.Length - 1];
                
                // Find the parent folder
                Node parentNode = root;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (parentNode.children.TryGetValue(parts[i], out var next))
                    {
                        parentNode = next;
                    }
                    else break;
                }
                
                // Download button file content
                string rawUrl = string.Format(RAW_BASE, REPO.Split('/')[0] + "/" + REPO.Split('/')[1], BRANCH) + buttonPath;
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    ApplyGitHubHeaders(client);
                    var response = await client.GetAsync(rawUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        content = content.Trim();
                        
                        var (displayName, order) = ParseButtonName(fileName);
                        
                        var button = new ButtonEntry
                        {
                            displayName = displayName,
                            url = content,
                            order = order
                        };
                        
                        // Check if this button was from a Post.N folder or Header folder
                        string folderName = parts.Length >= 2 ? parts[parts.Length - 2] : "";
                        if (IsPostFolder(folderName))
                        {
                            // Find the parent node and the corresponding post
                            Node actualParent = root;
                            for (int j = 0; j < parts.Length - 2; j++)
                            {
                                if (actualParent.children.TryGetValue(parts[j], out var nextParent))
                                {
                                    actualParent = nextParent;
                                }
                                else break;
                            }
                            
                            // Find the post that corresponds to this Post.N folder
                            var match = System.Text.RegularExpressions.Regex.Match(folderName, @"^Post\.([0-9]+)");
                            if (match.Success)
                            {
                                int postNum = int.Parse(match.Groups[1].Value);
                                var targetPost = actualParent.posts.FirstOrDefault(p => p.number == postNum);
                                if (targetPost.buttons == null)
                                {
                                    targetPost.buttons = new List<ButtonEntry>();
                                }
                                targetPost.buttons.Add(button);
                                
                                // Update the post in the list
                                for (int k = 0; k < actualParent.posts.Count; k++)
                                {
                                    if (actualParent.posts[k].number == postNum)
                                    {
                                        actualParent.posts[k] = targetPost;
                                        break;
                                    }
                                }
                            }
                        }
                        else if (folderName.Equals("Header", StringComparison.OrdinalIgnoreCase))
                        {
                            // Button is from a Header folder, add it to the parent's header node
                            Node actualParent = root;
                            for (int j = 0; j < parts.Length - 2; j++)
                            {
                                if (actualParent.children.TryGetValue(parts[j], out var nextParent))
                                {
                                    actualParent = nextParent;
                                }
                                else break;
                            }
                            
                            if (actualParent.children.TryGetValue("Header", out var headerNode))
                            {
                                headerNode.buttons.Add(button);
                            }
                        }
                        else
                        {
                            parentNode.buttons.Add(button);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to process button file {buttonPath}: {e.Message}");
            }
        }
        EditorApplication.delayCall += Repaint;
    }

    private void CollectPostFolders(Node node)
    {
        var postFolders = new List<string>();
        foreach (var kv in node.children.ToList())
        {
            if (IsPostFolder(kv.Key))
            {
                // Skip if post folder is hidden (contains Hide.txt)
                if (kv.Value.hidden)
                {
                    postFolders.Add(kv.Key);
                    continue;
                }
                
                var match = System.Text.RegularExpressions.Regex.Match(kv.Key, @"^Post\.([0-9]+)(?:\s+(.*))?$");
                if (match.Success)
                {
                    int postNum = int.Parse(match.Groups[1].Value);
                    string title = match.Groups.Count > 2 ? match.Groups[2].Value.Trim() : "";
                    if (string.IsNullOrEmpty(title)) title = $"Post {postNum}";
                    
                    var post = new PostEntry
                    {
                        number = postNum,
                        title = title,
                        imagePaths = new List<string>(kv.Value.imagePaths),
                        buttons = new List<ButtonEntry>(kv.Value.buttons)
                    };
                    node.posts.Add(post);
                    postFolders.Add(kv.Key);
                }
            }
        }
        // Remove post folders from navigation
        foreach (var pf in postFolders)
        {
            node.children.Remove(pf);
        }
        
        // Recurse into remaining children
        foreach (var child in node.children.Values)
        {
            CollectPostFolders(child);
        }
    }

    
    // Simple cache for downloaded textures within session
    private Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
    private Dictionary<string, string> texErrors = new Dictionary<string, string>();
    private HashSet<string> texDownloading = new HashSet<string>();
    private Queue<string> downloadQueue = new Queue<string>();
    private bool isProcessingQueue = false;
    private int downloadPriority = 0;
    private CancellationTokenSource downloadCancellation = new CancellationTokenSource();

    private Texture2D GetCachedTexture(string url)
    {
        texCache.TryGetValue(url, out var t);
        return t;
    }

    private bool IsDownloading(string url) => texDownloading.Contains(url);

    private void ClearDownloads()
    {
        // Cancel current downloads
        downloadCancellation.Cancel();
        downloadCancellation = new CancellationTokenSource();
        
        // Clear queue and downloading set
        downloadQueue.Clear();
        texDownloading.Clear();
        isProcessingQueue = false;
    }

    private void QueueDownloadByPriority(string url)
    {
        if (texDownloading.Contains(url) || texCache.ContainsKey(url) || downloadQueue.Contains(url)) return;
        downloadQueue.Enqueue(url);
        ProcessDownloadQueue();
    }

    private async void ProcessDownloadQueue()
    {
        if (isProcessingQueue || downloadQueue.Count == 0) return;
        isProcessingQueue = true;

        var cancellationToken = downloadCancellation.Token;
        try
        {
            while (downloadQueue.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                string url = downloadQueue.Dequeue();
                if (!texDownloading.Contains(url) && !texCache.ContainsKey(url))
                {
                    await StartDownload(url, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Downloads were cancelled, this is expected
        }
        finally
        {
            isProcessingQueue = false;
        }
    }

    private async Task StartDownload(string url, CancellationToken cancellationToken = default)
    {
        if (texDownloading.Contains(url)) return;
        texDownloading.Add(url);
        try
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                    client.DefaultRequestHeaders.Add("User-Agent", "Unity-The_News");
                var resp = await client.GetAsync(url, cancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    texErrors[url] = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
                }
                else
                {
                    var bytes = await resp.Content.ReadAsByteArrayAsync();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (bytes == null || bytes.Length == 0)
                    {
                        texErrors[url] = "Empty response";
                    }
                    else
                    {
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (tex.LoadImage(bytes))
                        {
                            texCache[url] = tex;
                            if (texErrors.ContainsKey(url)) texErrors.Remove(url);
                        }
                        else
                        {
                            texErrors[url] = "Unsupported format or corrupted data";
                        }
                    }
                }
            }
        }
        catch (TaskCanceledException)
        {
            texErrors[url] = "Request timeout";
        }
        catch (OperationCanceledException)
        {
            // Download was cancelled, don't treat as error
            texErrors.Remove(url);
        }
        catch (HttpRequestException e)
        {
            texErrors[url] = e.Message;
        }
        catch (Exception e)
        {
            Debug.LogWarning("Image download failed: " + e.Message + " url=" + url);
            texErrors[url] = e.Message;
        }
        finally
        {
            texDownloading.Remove(url);
            EditorApplication.delayCall += Repaint;
        }
    }
}
#endif
