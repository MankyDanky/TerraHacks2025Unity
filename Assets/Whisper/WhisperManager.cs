using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using System;
using TMPro; // Add TextMeshPro support

[System.Serializable]
public class WhisperResponse
{
    public string text;
    public string language;
    public Segment[] segments;
}

[System.Serializable]
public class VoiceChatResponse
{
    public string transcript;
    public string ai_response;
    public string language;
}

[System.Serializable]
public class Segment
{
    public float start;
    public float end;
    public string text;
}

[System.Serializable]
public class StreamingData
{
    public string type;
    public string content;
}

public class WhisperManager : MonoBehaviour
{
    [Header("Server Settings")]
    [SerializeField] private string serverUrl = "http://localhost:5000";
    [SerializeField] private bool autoStartRecording = false;
    [SerializeField] private bool useVoiceChat = true; // Use new voice chat endpoint
    
    [Header("Audio Settings")]
    [SerializeField] private int sampleRate = 16000;
    [SerializeField] private int maxRecordingLength = 30; // seconds
    
    [Header("AI Character Settings")]
    [SerializeField] private string systemPrompt = "You are a large, weathered rhinoceros living in the African savanna grasslands. You are naturally wary and cautious around humans, but will reluctantly share your knowledge about your habitat. Speak with a gruff, weary tone as if you've seen too much environmental destruction. Keep responses conversational and under 3 sentences. You know about droughts, poaching, habitat loss, climate change, and how they affect rhinos and other savanna species.";
    
    [Header("Streaming Settings")]
    [SerializeField] private bool useStreaming = true;
    [SerializeField] private ElevenLabsTTS ttsManager;
    
    [Header("UI References")]
    [SerializeField] private UnityEngine.UI.Button recordButton;
    [SerializeField] private TextMeshProUGUI statusText; // Changed to TextMeshPro
    [SerializeField] private TextMeshProUGUI transcriptionText; // Changed to TextMeshPro
    
    private AudioClip recordingClip;
    private bool isRecording = false;
    private string microphoneDevice;
    private Coroutine recordingCoroutine;
    
    // Events
    public System.Action<string> OnTranscriptionReceived; // Still available for transcribe-only mode
    public System.Action<string, string> OnVoiceChatReceived; // (transcript, ai_response)
    public System.Action<string> OnError;
    
    // Streaming events
    public System.Action<string> OnStreamingTextReceived;
    public System.Action OnStreamingComplete;

    NPC npc;

    void Start()
    {
        // Initialize microphone
        if (Microphone.devices.Length > 0)
        {
            microphoneDevice = Microphone.devices[0];
            Debug.Log($"Using microphone: {microphoneDevice}");
        }
        else
        {
            Debug.LogError("No microphone found!");
            return;
        }

        // Setup UI
        if (recordButton != null)
        {
            recordButton.onClick.AddListener(ToggleRecording);
        }

        // Test server connection
        StartCoroutine(TestServerConnection());

        if (autoStartRecording)
        {
            StartRecording();
        }

        npc = FindAnyObjectByType<NPC>();
    }
    
