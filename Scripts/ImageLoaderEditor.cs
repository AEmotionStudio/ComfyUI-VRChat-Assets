#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Reflection;
using VRC.SDKBase;
using UdonSharpEditor;
using UnityEngine.Networking;
using VRC.SDK3.Components;
using UdonSharp;
using VRC.Udon;
using VRC.Core;

// Custom ScriptableObject to hold VRCUrl data
public class VRCUrlData : ScriptableObject
{
    public string urlString = "";
}

[CustomEditor(typeof(ImageLoader))]
public class ImageLoaderEditor : Editor
{
    private string _statusMessage = "";
    private bool _isLoading = false;
    private List<string> _fetchedUrls = new List<string>();
    private List<string> _fetchedCaptions = new List<string>();

    // Keep references to created VRCUrl objects
    private List<VRCUrl> _vrcUrls = new List<VRCUrl>();

    [Header("Live Streaming Mode")]
    [SerializeField, Tooltip("Enable cycling through predefined URL slots for continuous updates")]
    private bool liveStreamingMode = true;

    [SerializeField, Tooltip("Number of seconds between checks for new images in streaming mode")]
    private float streamingUpdateInterval = 15f;

    [SerializeField, Tooltip("Show newest images first in streaming mode")]
    private bool newestImagesFirst = true;

    // Directory path for storing VRCUrl data objects
    private string _vrcUrlsDirectory = "Assets/VRCUrls";

