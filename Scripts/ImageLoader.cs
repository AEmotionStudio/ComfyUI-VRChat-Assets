﻿using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Image;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ImageLoader : UdonSharpBehaviour
{
    [Header("Basic Setup")]
    [SerializeField, Tooltip("Renderer to show downloaded images on")]
    private new Renderer renderer;
    
    [SerializeField, Tooltip("Text field for captions")]
    private Text captionText;
    
    [SerializeField, Tooltip("Duration in seconds until the next image is shown")]
    private float slideDurationSeconds = 10f;
    
    [Header("GitHub CDN List Settings")]
    [SerializeField, Tooltip("URL to the GitHub Pages text file containing Discord CDN URLs")]
    private VRCUrl githubCdnListUrl;
    
    [SerializeField, Tooltip("How often to check for updates to the CDN list (in seconds)")]
    private float checkForUpdatesInterval = 60f;
    
    [SerializeField, Tooltip("Caption for all images")]
    public string defaultCaption = "AI Generated Image";
    
    [SerializeField, Tooltip("Maximum images to keep in memory")]
    private int maxImageCount = 100;
    
    [Header("Pre-configured URLs")]
    [SerializeField, Tooltip("URLs that will be populated by the editor script")]
    public VRCUrl[] predefinedUrls;
    
    // These fields are used by the Editor script
    [HideInInspector]
    public string githubRawUrlString = "";
    
    [HideInInspector]
    public bool useGeneratedUrlData = false;
    
    [HideInInspector]
    public int urlCount = 0;
    
    // Internal state
    private VRCImageDownloader _imageDownloader;
    private IUdonEventReceiver _udonEventReceiver;
    private Texture2D[] _downloadedTextures = new Texture2D[0];
    private int[] _activeUrlIndices = new int[0]; // Indices into predefinedUrls array
    private string[] _captions = new string[0];
    private int _currentIndex = 0;
    private string _lastLoadedUrlList = "";
    
    // Current texture reference
    private Texture2D _currentTexture;
    private Material _originalMaterial;
    
    void Start()
    {
        // Initialize core components
        _imageDownloader = new VRCImageDownloader();
        _udonEventReceiver = (IUdonEventReceiver)this;
        
        // Store reference to original material
        if (renderer != null)
        {
            _originalMaterial = renderer.sharedMaterial;
            _currentTexture = null;
        }
        
        // If auto-populated URLs are available, initialize them directly
        if (useGeneratedUrlData && urlCount > 0)
        {
            Debug.Log($"Loading {urlCount} editor-generated URLs");
            InitFromGeneratedUrls();
        }
        
        // Start checking for URL updates from GitHub
        if (githubCdnListUrl != null) 
        {
            // Do first check immediately
            CheckForNewUrls();
            
            // Schedule periodic checks
            SendCustomEventDelayedSeconds(nameof(CheckForNewUrls), checkForUpdatesInterval);
        }
    }
    
    private void InitFromGeneratedUrls()
    {
        // Make sure predefinedUrls are properly initialized
        if (predefinedUrls == null || predefinedUrls.Length < urlCount)
        {
            Debug.LogError("PredefinedUrls array is not properly set up. Needs to be configured in editor.");
            return;
        }
        
        // For each available slot, populate with initial URLs
        for (int i = 0; i < urlCount; i++)
        {
            // Check if URL is valid
            if (predefinedUrls[i] != null)
            {
                // Initialize activeUrlIndices array with valid URLs
                AddImageIndex(i, defaultCaption);
            }
        }
        
        // Start the slideshow if we have images
        if (_activeUrlIndices.Length > 0)
        {
            Debug.Log("Starting initial image display");
            
            // Make sure we start fresh
            CancelPendingEvents();
            
            // Reset state for initial display
            _currentTexture = null;
            
            // Show the first image right away
            _currentIndex = 0;
            DisplayCurrentImage();
            
            // Schedule the next image change
            Debug.Log($"Scheduling first image change in {slideDurationSeconds} seconds");
            SendCustomEventDelayedSeconds(nameof(NextImage), slideDurationSeconds);
        }
    }
    
    public void CheckForNewUrls()
    {
        Debug.Log("Checking for new URLs from GitHub...");
        
        // Original behavior
        VRCStringDownloader.LoadUrl(githubCdnListUrl, _udonEventReceiver);
        
        // Schedule next check
        SendCustomEventDelayedSeconds(nameof(CheckForNewUrls), checkForUpdatesInterval);
    }
    
    private void DisplayCurrentImage()
    {
        if (renderer == null || _currentIndex >= _activeUrlIndices.Length) 
        {
            Debug.LogWarning("DisplayCurrentImage: Invalid renderer or index");
            return;
        }
        
        Debug.Log($"DisplayCurrentImage called for index {_currentIndex}");
        
        // Update caption
        if (captionText != null && _currentIndex < _captions.Length)
        {
            captionText.text = _captions[_currentIndex];
        }
        
        // Get the current URL index
        int urlIndex = _activeUrlIndices[_currentIndex];
        
        // Ensure URL exists for this index
        if (urlIndex >= predefinedUrls.Length || predefinedUrls[urlIndex] == null)
        {
            Debug.LogError($"No VRCUrl at index {urlIndex}");
            return;
        }
        
        // Check if already downloaded
        if (_downloadedTextures[_currentIndex] != null)
        {
            // Use cached image
            Texture2D texture = _downloadedTextures[_currentIndex];
            Debug.Log($"Using cached image {_currentIndex} (URL index: {urlIndex})");
            
            // Apply the texture
            if (texture != null)
            {
                // Direct texture application (most reliable)
                renderer.sharedMaterial.mainTexture = texture;
                _currentTexture = texture;
                
                Debug.Log($"Applied texture directly for image {_currentIndex}");
            }
            else
            {
                Debug.LogWarning("Downloaded texture is null");
            }
        }
        else
        {
            // Download it now
            Debug.Log($"Downloading image {_currentIndex} (URL index: {urlIndex})");
            var rgbInfo = new TextureInfo();
            rgbInfo.GenerateMipMaps = true;
            _imageDownloader.DownloadImage(predefinedUrls[urlIndex], renderer.material, _udonEventReceiver, rgbInfo);
        }
    }

    public void NextImage()
    {
        if (_activeUrlIndices.Length == 0) 
        {
            Debug.LogWarning("No active URL indices available, cannot change image");
            return;
        }
        
        Debug.Log($"NextImage called, current index: {_currentIndex}");
        
        // Cancel any pending events to avoid overlap
        CancelPendingEvents();
        
        // Store current texture reference
        _currentTexture = _currentIndex < _downloadedTextures.Length ? 
            _downloadedTextures[_currentIndex] : null;
        
        // Move to next image
        int previousIndex = _currentIndex;
        _currentIndex = (_currentIndex + 1) % _activeUrlIndices.Length;
        Debug.Log($"Moving from index {previousIndex} to {_currentIndex}");
        
        // Display the next image
        DisplayCurrentImage();
        
        // Schedule next image change
        Debug.Log($"Scheduling next image change in {slideDurationSeconds} seconds");
        SendCustomEventDelayedSeconds(nameof(NextImage), slideDurationSeconds);
    }
    
    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        string urlList = result.Result;
        
        Debug.Log($"Received URL list from GitHub, length: {urlList.Length} characters");
        
        // Check if we've already processed this exact list
        if (urlList == _lastLoadedUrlList)
        {
            Debug.Log("CDN list from GitHub unchanged, no updates needed");
            return;
        }
        
        // Store the loaded list for comparison next time
        _lastLoadedUrlList = urlList;
        
        // Split the file into lines
        string[] lines = urlList.Split('\n');
        
        bool foundNewImages = false;
        int newImagesProcessed = 0;
        
        Debug.Log($"Processing URL list with {lines.Length} lines");
        
        // Process each line (URL)
        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine)) continue;
            
            // Skip comments
            if (trimmedLine.StartsWith("#")) continue;
            
            // Extract the URL from the line (support various formats)
            string urlStr = ExtractUrlFromLine(trimmedLine);
            if (string.IsNullOrEmpty(urlStr))
            {
                Debug.LogWarning($"Could not extract URL from line: {trimmedLine}");
                continue;
            }
            
            // Find matching slots
            int matchingUrlIndex = FindMatchingUrlIndex(urlStr);
            if (matchingUrlIndex >= 0)
            {
                // Check if this URL index is already in our active URLs
                bool alreadyActive = false;
                for (int i = 0; i < _activeUrlIndices.Length; i++)
                {
                    if (_activeUrlIndices[i] == matchingUrlIndex)
                    {
                        alreadyActive = true;
                        break;
                    }
                }
                
                if (!alreadyActive)
                {
                    // Extract caption if available
                    string caption = ExtractCaptionFromLine(trimmedLine);
                    if (string.IsNullOrEmpty(caption))
                    {
                        caption = defaultCaption;
                    }
                    
                    // Add this URL index to our active list
                    AddImageIndex(matchingUrlIndex, caption);
                    foundNewImages = true;
                    newImagesProcessed++;
                    Debug.Log($"Added new image: {urlStr} (index: {matchingUrlIndex})");
                }
            }
            else
            {
                Debug.LogWarning($"No matching predefined URL found for: {urlStr}");
            }
        }
        
        // If we found new images
        if (foundNewImages)
        {
            Debug.Log($"Found {newImagesProcessed} new images. Continuing current slideshow.");
            
            // If we're at the beginning, start the slideshow
            if (_currentIndex == 0 && _activeUrlIndices.Length > 0)
            {
                DisplayCurrentImage();
                SendCustomEventDelayedSeconds(nameof(NextImage), slideDurationSeconds);
            }
        }
        
        // If we've exceeded the maximum, trim the oldest images
        if (_activeUrlIndices.Length > maxImageCount)
        {
            int countToRemove = _activeUrlIndices.Length - maxImageCount;
            TrimOldestImages(countToRemove);
        }
    }
    
    private string ExtractUrlFromLine(string line)
    {
        // Format 1: "URL" (direct URL)
        if (line.StartsWith("http"))
        {
            return line;
        }
        
        // Format 2: "filename.png: URL" (colon separated)
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
        
        // Format 3: "n. filename.png: URL" (numbered list)
        int httpIndex = line.IndexOf("http");
        if (httpIndex >= 0)
        {
            return line.Substring(httpIndex).Trim();
        }
        
        return "";
    }
    
    private string ExtractCaptionFromLine(string line)
    {
        // Try to extract a caption from lines like "filename.png: URL"
        if (line.Contains(".png:") || line.Contains(".jpg:") || line.Contains(".jpeg:"))
        {
            int dotIndex = line.IndexOf(".");
            if (dotIndex > 0)
            {
                // Look back from the dot to find potential starting point of filename
                int startIndex = dotIndex;
                while (startIndex > 0 && !char.IsWhiteSpace(line[startIndex - 1]))
                {
                    startIndex--;
                }
                
                if (startIndex < dotIndex)
                {
                    int endIndex = line.IndexOf(":", dotIndex);
                    if (endIndex > dotIndex)
                    {
                        return line.Substring(startIndex, endIndex - startIndex).Trim();
                    }
                }
            }
        }
        
        return "";
    }
    
    private int FindMatchingUrlIndex(string urlToFind)
    {
        // Exit early if we don't have predefined URLs
        if (predefinedUrls == null || predefinedUrls.Length == 0)
        {
            Debug.LogError("No predefined URLs available!");
            return -1;
        }
        
        // First try to find an exact match
        for (int i = 0; i < predefinedUrls.Length; i++)
        {
            if (predefinedUrls[i] != null && predefinedUrls[i].Get() == urlToFind)
            {
                return i;
            }
        }
        
        // If using generated data, we can use the cyclical assignment
        if (useGeneratedUrlData)
        {
            // Use the next available predefined URL slot (cyclical)
            int index = _activeUrlIndices.Length % urlCount;
            
            // Ensure the predefined URL at this index exists
            if (index < predefinedUrls.Length && predefinedUrls[index] != null)
            {
                return index;
            }
        }
        
        // Extract the filename from the URL as a fallback matching strategy
        string filename = ExtractFilenameFromUrl(urlToFind);
        if (!string.IsNullOrEmpty(filename))
        {
            for (int i = 0; i < predefinedUrls.Length; i++)
            {
                if (predefinedUrls[i] != null && 
                    !string.IsNullOrEmpty(predefinedUrls[i].Get()) && 
                    predefinedUrls[i].Get().Contains(filename))
                {
                    return i;
                }
            }
        }
        
        // Last resort: find any available predefined URL
        for (int i = 0; i < predefinedUrls.Length; i++)
        {
            if (predefinedUrls[i] != null)
            {
                return i;
            }
        }
        
        return -1;
    }
    
    private string ExtractFilenameFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        
        // Split by query parameters first
        string baseUrl = url.Split('?')[0];
        
        // Find last slash to extract the filename
        int lastSlashIndex = baseUrl.LastIndexOf('/');
        if (lastSlashIndex >= 0 && lastSlashIndex < baseUrl.Length - 1)
        {
            return baseUrl.Substring(lastSlashIndex + 1);
        }
        
        return "";
    }
    
    private int FindUrlSlotForFilename(string filename)
    {
        // If no filename, just use the first available slot
        if (string.IsNullOrEmpty(filename))
        {
            for (int i = 0; i < predefinedUrls.Length; i++)
            {
                if (predefinedUrls[i] != null)
                {
                    return i;
                }
            }
            
            Debug.LogError("No valid VRCUrl found in predefinedUrls array!");
            return -1;
        }
        
        // Find a predefined URL that matches this filename
        for (int i = 0; i < predefinedUrls.Length; i++)
        {
            if (predefinedUrls[i] != null && !string.IsNullOrEmpty(predefinedUrls[i].Get()))
            {
                if (predefinedUrls[i].Get().Contains(filename))
                {
                    return i;
                }
            }
        }
        
        // If no direct match, use the next available slot cyclically
        return _activeUrlIndices.Length % predefinedUrls.Length;
    }
    
    private void AddImageIndex(int urlIndex, string caption)
    {
        // Extend arrays
        int[] newIndices = new int[_activeUrlIndices.Length + 1];
        Texture2D[] newTextures = new Texture2D[_downloadedTextures.Length + 1];
        string[] newCaptions = new string[_captions.Length + 1];
        
        // Copy existing data
        for (int i = 0; i < _activeUrlIndices.Length; i++)
        {
            newIndices[i] = _activeUrlIndices[i];
            newTextures[i] = _downloadedTextures[i];
            newCaptions[i] = _captions[i];
        }
        
        // Add new data
        newIndices[_activeUrlIndices.Length] = urlIndex;
        newCaptions[_captions.Length] = caption;
        
        // Update arrays
        _activeUrlIndices = newIndices;
        _downloadedTextures = newTextures;
        _captions = newCaptions;
        
        Debug.Log($"Added new image (index: {_activeUrlIndices.Length-1}, URL index: {urlIndex})");
    }
    
    private void TrimOldestImages(int countToRemove)
    {
        if (countToRemove <= 0 || countToRemove >= _activeUrlIndices.Length) return;
        
        int newLength = _activeUrlIndices.Length - countToRemove;
        int[] newIndices = new int[newLength];
        Texture2D[] newTextures = new Texture2D[newLength];
        string[] newCaptions = new string[newLength];
        
        // Copy the newest entries (skip the oldest)
        for (int i = 0; i < newLength; i++)
        {
            newIndices[i] = _activeUrlIndices[i + countToRemove];
            newTextures[i] = _downloadedTextures[i + countToRemove];
            newCaptions[i] = _captions[i + countToRemove];
        }
        
        // Update our arrays
        _activeUrlIndices = newIndices;
        _downloadedTextures = newTextures;
        _captions = newCaptions;
        
        // Adjust current index if needed
        if (_currentIndex < countToRemove)
        {
            _currentIndex = 0;
        }
        else
        {
            _currentIndex -= countToRemove;
        }
        
        Debug.Log($"Trimmed {countToRemove} oldest images, new count: {_activeUrlIndices.Length}");
    }
    
    public override void OnImageLoadSuccess(IVRCImageDownload result)
    {
        if (result == null || result.Url == null) return;
        
        string loadedUrl = result.Url.Get();
        Debug.Log($"Image loaded successfully: {loadedUrl}");
        
        // Find which of our URLs this corresponds to
        for (int i = 0; i < _activeUrlIndices.Length; i++)
        {
            int urlIndex = _activeUrlIndices[i];
            if (urlIndex < predefinedUrls.Length && predefinedUrls[urlIndex] != null && 
                predefinedUrls[urlIndex].Get() == loadedUrl)
            {
                // Store the downloaded texture
                _downloadedTextures[i] = result.Result;
                Debug.Log($"Stored downloaded texture at index {i}");
                
                // If this is the currently displayed image, update the display
                if (i == _currentIndex)
                {
                    Debug.Log($"This is the current image being displayed ({i})");
                    
                    // Apply the texture directly to the material - simple and reliable
                    if (renderer != null && result.Result != null)
                    {
                        renderer.sharedMaterial.mainTexture = result.Result;
                        _currentTexture = result.Result;
                        
                        Debug.Log("Applied downloaded texture to material");
                    }
                    else
                    {
                        Debug.LogWarning("Cannot apply texture - renderer or texture is null");
                    }
                }
                
                break;
            }
        }
    }
    
    public override void OnStringLoadError(IVRCStringDownload result)
    {
        Debug.LogError($"Failed to load URL list: {result.Error}");
    }
    
    public override void OnImageLoadError(IVRCImageDownload result)
    {
        if (result != null && result.Url != null)
        {
            Debug.LogError($"Failed to load image: {result.Url.Get()}");
        }
    }
    
    private void OnDestroy()
    {
        if (_imageDownloader != null)
        {
            _imageDownloader.Dispose();
        }
    }
    
    // For UI buttons
    public void ForceNextImage()
    {
        Debug.Log("ForceNextImage called");
        
        // Cancel all pending events
        CancelPendingEvents();
        
        // Allow a small delay to ensure everything is cleared
        Debug.Log("Scheduling immediate image change");
        SendCustomEventDelayedSeconds(nameof(NextImage), 0.1f);
    }
    
    public void CancelPendingEvents()
    {
        // This is intentionally empty - UdonSharp will use this to clear pending events
    }

    // Expose a public method to manually force a refresh (useful for testing)
    public void ForceRefresh()
    {
        Debug.Log("Manual refresh requested");
        
        // Clear the last loaded URL list to force reprocessing
        _lastLoadedUrlList = "";
        
        // Cancel any pending checks and check immediately
        CancelPendingEvents();
        CheckForNewUrls();
    }

    // Clear all predefined URLs in one click
    public void ClearPredefinedUrls()
    {
        Debug.Log("Clearing all predefined URLs");
        
        // Reset the predefined URLs array values to null
        if (predefinedUrls != null)
        {
            for (int i = 0; i < predefinedUrls.Length; i++)
            {
                predefinedUrls[i] = null;
            }
        }
        
        // Reset URL count
        urlCount = 0;
        
        // Reset the active indices and downloaded textures
        _activeUrlIndices = new int[0];
        _downloadedTextures = new Texture2D[0];
        _captions = new string[0];
        _currentIndex = 0;
        
        // Cancel any pending slideshow events
        CancelPendingEvents();
        
        Debug.Log("All predefined URLs have been cleared");
    }
    
    // Clear older URLs from memory and reset to start fresh
    public void ClearOlderUrls()
    {
        Debug.Log("Clearing older URLs from memory");
        
        // Keep only the current URL if there is one
        if (_activeUrlIndices.Length > 0 && _currentIndex >= 0 && _currentIndex < _activeUrlIndices.Length)
        {
            int currentUrlIndex = _activeUrlIndices[_currentIndex];
            string currentCaption = _captions[_currentIndex];
            Texture2D currentTexture = _downloadedTextures[_currentIndex];
            
            // Reset to just the current URL
            _activeUrlIndices = new int[1] { currentUrlIndex };
            _captions = new string[1] { currentCaption };
            _downloadedTextures = new Texture2D[1] { currentTexture };
            _currentIndex = 0;
            
            Debug.Log("Kept only the current URL and cleared all others");
        }
        else
        {
            // If no current URL, clear everything
            _activeUrlIndices = new int[0];
            _downloadedTextures = new Texture2D[0];
            _captions = new string[0];
            _currentIndex = 0;
            
            Debug.Log("No current URL, cleared all URLs from memory");
        }
        
        // Reset the last loaded URL list to force a fresh load on next check
        _lastLoadedUrlList = "";
    }
}