    public void ToggleRecording()
    {
        if (isRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }
    
    public void StartRecording()
    {
        if (isRecording) return;
        
        isRecording = true;
        recordingClip = Microphone.Start(microphoneDevice, false, maxRecordingLength, sampleRate);
        recordingCoroutine = StartCoroutine(RecordingCoroutine());
        
        UpdateUI("Recording...");
        Debug.Log("Started recording");
    }
    
    public void StopRecording()
    {
        if (!isRecording) return;
        
        isRecording = false;
        
        if (recordingCoroutine != null)
        {
            StopCoroutine(recordingCoroutine);
        }
        
        // Stop microphone and get the actual length
        int recordedLength = Microphone.GetPosition(microphoneDevice);
        Microphone.End(microphoneDevice);
        
        if (recordedLength > 0)
        {
            // Create a new clip with the actual recorded length
            AudioClip trimmedClip = AudioClip.Create("Recording", recordedLength, 1, sampleRate, false);
            float[] samples = new float[recordedLength];
            recordingClip.GetData(samples, 0);
            trimmedClip.SetData(samples, 0);
            
            StartCoroutine(SendAudioToServer(trimmedClip));
        }
        else
        {
            UpdateUI("No audio recorded");
            OnError?.Invoke("No audio was recorded");
        }
        
        Debug.Log("Stopped recording");
    }
    
    private IEnumerator RecordingCoroutine()
    {
        float startTime = Time.time;
        
        while (isRecording && (Time.time - startTime) < maxRecordingLength)
        {
            yield return null;
        }
        
        if (isRecording)
        {
            StopRecording();
        }
    }
    
    private IEnumerator SendAudioToServer(AudioClip audioClip)
    {
        if (useStreaming)
        {
            yield return StartCoroutine(SendStreamingVoiceChatRequest(audioClip));
        }
        else if (useVoiceChat)
        {
            yield return StartCoroutine(SendVoiceChatRequest(audioClip));
        }
        else
        {
            yield return StartCoroutine(SendTranscribeRequest(audioClip));
        }
    }
    
    private IEnumerator SendVoiceChatRequest(AudioClip audioClip)
    {
        UpdateUI("Processing audio and generating AI response...");
        
        // Convert AudioClip to WAV bytes
        byte[] audioData = AudioClipToWAV(audioClip);
        
        // Create form data
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", audioData, "recording.wav", "audio/wav");
        
        // Add system prompt if specified
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            form.AddField("system_prompt", systemPrompt);
        }
        
        // Send request to voice-chat endpoint
        using (UnityWebRequest request = UnityWebRequest.Post($"{serverUrl}/voice-chat", form))
        {
            request.timeout = 45; // Longer timeout for AI processing
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    VoiceChatResponse response = JsonUtility.FromJson<VoiceChatResponse>(request.downloadHandler.text);
                    string transcript = response.transcript.Trim();
                    string aiResponse = response.ai_response.Trim();
                    
                    UpdateUI($"AI: {aiResponse}");
                    if (transcriptionText != null)
                    {
                        transcriptionText.text = $"You: {transcript}\nAI: {aiResponse}";
                    }
                    
                    OnVoiceChatReceived?.Invoke(transcript, aiResponse);
                    Debug.Log($"Voice chat - You: {transcript}, AI: {aiResponse}");
                }
                catch (Exception e)
                {
                    string error = $"Failed to parse voice chat response: {e.Message}";
                    UpdateUI(error);
                    OnError?.Invoke(error);
                    Debug.LogError(error);
                }
            }
            else
            {
                string error = $"Voice chat server error: {request.error}";
                UpdateUI(error);
                OnError?.Invoke(error);
                Debug.LogError(error);
            }
        }
    }
    
    private IEnumerator SendStreamingVoiceChatRequest(AudioClip audioClip)
    {
        UpdateUI("Processing audio and generating streaming AI response...");
        
        // Convert AudioClip to WAV bytes
        byte[] audioData = AudioClipToWAV(audioClip);
        
        // Create form data
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", audioData, "recording.wav", "audio/wav");
        
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            form.AddField("system_prompt", systemPrompt);
        }
        
        // Send request to streaming endpoint
        using (UnityWebRequest request = UnityWebRequest.Post($"{serverUrl}/voice-chat-stream", form))
        {
            request.timeout = 60; // Longer timeout for streaming
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                // Process streaming response
                string[] lines = request.downloadHandler.text.Split('\n');
                
                foreach (string line in lines)
                {
                    if (line.StartsWith("data: "))
                    {
                        string jsonData = line.Substring(6); // Remove "data: "
                        if (!string.IsNullOrEmpty(jsonData))
                        {
                            ProcessStreamingData(jsonData);
                        }
                    }
                }
            }
            else
            {
                string error = $"Streaming request failed: {request.error}";
                UpdateUI(error);
                OnError?.Invoke(error);
            }
        }
    }
    
    private void ProcessStreamingData(string jsonData)
    {
        try
        {
            var data = JsonUtility.FromJson<StreamingData>(jsonData);
            
            switch (data.type)
            {
                case "transcript":
                    Debug.Log($"Received transcript: {data.content}");
                    if (transcriptionText != null)
                    {
                        transcriptionText.text = $"You: {data.content}";
                    }
                    break;
                    
                case "text":
                    Debug.Log($"Received AI text: {data.content}");
                    OnStreamingTextReceived?.Invoke(data.content);
                    
                    // Send to TTS if available
                    if (ttsManager != null)
                    {
                        ttsManager.SpeakText(data.content);
                    }
                    
                    // Update UI
                    if (transcriptionText != null)
                    {
                        transcriptionText.text += $"\nAI: {data.content}";
                    }
                    break;
                    
                case "complete":
                    Debug.Log("Streaming complete");
                    OnStreamingComplete?.Invoke();
                    UpdateUI("Response complete");
                    break;
                    
                case "error":
                    Debug.LogError($"Streaming error: {data.content}");
                    OnError?.Invoke(data.content);
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to process streaming data: {e.Message}");
        }
    }
    
    private IEnumerator SendTranscribeRequest(AudioClip audioClip)
    {
        UpdateUI("Processing audio...");
        
        // Convert AudioClip to WAV bytes
        byte[] audioData = AudioClipToWAV(audioClip);
        
        // Create form data
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", audioData, "recording.wav", "audio/wav");
        
        // Send request to transcribe endpoint
        using (UnityWebRequest request = UnityWebRequest.Post($"{serverUrl}/transcribe", form))
        {
            request.timeout = 30; // 30 second timeout
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    WhisperResponse response = JsonUtility.FromJson<WhisperResponse>(request.downloadHandler.text);
                    string transcription = response.text.Trim();
                    
                    UpdateUI($"Transcription: {transcription}");
                    if (transcriptionText != null)
                    {
                        transcriptionText.text = transcription;
                    }
                    
                    OnTranscriptionReceived?.Invoke(transcription);
                    Debug.Log($"Transcription received: {transcription}");
                }
                catch (Exception e)
                {
                    string error = $"Failed to parse response: {e.Message}";
                    UpdateUI(error);
                    OnError?.Invoke(error);
                    Debug.LogError(error);
                }
            }
            else
            {
                string error = $"Server error: {request.error}";
                UpdateUI(error);
                OnError?.Invoke(error);
                Debug.LogError(error);
            }
        }
    }
    
    private IEnumerator TestServerConnection()
    {
        UpdateUI("Testing server connection...");
        
        using (UnityWebRequest request = UnityWebRequest.Get($"{serverUrl}/health"))
        {
            request.timeout = 10;
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                UpdateUI("Server connected successfully");
                Debug.Log("Server connection test successful");
            }
            else
            {
                UpdateUI("Server connection failed");
                Debug.LogWarning($"Server connection test failed: {request.error}");
            }
        }
    }
    
    private byte[] AudioClipToWAV(AudioClip clip)
    {
        // Get the audio data
        float[] samples = new float[clip.samples];
        clip.GetData(samples, 0);
        
        // Convert to 16-bit PCM
        byte[] pcmData = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short sample = (short)(samples[i] * 32767f);
            pcmData[i * 2] = (byte)(sample & 0xff);
            pcmData[i * 2 + 1] = (byte)((sample >> 8) & 0xff);
        }
        
        // Create WAV header
        byte[] wavHeader = CreateWAVHeader(pcmData.Length, clip.channels, clip.frequency);
        
        // Combine header and data
        byte[] wavData = new byte[wavHeader.Length + pcmData.Length];
        Array.Copy(wavHeader, 0, wavData, 0, wavHeader.Length);
        Array.Copy(pcmData, 0, wavData, wavHeader.Length, pcmData.Length);
        
        return wavData;
    }
    
    private byte[] CreateWAVHeader(int dataLength, int channels, int sampleRate)
    {
        byte[] header = new byte[44];
        
        // RIFF header
        byte[] riff = System.Text.Encoding.ASCII.GetBytes("RIFF");
        Array.Copy(riff, 0, header, 0, 4);
        
        byte[] chunkSize = BitConverter.GetBytes(36 + dataLength);
        Array.Copy(chunkSize, 0, header, 4, 4);
        
        byte[] wave = System.Text.Encoding.ASCII.GetBytes("WAVE");
        Array.Copy(wave, 0, header, 8, 4);
        
        // Format chunk
        byte[] fmt = System.Text.Encoding.ASCII.GetBytes("fmt ");
        Array.Copy(fmt, 0, header, 12, 4);
        
        byte[] subchunk1Size = BitConverter.GetBytes(16);
        Array.Copy(subchunk1Size, 0, header, 16, 4);
        
        byte[] audioFormat = BitConverter.GetBytes((short)1);  // PCM
        Array.Copy(audioFormat, 0, header, 20, 2);
        
        byte[] numChannels = BitConverter.GetBytes((short)channels);
        Array.Copy(numChannels, 0, header, 22, 2);
        
        byte[] sampleRateBytes = BitConverter.GetBytes(sampleRate);
        Array.Copy(sampleRateBytes, 0, header, 24, 4);
        
        byte[] byteRate = BitConverter.GetBytes(sampleRate * channels * 2);
        Array.Copy(byteRate, 0, header, 28, 4);
        
        byte[] blockAlign = BitConverter.GetBytes((short)(channels * 2));
        Array.Copy(blockAlign, 0, header, 32, 2);
        
        byte[] bitsPerSample = BitConverter.GetBytes((short)16);
        Array.Copy(bitsPerSample, 0, header, 34, 2);
        
        // Data chunk
        byte[] data = System.Text.Encoding.ASCII.GetBytes("data");
        Array.Copy(data, 0, header, 36, 4);
        
        byte[] dataLengthBytes = BitConverter.GetBytes(dataLength);
        Array.Copy(dataLengthBytes, 0, header, 40, 4);
        
        return header;
    }
    
    private void UpdateUI(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log($"WhisperManager: {message}");
    }
    
    void OnDestroy()
    {
        if (isRecording)
        {
            Microphone.End(microphoneDevice);
        }
    }
} 