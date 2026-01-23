#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using VRC.SDKBase;
using UdonSharpEditor;
using VRC.SDK3.Components;
using VRC.SDK3.Video.Components;
using VRC.SDK3.Video.Components.AVPro;

// Custom ScriptableObject to hold VRCUrl data for the video provider
public class VideoUrlData : ScriptableObject
{
    public string urlString = "";
}

[CustomEditor(typeof(VideoURLProvider))]
public class VideoURLProviderEditor : Editor
{
    private string _statusMessage = "";
    private bool _isLoading = false;
    private List<string> _fetchedUrls = new List<string>();
    private List<string> _fetchedCaptions = new List<string>();
    private bool _showVideoPlayersFoldout = true;

    // Directory path for storing VRCUrl data objects
    private string _vrcUrlsDirectory = "Assets/VRCUrls";

    private MessageType _messageType = MessageType.Info;
    private string PrefsKey => $"{Application.dataPath}_ComfyUI_VideoProvider_Dir";

    private void OnEnable()
    {
        _vrcUrlsDirectory = EditorPrefs.GetString(PrefsKey, "Assets/VRCUrls");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Get a reference to the target script
        VideoURLProvider videoProvider = (VideoURLProvider)target;

        // Create custom video player section with clear instructions
        _showVideoPlayersFoldout = EditorGUILayout.Foldout(_showVideoPlayersFoldout, "Video Player References", true, EditorStyles.foldoutHeader);

        if (_showVideoPlayersFoldout)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox("You can use either Unity Video Player OR AVPro Video Player. If both are assigned, AVPro will be used first.", MessageType.Info);

            // Get serialized properties
            SerializedProperty unityPlayerProp = serializedObject.FindProperty("unityVideoPlayer");
            SerializedProperty avproPlayerProp = serializedObject.FindProperty("avproVideoPlayer");
            SerializedProperty urlInputFieldProp = serializedObject.FindProperty("urlInputField");

            // Unity Video Player field with custom styling
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Unity Video Player", headerStyle);
            EditorGUILayout.PropertyField(unityPlayerProp, new GUIContent("VRC Unity Video Player", "Reference to the Unity Video Player component"));
            EditorGUILayout.Space();

            // AVPro Video Player field with custom styling
            EditorGUILayout.LabelField("AVPro Video Player", headerStyle);
            EditorGUILayout.PropertyField(avproPlayerProp, new GUIContent("VRC AVPro Video Player", "Reference to the AVPro Video Player component"));
            EditorGUILayout.Space();

            // URL Input field
            EditorGUILayout.PropertyField(urlInputFieldProp, new GUIContent("Url Input Field", "The VRCUrlInputField to update with new URLs (Optional)"));

            EditorGUI.indentLevel--;
        }

        // Draw the rest of the default inspector (without redrawing the fields we've already drawn)
        DrawPropertiesExcluding(serializedObject, new string[] { "unityVideoPlayer", "avproVideoPlayer", "urlInputField", "loopPlaylist", "autoAdvance" });

        // Add a space
        EditorGUILayout.Space();

        // Playback settings section
        EditorGUILayout.LabelField("Playback Controls", EditorStyles.boldLabel);

        // Get the autoAdvance property
        SerializedProperty autoAdvanceProp = serializedObject.FindProperty("autoAdvance");

        // Display the auto advance toggle with a custom label
        EditorGUILayout.PropertyField(autoAdvanceProp, new GUIContent("Auto Advance", "Automatically advance to the next video when the current one ends"));

        // Add a space
        EditorGUILayout.Space();

        // GitHub URL configuration
        EditorGUILayout.LabelField("Editor Configuration", EditorStyles.boldLabel);

        // Display the raw GitHub URL field
        EditorGUILayout.BeginHorizontal();
        string newUrl = EditorGUILayout.TextField(new GUIContent("GitHub Raw URL", "The raw URL of the Markdown file on GitHub containing your list of videos."), videoProvider.githubRawUrlString);
        if (newUrl != videoProvider.githubRawUrlString)
        {
            videoProvider.githubRawUrlString = newUrl != null ? newUrl.Trim() : "";
            EditorUtility.SetDirty(videoProvider);
        }

