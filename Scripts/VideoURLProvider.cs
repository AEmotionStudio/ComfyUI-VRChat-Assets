using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.StringLoading;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.Video.Components;
using VRC.SDK3.Video.Components.AVPro;
using VRC.SDK3.Video.Components.Base;
using System.Collections.Generic;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class VideoURLProvider : UdonSharpBehaviour
{
    [Header("Video Player References")]
    [SerializeField, Tooltip("Direct reference to the VRCUnityVideoPlayer (will find automatically if not set)")]
    public VRCUnityVideoPlayer unityVideoPlayer;
    
    [SerializeField, Tooltip("Direct reference to the VRCAVProVideoPlayer (will find automatically if not set)")]
    public VRCAVProVideoPlayer avproVideoPlayer;
    
    [SerializeField, Tooltip("The VRCUrlInputField to update with new URLs (Optional)")]
    private VRCUrlInputField urlInputField;
    
    // Reference to the video player - found at runtime if not set
    private BaseVRCVideoPlayer videoPlayer;
    
    [Header("GitHub CDN List Settings")]
    [SerializeField, Tooltip("URL to the GitHub Pages text file containing video URLs")]
    private VRCUrl githubCdnListUrl;
    
    [SerializeField, Tooltip("How often to check for updates to the CDN list (in seconds)")]
    private float checkForUpdatesInterval = 300f;
    
    [SerializeField, Tooltip("Maximum number of videos to keep in the playlist")]
    private int maxVideoCount = 50;
    
    [SerializeField, Tooltip("Caption for all videos when no specific caption is available")]
    public string defaultCaption = "Streamed Video";
    
    [Header("Pre-configured URLs")]
    [SerializeField, Tooltip("URLs that will be populated by the editor script")]
    public VRCUrl[] predefinedUrls;
    
    [Header("Playback Settings")]
    [SerializeField, Tooltip("Delay before starting to play videos (in seconds)")]
    private float startupDelay = 1f;
    
    [SerializeField, Tooltip("Number of retry attempts to play video if initial attempt fails")]
    private int playRetryAttempts = 3;
    
    [SerializeField, Tooltip("Delay between retry attempts (in seconds)")]
    private float playRetryDelay = 2f;
    
    [SerializeField, Tooltip("Maximum time to play a video before automatically advancing (in seconds)")]
    private float maxVideoPlaytime = 300f; // 5 minutes default
    
    [SerializeField, Tooltip("Whether to enforce maximum video playtime (if false, videos will play through their entire duration)")]
    private bool useMaxVideoPlaytime = true;
    
    [SerializeField, Tooltip("Whether to automatically advance to the next video when the current one ends")]
    private bool autoAdvance = true;
    
    // These fields are used by the Editor script
    [HideInInspector]
    public string githubRawUrlString = "";
    
    [HideInInspector]
    public bool useGeneratedUrlData = false;
    
    [HideInInspector]
    public int urlCount = 0;
    
    // Internal state
    private IUdonEventReceiver _udonEventReceiver;
    private int[] _activeUrlIndices = new int[0]; // Indices into predefinedUrls array
    private string[] _captions = new string[0];
    private int _currentIndex = 0;
    private string _lastLoadedUrlList = "";
    private float _lastChangeTime = 0f;
    private bool _isChangingVideo = false;
    private bool _isVideoPlayerReady = false;
    private bool _isCurrentlyPlaying = false;  // Track if video is currently playing properly
    private float _lastPlayingStateChange = 0f; // Track when playback state last changed
    
    // Track failed URLs to avoid retrying them
    private string[] _failedUrls = new string[5]; // Store up to 5 recently failed URLs
    private int _failedUrlCount = 0;
    
    // Track if we've already started the initial playlist
    private bool _hasStartedPlaylist = false;
    
    // Add a timestamp for last play attempt
    private float _lastPlayAttemptTime = 0f;
    private bool _playAttemptScheduled = false;
    
    void Start()
    {
        Debug.Log("[VideoURLProvider] Starting initialization...");
        _udonEventReceiver = (IUdonEventReceiver)this;
        
        // Clear any existing URLs to start fresh
        _activeUrlIndices = new int[0];
        _captions = new string[0];
        _currentIndex = 0;
        
        // Find the video player immediately to ensure we can use it
        FindVideoPlayer();
        
        // If we found a video player, set up URLs and prepare for auto-play
        if (_isVideoPlayerReady)
        {
            // Delay start to ensure everything is properly initialized
            SendCustomEventDelayedSeconds(nameof(DelayedStart), startupDelay);
        }
        else
        {
            Debug.LogError("[VideoURLProvider] No video player found on Start(). Trying again in delayed start.");
            SendCustomEventDelayedSeconds(nameof(DelayedStart), startupDelay);
        }
        
        // Start checking for URL updates from GitHub
        if (githubCdnListUrl != null && !string.IsNullOrEmpty(githubCdnListUrl.Get())) 
        {
            // Do first check immediately
            CheckForNewUrls();
            
            // Schedule periodic checks
            SendCustomEventDelayedSeconds(nameof(CheckForNewUrls), checkForUpdatesInterval);
        }
        
        // Add an additional delayed retry for VRChat initialization
        // This helps with first-time loading issues
        SendCustomEventDelayedSeconds(nameof(ExtraDelayedStartup), startupDelay + 5f);
    }
    
    public void DelayedStart()
    {
        Debug.Log("[VideoURLProvider] Delayed start executing...");
        
        // Find the video player again if we couldn't find it earlier
        if (!_isVideoPlayerReady)
        {
            FindVideoPlayer();
        }
        
        // Only proceed if we haven't already started the playlist
        if (_hasStartedPlaylist)
        {
            Debug.Log("[VideoURLProvider] Playlist already started, skipping initialization");
            return;
        }
        
        // Clear any existing URLs to prevent duplicates between CDN and predefined lists
        Debug.Log("[VideoURLProvider] Clearing existing URLs to prevent duplicates");
        _activeUrlIndices = new int[0];
        _captions = new string[0];
        _currentIndex = 0;
        
        // If predefined URLs are available from editor
        if (useGeneratedUrlData && urlCount > 0)
        {
            Debug.Log($"[VideoURLProvider] Starting with {urlCount} predefined URLs");
            InitFromGeneratedUrls();
            
            // Mark that we've started the playlist
            _hasStartedPlaylist = true;
        }
        else
        {
            Debug.Log("[VideoURLProvider] No predefined URLs available or useGeneratedUrlData is false");
        }
    }
    
    private void FindVideoPlayer()
    {
        // First try using direct AVPro reference if provided
        if (avproVideoPlayer != null)
        {
            videoPlayer = avproVideoPlayer;
            Debug.Log("[VideoURLProvider] Using provided AVPro Video Player reference: " + avproVideoPlayer.name);
            _isVideoPlayerReady = true;
            
            // Register for video player events right away
            RegisterVideoEvents();
            return;
        }
        
        // Then try using direct Unity reference if provided
        if (unityVideoPlayer != null)
        {
            videoPlayer = unityVideoPlayer;
            Debug.Log("[VideoURLProvider] Using provided Unity Video Player reference: " + unityVideoPlayer.name);
            _isVideoPlayerReady = true;
            
            // Register for video player events right away
            RegisterVideoEvents();
            return;
        }
        
        // First check if there's a VRCUnityVideoPlayer in our parent object
        videoPlayer = GetComponentInParent<VRCUnityVideoPlayer>();
        
        // If not found, check for AVPro player
        if (videoPlayer == null)
        {
            videoPlayer = GetComponentInParent<VRCAVProVideoPlayer>();
        }
        
        // If still not found, look more broadly in children
        if (videoPlayer == null)
        {
            videoPlayer = transform.root.GetComponentInChildren<BaseVRCVideoPlayer>();
        }
        
        // If we found a video player, we're good to go
        if (videoPlayer != null)
        {
            Debug.Log("[VideoURLProvider] Found video player: " + videoPlayer.name);
            _isVideoPlayerReady = true;
            
            // Register for video player events
            RegisterVideoEvents();
        }
        else
        {
            Debug.LogError("[VideoURLProvider] No video player found! Videos will not play automatically.");
        }
    }
    
    // Register this script to receive video player events
    private void RegisterVideoEvents()
    {
        if (videoPlayer == null) return;
        
        // For VRCUnityVideoPlayer specifically
        if (videoPlayer.GetType() == typeof(VRCUnityVideoPlayer))
        {
            Debug.Log("[VideoURLProvider] Registered for VRCUnityVideoPlayer events");
        }
        // For VRCAVProVideoPlayer specifically
        else if (videoPlayer.GetType() == typeof(VRCAVProVideoPlayer))
        {
            Debug.Log("[VideoURLProvider] Registered for VRCAVProVideoPlayer events");
        }
    }
    
    // Called when video is ready to play
    public void OnVideoReady()
    {
        Debug.Log("[VideoURLProvider] Video is ready to play");
    }
    
    private void InitFromGeneratedUrls()
    {
        // Make sure predefinedUrls are properly initialized
        if (predefinedUrls == null || predefinedUrls.Length < urlCount)
        {
            Debug.LogError("[VideoURLProvider] PredefinedUrls array is not properly set up. Needs to be configured in editor.");
            return;
        }
        
        Debug.Log("[VideoURLProvider] Initializing from " + predefinedUrls.Length + " predefined URLs");
        
        // Display all available URLs for debugging
        for (int i = 0; i < predefinedUrls.Length; i++)
        {
            if (predefinedUrls[i] != null)
            {
                Debug.Log($"[VideoURLProvider] URL[{i}]: {predefinedUrls[i].Get()}");
            }
            else
            {
                Debug.Log($"[VideoURLProvider] URL[{i}]: NULL");
            }
        }
        
        // Create a lookup map of URL strings to avoid duplicates
        string[] urlStrings = new string[predefinedUrls.Length];
        for (int i = 0; i < predefinedUrls.Length; i++)
        {
            if (predefinedUrls[i] != null)
            {
                urlStrings[i] = predefinedUrls[i].Get();
            }
            else
            {
                urlStrings[i] = "";
            }
        }
        
        // For each available slot, populate with initial URLs
        for (int i = 0; i < urlCount; i++)
        {
            // Check if URL is valid
            if (predefinedUrls[i] != null && !string.IsNullOrEmpty(predefinedUrls[i].Get()))
            {
                // Check for duplicates by comparing URL strings
                bool isDuplicate = false;
                string currentUrl = predefinedUrls[i].Get();
                
                for (int j = 0; j < i; j++)
                {
                    // Skip null URLs
                    if (predefinedUrls[j] == null) continue;
                    
                    if (urlStrings[j] == currentUrl)
                    {
                        isDuplicate = true;
                        Debug.Log($"[VideoURLProvider] Skipping duplicate URL at index {i} (matches index {j}): {currentUrl}");
                        break;
                    }
                }
                
                if (!isDuplicate)
                {
                    // Initialize activeUrlIndices array with valid URLs
                    AddUrlIndex(i, defaultCaption);
                    Debug.Log($"[VideoURLProvider] Added unique URL at index {i}: {currentUrl}");
                }
            }
        }
        
        // Make sure we don't have any duplicate videos in the playlist
        RebuildUniquePlaylist();
    }
    
    public void CheckForNewUrls()
    {
        Debug.Log("[VideoURLProvider] Checking for new URLs from GitHub...");
        if (githubCdnListUrl != null && !string.IsNullOrEmpty(githubCdnListUrl.Get()))
        {
            VRCStringDownloader.LoadUrl(githubCdnListUrl, _udonEventReceiver);
            
            // Schedule next check
            SendCustomEventDelayedSeconds(nameof(CheckForNewUrls), checkForUpdatesInterval);
        }
        else
        {
            Debug.LogWarning("[VideoURLProvider] No GitHub CDN List URL set");
        }
    }
    
    private void StartPlaylist()
    {
        if (_activeUrlIndices.Length == 0)
        {
            Debug.LogWarning("[VideoURLProvider] Cannot start playlist - no URLs available");
            return;
        }
        
        // Avoid starting if we've recently played
        if (_isCurrentlyPlaying && Time.time - _lastPlayAttemptTime < 2f)
        {
            Debug.Log("[VideoURLProvider] Skipping playlist start - already playing");
            return;
        }
        
        Debug.Log("[VideoURLProvider] Starting playlist with " + _activeUrlIndices.Length + " videos");
        _currentIndex = 0;
        
        // Show playlist contents for debugging
        DebugShowPlaylist();
        
        // Always set URL even before playing
        SetCurrentVideoUrl();
        
        // Play the current video
        PlayCurrentVideo();
    }
    
    // New method to just set the URL without playing
    private void SetCurrentVideoUrl()
    {
        // Make sure we have URLs available
        if (_activeUrlIndices.Length == 0 || _currentIndex >= _activeUrlIndices.Length)
        {
            Debug.LogError("[VideoURLProvider] Cannot set video URL - no valid URLs available");
            return;
        }
        
        // Get the index of the predefined URL to use
        int urlIndex = _activeUrlIndices[_currentIndex];
        
        // Make sure index is valid
        if (urlIndex >= predefinedUrls.Length || predefinedUrls[urlIndex] == null)
        {
            Debug.LogError($"[VideoURLProvider] Invalid URL index: {urlIndex}");
            return;
        }
        
        VRCUrl currentUrl = predefinedUrls[urlIndex];
        Debug.Log($"[VideoURLProvider] Setting video URL at index {_currentIndex}: {currentUrl.Get()}");
        
        // In VRChat, we can't directly set the URL property of VRCUnityVideoPlayer
        // But we can update the InputField if available
        if (urlInputField != null)
        {
            urlInputField.SetUrl(currentUrl);
            Debug.Log("[VideoURLProvider] Set URL in VRCUrlInputField");
        }
        
        // Store the current URL so PlayCurrentVideo can use it immediately
        Debug.Log("[VideoURLProvider] URL is prepared for playback");
    }
    
    private void PlayCurrentVideo()
    {
        // Make sure we have a video player and URLs
        if (_currentIndex >= _activeUrlIndices.Length)
        {
            Debug.LogError("[VideoURLProvider] Cannot play video - index out of range");
            return;
        }
        
        if ((!_isVideoPlayerReady && urlInputField == null))
        {
            Debug.LogError("[VideoURLProvider] Cannot play video - no video player or input field available");
            return;
        }
        
        // Always reset changing state at the beginning
        _isChangingVideo = false;
        
        // Cancel any pending next video events before starting a new one
        CancelPendingEvents();
        
        // Throttle play attempts - prevent multiple plays within 0.5 seconds
        float currentTime = Time.time;
        if (currentTime - _lastPlayAttemptTime < 0.5f)
        {
            Debug.Log($"[VideoURLProvider] Throttling play attempts - last attempt was {currentTime - _lastPlayAttemptTime:F1}s ago");
            
            // Only schedule if no attempt is already scheduled
            if (!_playAttemptScheduled)
            {
                _playAttemptScheduled = true;
                SendCustomEventDelayedSeconds(nameof(PlayThrottled), 0.5f);
            }
            return;
        }
        
        // Update last attempt time
        _lastPlayAttemptTime = currentTime;
        _playAttemptScheduled = false;
        
        // Update last change time
        _lastChangeTime = currentTime;
        
        // Get the index of the predefined URL to use
        int urlIndex = _activeUrlIndices[_currentIndex];
        
        // Make sure index is valid
        if (urlIndex >= predefinedUrls.Length || predefinedUrls[urlIndex] == null)
        {
            Debug.LogError($"[VideoURLProvider] Invalid URL index: {urlIndex}");
            return;
        }
        
        VRCUrl currentUrl = predefinedUrls[urlIndex];
        string urlString = currentUrl.Get();
        
        // Check if this URL is in our failed list
        if (IsFailedUrl(urlString))
        {
            Debug.LogWarning($"[VideoURLProvider] Skipping previously failed URL: {urlString}");
            
            // Try the next video instead
            if (autoAdvance)
            {
                NextVideo();
                return;
            }
        }
        
        Debug.Log($"[VideoURLProvider] Now playing video at index {_currentIndex}");
        
        // Just play the video directly
        if (videoPlayer != null)
        {
                videoPlayer.PlayURL(currentUrl);
            }
            
        // Update InputField if available
        if (urlInputField != null)
        {
            urlInputField.SetUrl(currentUrl);
            TriggerInputFieldSubmit();
        }
        
        // Mark video as currently playing
        _isCurrentlyPlaying = true;
        _lastPlayingStateChange = currentTime;
        
        // Schedule auto-advance if enabled
        if (autoAdvance)
        {
            // Set timer for next video
            float advanceTime = maxVideoPlaytime;
            SendCustomEventDelayedSeconds(nameof(NextVideo), advanceTime);
            
            // Also schedule a progress check at 75% of advance time
            float checkTime = advanceTime * 0.75f;
            SendCustomEventDelayedSeconds(nameof(CheckVideoProgress), checkTime);
        }
    }
    
    // Helper method to play after throttling
    public void PlayThrottled()
    {
        _playAttemptScheduled = false;
        Debug.Log("[VideoURLProvider] Playing after throttle delay");
        PlayCurrentVideo();
    }
    
    // Simplify and merge the PlayVideoStandard and ForcefullyPlayVideo methods
    private void PlayVideoStandard(VRCUrl currentUrl)
    {
        // Just directly play the video
        if (videoPlayer != null)
        {
            videoPlayer.PlayURL(currentUrl);
        }
        
        // Also update InputField if available
            if (urlInputField != null)
            {
            urlInputField.SetUrl(currentUrl);
            TriggerInputFieldSubmit();
        }
        
        // Set current state
        _isCurrentlyPlaying = true;
        _lastPlayingStateChange = Time.time;
        
        // Schedule automatic advance if enabled
        if (autoAdvance)
        {
            float advanceTime = maxVideoPlaytime;
            SendCustomEventDelayedSeconds(nameof(NextVideo), advanceTime);
            
            // Schedule a progress check at 75% of the time
            float checkTime = advanceTime * 0.75f;
            SendCustomEventDelayedSeconds(nameof(CheckVideoProgress), checkTime);
        }
    }
    
    // Simplify to use the same logic as PlayVideoStandard
    private void ForcefullyPlayVideo(VRCUrl url)
    {
        // Just directly play the video
            if (videoPlayer != null)
            {
                videoPlayer.PlayURL(url);
        }
        
        // Also update InputField if available
        if (urlInputField != null)
        {
            urlInputField.SetUrl(url);
            TriggerInputFieldSubmit();
        }
        
        // Set current state
        _isCurrentlyPlaying = true;
        _lastPlayingStateChange = Time.time;
        
        // Schedule automatic advance if enabled
        if (autoAdvance)
        {
            float advanceTime = maxVideoPlaytime;
            SendCustomEventDelayedSeconds(nameof(NextVideo), advanceTime);
            
            // Schedule a progress check at 75% of the time
            float checkTime = advanceTime * 0.75f;
            SendCustomEventDelayedSeconds(nameof(CheckVideoProgress), checkTime);
        }
    }
    
    // Method to detect if a video has completed playing but no event was fired
    public void DetectVideoCompletion()
    {
        // Don't do anything if we're already changing videos
        if (_isChangingVideo) return;
        
        // Make sure we have video URLs to work with
        if (_activeUrlIndices.Length == 0 || _currentIndex >= _activeUrlIndices.Length) return;
        
        Debug.Log("[VideoURLProvider] Running video completion check");
        
        if (videoPlayer != null)
        {
            // In VRChat UdonSharp, direct property checks like IsPlaying might not work as expected
            // Instead, we'll use a more reliable approach by trying to play the next video anyway
            // This is safe because the NextVideo method has guards against unnecessary transitions
            
            // Only auto-advance if enabled
            if (autoAdvance)
            {
                Debug.Log("[VideoURLProvider] Checking if video needs to advance to next");
                
                // Cancel any existing auto-advance timers first
                CancelPendingEvents();
                
                // Small delay before attempting to advance
                SendCustomEventDelayedSeconds(nameof(NextVideo), 0.5f);
            }
        }
    }
    
    public void NextVideo()
    {
        // Cancel any pending next video events
        CancelPendingEvents();
        
        if (_activeUrlIndices.Length == 0) return;
        
        // If we're still changing videos, force reset the state
        if (_isChangingVideo)
        {
            float stuckTime = Time.time - _lastChangeTime;
            Debug.Log($"[VideoURLProvider] Force advancing video despite changing state (stuck for {stuckTime:F1}s)");
            
            // Force reset the changing state
            _isChangingVideo = false;
        }
        
        // Throttle advancement - prevent multiple advances within 0.5 seconds
        float currentTime = Time.time;
        if (currentTime - _lastPlayAttemptTime < 0.5f)
        {
            Debug.Log($"[VideoURLProvider] Throttling next video - last attempt was {currentTime - _lastPlayAttemptTime:F1}s ago");
            
            // Schedule a delayed advance
            if (!_playAttemptScheduled)
            {
                _playAttemptScheduled = true;
                SendCustomEventDelayedSeconds(nameof(NextVideoThrottled), 0.6f);
            }
            return;
        }
        
        Debug.Log("[VideoURLProvider] Advancing to next video in playlist");
        
        // Save the current index for debugging
        int previousIndex = _currentIndex;
        
        // Always loop through videos continuously
        _currentIndex = (_currentIndex + 1) % _activeUrlIndices.Length;
        
        // If we've looped back to the beginning, log it
        if (_currentIndex == 0 && _activeUrlIndices.Length > 1)
        {
            Debug.Log("[VideoURLProvider] Reached end of playlist. Continuing from first video.");
        }
        
        // Debug playlist after changing index
        Debug.Log($"[VideoURLProvider] Advanced from index {previousIndex} to index {_currentIndex}");
        
        // Set the URL and directly play the video
        SetCurrentVideoUrl();
        PlayCurrentVideo();
    }
    
    // Helper method for throttled next video
    public void NextVideoThrottled()
    {
        _playAttemptScheduled = false;
        Debug.Log("[VideoURLProvider] Advancing to next video after throttle");
        NextVideo();
    }
    
    // A new method to play the currently set video with a slight delay
    public void PlaySetVideo()
    {
        // Force reset all state flags - critical to prevent getting stuck
        Debug.Log("[VideoURLProvider] PlaySetVideo called - ensuring clean state");
        _isChangingVideo = false;
        _playAttemptScheduled = false;
        _lastChangeTime = Time.time;
        
        // Cancel any pending next video events before starting a new one
        CancelPendingEvents();
        
        // Just directly play the current video at the current index
        PlayCurrentVideo();
    }
    
    // Add a method to handle the case where we're stuck in changing state
    public void ResetChangingState()
    {
        Debug.Log("[VideoURLProvider] Resetting changing state flag");
        _isChangingVideo = false;
        
        // If we have videos, try to continue playback
        if (_activeUrlIndices.Length > 0)
        {
            // Just retry playing the current video rather than trying to advance
            Debug.Log($"[VideoURLProvider] Force retry of current video at index {_currentIndex}");
            
            // Set the URL again
            SetCurrentVideoUrl();
            
            // Get the predefined URL for this index
            int urlIndex = _activeUrlIndices[_currentIndex];
            if (urlIndex < predefinedUrls.Length && predefinedUrls[urlIndex] != null)
            {
                VRCUrl currentUrl = predefinedUrls[urlIndex];
                
                // Try to play directly with the video player
                if (videoPlayer != null && _isVideoPlayerReady)
                {
                    Debug.Log($"[VideoURLProvider] Emergency direct play of current video: {currentUrl.Get()}");
                    videoPlayer.PlayURL(currentUrl);
                }
                
                // Also update input field
                if (urlInputField != null)
                {
                    Debug.Log($"[VideoURLProvider] Emergency direct set URL in input field: {currentUrl.Get()}");
                    urlInputField.SetUrl(currentUrl);
                    TriggerInputFieldSubmit();
                }
                
                // Schedule auto-advance only after a longer delay to ensure this video gets a chance to play
                if (autoAdvance)
                {
                    Debug.Log("[VideoURLProvider] Scheduling safety auto-advance in 30 seconds");
                    SendCustomEventDelayedSeconds(nameof(NextVideo), 30f);
                }
            }
        }
    }
    
    // New method to remove duplicate videos from the playlist
    private void RebuildUniquePlaylist()
    {
        if (_activeUrlIndices.Length == 0) return;
        
        // Track which URL indices we've already added
        bool[] added = new bool[predefinedUrls.Length];
        
        // First count how many unique items we'll have
        int uniqueCount = 0;
        for (int i = 0; i < _activeUrlIndices.Length; i++)
        {
            int urlIndex = _activeUrlIndices[i];
            
            // If we haven't counted this index yet, count it
            if (!added[urlIndex])
            {
                uniqueCount++;
                added[urlIndex] = true;
            }
        }
        
        // Reset the added array for reuse
        for (int i = 0; i < added.Length; i++)
        {
            added[i] = false;
        }
        
        // Create new arrays with the correct size
        int[] newIndices = new int[uniqueCount];
        string[] newCaptions = new string[uniqueCount];
        
        // Fill the arrays with unique items
        int newIndex = 0;
        for (int i = 0; i < _activeUrlIndices.Length; i++)
        {
            int urlIndex = _activeUrlIndices[i];
            
            // Skip this index if we've already added it
            if (added[urlIndex]) continue;
            
            // Add this index to the new playlist
            newIndices[newIndex] = urlIndex;
            newCaptions[newIndex] = _captions[i];
            added[urlIndex] = true;
            newIndex++;
        }
        
        // Update the playlist
        _activeUrlIndices = newIndices;
        _captions = newCaptions;
        
        // Adjust current index if needed
        if (_currentIndex >= _activeUrlIndices.Length)
        {
            _currentIndex = 0;
        }
        
        // Debug the rebuilt playlist
        Debug.Log($"[VideoURLProvider] Rebuilt playlist with {_activeUrlIndices.Length} unique videos");
        
        // Start playback from the beginning if we haven't started yet
        if (!_hasStartedPlaylist && _activeUrlIndices.Length > 0)
        {
            _currentIndex = 0;
            StartPlaylist();
            _hasStartedPlaylist = true;
        }
    }
    
    // Helper method to trigger the InputField's submission as if Enter was pressed
    public void TriggerInputFieldSubmit()
    {
        if (urlInputField == null) return;
        
        // Access the gameObject of the input field
        GameObject inputFieldObject = urlInputField.gameObject;
        if (inputFieldObject == null) return;
        
        Debug.Log("[VideoURLProvider] Attempting URL input field submission now");
        
        // Since we can't directly invoke button.onClick in UdonSharp, we'll try other approaches
        
        // Get the current URL from the input field and try to play it directly with the video player
        if (videoPlayer == null)
        {
            Debug.Log("[VideoURLProvider] No video player available for direct submission");
            return;
        }
        
            // Check if it's a VRCUnityVideoPlayer
            if (videoPlayer.GetType() == typeof(VRCUnityVideoPlayer))
            {
            // Get the current URL from the input field
            VRCUrl currentUrl = urlInputField.GetUrl();
            
            // Make sure the URL is valid
            if (currentUrl == null)
            {
                Debug.Log("[VideoURLProvider] URL from input field is null");
                return;
            }
            
            if (string.IsNullOrEmpty(currentUrl.Get()))
            {
                Debug.Log("[VideoURLProvider] URL from input field is empty");
                return;
            }
            
            Debug.Log("[VideoURLProvider] Using video player to directly play URL from input field");
            VRCUnityVideoPlayer unityPlayer = (VRCUnityVideoPlayer)videoPlayer;
            unityPlayer.PlayURL(currentUrl);
        }
        else if (videoPlayer.GetType() == typeof(VRCAVProVideoPlayer))
        {
            // Get the current URL from the input field
            VRCUrl currentUrl = urlInputField.GetUrl();
            
            // Make sure the URL is valid
            if (currentUrl == null || string.IsNullOrEmpty(currentUrl.Get()))
            {
                Debug.Log("[VideoURLProvider] URL from input field is invalid");
            return;
        }
        
            Debug.Log("[VideoURLProvider] Using AVPro video player to directly play URL from input field");
            VRCAVProVideoPlayer avproPlayer = (VRCAVProVideoPlayer)videoPlayer;
            avproPlayer.PlayURL(currentUrl);
        }
        
        // In VRChat, direct manipulation of the UI system is limited in UdonSharp
        // We'll rely on the VRCVideoPlayer's auto-play setting being enabled
        Debug.Log("[VideoURLProvider] Relying on auto-play setting of the video player");
    }
    
    public void PreviousVideo()
    {
        // Cancel any pending next video events
        CancelPendingEvents();
        
        if (_activeUrlIndices.Length == 0) return;
        
        if (_isChangingVideo) return;
        
        _currentIndex = (_currentIndex - 1 + _activeUrlIndices.Length) % _activeUrlIndices.Length;
        PlayCurrentVideo();
    }
    
    public void CancelPendingEvents()
    {
        // This intentionally empty method will cancel all pending events
    }
    
    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        string urlList = result.Result;
        
        // Check if we've already processed this exact list
        if (urlList == _lastLoadedUrlList)
        {
            Debug.Log("[VideoURLProvider] CDN list unchanged, no updates needed");
            return;
        }
        
        _lastLoadedUrlList = urlList;
        
        // Split the file into lines
        string[] lines = urlList.Split('\n');
        
        bool foundNewVideos = false;
        int newVideosProcessed = 0;
        
        Debug.Log($"[VideoURLProvider] Processing {lines.Length} lines from URL list");
        
        // Process each line (URL)
        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#")) continue;
            
            string urlStr = ExtractUrlFromLine(trimmedLine);
            if (string.IsNullOrEmpty(urlStr)) continue;
            
            // Find matching slot in predefined URLs
            int matchingUrlIndex = FindMatchingUrlIndex(urlStr);
            
            // Check the return value appropriately
            if (matchingUrlIndex >= 0)
                {
                    // Extract caption if available
                    string caption = ExtractCaptionFromLine(trimmedLine);
                    if (string.IsNullOrEmpty(caption))
                    {
                        caption = defaultCaption;
                    }
                    
                    // Add this URL index to our active list
                    AddUrlIndex(matchingUrlIndex, caption);
                    foundNewVideos = true;
                    newVideosProcessed++;
                    Debug.Log($"[VideoURLProvider] Added new video: {urlStr} (index: {matchingUrlIndex})");
                }
            else if (matchingUrlIndex == -1)
            {
                // Skip this URL as it was determined to be a duplicate by FindMatchingUrlIndex
                Debug.Log($"[VideoURLProvider] Skipping duplicate URL: {urlStr}");
            }
            else
            {
                Debug.LogWarning($"[VideoURLProvider] No matching predefined URL found for: {urlStr}");
            }
        }
        
        // If we found new videos
        if (foundNewVideos)
        {
            Debug.Log($"[VideoURLProvider] Found {newVideosProcessed} new videos. Continuing current playlist.");
            
            // Make sure we don't have any duplicate videos in the playlist
            RebuildUniquePlaylist();
            
            // If we haven't started the playlist yet and we have videos, start it now
            if (!_hasStartedPlaylist && _activeUrlIndices.Length > 0 && !_isChangingVideo)
            {
                CancelPendingEvents();
                StartPlaylist();
                _hasStartedPlaylist = true;
            }
        }
        
        // If we've exceeded the maximum, trim the oldest videos
        if (_activeUrlIndices.Length > maxVideoCount)
        {
            int countToRemove = _activeUrlIndices.Length - maxVideoCount;
            TrimOldestUrls(countToRemove);
        }
    }
    
    private int FindMatchingUrlIndex(string urlToFind)
    {
        // Exit early if we don't have predefined URLs
        if (predefinedUrls == null || predefinedUrls.Length == 0)
        {
            Debug.LogError("[VideoURLProvider] No predefined URLs available!");
            return -2; // Changed to -2 to distinguish from duplicate case
        }
        
        // First try to find an exact match in predefined URLs
        for (int i = 0; i < predefinedUrls.Length; i++)
        {
            if (predefinedUrls[i] != null && predefinedUrls[i].Get() == urlToFind)
            {
                // Check if this URL index is already in our active indices
                for (int j = 0; j < _activeUrlIndices.Length; j++)
                {
                    if (_activeUrlIndices[j] == i)
                    {
                        Debug.Log($"[VideoURLProvider] URL already exists in playlist at index {j}, skipping duplicate: {urlToFind}");
                        return -1; // Return -1 to indicate we should skip adding this duplicate
                    }
                }
                
                // If not already in playlist, return this index
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
                // Check if this URL is already used
                string newUrl = predefinedUrls[index].Get();
                for (int j = 0; j < _activeUrlIndices.Length; j++)
                {
                    int existingIndex = _activeUrlIndices[j];
                    if (existingIndex < predefinedUrls.Length && predefinedUrls[existingIndex] != null)
                    {
                        if (predefinedUrls[existingIndex].Get() == newUrl)
                        {
                            Debug.Log($"[VideoURLProvider] URL already exists in playlist, skipping duplicate: {newUrl}");
                            return -1; // Skip adding duplicate
                        }
                    }
                }
                
                return index;
            }
        }
        
        // Last resort: find any available predefined URL
        // REMOVED for security: preventing content spoofing.
        // If the URL is not found in the predefined list, we should NOT display a random video.
        
        return -2; // No suitable index found
    }
    
    private void AddUrlIndex(int urlIndex, string caption)
    {
        // Extend arrays
        int[] newIndices = new int[_activeUrlIndices.Length + 1];
        string[] newCaptions = new string[_captions.Length + 1];
        
        // Copy existing data
        for (int i = 0; i < _activeUrlIndices.Length; i++)
        {
            newIndices[i] = _activeUrlIndices[i];
            newCaptions[i] = _captions[i];
        }
        
        // Add new data
        newIndices[_activeUrlIndices.Length] = urlIndex;
        newCaptions[_captions.Length] = caption;
        
        // Update arrays
        _activeUrlIndices = newIndices;
        _captions = newCaptions;
        
        Debug.Log($"[VideoURLProvider] Added new video (index: {_activeUrlIndices.Length-1}, URL index: {urlIndex})");
    }
    
    private void TrimOldestUrls(int countToRemove)
    {
        if (countToRemove <= 0 || countToRemove >= _activeUrlIndices.Length) return;
        
        int newLength = _activeUrlIndices.Length - countToRemove;
        int[] newIndices = new int[newLength];
        string[] newCaptions = new string[newLength];
        
        // Copy the newest entries (skip the oldest)
        for (int i = 0; i < newLength; i++)
        {
            newIndices[i] = _activeUrlIndices[i + countToRemove];
            newCaptions[i] = _captions[i + countToRemove];
        }
        
        // Update our arrays
        _activeUrlIndices = newIndices;
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
        
        Debug.Log($"[VideoURLProvider] Trimmed {countToRemove} oldest videos, new count: {_activeUrlIndices.Length}");
    }
    
    private string ExtractUrlFromLine(string line)
    {
        string url = "";

        // Direct URL format
        if (line.StartsWith("http"))
        {
            url = line;
        }
        
        // Title: URL format
        else if (line.Contains(":"))
        {
            int colonPos = line.IndexOf(":");
            if (colonPos >= 0 && colonPos < line.Length - 1)
            {
                string afterColon = line.Substring(colonPos + 1).Trim();
                if (afterColon.StartsWith("http"))
                {
                    url = afterColon;
                }
            }
        }
        
        // Find any URL in the line
        if (string.IsNullOrEmpty(url))
        {
            int httpIndex = line.IndexOf("http");
            if (httpIndex >= 0)
            {
                string substr = line.Substring(httpIndex);
                // Attempt to find the end of the URL by looking for whitespace
                int spaceIndex = substr.IndexOf(' ');
                url = spaceIndex > 0 ? substr.Substring(0, spaceIndex) : substr;
            }
        }

        // Security enhancement: Enforce HTTPS
        // Automatically upgrade http:// to https:// to prevent mixed content/insecure loads
        if (!string.IsNullOrEmpty(url) && url.StartsWith("http://"))
        {
            url = "https://" + url.Substring(7);
        }
        
        return url;
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
                    int parsedNumber;
                    if (int.TryParse(numPart, out parsedNumber))
                    {
                        captionText = captionText.Substring(dotIndex + 1).Trim();
                    }
                }
                return captionText;
            }
        }
        
        return "";
    }
    
    public override void OnStringLoadError(IVRCStringDownload result)
    {
        Debug.LogError($"[VideoURLProvider] Failed to load URL list: {result.Error}");
    }
    
    // Called when video ends - can be connected to video player events
    public void OnVideoEnd()
    {
        Debug.Log("[VideoURLProvider] OnVideoEnd called - video ended naturally");
        _isCurrentlyPlaying = false;
        _lastPlayingStateChange = Time.time;
        _isChangingVideo = false; // Ensure state is reset
        
        // Advance to next video only if auto-advance is enabled
        if (autoAdvance)
        {
            Debug.Log("[VideoURLProvider] Auto-advancing to next video after end event");
            
            // Cancel any existing auto-advance timers first
            CancelPendingEvents();
            
            // Use a very short delay for more reliable advancement
            SendCustomEventDelayedSeconds(nameof(NextVideo), 0.2f);
        }
    }
    
    // Handle video errors - can also indicate video ended successfully in some cases
    public void HandleVideoError(VRC.SDK3.Components.Video.VideoError videoError)
    {
        Debug.Log($"[VideoURLProvider] HandleVideoError called with error: {videoError}");
        
        // Cancel any pending attempts
                CancelPendingEvents();
                
        // Update playing state
        _isCurrentlyPlaying = false;
        _lastPlayingStateChange = Time.time;
        _isChangingVideo = false; // Ensure state is reset
        _playAttemptScheduled = false;
        
        // Get the current URL that caused the error
        string currentUrlString = "";
        if (_activeUrlIndices.Length > 0 && _currentIndex < _activeUrlIndices.Length)
        {
            int urlIndex = _activeUrlIndices[_currentIndex];
            if (urlIndex < predefinedUrls.Length && predefinedUrls[urlIndex] != null)
            {
                currentUrlString = predefinedUrls[urlIndex].Get();
            }
        }
        
        // Add this URL to failed URLs list if it's not empty
        if (!string.IsNullOrEmpty(currentUrlString))
        {
            AddToFailedUrls(currentUrlString);
        }
        
        // For certain errors, try to advance to next video after a slight delay
        if (autoAdvance && videoError != VRC.SDK3.Components.Video.VideoError.RateLimited)
        {
            Debug.Log("[VideoURLProvider] Scheduling advance to next video due to error");
            // Use a delay for more reliable advancement
                SendCustomEventDelayedSeconds(nameof(NextVideo), 1.0f);
        }
    }
    
    // Called when the video player successfully loads and begins playing
    public void HandleVideoPlay(VRC.SDK3.Components.Video.VideoError videoError) 
    {
        Debug.Log("[VideoURLProvider] HandleVideoPlay called with error: " + videoError);
        
        // Cancel any pending retries to avoid duplicate playback
        CancelPendingEvents();
        
        // Video playback started successfully
        if (videoError == VRC.SDK3.Components.Video.VideoError.Unknown)
        {
            Debug.Log("[VideoURLProvider] Video playback started successfully");
            _isVideoPlayerReady = true;
            _isCurrentlyPlaying = true;
            _lastPlayingStateChange = Time.time;
            _lastPlayAttemptTime = Time.time; // Update this to prevent immediate retries
            _isChangingVideo = false; // Reset state flag
            _playAttemptScheduled = false; // Clear any scheduled attempts
            
            // Always schedule the next video check if auto-advance is enabled
            // This ensures videos advance even if Use Max Video Playtime is disabled
            if (autoAdvance)
            {
                float advanceTime = maxVideoPlaytime;
                Debug.Log($"[VideoURLProvider] Scheduling next video check in {advanceTime} seconds");
                SendCustomEventDelayedSeconds(nameof(NextVideo), advanceTime);
                
                // Also schedule a backup check at 75% of the advance time
                float backupCheckTime = advanceTime * 0.75f;
                SendCustomEventDelayedSeconds(nameof(CheckVideoProgress), backupCheckTime);
            }
        }
    }
    
    // Method to check video progress and ensure advancement
    public void CheckVideoProgress()
    {
        // Only proceed if auto-advance is enabled
        if (!autoAdvance) return;
        
        // If we're currently changing videos, don't interfere
        if (_isChangingVideo)
        {
            // But check if we've been stuck in this state too long
            float stuckTime = Time.time - _lastChangeTime;
            if (stuckTime > 10f)
            {
                // Force reset the changing state
                _isChangingVideo = false;
                
                // Force the next video to play after a small delay
                SendCustomEventDelayedSeconds(nameof(NextVideo), 0.5f);
            }
            return;
        }
        
        // Check if the video has been playing properly
        float timeSinceStateChange = Time.time - _lastPlayingStateChange;
        
        // If the video is not playing or hasn't changed state in a long time
        if (!_isCurrentlyPlaying || timeSinceStateChange > 30f)
        {
            // Force advancement to the next video
            NextVideo();
        }
    }
    
    // Clear all predefined URLs in one click
    public void ClearPredefinedUrls()
    {
        Debug.Log("[VideoURLProvider] Clearing all predefined URLs");
        
        // Reset active indices
        _activeUrlIndices = new int[0];
        _captions = new string[0];
        _currentIndex = 0;
        
        // Cancel any pending slideshow events
        CancelPendingEvents();
        
        Debug.Log("[VideoURLProvider] All predefined URLs have been cleared");
    }
    
    // Clear older URLs from memory and reset to start fresh
    public void ClearOlderUrls()
    {
        Debug.Log("[VideoURLProvider] Clearing older URLs from memory");
        
        // Keep only the current URL if there is one
        if (_activeUrlIndices.Length > 0 && _currentIndex >= 0 && _currentIndex < _activeUrlIndices.Length)
        {
            int currentUrlIndex = _activeUrlIndices[_currentIndex];
            string currentCaption = _captions[_currentIndex];
            
            // Reset to just the current URL
            _activeUrlIndices = new int[1] { currentUrlIndex };
            _captions = new string[1] { currentCaption };
            _currentIndex = 0;
            
            Debug.Log("[VideoURLProvider] Kept only the current URL and cleared all others");
        }
        else
        {
            // If no current URL, clear everything
            _activeUrlIndices = new int[0];
            _captions = new string[0];
            _currentIndex = 0;
            
            Debug.Log("[VideoURLProvider] No current URL, cleared all URLs from memory");
        }
        
        // Reset the last loaded URL list to force a fresh load on next check
        _lastLoadedUrlList = "";
    }
    
    // Additional delayed startup to ensure video starts playing on first join
    public void ExtraDelayedStartup()
    {
        Debug.Log("[VideoURLProvider] Extra delayed startup executing...");
        
        // Print diagnostics about the current video player state
        if (videoPlayer != null)
        {
            string playerTypeName = "Unknown";
            if (videoPlayer.GetType() == typeof(VRCUnityVideoPlayer))
            {
                playerTypeName = "Unity Video Player";
            }
            else if (videoPlayer.GetType() == typeof(VRCAVProVideoPlayer))
            {
                playerTypeName = "AVPro Video Player";
            }
            
            Debug.Log($"[VideoURLProvider] Using {playerTypeName} for video playback");
        }
        else
        {
            Debug.LogWarning("[VideoURLProvider] No video player has been found yet!");
            
            // One last attempt to find a video player
            FindVideoPlayer();
        }
        
        // Only try to start if we haven't already
        if (!_hasStartedPlaylist && _activeUrlIndices.Length > 0 && _isVideoPlayerReady)
        {
            Debug.Log("[VideoURLProvider] Extra delayed startup - attempting to play video");
            
            SetCurrentVideoUrl();
            PlayCurrentVideo();
            _hasStartedPlaylist = true;
        }
    }
    
    // Track failed URLs to avoid repeated retries
    private void AddToFailedUrls(string url)
    {
        // Check if this URL is already in our failed list
        for (int i = 0; i < _failedUrlCount; i++)
        {
            if (_failedUrls[i] == url)
            {
                // Already tracked, no need to add again
                return;
            }
        }
        
        // Add to the list (with circular buffer behavior)
        if (_failedUrlCount < _failedUrls.Length)
        {
            _failedUrls[_failedUrlCount] = url;
            _failedUrlCount++;
        }
        else
        {
            // Shift all entries down one position
            for (int i = 0; i < _failedUrls.Length - 1; i++)
            {
                _failedUrls[i] = _failedUrls[i + 1];
            }
            // Add new entry at the end
            _failedUrls[_failedUrls.Length - 1] = url;
        }
    }
    
    // Check if a URL is in the failed list
    private bool IsFailedUrl(string url)
    {
        for (int i = 0; i < _failedUrlCount; i++)
        {
            if (_failedUrls[i] == url)
            {
                return true;
            }
        }
        return false;
    }
    
    // Add a method to directly force play a specific video index
    public void ForcePlayAt(int index)
    {
        Debug.Log($"[VideoURLProvider] Force playing video at index {index}");
        
        // Make sure index is within bounds
        if (index < 0 || index >= _activeUrlIndices.Length)
        {
            Debug.LogError($"[VideoURLProvider] Cannot force play index {index} - out of bounds (0-{_activeUrlIndices.Length-1})");
            return;
        }
        
        // Only proceed if we're not already playing this index or we haven't played in a while
        if (index == _currentIndex && _isCurrentlyPlaying && Time.time - _lastPlayAttemptTime < 3f)
        {
            Debug.Log($"[VideoURLProvider] Already playing index {index}, skipping duplicate play");
            return;
        }
        
        // Reset all state variables completely
        _isChangingVideo = false;
        _lastChangeTime = Time.time;
        _lastPlayAttemptTime = Time.time;
        _playAttemptScheduled = false;
        CancelPendingEvents();
        
        // Set index and print playlist
        _currentIndex = index;
        Debug.Log($"[VideoURLProvider] Force set current index to {_currentIndex}");
        DebugShowPlaylist();
        
        // Get the URL directly
        int urlIndex = _activeUrlIndices[_currentIndex];
        if (urlIndex >= predefinedUrls.Length || predefinedUrls[urlIndex] == null)
        {
            Debug.LogError($"[VideoURLProvider] Invalid URL index: {urlIndex}");
            return;
        }
        
        VRCUrl currentUrl = predefinedUrls[urlIndex];
        Debug.Log($"[VideoURLProvider] Force playing URL: {currentUrl.Get()}");
        
        // Try to play directly with the video player first
        if (videoPlayer != null)
        {
            Debug.Log("[VideoURLProvider] Playing directly with video player");
            videoPlayer.PlayURL(currentUrl);
        }
        
        // Also update the input field to ensure playback
        if (urlInputField != null)
        {
            Debug.Log("[VideoURLProvider] Setting URL in input field");
            urlInputField.SetUrl(currentUrl);
            TriggerInputFieldSubmit();
        }
        
        // Mark as playing
        _isCurrentlyPlaying = true;
        _lastPlayingStateChange = Time.time;
        
        // Schedule auto-advance to ensure this video has time to play
        if (autoAdvance)
        {
            Debug.Log("[VideoURLProvider] Scheduling auto-advance");
            SendCustomEventDelayedSeconds(nameof(NextVideo), maxVideoPlaytime);
        }
    }

    // Debug method to show the current playlist structure
    public void DebugShowPlaylist()
    {
        Debug.Log($"[VideoURLProvider] Total videos: {_activeUrlIndices.Length}, Current position: {_currentIndex}");
    }

    // Replace DiagnosePlaylistIssues method with empty method to avoid errors if it's referenced elsewhere
    public void DiagnosePlaylistIssues()
    {
        // Empty method to avoid errors
    }

    // Add a method to specifically force play the second video
    public void ForcePlaySecondVideo()
    {
        Debug.Log("[VideoURLProvider] Force playing second video in playlist");
        
        // Make sure we have at least 2 videos
        if (_activeUrlIndices.Length < 2)
        {
            Debug.LogError("[VideoURLProvider] Cannot play second video - playlist has less than 2 videos");
            return;
        }
        
        // Use our strong direct play method to force play the second video
        ForcePlayAt(1);
        
        Debug.Log("[VideoURLProvider] Forced play of second video complete");
    }
} 