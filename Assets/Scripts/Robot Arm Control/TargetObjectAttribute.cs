using UnityEngine;

public class TargetObjectAttribute : MonoBehaviour
{
    public enum TargetOrientationMode
    {
        ObjectOrientation,
        GraspPointTransform,
        ForwardUpUpToBiasTarget
    }

    [Header("Semantic Attributes")]
    public string objectId = "";
    public string displayName = "";
    public string color = "蓝色";
    public string objectType = "球";

    [Header("Grasp Settings")]
    public bool isGrabbable = true;
    public Transform graspPoint;

    [Tooltip("Optional. If empty, the object transform is used.")]
    public Transform releaseReference;

    [Header("Grab Target Orientation")]
    [Tooltip("Defines how the grab target orientation is calculated.")]
    public TargetOrientationMode targetOrientation = TargetOrientationMode.ForwardUpUpToBiasTarget;

    [Tooltip("Used when Target Orientation is Forward Up Up To Bias Target. Only the world position is used.")]
    public Transform orientationBiasTarget;

    [Header("Orientation Gizmo")]
    public bool showGrabTargetOrientationGizmo = true;

    [Min(0.01f)]
    public float orientationGizmoLength = 0.15f;

    [Min(0.001f)]
    public float orientationGizmoHeadSize = 0.025f;

    [Header("Matching")]
    public int priority = 0;

    [HideInInspector]
    public Quaternion grabTargetOrientation = Quaternion.identity;

    public Transform EffectiveGraspPoint
    {
        get
        {
            return graspPoint != null ? graspPoint : transform;
        }
    }

    public Transform EffectiveReleaseReference
    {
        get
        {
            return releaseReference != null ? releaseReference : transform;
        }
    }

    private void Awake()
    {
        UpdateGrabTargetOrientation();
    }

    private void OnValidate()
    {
        UpdateGrabTargetOrientation();
    }

    private void OnDrawGizmos()
    {
        DrawGrabTargetOrientationGizmo();
    }

    private void OnDrawGizmosSelected()
    {
        DrawGrabTargetOrientationGizmo();
    }

    public Quaternion GetGrabTargetOrientation()
    {
        UpdateGrabTargetOrientation();
        return grabTargetOrientation;
    }

    public void UpdateGrabTargetOrientation()
    {
        grabTargetOrientation = GetTargetOrientation();
    }

    private Quaternion GetTargetOrientation()
    {
        switch (targetOrientation)
        {
            case TargetOrientationMode.ObjectOrientation:
                return transform.rotation;

            case TargetOrientationMode.GraspPointTransform:
                return EffectiveGraspPoint.rotation;

            case TargetOrientationMode.ForwardUpUpToBiasTarget:
                return GetForwardUpUpToBiasTargetOrientation();

            default:
                return transform.rotation;
        }
    }

    private Quaternion GetForwardUpUpToBiasTargetOrientation()
    {
        Transform originTransform = EffectiveGraspPoint;
        Vector3 origin = originTransform != null ? originTransform.position : transform.position;

        Vector3 forward = Vector3.up;
        Vector3 up = Vector3.zero;

        if (orientationBiasTarget != null)
        {
            Vector3 directionToBiasTarget = orientationBiasTarget.position - origin;

            if (directionToBiasTarget.sqrMagnitude > 0.000001f)
            {
                up = Vector3.ProjectOnPlane(directionToBiasTarget, forward);
            }
        }

        if (up.sqrMagnitude <= 0.000001f && originTransform != null)
        {
            up = Vector3.ProjectOnPlane(-originTransform.up, forward);
        }

        if (up.sqrMagnitude <= 0.000001f && originTransform != null)
        {
            up = Vector3.ProjectOnPlane(originTransform.forward, forward);
        }

        if (up.sqrMagnitude <= 0.000001f)
        {
            up = Vector3.forward;
        }

        up.Normalize();

        return Quaternion.LookRotation(forward, up);
    }

    private void DrawGrabTargetOrientationGizmo()
    {
        if (!showGrabTargetOrientationGizmo)
        {
            return;
        }

        Transform originTransform = EffectiveGraspPoint;

        if (originTransform == null)
        {
            return;
        }

        UpdateGrabTargetOrientation();

        Vector3 origin = originTransform.position;

        Vector3 forward = grabTargetOrientation * Vector3.forward;
        Vector3 up = grabTargetOrientation * Vector3.up;
        Vector3 down = -up;
        Vector3 right = grabTargetOrientation * Vector3.right;
        Vector3 left = -right;

        Gizmos.color = Color.white;
        Gizmos.DrawSphere(origin, orientationGizmoHeadSize * 0.5f);

        Gizmos.color = Color.blue;
        DrawArrow(
            origin,
            forward,
            orientationGizmoLength,
            orientationGizmoHeadSize
        );

        Gizmos.color = Color.green;
        DrawArrow(
            origin,
            down,
            orientationGizmoLength * 0.75f,
            orientationGizmoHeadSize * 0.8f
        );

        Gizmos.color = Color.red;
        DrawArrow(
            origin,
            right,
            orientationGizmoLength * 0.75f,
            orientationGizmoHeadSize * 0.8f
        );

        Gizmos.color = Color.red;
        DrawArrow(
            origin,
            left,
            orientationGizmoLength * 0.75f,
            orientationGizmoHeadSize * 0.8f
        );

        if (orientationBiasTarget != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(origin, orientationBiasTarget.position);
            Gizmos.DrawSphere(
                orientationBiasTarget.position,
                orientationGizmoHeadSize * 0.45f
            );
        }
    }

    private void DrawArrow(Vector3 origin, Vector3 direction, float length, float headSize)
    {
        if (direction.sqrMagnitude <= 0.000001f)
        {
            return;
        }

        direction.Normalize();

        Vector3 end = origin + direction * length;

        Gizmos.DrawLine(origin, end);
        Gizmos.DrawSphere(end, headSize * 0.35f);

        Quaternion arrowRotation = GetStableArrowRotation(direction);

        Vector3 sideA = arrowRotation * Quaternion.Euler(0f, 150f, 0f) * Vector3.forward;
        Vector3 sideB = arrowRotation * Quaternion.Euler(0f, -150f, 0f) * Vector3.forward;

        Gizmos.DrawLine(end, end + sideA * headSize);
        Gizmos.DrawLine(end, end + sideB * headSize);
    }

    private Quaternion GetStableArrowRotation(Vector3 forward)
    {
        if (forward.sqrMagnitude <= 0.000001f)
        {
            return Quaternion.identity;
        }

        forward.Normalize();

        Vector3 up = Vector3.up;

        if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.98f)
        {
            up = Vector3.forward;
        }

        return Quaternion.LookRotation(forward, up);
    }

    public bool Matches(TargetObjectQuery query)
    {
        if (query == null)
        {
            return false;
        }

        if (!isGrabbable)
        {
            return false;
        }

        if (query.HasObjectType && !LooseEquals(objectType, query.objectType))
        {
            return false;
        }

        if (query.HasColor && !LooseEquals(color, query.color))
        {
            return false;
        }

        return true;
    }

    public string GetDisplayName()
    {
        if (!string.IsNullOrEmpty(displayName))
        {
            return displayName;
        }

        return color + objectType;
    }

    private bool LooseEquals(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return false;
        }

        return Normalize(a) == Normalize(b);
    }

    private string Normalize(string value)
    {
        return value
            .Replace(" ", "")
            .Replace("的", "")
            .Trim();
    }
}