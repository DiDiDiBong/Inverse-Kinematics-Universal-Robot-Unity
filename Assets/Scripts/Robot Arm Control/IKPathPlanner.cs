using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Rendering;
#endif

public class IKPathPlanner : MonoBehaviour
{
    [Header("References")]
    public Transform ikTarget;
    public Transform pointA;
    public Transform pointB;

    [Tooltip("Path control point. In the recommended setup, GripPoint is the parent/control object and ikTarget is its child.")]
    public Transform GripPoint;

    [Header("Grip Point Control")]
    [Tooltip("If enabled, PointA/PointB and plannedWorldPath are interpreted as GripPoint poses. The script moves GripPoint directly; ikTarget follows as its child.")]
    public bool useGripPointAsPathControl = true;

    [Tooltip("Require ikTarget to be a child of GripPoint. This is the recommended setup for using GripPoint as the motion root.")]
    public bool requireIKTargetAsGripPointChild = true;

    public bool drawCurrentGripPointGizmo = true;
    public Color currentGripPointGizmoColor = Color.white;

    [Tooltip("Only used as the center of the base avoidance area.")]
    public Transform robotBase;

    [Tooltip("Defines the planning plane. Local XZ is the planning plane, and local Y is height.")]
    public Transform basePlaneTransform;

    public enum RotationGizmoAxis
    {
        ForwardZ,
        UpY,
        RightX,
        NegativeForwardZ,
        NegativeUpY,
        NegativeRightX
    }

    [Header("IK Target Writing")]
    public bool writeIKTargetInLocalSpace = true;

    [Header("Motion")]
    public bool startOnPlay = false;
    public float moveSpeed = 0.25f;
    public float waypointEpsilon = 0.003f;

    [Header("Base Avoidance")]
    public bool avoidBaseArea = true;
    public float avoidRadius = 0.30f;
    public float clearance = 0.05f;

    [Tooltip("Extra margin outside the visible avoidance circle. This prevents sampled arc chords from cutting into the avoidance area.")]
    public float routeSafetyMargin = 0.03f;

    [Tooltip("Arc sampling resolution for the 2D detour around the base. Smaller values produce smoother arcs.")]
    public float arcStepDegrees = 4.0f;

    [Tooltip("If the start or end projection is inside the base avoidance circle, push the transport route endpoint outside.")]
    public bool pushRouteEndpointsOutsideAvoidance = true;

    [Header("Height Constraints")]
    public bool constrainAboveAbsoluteMinimum = true;

    [Tooltip("Hard lower bound. No generated path point should be lower than this local Y height above basePlaneTransform.")]
    public float absoluteMinimumHeightAbovePlane = 0.02f;

    public bool usePreferredMinimumHeight = true;

    [Tooltip("Soft preferred transport height. The middle part of the trajectory is pulled up to this height.")]
    public float preferredMinimumHeightAbovePlane = 0.18f;

    [Tooltip("Optional extra height added above the preferred transport height.")]
    public float preferredHeightExtra = 0.0f;

    [Header("Arch Shape")]
    [Range(0.05f, 0.45f)]
    public float risePortion = 0.25f;

    [Range(0.05f, 0.45f)]
    public float fallPortion = 0.25f;

    [Tooltip("Distance between sampled path points in plane space. Smaller value means smoother path and more IK updates.")]
    public float routeSampleSpacing = 0.025f;

    [Tooltip("Minimum number of path samples, even for short paths.")]
    public int minimumPathSamples = 40;

    [Tooltip("Use smootherstep instead of smoothstep for height transition.")]
    public bool useSmootherHeightProfile = true;

    [Header("Rotation")]
    public bool interpolateRotation = true;

    [Header("Debug")]
    public bool drawDebugPath = true;
    public bool drawAvoidanceCircle = true;
    public bool drawPreferredHeightPlane = true;
    public bool drawAbsoluteMinimumPlane = true;
    public float debugPlaneSize = 0.8f;
    public float debugSphereSize = 0.012f;

    [Header("Endpoint Rotation Gizmos")]
    public bool drawEndpointRotationGizmos = true;
    public RotationGizmoAxis endpointRotationAxis = RotationGizmoAxis.ForwardZ;

    [Header("Endpoint Rotation Gizmo Style")]

    public float endpointArrowLength = 0.18f;
    public float endpointArrowHeadLength = 0.04f;
    public float endpointArrowHeadAngle = 25.0f;
    public Color pointAArrowColor = Color.blue;
    public Color pointBArrowColor = Color.magenta;
    public float endpointArrowLineWidth = 6.0f;

    [Range(0.0f, 1.0f)]
    public float endpointArrowVisibleAlpha = 1.0f;

