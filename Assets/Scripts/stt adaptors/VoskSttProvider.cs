using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class VoskSttProvider : SttProviderBase
{
    [Header("Vosk")]
    [SerializeField] private VoskSpeechToText vosk;

    [Header("Text Input Mode")]
    [SerializeField] private bool useTextInput = false;

    [TextArea(1, 3)]
    [SerializeField] private string textInput = "";

    public bool UseTextInput
    {
        get => useTextInput;
        set => useTextInput = value;
    }

    public string TextInput
    {
        get => textInput;
        set => textInput = value;
    }

    public override bool IsRunning
    {
        get
        {
            if (useTextInput)
                return true;

            return vosk != null;
        }
    }

    private void Awake()
    {
        if (vosk == null)
            vosk = GetComponent<VoskSpeechToText>();

        if (vosk != null)
        {
            vosk.OnTranscriptionResult += HandleVoskResult;
            vosk.OnStatusUpdated += RaiseStatus;
        }
    }

    private void OnDestroy()
    {
        if (vosk != null)
        {
            vosk.OnTranscriptionResult -= HandleVoskResult;
            vosk.OnStatusUpdated -= RaiseStatus;
        }
    }

    public override void StartListening()
    {
        if (useTextInput)
        {
            RaiseStatus("Text input mode enabled. Voice input is disabled.");
            return;
        }

        if (vosk == null)
            return;

        // If Vosk is already AutoStart, you may not need to call this.
        // Otherwise, use vosk.StartVoskStt(...) or vosk.ToggleRecording()
        vosk.ToggleRecording();
    }

    public override void StopListening()
    {
        if (useTextInput)
        {
            RaiseStatus("Text input mode enabled. Voice input is disabled.");
            return;
        }

        if (vosk == null)
            return;

        vosk.ToggleRecording();
    }

    public void ConfirmTextInput()
    {
        if (!useTextInput)
        {
            RaiseStatus("Text input mode is disabled.");
            return;
        }

        string normalizedText = NormalizeText(textInput);

        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            RaiseStatus("Text input is empty.");
            return;
        }

        var candidates = new List<SttCandidate>
        {
            new SttCandidate(normalizedText, 1f)
        };

        var result = new SttResult(
            SttSourceType.Vosk,
            normalizedText,
            true,
            textInput
        );

        result.candidates = candidates;

        RaiseResult(result);
        RaiseStatus("Text input submitted: " + normalizedText);
    }

    private void HandleVoskResult(string json)
    {
        if (useTextInput)
            return;

        if (string.IsNullOrWhiteSpace(json))
            return;

        var result = ParseVoskJson(json);

        if (result == null || string.IsNullOrWhiteSpace(result.bestText))
            return;

        RaiseResult(result);
    }

    private SttResult ParseVoskJson(string json)
    {
        var node = JSON.Parse(json);
        if (node == null)
            return null;

        var candidates = new List<SttCandidate>();

        if (node["alternatives"] != null && node["alternatives"].Count > 0)
        {
            foreach (JSONNode alt in node["alternatives"].AsArray)
            {
                string text = NormalizeText(alt["text"]);
                float confidence = alt["confidence"].AsFloat;

                if (!string.IsNullOrWhiteSpace(text))
                    candidates.Add(new SttCandidate(text, confidence));
            }
        }
        else
        {
            string text = NormalizeText(node["text"]);
            if (!string.IsNullOrWhiteSpace(text))
                candidates.Add(new SttCandidate(text));
        }

        if (candidates.Count == 0)
            return null;

        var sttResult = new SttResult(
            SttSourceType.Vosk,
            candidates[0].text,
            true,
            json
        );

        sttResult.candidates = candidates;
        return sttResult;
    }

    private string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        return text
            .Replace(" ", "")
            .Replace("，", "")
            .Replace("。", "")
            .Replace(",", "")
            .Replace(".", "")
            .Trim();
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(VoskSttProvider))]
public class VoskSttProviderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        VoskSttProvider provider = (VoskSttProvider)target;

        using (new EditorGUI.DisabledScope(!provider.UseTextInput))
        {
            if (GUILayout.Button("Confirm Text Input"))
            {
                provider.ConfirmTextInput();
            }
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Text input submission is intended to be used in Play Mode.",
                MessageType.Info
            );
        }
    }
}
#endif