using UnityEngine;
using Whisper;
using Whisper.Utils;

public class WhisperSttProvider : SttProviderBase
{
    [Header("Whisper Components")]
    [SerializeField] private WhisperManager whisperManager;
    [SerializeField] private MicrophoneRecord microphoneRecord;

    [Header("Command Output")]
    [SerializeField] private bool useSegmentUpdatedForCommands = true;
    [SerializeField] private bool useSegmentFinishedForCommands = true;
    [SerializeField] private float duplicateCooldown = 1.0f;

    [Header("Debug")]
    [SerializeField] private bool printWhisperDebug = true;

    private WhisperStream _stream;
    private bool _isRunning;
    private string _lastEmittedText = "";
    private float _lastEmitTime = -999f;

    public override bool IsRunning => _isRunning;

    private async void Start()
    {
        if (whisperManager == null)
            whisperManager = GetComponent<WhisperManager>();

        if (microphoneRecord == null)
            microphoneRecord = GetComponent<MicrophoneRecord>();

        if (whisperManager == null || microphoneRecord == null)
        {
            Debug.LogError("[WhisperSttProvider] Missing WhisperManager or MicrophoneRecord.");
            return;
        }

        if (!whisperManager.IsLoaded)
        {
            RaiseStatus("Whisper: loading model...");
            await whisperManager.InitModel();
        }

        _stream = await whisperManager.CreateStream(microphoneRecord);

        if (_stream == null)
        {
            Debug.LogError("[WhisperSttProvider] Failed to create WhisperStream.");
            return;
        }

        _stream.OnResultUpdated += OnResultUpdated;
        _stream.OnSegmentUpdated += OnSegmentUpdated;
        _stream.OnSegmentFinished += OnSegmentFinished;
        _stream.OnStreamFinished += OnStreamFinished;

        microphoneRecord.OnRecordStop += OnRecordStop;

        RaiseStatus("Whisper: stream ready.");
    }

    public void ToggleListening()
    {
        if (IsRunning)
        {
            StopListening();
        }
        else
        {
            StartListening();
        }
    }

    private void OnDestroy()
    {
        if (_stream != null)
        {
            _stream.OnResultUpdated -= OnResultUpdated;
            _stream.OnSegmentUpdated -= OnSegmentUpdated;
            _stream.OnSegmentFinished -= OnSegmentFinished;
            _stream.OnStreamFinished -= OnStreamFinished;
        }

        if (microphoneRecord != null)
        {
            microphoneRecord.OnRecordStop -= OnRecordStop;
        }
    }

    public override void StartListening()
    {
        if (_stream == null || microphoneRecord == null)
        {
            Debug.LogWarning("[WhisperSttProvider] Stream is not ready yet.");
            return;
        }

        if (_isRunning)
            return;

        _lastEmittedText = "";
        _lastEmitTime = -999f;

        _stream.StartStream();
        microphoneRecord.StartRecord();

        _isRunning = true;
        RaiseStatus("Whisper: listening.");

        if (printWhisperDebug)
            Debug.Log("[WhisperSttProvider] Listening started.");
    }

    public override void StopListening()
    {
        if (!_isRunning)
            return;

        // Follow the sample scene logic:
        // stop microphone first, then WhisperStream will finish through OnRecordStop.
        if (microphoneRecord != null && microphoneRecord.IsRecording)
        {
            microphoneRecord.StopRecord();
        }
        else if (_stream != null)
        {
            _stream.StopStream();
        }

        _isRunning = false;
        RaiseStatus("Whisper: stopping.");

        if (printWhisperDebug)
            Debug.Log("[WhisperSttProvider] Stop requested.");
    }

    private void OnResultUpdated(string result)
    {
        // This is cumulative full transcript. Good for UI, not ideal for command trigger.
        if (printWhisperDebug)
            Debug.Log("[WhisperSttProvider] Full result updated: " + result);
    }

    private void OnSegmentUpdated(WhisperResult segment)
    {
        if (printWhisperDebug)
            Debug.Log("[WhisperSttProvider] Segment updated: " + segment?.Result);

        if (!useSegmentUpdatedForCommands)
            return;

        EmitCommandCandidate(segment, false);
    }

    private void OnSegmentFinished(WhisperResult segment)
    {
        if (printWhisperDebug)
            Debug.Log("[WhisperSttProvider] Segment finished: " + segment?.Result);

        if (!useSegmentFinishedForCommands)
            return;

        EmitCommandCandidate(segment, true);
    }

    private void OnStreamFinished(string finalResult)
    {
        if (printWhisperDebug)
            Debug.Log("[WhisperSttProvider] Stream finished: " + finalResult);

        _isRunning = false;
        RaiseStatus("Whisper: stream finished.");
    }

    private void OnRecordStop(AudioChunk recordedAudio)
    {
        if (printWhisperDebug)
            Debug.Log("[WhisperSttProvider] Microphone record stopped.");
    }

    private void EmitCommandCandidate(WhisperResult whisperResult, bool isFinal)
    {
        if (whisperResult == null)
            return;

        string text = NormalizeText(whisperResult.Result);

        if (string.IsNullOrEmpty(text))
            return;

        if (IsDuplicate(text))
            return;

        _lastEmittedText = text;
        _lastEmitTime = Time.time;

        SttResult result = new SttResult(
            SttSourceType.Whisper,
            text,
            isFinal,
            whisperResult.Result
        );

        result.candidates.Add(new SttCandidate(text, 0f));

        if (printWhisperDebug)
            Debug.Log("[WhisperSttProvider] Emit STT result: " + text);

        RaiseResult(result);
    }

    private bool IsDuplicate(string text)
    {
        if (text == _lastEmittedText && Time.time - _lastEmitTime < duplicateCooldown)
            return true;

        return false;
    }

    private string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        return text
            .Replace(" ", "")
            .Replace("，", "")
            .Replace("。", "")
            .Replace("？", "")
            .Replace("！", "")
            .Replace(",", "")
            .Replace(".", "")
            .Replace("?", "")
            .Replace("!", "")
            .Trim();
    }
}