    [Range(0.0f, 1.0f)]
    public float endpointArrowOccludedAlpha = 0.45f;

    public bool drawEndpointArrowWhenOccluded = true;
    public bool forceEndpointArrowAlwaysVisible = false;

    public float endpointArrowHeadSize = 0.045f;

    private readonly List<Vector3> plannedWorldPath = new List<Vector3>();
    private Coroutine moveCoroutine;
    private bool isPaused;

    public bool IsMoving => moveCoroutine != null;
    public bool IsPaused => isPaused;

    public Vector3 CurrentIKWorldPosition => ikTarget != null ? ikTarget.position : Vector3.zero;
    public Quaternion CurrentIKWorldRotation => ikTarget != null ? ikTarget.rotation : Quaternion.identity;

    public Vector3 CurrentControlWorldPosition => GetCurrentControlWorldPosition();
    public Quaternion CurrentControlWorldRotation => GetCurrentControlWorldRotation();

    public IReadOnlyList<Vector3> PlannedWorldPath => plannedWorldPath;

    private const float TwoPi = Mathf.PI * 2.0f;

    private void Start()
    {
        if (startOnPlay)
        {
            MoveFromAToB();
        }
    }

    public void MoveCurrentIKToTransform(Transform target)
    {
        if (target == null)
        {
            Debug.LogWarning("[IKPathPlanner] Move target transform is null.");
            return;
        }

        MoveCurrentIKToPose(target.position, target.rotation);
    }

    public void MoveCurrentIKToPose(Vector3 endWorld, Quaternion endWorldRot)
    {
        if (!ValidateCoreReferences())
            return;

        // When GripPoint control is enabled, endWorld/endWorldRot are interpreted as the desired GripPoint pose.
        Vector3 startWorld = GetCurrentControlWorldPosition();
        Quaternion startWorldRot = GetCurrentControlWorldRotation();

        StartMove(startWorld, endWorld, startWorldRot, endWorldRot);
    }

    public void StopMove()
    {
        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
            moveCoroutine = null;
        }