    public override void OnInspectorGUI()
    {
        // Draw the default inspector
        DrawDefaultInspector();

        // Get the target
        ImageLoader imageLoader = (ImageLoader)target;

        // Add a space
        EditorGUILayout.Space();

        // GitHub URL configuration
        EditorGUILayout.LabelField("Editor Configuration", EditorStyles.boldLabel);

        // Display the raw GitHub URL field
        EditorGUILayout.BeginHorizontal();
        imageLoader.githubRawUrlString = EditorGUILayout.TextField("GitHub Raw URL", imageLoader.githubRawUrlString);
        EditorGUILayout.EndHorizontal();

        // Check for common GitHub URL mistake (using blob instead of raw)
        if (!string.IsNullOrEmpty(imageLoader.githubRawUrlString) &&
            imageLoader.githubRawUrlString.Contains("github.com") &&
            imageLoader.githubRawUrlString.Contains("/blob/"))
        {
            EditorGUILayout.HelpBox("It looks like you're using a standard GitHub URL. Please use the 'Raw' version.", MessageType.Warning);

            if (GUILayout.Button("Fix URL automatically"))
            {
                // Convert: https://github.com/user/repo/blob/branch/file
                // To: https://raw.githubusercontent.com/user/repo/branch/file

                string fixedUrl = imageLoader.githubRawUrlString
                    .Replace("github.com", "raw.githubusercontent.com")
                    .Replace("/blob/", "/");

                imageLoader.githubRawUrlString = fixedUrl;
                GUI.FocusControl(null); // Clear focus to update the field
                Repaint();
            }
        }

        // Show a textual hint about GitHub URLs
        EditorGUILayout.HelpBox("For GitHub URLs, use the 'raw' URL format. For example:\nhttps://raw.githubusercontent.com/username/repo/branch/file.txt", MessageType.Info);

        EditorGUILayout.Space();

        // Fetch and populate buttons
        EditorGUILayout.BeginHorizontal();

        GUI.enabled = !string.IsNullOrEmpty(imageLoader.githubRawUrlString) && !_isLoading;
        if (GUILayout.Button("Fetch URLs from GitHub"))
        {
            FetchUrlsFromGitHub(imageLoader);
        }

        GUI.enabled = _fetchedUrls.Count > 0 && !_isLoading;
        if (GUILayout.Button("Auto-Generate Predefined URLs"))
        {
            AutoGeneratePredefinedUrls(imageLoader);
        }

        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        // URL directory configuration
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("URL Storage Location", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        _vrcUrlsDirectory = EditorGUILayout.TextField("VRCUrls Directory", _vrcUrlsDirectory);

        if (GUILayout.Button("Browse...", GUILayout.Width(80)))
        {
            string selectedPath = EditorUtility.OpenFolderPanel("Select VRCUrls Directory", "Assets", "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                // Convert absolute path to project-relative path
                if (selectedPath.StartsWith(Application.dataPath))
                {
                    _vrcUrlsDirectory = "Assets" + selectedPath.Substring(Application.dataPath.Length);
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid Folder", "Please select a folder inside your Unity project.", "OK");
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        // Add buttons for clearing URLs
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("URL Management", EditorStyles.boldLabel);

        // Split buttons across multiple rows for better visibility

        // Row 1: Clear predefined URLs
        EditorGUILayout.BeginHorizontal();

        // Button to clear all predefined URLs
        if (GUILayout.Button("Clear All Predefined URLs"))
        {
            if (EditorUtility.DisplayDialog("Clear Predefined URLs",
                "Are you sure you want to clear all predefined URLs? This action cannot be undone.",
                "Yes, Clear All", "Cancel"))
            {
                // Access the serialized predefinedUrls property
                SerializedProperty predefinedUrlsProp = serializedObject.FindProperty("predefinedUrls");

                // Resize the array to 0 (actually removes the elements completely)
                predefinedUrlsProp.arraySize = 0;
                serializedObject.ApplyModifiedProperties();

                // Reset URL count on the imageLoader
                imageLoader.urlCount = 0;
                imageLoader.useGeneratedUrlData = false;
                EditorUtility.SetDirty(imageLoader);

                // Call the method on the target
                UdonSharpEditorUtility.GetBackingUdonBehaviour(imageLoader).SendCustomEvent("ClearPredefinedUrls");

                // Update the editor state
                _statusMessage = "All predefined URLs have been cleared.";
                Repaint();
            }
        }

        EditorGUILayout.EndHorizontal();

        // Row 2: Delete asset files and clear runtime memory
        EditorGUILayout.BeginHorizontal();

        // Button to delete VRCUrl assets from the folder
        if (GUILayout.Button("Delete VRCUrl Files From Disk"))
        {
            if (EditorUtility.DisplayDialog("Delete VRCUrl Assets",
                "This will permanently delete all VRCUrl files from the '" + _vrcUrlsDirectory + "' folder.\n\nThis action cannot be undone!",
                "Yes, Delete Files", "Cancel"))
            {
                int deletedCount = DeleteVRCUrlAssets(_vrcUrlsDirectory);
                _statusMessage = $"Deleted {deletedCount} VRCUrl assets from '{_vrcUrlsDirectory}'.";
                Repaint();
            }
        }

        // Button to clear older URLs (with improved tooltip)
        if (GUILayout.Button(new GUIContent("Clear Runtime Cache",
            "Keeps only the currently displayed image in memory and clears all other downloaded images. This affects only the runtime state, not the predefined URLs.")))
        {
            if (EditorUtility.DisplayDialog("Clear Runtime Cache",
                "This will clear all downloaded images from memory except the currently displayed one.\n\nThis affects only the runtime state, not your predefined URLs configuration.",
                "Clear Cache", "Cancel"))
            {
                // Call the method on the target
                UdonSharpEditorUtility.GetBackingUdonBehaviour(imageLoader).SendCustomEvent("ClearOlderUrls");

                // Update the editor state
                serializedObject.Update();
                _statusMessage = "Runtime image cache cleared, keeping only the current image (if any).";
                Repaint();
            }
        }

        EditorGUILayout.EndHorizontal();

        // Display status message
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
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
                EditorGUILayout.LabelField($"{i+1}. {_fetchedUrls[i]}", EditorStyles.wordWrappedLabel);
            }
            if (_fetchedUrls.Count > 5)
            {
                EditorGUILayout.LabelField("... and more", EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndVertical();
        }

        // Apply changes
        serializedObject.ApplyModifiedProperties();
    }

    private void FetchUrlsFromGitHub(ImageLoader imageLoader)
    {
        _isLoading = true;
        _statusMessage = "Fetching URLs from GitHub...";
        _fetchedUrls.Clear();
        _fetchedCaptions.Clear();

        try
        {
            // Create a web client
            using (WebClient client = new WebClient())
            {
                // Download the file
                string content = client.DownloadString(imageLoader.githubRawUrlString);

                // Process the content
                string[] lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();

                    // Skip empty lines, comments, and headers
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#") || !trimmedLine.Contains("https://"))
                        continue;

                    // Extract the URL from the line (support various formats)
                    string urlStr = "";
                    string caption = imageLoader.defaultCaption;

                    // Format 1: "URL" (direct URL)
                    if (trimmedLine.StartsWith("http"))
                    {
                        urlStr = trimmedLine;
                    }
                    // Format 2: "filename.png: URL" (colon separated)
                    else if (trimmedLine.Contains(":"))
                    {
                        // Try to extract caption
                        if (trimmedLine.Contains(".png:") || trimmedLine.Contains(".jpg:") || trimmedLine.Contains(".jpeg:"))
                        {
                            int startIndex = trimmedLine.IndexOf(".") - 36; // UUID length is typically 36 chars
                            if (startIndex > 0)
                            {
                                int endIndex = trimmedLine.IndexOf(":");
                                if (endIndex > startIndex)
                                {
                                    caption = trimmedLine.Substring(startIndex, endIndex - startIndex).Trim();
                                }
                            }
                        }

                        int colonPos = trimmedLine.IndexOf(":");
                        string afterColon = trimmedLine.Substring(colonPos + 1).Trim();

                        if (afterColon.StartsWith("http"))
                            urlStr = afterColon;
                    }
                    // Format 3: "n. filename.png: URL" (numbered list)
                    else if (trimmedLine.Contains(".") && trimmedLine.Contains(":"))
                    {
                        int urlStartIndex = trimmedLine.IndexOf("https://");
                        if (urlStartIndex >= 0)
                            urlStr = trimmedLine.Substring(urlStartIndex).Trim();

                        // Try to extract caption
                        if (trimmedLine.Contains(".png:") || trimmedLine.Contains(".jpg:") || trimmedLine.Contains(".jpeg:"))
                        {
                            int startIndex = trimmedLine.IndexOf(".") - 36; // UUID length is typically 36 chars
                            if (startIndex > 0)
                            {
                                int endIndex = trimmedLine.IndexOf(":");
                                if (endIndex > startIndex)
                                {
                                    caption = trimmedLine.Substring(startIndex, endIndex - startIndex).Trim();
                                }
                            }
                        }
                    }

                    // If we couldn't extract a URL, skip this line
                    if (string.IsNullOrEmpty(urlStr) || !urlStr.StartsWith("https://"))
                        continue;

                    // Add to our list
                    _fetchedUrls.Add(urlStr);
                    _fetchedCaptions.Add(caption);
                }

                _statusMessage = $"Successfully fetched {_fetchedUrls.Count} URLs from GitHub.";
            }
        }
        catch (Exception ex)
        {
            _statusMessage = $"Error fetching URLs: {ex.Message}";
            Debug.LogError($"Error fetching URLs: {ex}");
        }
        finally
        {
            _isLoading = false;
        }

        // Force the inspector to repaint
        Repaint();
    }

    private void AutoGeneratePredefinedUrls(ImageLoader imageLoader)
    {
        try
        {
            // Get the serialized property for predefinedUrls
            SerializedProperty predefinedUrlsProp = serializedObject.FindProperty("predefinedUrls");
            if (predefinedUrlsProp == null)
            {
                _statusMessage = "Error: Could not find predefinedUrls property";
                return;
            }

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

            // Resize the predefinedUrls array
            predefinedUrlsProp.arraySize = urlCount;

            // Keep track of success count
            int successCount = 0;

            // First, create the VRCUrl objects in memory
            _vrcUrls.Clear();
            for (int i = 0; i < urlCount; i++)
            {
                string url = _fetchedUrls[i];
                _vrcUrls.Add(new VRCUrl(url));
            }

            // Create VRCUrl assets one by one
            for (int i = 0; i < urlCount; i++)
            {
                string url = _fetchedUrls[i];

                try
                {
                    // Create a sanitized filename for the asset
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
                    string urlDataPath = $"{directoryPath}/VRCUrlData_{i}_{filename}.asset";

                    // Check if the asset already exists
                    VRCUrlData existingUrlData = AssetDatabase.LoadAssetAtPath<VRCUrlData>(urlDataPath);
                    VRCUrlData urlData;

                    if (existingUrlData != null)
                    {
                        // Update the existing VRCUrlData
                        urlData = existingUrlData;
                        urlData.urlString = url;
                        EditorUtility.SetDirty(urlData);
                    }
                    else
                    {
                        // Create a new VRCUrlData
                        urlData = ScriptableObject.CreateInstance<VRCUrlData>();
                        urlData.urlString = url;
                        AssetDatabase.CreateAsset(urlData, urlDataPath);
                    }

                    successCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to create VRCUrl data for URL {i}: {url}\nError: {ex.Message}");
                }
            }

            // Apply all changes to the serialized object
            serializedObject.ApplyModifiedProperties();

            // Directly set the predefinedUrls array on the ImageLoader component
            imageLoader.predefinedUrls = _vrcUrls.ToArray();

            EditorUtility.SetDirty(imageLoader);
            AssetDatabase.SaveAssets();

            // Update the ImageLoader to use the generated data
            imageLoader.useGeneratedUrlData = true;
            imageLoader.urlCount = successCount;

            if (successCount == urlCount)
            {
                _statusMessage = $"Successfully created {successCount} VRCUrl objects and assigned them to predefinedUrls.";
            }
            else
            {
                _statusMessage = $"Created {successCount} of {urlCount} VRCUrl objects. Check console for details on errors.";
            }
        }
        catch (Exception ex)
        {
            _statusMessage = $"Error generating VRCUrl assets: {ex.Message}";
            Debug.LogError($"Error generating VRCUrl assets: {ex}");
        }

        // Force the inspector to repaint
        Repaint();
    }

    // Add a method to delete VRCUrl assets from the folder
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

            foreach (string file in assetFiles)
            {
                // Convert to project-relative path
                string projectPath = "Assets" + file.Substring(Application.dataPath.Length).Replace('\\', '/');

                // Check if this is actually a VRCUrlData asset or contains "VRCUrlData" in the name
                if (projectPath.Contains("VRCUrlData") ||
                    projectPath.Contains("url_") ||
                    AssetDatabase.LoadAssetAtPath<VRCUrlData>(projectPath) != null)
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

            // Delete URL_ assets that have the cube icon (these are the VRCUrl assets)
            string[] urlAssets = Directory.GetFiles(absolutePath, "URL_*.asset");
            foreach (string file in urlAssets)
            {
                string projectPath = "Assets" + file.Substring(Application.dataPath.Length).Replace('\\', '/');
                if (AssetDatabase.DeleteAsset(projectPath))
                {
                    deletedCount++;
                    Debug.Log($"Deleted URL asset: {projectPath}");
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
    }
}
#endif