using System;
using System.Collections.Generic;

public enum SttSourceType
{
    Vosk,
    Whisper
}

[Serializable]
public class SttCandidate
{
    public string text;
    public float confidence;

    public SttCandidate(string text, float confidence = 0f)
    {
        this.text = text;
        this.confidence = confidence;
    }
}

[Serializable]
public class SttResult
{
    public SttSourceType source;
    public string bestText;
    public List<SttCandidate> candidates = new List<SttCandidate>();
    public bool isFinal = true;
    public string rawResult;

    public SttResult(SttSourceType source, string bestText, bool isFinal = true, string rawResult = "")
    {
        this.source = source;
        this.bestText = bestText;
        this.isFinal = isFinal;
        this.rawResult = rawResult;
    }
}