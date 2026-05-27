using System;
using UnityEngine;

public abstract class SttProviderBase : MonoBehaviour
{
    public event Action<SttResult> OnResult;
    public event Action<string> OnStatus;

    public abstract bool IsRunning { get; }

    public abstract void StartListening();
    public abstract void StopListening();

    protected void RaiseResult(SttResult result)
    {
        OnResult?.Invoke(result);
    }

    protected void RaiseStatus(string status)
    {
        OnStatus?.Invoke(status);
    }
}