using UnityEngine;
using UnityEngine.UI;
using TMPro; // Add TextMeshPro support

public class VoiceCommandExample : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WhisperManager whisperManager;
    [SerializeField] private TextMeshProUGUI commandText; // Changed to TextMeshPro
    
    void Start()
    {
        if (whisperManager == null)
        {
            whisperManager = FindObjectOfType<WhisperManager>();
        }
        
        // Subscribe to events
        whisperManager.OnTranscriptionReceived += HandleTranscription;
        whisperManager.OnVoiceChatReceived += HandleVoiceChat;
        whisperManager.OnError += HandleError;
    }
    
    void HandleTranscription(string transcription)
    {
        Debug.Log($"Received transcription: {transcription}");
        
        if (commandText != null)
        {
            commandText.text = $"You said: {transcription}";
        }
    }
    
    void HandleVoiceChat(string transcript, string aiResponse)
    {
        Debug.Log($"Voice Chat - You: {transcript}, AI: {aiResponse}");
        
        if (commandText != null)
        {
            commandText.text = $"You: {transcript}\n\nAI Character: {aiResponse}";
        }
    }
    
    void HandleError(string error)
    {
        Debug.LogError($"Whisper error: {error}");
        if (commandText != null)
        {
            commandText.text = $"Error: {error}";
        }
    }
    
    void OnDestroy()
    {
        if (whisperManager != null)
        {
            whisperManager.OnTranscriptionReceived -= HandleTranscription;
            whisperManager.OnVoiceChatReceived -= HandleVoiceChat;
            whisperManager.OnError -= HandleError;
        }
    }
} 