        if (GUILayout.Button(new GUIContent("Paste", "Paste URL from clipboard"), GUILayout.Width(45)))
        {
            string clipboard = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(clipboard))
            {
                videoProvider.githubRawUrlString = clipboard.Trim();
                EditorUtility.SetDirty(videoProvider);
                GUI.FocusControl(null); // Clear focus to update the field
                Repaint();
            }
        }

        if (GUILayout.Button(new GUIContent("Open", "Open this URL in your default web browser"), GUILayout.Width(45)))
        {
            if (!string.IsNullOrEmpty(videoProvider.githubRawUrlString))
            {
                Application.OpenURL(videoProvider.githubRawUrlString);
            }
            else
            {
                EditorUtility.DisplayDialog("Invalid URL", "The URL field is empty.", "OK");
            }
        }
        EditorGUILayout.EndHorizontal();

        // Check for common GitHub URL mistake (using blob instead of raw)
        if (!string.IsNullOrEmpty(videoProvider.githubRawUrlString) &&
            videoProvider.githubRawUrlString.Contains("github.com") &&
            videoProvider.githubRawUrlString.Contains("/blob/"))
        {
            EditorGUILayout.HelpBox("It looks like you're using a standard GitHub URL. Please use the 'Raw' version.", MessageType.Warning);

            if (GUILayout.Button(new GUIContent("Fix URL automatically", "Convert the standard GitHub blob URL to a raw content URL.")))
            {
                // Convert: https://github.com/user/repo/blob/branch/file
                // To: https://raw.githubusercontent.com/user/repo/branch/file

                string fixedUrl = videoProvider.githubRawUrlString
                    .Replace("github.com", "raw.githubusercontent.com")
                    .Replace("/blob/", "/");

                videoProvider.githubRawUrlString = fixedUrl;
                EditorUtility.SetDirty(videoProvider);
                GUI.FocusControl(null); // Clear focus to update the field
                Repaint();
            }
        }

        // Show a textual hint about GitHub URLs
        EditorGUILayout.HelpBox("For GitHub URLs, use the 'raw' URL format. For example:\nhttps://raw.githubusercontent.com/username/repo/branch/file.txt", MessageType.Info);

        EditorGUILayout.Space();

        // Fetch and populate buttons
        EditorGUILayout.BeginHorizontal();

        GUI.enabled = !string.IsNullOrEmpty(videoProvider.githubRawUrlString) && !_isLoading;
        if (GUILayout.Button(new GUIContent("Fetch URLs from GitHub", "Download and parse the list of URLs from the provided GitHub Raw URL.")))
        {
            FetchUrlsFromGitHub(videoProvider);
        }

        GUI.enabled = _fetchedUrls.Count > 0 && !_isLoading;
        if (GUILayout.Button(new GUIContent("Auto-Generate Predefined URLs", "Create VRCUrl assets for the fetched URLs and assign them to the component.")))
        {
            AutoGeneratePredefinedUrls(videoProvider);
        }

        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        // URL directory configuration
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("URL Storage Location", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        _vrcUrlsDirectory = EditorGUILayout.TextField("VRCUrls Directory", _vrcUrlsDirectory);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetString(PrefsKey, _vrcUrlsDirectory);
        }

        if (GUILayout.Button(new GUIContent("Browse...", "Select a folder in your project to store the generated VRCUrl assets."), GUILayout.Width(80)))
        {
            // Try to use the current directory as start path
            string defaultPath = "Assets";
            if (!string.IsNullOrEmpty(_vrcUrlsDirectory))
            {
                // Handle "Assets" or "Assets/..." paths
                string relativePath = _vrcUrlsDirectory;
                if (relativePath.StartsWith("Assets/"))
                    relativePath = relativePath.Substring(7);
                else if (relativePath == "Assets")
                    relativePath = "";

                string absPath = Path.Combine(Application.dataPath, relativePath);
                if (Directory.Exists(absPath))
                {
                    defaultPath = absPath;
                }
            }

            string selectedPath = EditorUtility.OpenFolderPanel("Select VRCUrls Directory", defaultPath, "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                // Convert absolute path to project-relative path
                if (selectedPath.StartsWith(Application.dataPath))
                {
                    _vrcUrlsDirectory = "Assets" + selectedPath.Substring(Application.dataPath.Length);
                    EditorPrefs.SetString(PrefsKey, _vrcUrlsDirectory);

                    // Clear focus to update the field
                    GUI.FocusControl(null);
                    Repaint();
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid Folder", "Please select a folder inside your Unity project.", "OK");
                }
            }
        }

        if (GUILayout.Button(new GUIContent("Open", "Open this folder in your file browser"), GUILayout.Width(50)))
        {
            string relativePath = _vrcUrlsDirectory;
            if (relativePath.StartsWith("Assets/"))
                relativePath = relativePath.Substring(7);
            else if (relativePath == "Assets")
                relativePath = "";

            string absPath = Path.Combine(Application.dataPath, relativePath);

            if (Directory.Exists(absPath))
            {
                EditorUtility.RevealInFinder(absPath);
            }
            else
            {
                EditorUtility.DisplayDialog("Folder Not Found", $"The directory '{_vrcUrlsDirectory}' does not exist.", "OK");
            }
        }
        EditorGUILayout.EndHorizontal();

        // Add buttons for clearing URLs
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("URL Management", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        // Button to clear all predefined URLs
        Color originalBackgroundColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(1f, 0.5f, 0.5f); // Soft red
        if (GUILayout.Button(new GUIContent("Clear All Predefined URLs", "Remove all URLs from the component's list. Does not delete asset files.")))
        {
            GUI.backgroundColor = originalBackgroundColor; // Reset immediately
            if (EditorUtility.DisplayDialog("Clear Predefined URLs",
                "Are you sure you want to clear all predefined URLs? This action cannot be undone.",
                "Yes, Clear All", "Cancel"))
            {
                // Access the serialized predefinedUrls property
                SerializedProperty predefinedUrlsProp = serializedObject.FindProperty("predefinedUrls");

                // Resize the array to 0 (actually removes the elements completely)
                predefinedUrlsProp.arraySize = 0;
                serializedObject.ApplyModifiedProperties();

                // Reset URL count on the provider
                videoProvider.urlCount = 0;
                videoProvider.useGeneratedUrlData = false;
                EditorUtility.SetDirty(videoProvider);

                // Call the method on the target
                UdonSharpEditorUtility.GetBackingUdonBehaviour(videoProvider).SendCustomEvent("ClearPredefinedUrls");

                // Update the editor state
                _statusMessage = "All predefined URLs have been cleared.";
                _messageType = MessageType.Info;
                Repaint();
            }
        }
        GUI.backgroundColor = originalBackgroundColor; // Reset if not clicked

        GUI.backgroundColor = new Color(1f, 0.5f, 0.5f); // Soft red
        if (GUILayout.Button(new GUIContent("Delete VRCUrl Files From Disk", "Permanently delete the generated VRCUrl asset files from the selected directory.")))
        {
            GUI.backgroundColor = originalBackgroundColor; // Reset immediately
            if (EditorUtility.DisplayDialog("Delete VRCUrl Assets",
                "This will permanently delete all VRCUrl files from the '" + _vrcUrlsDirectory + "' folder.\n\nThis action cannot be undone!",
                "Yes, Delete Files", "Cancel"))
            {
                int deletedCount = DeleteVRCUrlAssets(_vrcUrlsDirectory);
                _statusMessage = $"Deleted {deletedCount} VRCUrl assets from '{_vrcUrlsDirectory}'.";
                _messageType = MessageType.Info;
                Repaint();
            }
        }
        GUI.backgroundColor = originalBackgroundColor; // Reset if not clicked

        // Button to clear older URLs
        if (GUILayout.Button(new GUIContent("Clear Runtime Cache",
            "Keeps only the currently displayed video in memory and clears all other URLs. This affects only the runtime state, not the predefined URLs.")))
        {
            if (EditorUtility.DisplayDialog("Clear Runtime Cache",
                "This will clear all downloaded URLs from memory except the currently displayed one.\n\nThis affects only the runtime state, not your predefined URLs configuration.",
                "Clear Cache", "Cancel"))
            {
                // Call the method on the target
                UdonSharpEditorUtility.GetBackingUdonBehaviour(videoProvider).SendCustomEvent("ClearOlderUrls");

                // Update the editor state
                serializedObject.Update();
                _statusMessage = "Runtime URL cache cleared, keeping only the current URL (if any).";
                _messageType = MessageType.Info;
                Repaint();
            }
        }

        EditorGUILayout.EndHorizontal();

        // Display status message
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(_statusMessage, _messageType);
        }

        // Display fetched URLs count
        if (_fetchedUrls.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Fetched URLs: {_fetchedUrls.Count}", EditorStyles.boldLabel);

            // Show a scrollable list of the first few URLs
            EditorGUILayout.BeginVertical(GUI.skin.box);
            int displayCount = Mathf.Min(_fetchedUrls.Count, 5);
            for (int i = 0; i < displayCount; i++)
            {
                string displayText = _fetchedUrls[i];
                if (i < _fetchedCaptions.Count && !string.IsNullOrEmpty(_fetchedCaptions[i]))
                {
                    displayText = $"{_fetchedCaptions[i]}: {displayText}";
                }

                EditorGUILayout.LabelField($"{i+1}. {displayText}", EditorStyles.wordWrappedLabel);
            }
            if (_fetchedUrls.Count > 5)
            {
                EditorGUILayout.LabelField("... and more", EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndVertical();
        }

        // Apply changes
        serializedObject.ApplyModifiedProperties();

        // Display active player type (if any)
        if (videoProvider.avproVideoPlayer != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Active video player: AVPro Video Player", MessageType.Info);
        }
        else if (videoProvider.unityVideoPlayer != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Active video player: Unity Video Player", MessageType.Info);
        }
    }

    private void FetchUrlsFromGitHub(VideoURLProvider videoProvider)
    {
        // Security check: Ensure URL uses HTTP/HTTPS to prevent SSRF (file:// access)
        string urlToCheck = videoProvider.githubRawUrlString.Trim();
        if (!urlToCheck.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !urlToCheck.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _statusMessage = "Error: Invalid URL scheme. Only 'http://' and 'https://' are allowed.";
            _messageType = MessageType.Error;
            Repaint();
            return;
        }

        _isLoading = true;
        _statusMessage = "Fetching URLs from GitHub...";
        _messageType = MessageType.Info;
        _fetchedUrls.Clear();
        _fetchedCaptions.Clear();

        try
        {
            // Create a web client
            using (WebClient client = new WebClient())
            {
                // Download the file
                string content = client.DownloadString(videoProvider.githubRawUrlString);

                // Process the content
                string[] lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();

                    // Skip empty lines, comments, and headers
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#") || !trimmedLine.Contains("https://"))
                        continue;

                    // Extract the URL from the line (support various formats)
                    string urlStr = ExtractUrlFromLine(trimmedLine);
                    if (string.IsNullOrEmpty(urlStr))
                        continue;

                    // Extract caption
                    string caption = ExtractCaptionFromLine(trimmedLine);

                    // Add to our lists
                    _fetchedUrls.Add(urlStr);
                    _fetchedCaptions.Add(caption);
                }

                if (_fetchedUrls.Count > 0)
                {
                    _statusMessage = $"Successfully fetched {_fetchedUrls.Count} URLs from GitHub.";
                    _messageType = MessageType.Info;
                }
                else
                {
                    _statusMessage = "Fetched content but found 0 valid URLs. Please check the file format.";
                    _messageType = MessageType.Warning;
                }
            }
        }
        catch (Exception ex)
        {
            _statusMessage = $"Error fetching URLs: {ex.Message}";
            _messageType = MessageType.Error;
            Debug.LogError($"Error fetching URLs: {ex}");
        }
        finally
        {
            _isLoading = false;
        }

        // Force the inspector to repaint
        Repaint();
    }

    private void AutoGeneratePredefinedUrls(VideoURLProvider videoProvider)
    {
        try
        {
            // Create assets directory for VRCUrl data objects
            string directoryPath = _vrcUrlsDirectory;
            if (!AssetDatabase.IsValidFolder(directoryPath))
            {
                string folderName = directoryPath.Substring(directoryPath.LastIndexOf('/') + 1);
                string parentPath = directoryPath.Substring(0, directoryPath.LastIndexOf('/'));
                AssetDatabase.CreateFolder(parentPath, folderName);
                AssetDatabase.Refresh();
            }

            int urlCount = _fetchedUrls.Count;

            // First, ensure we have the proper backing UdonBehaviour
            var udonBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(videoProvider);
            if (udonBehaviour == null)
            {
                _statusMessage = "Error: Could not find backing UdonBehaviour";
                _messageType = MessageType.Error;
                return;
            }

            // Keep track of success count
            int successCount = 0;

            // Create VRCUrl objects for each URL
            List<VRCUrl> vrcUrls = new List<VRCUrl>();
            for (int i = 0; i < urlCount; i++)
            {
                string url = _fetchedUrls[i];
                vrcUrls.Add(new VRCUrl(url));
            }

            // Resize the predefinedUrls array directly on the videoProvider
            videoProvider.predefinedUrls = new VRCUrl[urlCount];

            // Store VRCUrl objects in the array
            for (int i = 0; i < urlCount; i++)
            {
                if (i % 5 == 0 || i == urlCount - 1)
                {
                    EditorUtility.DisplayProgressBar("Generating Assets",
                        $"Creating VRCUrl asset {i + 1}/{urlCount}",
                        (float)(i + 1) / urlCount);
                }

                try
                {
                    string url = _fetchedUrls[i];

                    // Create a sanitized filename for the asset reference
                    string filename = $"url_{i}";
                    try
                    {
                        Uri uri = new Uri(url);
                        string path = uri.AbsolutePath;
                        string lastSegment = path.Substring(path.LastIndexOf('/') + 1);
                        if (!string.IsNullOrEmpty(lastSegment))
                        {
                            int queryIndex = lastSegment.IndexOf('?');
                            if (queryIndex > 0)
                                lastSegment = lastSegment.Substring(0, queryIndex);

                            filename = lastSegment;
                        }
                    }
                    catch {}

                    // Clean the filename
                    filename = string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
                    if (filename.Length > 40) filename = filename.Substring(0, 40);

                    // Store the URL data as a ScriptableObject for reference
                    string urlDataPath = $"{directoryPath}/VideoUrlData_{i}_{filename}.asset";

                    // Check if the asset already exists
                    VideoUrlData existingUrlData = AssetDatabase.LoadAssetAtPath<VideoUrlData>(urlDataPath);
                    VideoUrlData urlData;

                    if (existingUrlData != null)
                    {
                        // Update the existing VideoUrlData
                        urlData = existingUrlData;
                        urlData.urlString = url;
                        EditorUtility.SetDirty(urlData);
                    }
                    else
                    {
                        // Create a new VideoUrlData
                        urlData = ScriptableObject.CreateInstance<VideoUrlData>();
                        urlData.urlString = url;
                        AssetDatabase.CreateAsset(urlData, urlDataPath);
                    }

                    // Assign the VRCUrl to the predefinedUrls array
                    videoProvider.predefinedUrls[i] = vrcUrls[i];
                    successCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to create VRCUrl data for URL {i}: {_fetchedUrls[i]}\nError: {ex.Message}");
                }
            }

            // Update the VideoURLProvider to use the generated data
            videoProvider.useGeneratedUrlData = true;
            videoProvider.urlCount = successCount;

            // Apply changes
            EditorUtility.SetDirty(videoProvider);

            // Synchronize with the UdonBehaviour
            UdonSharpEditorUtility.CopyProxyToUdon(videoProvider);

            // Save all assets
            AssetDatabase.SaveAssets();

            if (successCount == urlCount)
            {
                _statusMessage = $"Successfully created {successCount} VRCUrl objects and assigned them to predefinedUrls.";
                _messageType = MessageType.Info;
            }
            else
            {
                _statusMessage = $"Created {successCount} of {urlCount} VRCUrl objects. Check console for details on errors.";
                _messageType = MessageType.Warning;
            }
        }
        catch (Exception ex)
        {
            _statusMessage = $"Error generating VRCUrl data: {ex.Message}";
            _messageType = MessageType.Error;
            Debug.LogError($"Error generating VRCUrl data: {ex}");
            Debug.LogException(ex);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // Force the inspector to repaint
        Repaint();
    }

    private int DeleteVRCUrlAssets(string folderPath)
    {
        int deletedCount = 0;

        try
        {
            // Convert the project-relative path to an absolute path
            string absolutePath = Path.Combine(Application.dataPath, folderPath.Substring("Assets/".Length));

            if (!Directory.Exists(absolutePath))
            {
                Debug.LogWarning($"Directory not found: {absolutePath}");
                return 0;
            }

            // Get all .asset files in the directory
            string[] assetFiles = Directory.GetFiles(absolutePath, "*.asset");

            for (int i = 0; i < assetFiles.Length; i++)
            {
                string file = assetFiles[i];
                if (i % 10 == 0 || i == assetFiles.Length - 1)
                {
                    EditorUtility.DisplayProgressBar("Deleting Assets",
                        $"Processing file {i + 1}/{assetFiles.Length}",
                        (float)(i + 1) / assetFiles.Length);
                }

                // Convert to project-relative path
                string projectPath = "Assets" + file.Substring(Application.dataPath.Length).Replace('\\', '/');

                // Check if this is a VideoUrlData asset
                if (projectPath.Contains("VideoUrlData") || AssetDatabase.LoadAssetAtPath<VideoUrlData>(projectPath) != null)
                {
                    if (AssetDatabase.DeleteAsset(projectPath))
                    {
                        deletedCount++;
                        Debug.Log($"Deleted asset: {projectPath}");
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to delete asset: {projectPath}");
                    }
                }
            }

            // Refresh the AssetDatabase
            AssetDatabase.Refresh();

            return deletedCount;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error deleting VRCUrl assets: {ex.Message}");
            return deletedCount;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private string ExtractUrlFromLine(string line)
    {
        // Direct URL format
        if (line.StartsWith("http"))
        {
            return line;
        }

        // Title: URL format
        if (line.Contains(":"))
        {
            int colonPos = line.IndexOf(":");
            if (colonPos >= 0 && colonPos < line.Length - 1)
            {
                string afterColon = line.Substring(colonPos + 1).Trim();
                if (afterColon.StartsWith("http"))
                {
                    return afterColon;
                }
            }
        }

        // Find any URL in the line
        int httpIndex = line.IndexOf("http");
        if (httpIndex >= 0)
        {
            string substr = line.Substring(httpIndex);
            // Attempt to find the end of the URL by looking for whitespace
            int spaceIndex = substr.IndexOf(' ');
            return spaceIndex > 0 ? substr.Substring(0, spaceIndex) : substr;
        }

        return "";
    }

    private string ExtractCaptionFromLine(string line)
    {
        if (line.Contains(":"))
        {
            int colonIndex = line.IndexOf(":");
            if (colonIndex > 0)
            {
                string captionText = line.Substring(0, colonIndex).Trim();
                // Skip numeric prefixes like "1. "
                int dotIndex = captionText.IndexOf('.');
                if (dotIndex >= 0 && dotIndex < captionText.Length - 1)
                {
                    // Check if the part before the dot is a number
                    string numPart = captionText.Substring(0, dotIndex);
                    if (int.TryParse(numPart, out int _))
                    {
                        captionText = captionText.Substring(dotIndex + 1).Trim();
                    }
                }
                return captionText;
            }
        }

        return "";
    }
}
#endif