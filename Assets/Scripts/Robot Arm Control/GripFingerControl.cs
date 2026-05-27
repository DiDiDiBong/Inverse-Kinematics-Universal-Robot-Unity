using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class GripFingerControl : MonoBehaviour
{
    [Serializable]
    public class LocalPose
    {
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Vector3 localScale = Vector3.one;

        public void RecordFrom(Transform target)
        {
            if (target == null) return;

            localPosition = target.localPosition;
            localEulerAngles = target.localEulerAngles;
            localScale = target.localScale;
        }

        public void ApplyTo(Transform target)
        {
            if (target == null) return;

            target.localPosition = localPosition;
            target.localRotation = Quaternion.Euler(localEulerAngles);
            target.localScale = localScale;
        }
    }

    [Serializable]
    public class Finger
    {
        public Transform finger;

        public LocalPose openPose = new LocalPose();
        public LocalPose closedPose = new LocalPose();

        public bool usePosition = true;
        public bool useRotation = true;
        public bool useScale = false;

        public void RecordOpen()
        {
            if (finger == null) return;
            openPose.RecordFrom(finger);
        }

        public void RecordClosed()
        {
            if (finger == null) return;
            closedPose.RecordFrom(finger);
        }

        public void Apply(float amount)
        {
            if (finger == null) return;

            amount = Mathf.Clamp01(amount);

            if (usePosition)
            {
                finger.localPosition = Vector3.Lerp(
                    openPose.localPosition,
                    closedPose.localPosition,
                    amount
                );
            }

            if (useRotation)
            {
                Quaternion openRot = Quaternion.Euler(openPose.localEulerAngles);
                Quaternion closedRot = Quaternion.Euler(closedPose.localEulerAngles);

                finger.localRotation = Quaternion.Slerp(
                    openRot,
                    closedRot,
                    amount
                );
            }

            if (useScale)
            {
                finger.localScale = Vector3.Lerp(
                    openPose.localScale,
                    closedPose.localScale,
                    amount
                );
            }
        }
    }

    [Header("Finger List")]
    public List<Finger> fingers = new List<Finger>();

    [Header("Grip Control")]
    [Range(0f, 1f)]
    public float gripAmount = 0f;

    [Header("Editor Preview")]
    public bool previewInEditMode = true;

    private float lastGripAmount = -1f;

    private void Update()
    {
        if (!Application.isPlaying && !previewInEditMode)
            return;

        if (!Mathf.Approximately(lastGripAmount, gripAmount))
        {
            ApplyGrip();
        }
    }

    private void OnValidate()
    {
        gripAmount = Mathf.Clamp01(gripAmount);

        if (!Application.isPlaying && previewInEditMode)
        {
            ApplyGrip();
        }
    }

    public void SetGrip(float amount)
    {
        gripAmount = Mathf.Clamp01(amount);
        ApplyGrip();
    }

    public void Open()
    {
        SetGrip(0f);
    }

    public void Close()
    {
        SetGrip(1f);
    }

    public void ApplyGrip()
    {
        gripAmount = Mathf.Clamp01(gripAmount);

        foreach (Finger finger in fingers)
        {
            finger?.Apply(gripAmount);
        }

        lastGripAmount = gripAmount;
    }

    [ContextMenu("Record Current As Open Pose")]
    public void RecordCurrentAsOpenPose()
    {
#if UNITY_EDITOR
        Undo.RecordObject(this, "Record Open Finger Pose");
#endif

        foreach (Finger finger in fingers)
        {
            finger?.RecordOpen();
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
    }

    [ContextMenu("Record Current As Closed Pose")]
    public void RecordCurrentAsClosedPose()
    {
#if UNITY_EDITOR
        Undo.RecordObject(this, "Record Closed Finger Pose");
#endif

        foreach (Finger finger in fingers)
        {
            finger?.RecordClosed();
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
    }

    [ContextMenu("Apply Open Pose")]
    public void ApplyOpenPose()
    {
        gripAmount = 0f;
        ApplyGrip();
    }

    [ContextMenu("Apply Closed Pose")]
    public void ApplyClosedPose()
    {
        gripAmount = 1f;
        ApplyGrip();
    }
}