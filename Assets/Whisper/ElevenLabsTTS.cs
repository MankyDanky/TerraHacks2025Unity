using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;

[System.Serializable]
public class ElevenLabsRequest
{
    public string text;
    public string model_id = "eleven_flash_v2_5";
    public VoiceSettings voice_settings = new VoiceSettings();
}

[System.Serializable]
public class VoiceSettings
{
    public float stability = 0.7f;        // Higher = more consistent, less emotional variation
    public float similarity_boost = 0.6f; // Higher = closer to original voice character
}

public class ElevenLabsTTS : MonoBehaviour
{
    [Header("ElevenLabs Settings")]
    [SerializeField] private string apiKey = "";
    [SerializeField] private string voiceId = "PASTE_YOUR_VOICE_ID_HERE"; // 🎭 CHANGE THIS to your chosen voice ID
    
    [Header("Audio Settings")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private bool debugMode = true;
    
    [Header("Queue Settings")]
    [SerializeField] private int maxQueueSize = 10;
    
    private Queue<string> speechQueue = new Queue<string>();
    private bool isSpeaking = false;
    
    // Events
    public System.Action<string> OnSpeechStarted;
    public System.Action OnSpeechFinished;
    public System.Action<string> OnError;
    public System.Action<string> OnTextQueued;

    NPC npc;

    void Start()
    {
        npc = FindAnyObjectByType<NPC>();

        // Get API key from environment if not set in inspector
        if (string.IsNullOrEmpty(apiKey))
        {
            apiKey = System.Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                LogDebug("ElevenLabs API key not found. Please set ELEVENLABS_API_KEY in environment or inspector.");
            }
        }
        
        // Create audio source if not assigned
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.volume = 0.8f;
            audioSource.spatialBlend = 0f; // 2D audio
        }
        