        isPaused = false;
    }

    public void PauseMove()
    {
        if (moveCoroutine == null)
            return;

        isPaused = true;
    }

    public void ResumeMove()
    {
        if (moveCoroutine == null)
            return;

        isPaused = false;
    }

    public void SnapCurrentIKToTransform(Transform target)
    {
        if (target == null || !ValidateCoreReferences())
            return;

        StopMove();
        ApplyControlWorldPose(target.position, target.rotation);
        plannedWorldPath.Clear();
    }

    [ContextMenu("Move From A To B")]
    public void MoveFromAToB()
    {
        if (!ValidateReferences())
            return;

        Vector3 startWorld = pointA.position;
        Vector3 endWorld = pointB.position;

        Quaternion startWorldRot = pointA.rotation;
        Quaternion endWorldRot = pointB.rotation;

        ApplyControlWorldPose(startWorld, startWorldRot);
        StartMove(startWorld, endWorld, startWorldRot, endWorldRot);
    }

    [ContextMenu("Move Current IK To B")]
    public void MoveCurrentIKToB()
    {
        if (!ValidateReferences())
            return;

        Vector3 startWorld = GetCurrentControlWorldPosition();
        Vector3 endWorld = pointB.position;

        Quaternion startWorldRot = GetCurrentControlWorldRotation();
        Quaternion endWorldRot = pointB.rotation;

        StartMove(startWorld, endWorld, startWorldRot, endWorldRot);
    }

    public void StartMove(Vector3 startWorld, Vector3 endWorld, Quaternion startWorldRot, Quaternion endWorldRot)
    {
        if (!ValidateCoreReferences())
            return;

        plannedWorldPath.Clear();
        plannedWorldPath.AddRange(BuildWorldPath(startWorld, endWorld));

        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
        }

        isPaused = false;
        moveCoroutine = StartCoroutine(FollowWorldPath(startWorldRot, endWorldRot));
    }

    private IEnumerator FollowWorldPath(Quaternion startWorldRot, Quaternion endWorldRot)
    {
        if (plannedWorldPath.Count < 2)
            yield break;

        float totalLength = ComputePathLength(plannedWorldPath);
        float travelledBeforeSegment = 0.0f;

        for (int i = 0; i < plannedWorldPath.Count - 1; i++)
        {
            while (isPaused)
            {
                yield return null;
            }

            Vector3 segmentStart = plannedWorldPath[i];
            Vector3 segmentEnd = plannedWorldPath[i + 1];
            float segmentLength = Vector3.Distance(segmentStart, segmentEnd);

            if (segmentLength < 0.0001f)
                continue;

            while (Vector3.Distance(GetCurrentControlWorldPosition(), segmentEnd) > waypointEpsilon)
            {
                while (isPaused)
                {
                    yield return null;
                }

                Vector3 currentWorld = GetCurrentControlWorldPosition();
                float step = moveSpeed * Time.deltaTime;
                Vector3 nextWorld = Vector3.MoveTowards(currentWorld, segmentEnd, step);

                float segmentProgress = Vector3.Distance(segmentStart, nextWorld);
                float globalProgress = totalLength > 0.0001f
                    ? Mathf.Clamp01((travelledBeforeSegment + segmentProgress) / totalLength)
                    : 1.0f;

                Quaternion nextWorldRot = interpolateRotation
                    ? Quaternion.Slerp(startWorldRot, endWorldRot, globalProgress)
                    : GetCurrentControlWorldRotation();

                ApplyControlWorldPose(nextWorld, nextWorldRot);

                yield return null;
            }

            travelledBeforeSegment += segmentLength;

            float endProgress = totalLength > 0.0001f
                ? Mathf.Clamp01(travelledBeforeSegment / totalLength)
                : 1.0f;

            Quaternion segmentEndRot = interpolateRotation
                ? Quaternion.Slerp(startWorldRot, endWorldRot, endProgress)
                : GetCurrentControlWorldRotation();

            ApplyControlWorldPose(segmentEnd, segmentEndRot);
        }

        ApplyControlWorldPose(plannedWorldPath[plannedWorldPath.Count - 1], endWorldRot);
        moveCoroutine = null;
        isPaused = false;
    }

    private List<Vector3> BuildWorldPath(Vector3 startWorld, Vector3 endWorld)
    {
        Vector3 startPlane = WorldToPlane(startWorld);
        Vector3 endPlane = WorldToPlane(endWorld);

        startPlane = ClampAboveAbsoluteMinimum(startPlane);
        endPlane = ClampAboveAbsoluteMinimum(endPlane);

        Vector2 start2D = ToPlane2D(startPlane);
        Vector2 end2D = ToPlane2D(endPlane);

        List<Vector2> route2D = BuildSafe2DRoute(start2D, end2D);

        List<Vector3> planePath = BuildArchPlanePath(route2D, startPlane.y, endPlane.y);

        if (avoidBaseArea)
        {
            ValidateGeneratedPathAgainstBase(planePath);
        }

        List<Vector3> worldPath = new List<Vector3>();

        for (int i = 0; i < planePath.Count; i++)
        {
            AddWorldPointIfFar(worldPath, PlaneToWorld(planePath[i]));
        }

        return worldPath;
    }

    private List<Vector2> BuildSafe2DRoute(Vector2 start2D, Vector2 end2D)
    {
        List<Vector2> route = new List<Vector2>();

        if (!avoidBaseArea)
        {
            route.Add(start2D);
            route.Add(end2D);
            return route;
        }

        Vector3 basePlane = WorldToPlane(robotBase.position);
        Vector2 center2D = ToPlane2D(basePlane);

        float visibleSafeRadius = avoidRadius + clearance;
        float planningRadius = visibleSafeRadius + routeSafetyMargin;

        Vector2 routeStart = start2D;
        Vector2 routeEnd = end2D;

        if (pushRouteEndpointsOutsideAvoidance)
        {
            routeStart = PushPointOutsideCircle(routeStart, center2D, planningRadius);
            routeEnd = PushPointOutsideCircle(routeEnd, center2D, planningRadius);
        }

        AddPointIfFar(route, start2D);

        if (Vector2.Distance(start2D, routeStart) > 0.0001f)
        {
            Debug.LogWarning("Start projection is inside the base avoidance area. The route endpoint was pushed outside, but the initial segment may still start inside the area.");
            AddPointIfFar(route, routeStart);
        }

        List<Vector2> middleRoute;

        if (!SegmentIntersectsCircle(routeStart, routeEnd, center2D, visibleSafeRadius))
        {
            middleRoute = new List<Vector2>
            {
                routeStart,
                routeEnd
            };
        }
        else
        {
            bool success = TryBuildTangent2DRoute(
                routeStart,
                routeEnd,
                center2D,
                planningRadius,
                visibleSafeRadius,
                out middleRoute
            );

            if (!success)
            {
                Debug.LogWarning("Tangent route failed. Using fallback side waypoint.");
                middleRoute = BuildFallback2DRoute(routeStart, routeEnd, center2D, planningRadius, visibleSafeRadius);
            }
        }

        for (int i = 0; i < middleRoute.Count; i++)
        {
            AddPointIfFar(route, middleRoute[i]);
        }

        if (Vector2.Distance(routeEnd, end2D) > 0.0001f)
        {
            AddPointIfFar(route, end2D);
        }

        return route;
    }

    private bool TryBuildTangent2DRoute(
        Vector2 start,
        Vector2 end,
        Vector2 center,
        float planningRadius,
        float validationRadius,
        out List<Vector2> bestRoute)
    {
        bestRoute = null;

        List<Vector2> startTangents = GetTangentPoints(start, center, planningRadius);
        List<Vector2> endTangents = GetTangentPoints(end, center, planningRadius);

        if (startTangents.Count == 0 || endTangents.Count == 0)
            return false;

        float bestLength = float.MaxValue;

        foreach (Vector2 startTangent in startTangents)
        {
            foreach (Vector2 endTangent in endTangents)
            {
                for (int direction = -1; direction <= 1; direction += 2)
                {
                    List<Vector2> candidate = BuildArc2DRoute(
                        start,
                        end,
                        center,
                        planningRadius,
                        startTangent,
                        endTangent,
                        direction
                    );

                    if (!Is2DRouteValid(candidate, center, validationRadius))
                        continue;

                    float length = Compute2DPathLength(candidate);

                    if (length < bestLength)
                    {
                        bestLength = length;
                        bestRoute = candidate;
                    }
                }
            }
        }

        return bestRoute != null;
    }

    private List<Vector2> BuildArc2DRoute(
        Vector2 start,
        Vector2 end,
        Vector2 center,
        float radius,
        Vector2 startTangent,
        Vector2 endTangent,
        int direction)
    {
        List<Vector2> route = new List<Vector2>();

        route.Add(start);
        route.Add(startTangent);

        float angleStart = Mathf.Atan2(startTangent.y - center.y, startTangent.x - center.x);
        float angleEnd = Mathf.Atan2(endTangent.y - center.y, endTangent.x - center.x);
        float delta = GetDirectedAngleDelta(angleStart, angleEnd, direction);

        float arcStepRad = Mathf.Max(1.0f, arcStepDegrees) * Mathf.Deg2Rad;
        int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(delta) / arcStepRad));

        for (int i = 1; i < steps; i++)
        {
            float t = i / (float)steps;
            float angle = angleStart + delta * t;

            Vector2 p = new Vector2(
                center.x + Mathf.Cos(angle) * radius,
                center.y + Mathf.Sin(angle) * radius
            );

            route.Add(p);
        }

        route.Add(endTangent);
        route.Add(end);

        return route;
    }

    private List<Vector2> BuildFallback2DRoute(
        Vector2 start,
        Vector2 end,
        Vector2 center,
        float planningRadius,
        float validationRadius)
    {
        List<Vector2> bestRoute = null;
        float bestLength = float.MaxValue;

        Vector2 direction = end - start;

        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector2.right;
        }

        direction.Normalize();

        Vector2 sideA = new Vector2(-direction.y, direction.x);
        Vector2 sideB = -sideA;

        float fallbackRadius = planningRadius * 1.5f;

        Vector2[] candidates =
        {
            center + sideA * fallbackRadius,
            center + sideB * fallbackRadius
        };

        foreach (Vector2 candidatePoint in candidates)
        {
            List<Vector2> candidateRoute = new List<Vector2>
            {
                start,
                candidatePoint,
                end
            };

            if (!Is2DRouteValid(candidateRoute, center, validationRadius))
                continue;

            float length = Compute2DPathLength(candidateRoute);

            if (length < bestLength)
            {
                bestLength = length;
                bestRoute = candidateRoute;
            }
        }

        if (bestRoute != null)
            return bestRoute;

        Debug.LogWarning("Fallback route still intersects the base avoidance area. Increase avoidRadius, clearance, or routeSafetyMargin.");

        return new List<Vector2> { start, end };
    }

    private List<Vector3> BuildArchPlanePath(List<Vector2> route2D, float startY, float endY)
    {
        List<Vector3> planePath = new List<Vector3>();

        if (route2D == null || route2D.Count < 2)
            return planePath;

        float routeLength = Compute2DPathLength(route2D);

        int sampleCount = Mathf.Max(
            minimumPathSamples,
            Mathf.CeilToInt(routeLength / Mathf.Max(0.001f, routeSampleSpacing))
        );

        float cruiseY = GetPreferredCruiseHeight(startY, endY);

        for (int i = 0; i <= sampleCount; i++)
        {
            float t = i / (float)sampleCount;
            float distance = routeLength * t;

            Vector2 p2D = Sample2DRouteAtDistance(route2D, distance);
            float y = EvaluateHeightProfile(startY, endY, cruiseY, t);

            Vector3 planePoint = FromPlane2D(p2D, y);
            planePoint = ClampAboveAbsoluteMinimum(planePoint);

            AddPlanePointIfFar(planePath, planePoint);
        }

        return planePath;
    }

    private float GetPreferredCruiseHeight(float startY, float endY)
    {
        float cruiseY = Mathf.Max(startY, endY);

        if (usePreferredMinimumHeight)
        {
            cruiseY = Mathf.Max(cruiseY, preferredMinimumHeightAbovePlane);
        }

        cruiseY += preferredHeightExtra;

        if (constrainAboveAbsoluteMinimum)
        {
            cruiseY = Mathf.Max(cruiseY, absoluteMinimumHeightAbovePlane);
        }

        return cruiseY;
    }

    private float EvaluateHeightProfile(float startY, float endY, float cruiseY, float t)
    {
        float rise = Mathf.Clamp(risePortion, 0.01f, 0.49f);
        float fall = Mathf.Clamp(fallPortion, 0.01f, 0.49f);

        float sum = rise + fall;

        if (sum > 0.90f)
        {
            float scale = 0.90f / sum;
            rise *= scale;
            fall *= scale;
        }

        if (t < rise)
        {
            float u = t / rise;
            u = Ease01(u);
            return Mathf.Lerp(startY, cruiseY, u);
        }

        if (t > 1.0f - fall)
        {
            float u = (t - (1.0f - fall)) / fall;
            u = Ease01(u);
            return Mathf.Lerp(cruiseY, endY, u);
        }

        return cruiseY;
    }

    private float Ease01(float t)
    {
        t = Mathf.Clamp01(t);

        if (useSmootherHeightProfile)
        {
            return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
        }

        return t * t * (3.0f - 2.0f * t);
    }

    private Vector2 Sample2DRouteAtDistance(List<Vector2> route, float targetDistance)
    {
        if (route == null || route.Count == 0)
            return Vector2.zero;

        if (route.Count == 1)
            return route[0];

        float travelled = 0.0f;

        for (int i = 0; i < route.Count - 1; i++)
        {
            Vector2 a = route[i];
            Vector2 b = route[i + 1];
            float segmentLength = Vector2.Distance(a, b);

            if (segmentLength < 0.0001f)
                continue;

            if (travelled + segmentLength >= targetDistance)
            {
                float localT = (targetDistance - travelled) / segmentLength;
                return Vector2.Lerp(a, b, Mathf.Clamp01(localT));
            }

            travelled += segmentLength;
        }

        return route[route.Count - 1];
    }

    private void ValidateGeneratedPathAgainstBase(List<Vector3> planePath)
    {
        if (!avoidBaseArea || planePath == null || planePath.Count < 2)
            return;

        Vector3 basePlane = WorldToPlane(robotBase.position);
        Vector2 center2D = ToPlane2D(basePlane);
        float visibleSafeRadius = avoidRadius + clearance;

        bool valid = IsPlanePathValid(planePath, center2D, visibleSafeRadius);

        if (!valid)
        {
            Debug.LogWarning("Generated curved path entered the base avoidance area. Increase routeSafetyMargin, reduce arcStepDegrees, or increase avoidRadius/clearance.");
        }
    }

    private bool IsPlanePathValid(List<Vector3> planePath, Vector2 center, float radius)
    {
        for (int i = 0; i < planePath.Count; i++)
        {
            Vector2 p = ToPlane2D(planePath[i]);

            if (Vector2.Distance(p, center) < radius)
                return false;
        }

        for (int i = 0; i < planePath.Count - 1; i++)
        {
            Vector2 a = ToPlane2D(planePath[i]);
            Vector2 b = ToPlane2D(planePath[i + 1]);

            if (SegmentIntersectsCircle(a, b, center, radius))
                return false;
        }

        return true;
    }

    private bool Is2DRouteValid(List<Vector2> route, Vector2 center, float radius)
    {
        if (route == null || route.Count < 2)
            return false;

        for (int i = 0; i < route.Count; i++)
        {
            if (Vector2.Distance(route[i], center) < radius)
                return false;
        }

        for (int i = 0; i < route.Count - 1; i++)
        {
            if (SegmentIntersectsCircle(route[i], route[i + 1], center, radius))
                return false;
        }

        return true;
    }

    private Vector3 ClampAboveAbsoluteMinimum(Vector3 planePoint)
    {
        if (!constrainAboveAbsoluteMinimum)
            return planePoint;

        if (planePoint.y < absoluteMinimumHeightAbovePlane)
        {
            planePoint.y = absoluteMinimumHeightAbovePlane;
        }

        return planePoint;
    }

    private Vector2 PushPointOutsideCircle(Vector2 point, Vector2 center, float radius)
    {
        Vector2 fromCenter = point - center;
        float distance = fromCenter.magnitude;

        if (distance >= radius)
            return point;

        if (distance < 0.0001f)
        {
            fromCenter = Vector2.right;
        }
        else
        {
            fromCenter /= distance;
        }

        return center + fromCenter * radius;
    }

    private List<Vector2> GetTangentPoints(Vector2 point, Vector2 center, float radius)
    {
        List<Vector2> points = new List<Vector2>();

        Vector2 fromCenter = point - center;
        float distance = fromCenter.magnitude;

        if (distance <= radius + 0.0001f)
            return points;

        float baseAngle = Mathf.Atan2(fromCenter.y, fromCenter.x);
        float offsetAngle = Mathf.Acos(radius / distance);

        float angleA = baseAngle + offsetAngle;
        float angleB = baseAngle - offsetAngle;

        points.Add(new Vector2(
            center.x + Mathf.Cos(angleA) * radius,
            center.y + Mathf.Sin(angleA) * radius
        ));

        points.Add(new Vector2(
            center.x + Mathf.Cos(angleB) * radius,
            center.y + Mathf.Sin(angleB) * radius
        ));

        return points;
    }

    private bool SegmentIntersectsCircle(Vector2 a, Vector2 b, Vector2 center, float radius)
    {
        Vector2 ab = b - a;

        if (ab.sqrMagnitude < 0.0001f)
        {
            return Vector2.Distance(a, center) < radius;
        }

        float t = Vector2.Dot(center - a, ab) / ab.sqrMagnitude;
        t = Mathf.Clamp01(t);

        Vector2 closest = a + ab * t;
        return Vector2.Distance(closest, center) < radius;
    }

    private float GetDirectedAngleDelta(float start, float end, int direction)
    {
        if (direction >= 0)
        {
            return Mathf.Repeat(end - start, TwoPi);
        }

        return -Mathf.Repeat(start - end, TwoPi);
    }

    private float Compute2DPathLength(List<Vector2> path)
    {
        if (path == null || path.Count < 2)
            return 0.0f;

        float length = 0.0f;

        for (int i = 0; i < path.Count - 1; i++)
        {
            length += Vector2.Distance(path[i], path[i + 1]);
        }

        return length;
    }

    private float ComputePathLength(List<Vector3> path)
    {
        if (path == null || path.Count < 2)
            return 0.0f;

        float length = 0.0f;

        for (int i = 0; i < path.Count - 1; i++)
        {
            length += Vector3.Distance(path[i], path[i + 1]);
        }

        return length;
    }

    private Vector2 ToPlane2D(Vector3 planePosition)
    {
        return new Vector2(planePosition.x, planePosition.z);
    }

    private Vector3 FromPlane2D(Vector2 plane2D, float height)
    {
        return new Vector3(plane2D.x, height, plane2D.y);
    }

    private Vector3 WorldToPlane(Vector3 worldPosition)
    {
        if (basePlaneTransform == null)
            return worldPosition;

        return basePlaneTransform.InverseTransformPoint(worldPosition);
    }

    private Vector3 PlaneToWorld(Vector3 planePosition)
    {
        if (basePlaneTransform == null)
            return planePosition;

        return basePlaneTransform.TransformPoint(planePosition);
    }

    private void AddPlanePointIfFar(List<Vector3> path, Vector3 point)
    {
        if (path.Count == 0 || Vector3.Distance(path[path.Count - 1], point) > 0.0001f)
        {
            path.Add(point);
        }
    }

    private void AddWorldPointIfFar(List<Vector3> path, Vector3 point)
    {
        if (path.Count == 0 || Vector3.Distance(path[path.Count - 1], point) > 0.0001f)
        {
            path.Add(point);
        }
    }

    private void AddPointIfFar(List<Vector2> path, Vector2 point)
    {
        if (path.Count == 0 || Vector2.Distance(path[path.Count - 1], point) > 0.0001f)
        {
            path.Add(point);
        }
    }

    private bool UseGripPointControl()
    {
        return useGripPointAsPathControl && GripPoint != null;
    }

    private Transform GetMotionRoot()
    {
        if (UseGripPointControl())
            return GripPoint;

        return ikTarget;
    }

    private Vector3 GetCurrentControlWorldPosition()
    {
        Transform motionRoot = GetMotionRoot();
        return motionRoot != null ? motionRoot.position : Vector3.zero;
    }

    private Quaternion GetCurrentControlWorldRotation()
    {
        Transform motionRoot = GetMotionRoot();
        return motionRoot != null ? motionRoot.rotation : Quaternion.identity;
    }

    private void ApplyControlWorldPose(Vector3 desiredControlWorldPosition, Quaternion desiredControlWorldRotation)
    {
        if (UseGripPointControl())
        {
            GripPoint.SetPositionAndRotation(desiredControlWorldPosition, desiredControlWorldRotation);
            return;
        }

        ApplyIKWorldPose(desiredControlWorldPosition, desiredControlWorldRotation);
    }

    private void ApplyIKWorldPose(Vector3 worldPosition, Quaternion worldRotation)
    {
        if (writeIKTargetInLocalSpace && ikTarget.parent != null)
        {
            ikTarget.localPosition = ikTarget.parent.InverseTransformPoint(worldPosition);
            ikTarget.localRotation = Quaternion.Inverse(ikTarget.parent.rotation) * worldRotation;
        }
        else
        {
            ikTarget.position = worldPosition;
            ikTarget.rotation = worldRotation;
        }
    }

    private bool ValidateCoreReferences()
    {
        if (ikTarget == null || basePlaneTransform == null)
        {
            Debug.LogError("[IKPathPlanner] Missing required references. ikTarget and basePlaneTransform are required.");
            return false;
        }

        if (avoidBaseArea && robotBase == null)
        {
            Debug.LogError("[IKPathPlanner] robotBase is required when avoidBaseArea is enabled.");
            return false;
        }

        if (useGripPointAsPathControl)
        {
            if (GripPoint == null)
            {
                Debug.LogError("[IKPathPlanner] GripPoint is required when useGripPointAsPathControl is enabled.");
                return false;
            }

            if (requireIKTargetAsGripPointChild && !ikTarget.IsChildOf(GripPoint))
            {
                Debug.LogError("[IKPathPlanner] In GripPoint control mode, ikTarget must be a child of GripPoint. Recommended hierarchy: GripPoint -> ikTarget.");
                return false;
            }
        }

        return true;
    }

    private bool ValidateReferences()
    {
        if (!ValidateCoreReferences())
            return false;

        if (pointA == null || pointB == null)
        {
            Debug.LogError("[IKPathPlanner] pointA and pointB are required for preview A/B movement.");
            return false;
        }

        return true;
    }

    private void OnDrawGizmos()
    {
        if (!drawDebugPath)
            return;

        if (basePlaneTransform != null && drawAbsoluteMinimumPlane)
        {
            DrawHeightPlaneGizmo(absoluteMinimumHeightAbovePlane, Color.yellow);
        }

        if (basePlaneTransform != null && drawPreferredHeightPlane)
        {
            DrawHeightPlaneGizmo(preferredMinimumHeightAbovePlane, Color.cyan);
        }

        if (robotBase != null && basePlaneTransform != null && drawAvoidanceCircle)
        {
            DrawAvoidanceCircleGizmo();
        }

        if (plannedWorldPath != null && plannedWorldPath.Count >= 2)
        {
            Gizmos.color = Color.green;

            for (int i = 0; i < plannedWorldPath.Count - 1; i++)
            {
                Gizmos.DrawLine(plannedWorldPath[i], plannedWorldPath[i + 1]);
                Gizmos.DrawSphere(plannedWorldPath[i], debugSphereSize);
            }

            Gizmos.DrawSphere(plannedWorldPath[plannedWorldPath.Count - 1], debugSphereSize);
        }
        if (drawCurrentGripPointGizmo && GripPoint != null)
        {
            Gizmos.color = currentGripPointGizmoColor;
            Gizmos.DrawWireSphere(GripPoint.position, debugSphereSize * 2.5f);
        }

        if (drawEndpointRotationGizmos)
        {
            DrawEndpointRotationGizmo(pointA, pointAArrowColor, "A");
            DrawEndpointRotationGizmo(pointB, pointBArrowColor, "B");
        }
    }

    private void DrawHeightPlaneGizmo(float height, Color color)
    {
        float half = debugPlaneSize * 0.5f;

        Vector3 p0 = basePlaneTransform.TransformPoint(new Vector3(-half, height, -half));
        Vector3 p1 = basePlaneTransform.TransformPoint(new Vector3(half, height, -half));
        Vector3 p2 = basePlaneTransform.TransformPoint(new Vector3(half, height, half));
        Vector3 p3 = basePlaneTransform.TransformPoint(new Vector3(-half, height, half));

        Gizmos.color = color;
        Gizmos.DrawLine(p0, p1);
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p0);
    }

    private void DrawAvoidanceCircleGizmo()
    {
        Vector3 basePlane = WorldToPlane(robotBase.position);
        Vector2 center2D = ToPlane2D(basePlane);

        float radius = avoidRadius + clearance;
        int segments = 72;

        float y = usePreferredMinimumHeight
            ? preferredMinimumHeightAbovePlane
            : absoluteMinimumHeightAbovePlane;

        Gizmos.color = Color.red;

        Vector3 previous = PlaneToWorld(FromPlane2D(
            center2D + new Vector2(radius, 0.0f),
            y
        ));

        for (int i = 1; i <= segments; i++)
        {
            float angle = TwoPi * i / segments;

            Vector2 p2D = center2D + new Vector2(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius
            );

            Vector3 next = PlaneToWorld(FromPlane2D(p2D, y));

            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
    
    private void DrawEndpointRotationGizmo(Transform target, Color baseColor, string label)
    {
        if (target == null)
            return;

        Vector3 origin = target.position;
        Vector3 direction = GetRotationGizmoDirection(target);

        if (direction.sqrMagnitude < 0.0001f)
            return;

        direction.Normalize();

        Vector3 tip = origin + direction * endpointArrowLength;

    #if UNITY_EDITOR
        DrawEndpointArrowHandle(origin, tip, direction, baseColor, label);
    #else
        Gizmos.color = baseColor;
        Gizmos.DrawLine(origin, tip);
        Gizmos.DrawSphere(origin, debugSphereSize * 1.5f);
    #endif
    }

    #if UNITY_EDITOR
    private void DrawEndpointArrowHandle(
        Vector3 origin,
        Vector3 tip,
        Vector3 direction,
        Color baseColor,
        string label)
    {
        CompareFunction previousZTest = Handles.zTest;
        Color previousColor = Handles.color;

        Color visibleColor = WithAlpha(baseColor, endpointArrowVisibleAlpha);
        Color occludedColor = WithAlpha(baseColor, endpointArrowOccludedAlpha);

        if (forceEndpointArrowAlwaysVisible)
        {
            Handles.zTest = CompareFunction.Always;
            Handles.color = visibleColor;

            DrawThickArrow(origin, tip, direction);
        }
        else
        {
            if (drawEndpointArrowWhenOccluded && endpointArrowOccludedAlpha > 0.001f)
            {
                Handles.zTest = CompareFunction.Always;
                Handles.color = occludedColor;

                DrawThickArrow(origin, tip, direction);
            }

            Handles.zTest = CompareFunction.LessEqual;
            Handles.color = visibleColor;

            DrawThickArrow(origin, tip, direction);
        }

        Handles.color = visibleColor;
        Handles.Label(tip + Vector3.up * 0.03f, "Point " + label + " Rot");

        Handles.zTest = previousZTest;
        Handles.color = previousColor;
    }

    private void DrawThickArrow(Vector3 origin, Vector3 tip, Vector3 direction)
    {
        Handles.DrawAAPolyLine(endpointArrowLineWidth, origin, tip);

        Handles.ConeHandleCap(
            0,
            tip,
            Quaternion.LookRotation(direction),
            endpointArrowHeadSize,
            EventType.Repaint
        );

        Handles.SphereHandleCap(
            0,
            origin,
            Quaternion.identity,
            debugSphereSize * 2.5f,
            EventType.Repaint
        );
    }
    #endif

    private Color WithAlpha(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        return color;
    }

    private Vector3 GetRotationGizmoDirection(Transform target)
    {
        switch (endpointRotationAxis)
        {
            case RotationGizmoAxis.ForwardZ:
                return target.forward;

            case RotationGizmoAxis.UpY:
                return target.up;

            case RotationGizmoAxis.RightX:
                return target.right;

            case RotationGizmoAxis.NegativeForwardZ:
                return -target.forward;

            case RotationGizmoAxis.NegativeUpY:
                return -target.up;

            case RotationGizmoAxis.NegativeRightX:
                return -target.right;

            default:
                return target.forward;
        }
    }

    // private void DrawArrowHead(Vector3 arrowTip, Vector3 direction, Color color)
    // {
    //     Gizmos.color = color;

    //     Vector3 referenceUp = Vector3.up;

    //     if (Mathf.Abs(Vector3.Dot(direction.normalized, referenceUp)) > 0.95f)
    //     {
    //         referenceUp = Vector3.right;
    //     }

    //     Vector3 right = Vector3.Cross(direction, referenceUp).normalized;
    //     Vector3 up = Vector3.Cross(right, direction).normalized;

    //     float angleRad = endpointArrowHeadAngle * Mathf.Deg2Rad;
    //     float backLength = endpointArrowHeadLength;
    //     float sideLength = Mathf.Tan(angleRad) * backLength;

    //     Vector3 backCenter = arrowTip - direction.normalized * backLength;

    //     Vector3 p1 = backCenter + right * sideLength;
    //     Vector3 p2 = backCenter - right * sideLength;
    //     Vector3 p3 = backCenter + up * sideLength;
    //     Vector3 p4 = backCenter - up * sideLength;

    //     Gizmos.DrawLine(arrowTip, p1);
    //     Gizmos.DrawLine(arrowTip, p2);
    //     Gizmos.DrawLine(arrowTip, p3);
    //     Gizmos.DrawLine(arrowTip, p4);
    // }
}