        LogDebug($"ElevenLabs TTS initialized with Voice ID: {voiceId}");
    }

    /// <summary>
    /// Add text to the speech queue. Will start speaking immediately if not already speaking.
    /// </summary>
    /// <param name="text">Text to convert to speech</param>
    public void SpeakText(string text)
    {
        if (string.IsNullOrEmpty(text)) 
        {
            LogDebug("Cannot speak empty text");
            return;
        }
        
        // Trim and clean the text
        text = text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        
        // Check queue size limit
        if (speechQueue.Count >= maxQueueSize)
        {
            LogDebug($"Speech queue full ({maxQueueSize} items). Skipping: {text}");
            OnError?.Invoke("Speech queue is full");
            return;
        }
        
        speechQueue.Enqueue(text);
        OnTextQueued?.Invoke(text);
        LogDebug($"Queued for speech: {text}");
        
        // Start processing if not already speaking
        if (!isSpeaking)
        {
            StartCoroutine(ProcessSpeechQueue());
        }
    }

    /// <summary>
    /// Stop all speech and clear the queue
    /// </summary>
    public void StopSpeaking()
    {
        LogDebug("Stopping all speech");
        StopAllCoroutines();
        audioSource.Stop();
        speechQueue.Clear();
        isSpeaking = false;
        OnSpeechFinished?.Invoke();
    }

    /// <summary>
    /// Clear the speech queue but allow current speech to finish
    /// </summary>
    public void ClearQueue()
    {
        speechQueue.Clear();
        LogDebug("Speech queue cleared");
    }

    /// <summary>
    /// Get the current queue size
    /// </summary>
    public int GetQueueSize()
    {
        return speechQueue.Count;
    }

    /// <summary>
    /// Check if currently speaking
    /// </summary>
    public bool IsSpeaking()
    {
        return isSpeaking;
    }

    private IEnumerator ProcessSpeechQueue()
    {
        isSpeaking = true;
        LogDebug("Starting speech queue processing");
        
        while (speechQueue.Count > 0)
        {
            string textToSpeak = speechQueue.Dequeue();
            yield return StartCoroutine(SpeakTextInternal(textToSpeak));
            
            // Small delay between speech segments for natural flow
            yield return new WaitForSeconds(0.1f);
        }
        
        isSpeaking = false;
        LogDebug("Speech queue processing complete");
        OnSpeechFinished?.Invoke();
    }

    private IEnumerator SpeakTextInternal(string text)
    {
        OnSpeechStarted?.Invoke(text);
        LogDebug($"Starting TTS for: {text}");
        
        // Check if API key is available
        if (string.IsNullOrEmpty(apiKey))
        {
            LogDebug("No API key available, using test audio");
            yield return StartCoroutine(PlayTestAudio(text));
            yield break;
        }
        
        // Create request payload
        ElevenLabsRequest request = new ElevenLabsRequest
        {
            text = text
        };
        
        string jsonData = JsonUtility.ToJson(request);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
        
        // Create web request with PCM format for easier Unity handling
        string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}?output_format=pcm_16000";
        
        using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
        {
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("xi-api-key", apiKey);
            webRequest.timeout = 30; // 30 second timeout
            
            LogDebug($"Sending TTS request to ElevenLabs for: {text.Substring(0, Math.Min(50, text.Length))}...");
            
            yield return webRequest.SendWebRequest();
            
            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                LogDebug($"TTS request successful, received {webRequest.downloadHandler.data.Length} bytes");
                
                // Convert audio data to AudioClip
                byte[] audioData = webRequest.downloadHandler.data;
                AudioClip audioClip = ConvertBytesToAudioClip(audioData);

                if (audioClip != null)
                {
                    // Play the audio
                    audioSource.clip = audioClip;
                    audioSource.Play();

                    LogDebug($"Playing TTS audio, duration: {audioClip.length:F2}s");
                    npc.StartTalking();
                    // Wait for audio to finish playing
                    yield return new WaitForSeconds(audioClip.length);
                    npc.StopTalking();
                }
                else
                {
                    string error = "Failed to create AudioClip from ElevenLabs response";
                    LogDebug(error);
                    OnError?.Invoke(error);
                }
            }
            else
            {
                string error = $"ElevenLabs TTS Error: {webRequest.error}\nResponse: {webRequest.downloadHandler.text}";
                LogDebug(error);
                OnError?.Invoke(error);
                
                // Fallback to test audio on error
                yield return StartCoroutine(PlayTestAudio(text));
            }
        }
    }

    /// <summary>
    /// Convert PCM audio bytes from ElevenLabs to Unity AudioClip
    /// </summary>
    private AudioClip ConvertBytesToAudioClip(byte[] audioData)
    {
        try
        {
            // ElevenLabs PCM format: 16-bit, 16kHz, mono
            int sampleCount = audioData.Length / 2; // 16-bit = 2 bytes per sample
            float[] samples = new float[sampleCount];
            
            // Convert 16-bit PCM to float samples
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(audioData, i * 2);
                samples[i] = sample / 32768.0f; // Convert to float range [-1, 1]
            }
            
            // Create AudioClip
            AudioClip clip = AudioClip.Create("ElevenLabs_TTS", sampleCount, 1, 16000, false);
            clip.SetData(samples, 0);
            
            LogDebug($"Successfully created AudioClip: {clip.length:F2}s, {sampleCount} samples");
            return clip;
        }
        catch (System.Exception e)
        {
            LogDebug($"Error converting audio bytes: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Play a test beep sound when API is not available
    /// </summary>
    private IEnumerator PlayTestAudio(string text)
    {
        LogDebug($"Playing test audio for: {text}");
        
        // Generate a simple test tone
        int sampleRate = 8000;
        float duration = Mathf.Min(2.0f, text.Length * 0.1f); // Variable duration based on text length
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        
        float[] samples = new float[sampleCount];
        
        // Generate a pleasant tone sequence
        for (int i = 0; i < sampleCount; i++)
        {
            float time = (float)i / sampleRate;
            float frequency = 440.0f + Mathf.Sin(time * 2.0f) * 100.0f; // Varying frequency
            samples[i] = Mathf.Sin(2.0f * Mathf.PI * frequency * time) * 0.1f;
        }
        
        AudioClip testClip = AudioClip.Create("Test_TTS", sampleCount, 1, sampleRate, false);
        testClip.SetData(samples, 0);
        
        audioSource.clip = testClip;
        audioSource.Play();
        
        yield return new WaitForSeconds(duration);
    }

    private void LogDebug(string message)
    {
        if (debugMode)
        {
            Debug.Log($"[ElevenLabsTTS] {message}");
        }
    }

    private void OnDestroy()
    {
        StopSpeaking();
    }

    // Editor helper methods
    #if UNITY_EDITOR
    [ContextMenu("Test TTS")]
    public void TestTTS()
    {
        SpeakText("This is a test of the ElevenLabs text to speech system.");
    }

    [ContextMenu("Stop All Speech")]
    public void EditorStopSpeech()
    {
        StopSpeaking();
    }
    #endif
}