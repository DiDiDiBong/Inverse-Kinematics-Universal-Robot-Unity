using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class RobotArmTaskController : MonoBehaviour
{
    [Serializable]
    public class DestinationTarget
    {
        public string displayName = "Destination";

        [Tooltip("The position of this transform is used as the destination point. Its rotation is ignored.")]
        public Transform target;

        [Tooltip("Optional compatibility with RobotArmVoiceCommandExtractor output.")]
        public RobotArmDestinationType destinationType = RobotArmDestinationType.None;

        [Tooltip("Text labels used to match raw voice command text, for example: 我这里, 那边, 左边, 盒子里.")]
        public List<string> labels = new List<string>();

        [Tooltip("Used when the voice command does not provide a clear destination.")]
        public bool useAsDefaultWhenDestinationMissing = false;
    }

    private class HeldObjectData
    {
        public TargetObjectAttribute target;
        public Transform rootTransform;
        public Transform originalParent;
        public Rigidbody rigidbody;
        public bool originalIsKinematic;
    }

    [Header("Command Input")]
    public bool acceptVoiceCommands = true;
    public RobotArmVoiceCommandExtractor commandExtractor;

    [Header("Motion")]
    public IKPathPlanner pathPlanner;

    [Tooltip("The position and rotation of this transform are both used for reset.")]
    public Transform resetPose;

    [Header("Destination Targets")]
    public List<DestinationTarget> destinationTargets = new List<DestinationTarget>();

    [Header("Target Rotation")]
    [Tooltip("Default world rotation used when no TargetObjectAttribute.targetRotation is found. Default means forward +Z and up +Y.")]
    public Vector3 defaultTargetEulerAngles = new Vector3(0, 1f, 1f);

    [Header("Grip Control")]
    public GripFingerControl gripFingerControl;

    [Range(0f, 1f)]
    public float openGripAmount = 0f;

    [Range(0f, 1f)]
    public float closedGripAmount = 0.5f;

    public bool initializeGripOpenOnAwake = true;

    [Tooltip("Usually true, otherwise the held rigidbody may fight against transform parenting.")]
    public bool makeHeldRigidbodyKinematic = true;

    [Header("Placement")]
    [Tooltip("When placing an object, the arm moves to destination position + Vector3.up * this value.")]
    public float placeHeightOffset = 0.08f;

    [Tooltip("Base reference used to orient the gripper during placement. Placement rotation uses forward = world down, up = direction toward this base.")]
    public Transform robotBase;

    [Header("Forward Blend To Base")]
    [Range(0f, 1f)]
    [Tooltip("0 = keep original grab forward. 1 = make grab forward point from the grasp point toward Robot Base.")]
    public float grabForwardToBaseBlend = 0f;

    [Range(0f, 1f)]
    [Tooltip("0 = keep original placement forward. 1 = make placement forward point from the placement point toward Robot Base.")]
    public float placeForwardToBaseBlend = 0f;

    [Header("Action Settings")]
    public float settleBeforeGrabDelay = 0.15f;
    public float grabDelay = 0.25f;
    public float settleBeforeReleaseDelay = 0.15f;
    public float releaseDelay = 0.25f;
    public float scanDelay = 0.5f;

    [Header("UI")]
    public Text statusText;

    [Header("Debug")]
    public bool printDebug = true;

    public RobotArmRuntimeState CurrentState { get; private set; } = RobotArmRuntimeState.Idle;
    public RobotArmGripState GripState { get; private set; } = RobotArmGripState.Open;
    public TargetObjectAttribute HeldTarget { get; private set; }

    private RobotArmTask currentTask;
    private Coroutine activeTaskRoutine;
    private HeldObjectData heldObjectData;

    private readonly List<Transform> runtimeMoveTargets = new List<Transform>();

    private void Awake()
    {
        if (commandExtractor != null)
        {
            commandExtractor.OnCommandExtracted += OnVoiceCommandReceived;
        }

        if (initializeGripOpenOnAwake && gripFingerControl != null)
        {
            gripFingerControl.SetGrip(openGripAmount);
            GripState = RobotArmGripState.Open;
        }

        UpdateStatusUI("Ready.");
    }

    private void OnDestroy()
    {
        if (commandExtractor != null)
        {
            commandExtractor.OnCommandExtracted -= OnVoiceCommandReceived;
        }

        ClearRuntimeMoveTargets();
    }

    private void OnVoiceCommandReceived(RobotArmCommand command)
    {
        if (!acceptVoiceCommands)
        {
            Log("[RobotArmTaskController] Voice command ignored because acceptVoiceCommands is false.");
            return;
        }

        SubmitCommand(command);
    }

    public void SubmitCommand(RobotArmCommand command)
    {
        if (command == null || !command.isValid)
        {
            return;
        }

        Log("[RobotArmTaskController] Received command: " + command);

        if (!CanAcceptCommand(command))
        {
            string reason = $"Command rejected. Current state = {CurrentState}. Say 中断 or 停止 first.";
            Log(reason);
            UpdateStatusUI(reason);
            return;
        }

        if (IsImmediateControlCommand(command.taskType))
        {
            ExecuteImmediateControlCommand(command);
            return;
        }

        StartNewTask(command);
    }

    private bool CanAcceptCommand(RobotArmCommand command)
    {
        if (command == null)
        {
            return false;
        }

        switch (CurrentState)
        {
            case RobotArmRuntimeState.Idle:
            case RobotArmRuntimeState.Interrupted:
            case RobotArmRuntimeState.Stopped:
                return true;

            case RobotArmRuntimeState.Running:
                return command.taskType == RobotArmTaskType.Pause ||
                       command.taskType == RobotArmTaskType.Interrupt ||
                       command.taskType == RobotArmTaskType.Stop;

            case RobotArmRuntimeState.Paused:
                return command.taskType == RobotArmTaskType.Resume ||
                       command.taskType == RobotArmTaskType.Interrupt ||
                       command.taskType == RobotArmTaskType.Stop;

            case RobotArmRuntimeState.Error:
                return command.taskType == RobotArmTaskType.Reset ||
                       command.taskType == RobotArmTaskType.Stop;

            default:
                return false;
        }
    }

    private bool IsImmediateControlCommand(RobotArmTaskType taskType)
    {
        return taskType == RobotArmTaskType.Stop ||
               taskType == RobotArmTaskType.Pause ||
               taskType == RobotArmTaskType.Resume ||
               taskType == RobotArmTaskType.Interrupt;
    }

    private void ExecuteImmediateControlCommand(RobotArmCommand command)
    {
        switch (command.taskType)
        {
            case RobotArmTaskType.Stop:
                StopCurrentTask();
                CurrentState = RobotArmRuntimeState.Stopped;
                UpdateStatusUI("Stopped.");
                break;

            case RobotArmTaskType.Pause:
                PauseCurrentTask();
                break;

            case RobotArmTaskType.Resume:
                ResumeCurrentTask();
                break;

            case RobotArmTaskType.Interrupt:
                InterruptCurrentTask();
                break;
        }
    }

    private void StartNewTask(RobotArmCommand command)
    {
        StopActiveRoutineOnly();
        ClearRuntimeMoveTargets();

        currentTask = BuildTask(command);

        if (currentTask == null || currentTask.actions.Count == 0)
        {
            CurrentState = RobotArmRuntimeState.Error;
            UpdateStatusUI("Failed to build task: " + command.rawText);
            return;
        }

        activeTaskRoutine = StartCoroutine(ExecuteTaskRoutine(currentTask));
    }

    private RobotArmTask BuildTask(RobotArmCommand command)
    {
        RobotArmTask task = new RobotArmTask(command);

        switch (command.taskType)
        {
            case RobotArmTaskType.Pick:
                BuildPickTask(task, command);
                break;

            case RobotArmTaskType.PickAndMove:
                BuildPickAndMoveTask(task, command);
                break;

            case RobotArmTaskType.Release:
                BuildReleaseTask(task, command);
                break;

            case RobotArmTaskType.Reset:
                BuildResetTask(task);
                break;

            case RobotArmTaskType.Scan:
                BuildScanTask(task);
                break;
        }

        return task;
    }

    private void BuildPickTask(RobotArmTask task, RobotArmCommand command)
    {
        TargetObjectAttribute target = FindTargetObject(command.target);

        if (target == null)
        {
            task.actions.Add(new RobotArmActionStep(RobotArmActionType.None, "Target not found."));
            return;
        }

        task.actions.Add(CreateMoveStepToTargetObject(target, "Move to target grasp point."));

        task.actions.Add(new RobotArmActionStep(RobotArmActionType.Grab, "Grab target.")
        {
            targetObject = target,
            waitAfterAction = grabDelay
        });
    }

    private void BuildPickAndMoveTask(RobotArmTask task, RobotArmCommand command)
    {
        TargetObjectAttribute target = FindTargetObject(command.target);
        DestinationTarget destination = ResolveDestination(command, true);

        if (target == null)
        {
            task.actions.Add(new RobotArmActionStep(RobotArmActionType.None, "Target not found."));
            return;
        }

        if (destination == null || destination.target == null)
        {
            task.actions.Add(new RobotArmActionStep(RobotArmActionType.None, "Destination not assigned."));
            return;
        }

        task.actions.Add(CreateMoveStepToTargetObject(target, "Move to target grasp point."));

        task.actions.Add(new RobotArmActionStep(RobotArmActionType.Grab, "Grab target.")
        {
            targetObject = target,
            waitAfterAction = grabDelay
        });

        task.actions.Add(CreateMoveStepToPlacementPoint(destination, "Move to placement point above destination."));

        task.actions.Add(new RobotArmActionStep(RobotArmActionType.Release, "Release target at destination.")
        {
            waitAfterAction = releaseDelay
        });

        AddResetStepIfAssigned(task);
    }

    private void BuildReleaseTask(RobotArmTask task, RobotArmCommand command)
    {
        DestinationTarget destination = ResolveDestination(command, false);

        if (destination != null && destination.target != null)
        {
            task.actions.Add(CreateMoveStepToPlacementPoint(destination, "Move to placement point above destination."));
        }

        task.actions.Add(new RobotArmActionStep(RobotArmActionType.Release, "Release held object.")
        {
            waitAfterAction = releaseDelay
        });

        if (destination != null)
        {
            AddResetStepIfAssigned(task);
        }
    }

    private void BuildResetTask(RobotArmTask task)
    {
        if (resetPose == null)
        {
            task.actions.Add(new RobotArmActionStep(RobotArmActionType.None, "Reset pose not assigned."));
            return;
        }

        task.actions.Add(CreateMoveStep(
            "Move to reset pose.",
            resetPose.position,
            resetPose.rotation
        ));
    }

    private void BuildScanTask(RobotArmTask task)
    {
        task.actions.Add(new RobotArmActionStep(RobotArmActionType.ScanScene, "Scan scene.")
        {
            waitAfterAction = scanDelay
        });
    }

    private void AddResetStepIfAssigned(RobotArmTask task)
    {
        if (resetPose == null)
        {
            return;
        }

        task.actions.Add(CreateMoveStep(
            "Move to reset pose.",
            resetPose.position,
            resetPose.rotation
        ));
    }

    private RobotArmActionStep CreateMoveStepToTargetObject(TargetObjectAttribute target, string description)
    {
        Transform graspPoint = target.EffectiveGraspPoint != null
            ? target.EffectiveGraspPoint
            : target.transform;

        Quaternion grabTargetRotation = ResolveRotationForTargetObject(target);

        // Only grabbing uses the opposite forward logic.
        Quaternion originalGripRotation = GetOppositeOrientation(grabTargetRotation);

        // Then optionally blend the final forward direction toward the base.
        Quaternion gripRotation = ApplyForwardBlendTowardBase(
            originalGripRotation,
            graspPoint.position,
            grabForwardToBaseBlend
        );

        return CreateMoveStep(
            description,
            graspPoint.position,
            gripRotation,
            target
        );
    }

    private RobotArmActionStep CreateMoveStepToPlacementPoint(DestinationTarget destination, string description)
    {
        Vector3 position = destination.target.position + Vector3.up * placeHeightOffset;

        Quaternion originalPlacementRotation = GetPlacementRotation(position);

        Quaternion placementRotation = ApplyForwardBlendTowardBase(
            originalPlacementRotation,
            position,
            placeForwardToBaseBlend
        );

        return CreateMoveStep(
            description,
            position,
            placementRotation
        );
    }

    private RobotArmActionStep CreateMoveStep(
        string description,
        Vector3 position,
        Quaternion rotation,
        TargetObjectAttribute targetObject = null
    )
    {
        Transform runtimeTarget = CreateRuntimeMoveTarget(description, position, rotation);

        return new RobotArmActionStep(RobotArmActionType.MovePath, description)
        {
            targetTransform = runtimeTarget,
            targetObject = targetObject
        };
    }

    private Transform CreateRuntimeMoveTarget(string description, Vector3 position, Quaternion rotation)
    {
        GameObject targetObject = new GameObject("RuntimeMoveTarget_" + description);
        targetObject.hideFlags = HideFlags.HideInHierarchy;

        Transform target = targetObject.transform;
        target.SetPositionAndRotation(position, rotation);
        target.SetParent(transform, true);

        runtimeMoveTargets.Add(target);

        return target;
    }

    private void ClearRuntimeMoveTargets()
    {
        for (int i = 0; i < runtimeMoveTargets.Count; i++)
        {
            Transform target = runtimeMoveTargets[i];

            if (target == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(target.gameObject);
            }
            else
            {
                DestroyImmediate(target.gameObject);
            }
        }

        runtimeMoveTargets.Clear();
    }

    private IEnumerator ExecuteTaskRoutine(RobotArmTask task)
    {
        CurrentState = RobotArmRuntimeState.Running;
        UpdateStatusUI("Task started: " + task.sourceCommand.debugMessage);

        for (int i = 0; i < task.actions.Count; i++)
        {
            task.currentActionIndex = i;
            RobotArmActionStep step = task.actions[i];

            if (step.actionType == RobotArmActionType.None)
            {
                CurrentState = RobotArmRuntimeState.Error;
                UpdateStatusUI("Task error: " + step.description);
                yield break;
            }

            UpdateStatusUI("Action: " + step.description);

            yield return ExecuteActionStep(step);

            if (CurrentState == RobotArmRuntimeState.Interrupted ||
                CurrentState == RobotArmRuntimeState.Stopped ||
                CurrentState == RobotArmRuntimeState.Error)
            {
                yield break;
            }
        }

        CurrentState = RobotArmRuntimeState.Idle;
        activeTaskRoutine = null;
        ClearRuntimeMoveTargets();
        UpdateStatusUI("Task finished.");
    }

    private IEnumerator ExecuteActionStep(RobotArmActionStep step)
    {
        switch (step.actionType)
        {
            case RobotArmActionType.MovePath:
                yield return ExecuteMoveAction(step);
                break;

            case RobotArmActionType.Grab:
                yield return ExecuteGrabAction(step);
                break;

            case RobotArmActionType.Release:
                yield return ExecuteReleaseAction(step);
                break;

            case RobotArmActionType.ScanScene:
                yield return ExecuteScanAction(step);
                break;
        }
    }

    private IEnumerator ExecuteMoveAction(RobotArmActionStep step)
    {
        if (pathPlanner == null)
        {
            CurrentState = RobotArmRuntimeState.Error;
            UpdateStatusUI("Path planner is missing.");
            yield break;
        }

        if (step.targetTransform == null)
        {
            CurrentState = RobotArmRuntimeState.Error;
            UpdateStatusUI("Move target is missing.");
            yield break;
        }

        pathPlanner.MoveCurrentIKToTransform(step.targetTransform);

        while (pathPlanner.IsMoving)
        {
            if (CurrentState == RobotArmRuntimeState.Interrupted ||
                CurrentState == RobotArmRuntimeState.Stopped)
            {
                pathPlanner.StopMove();
                yield break;
            }

            yield return null;
        }
    }

    private IEnumerator ExecuteGrabAction(RobotArmActionStep step)
    {
        if (gripFingerControl == null)
        {
            CurrentState = RobotArmRuntimeState.Error;
            UpdateStatusUI("GripFingerControl is missing.");
            yield break;
        }

        if (step.targetObject == null)
        {
            CurrentState = RobotArmRuntimeState.Error;
            UpdateStatusUI("Grab target is missing.");
            yield break;
        }

        if (HeldTarget != null)
        {
            CurrentState = RobotArmRuntimeState.Error;
            UpdateStatusUI("Cannot grab because another object is already held.");
            yield break;
        }

        yield return WaitForTaskSeconds(settleBeforeGrabDelay);

        if (CurrentState == RobotArmRuntimeState.Interrupted ||
            CurrentState == RobotArmRuntimeState.Stopped ||
            CurrentState == RobotArmRuntimeState.Error)
        {
            yield break;
        }

        GripState = RobotArmGripState.Closed;
        gripFingerControl.SetGrip(closedGripAmount);

        AttachTargetToGrip(step.targetObject);

        yield return WaitForTaskSeconds(step.waitAfterAction);

        GripState = RobotArmGripState.Holding;
        HeldTarget = step.targetObject;

        UpdateStatusUI("Holding: " + HeldTarget.GetDisplayName());
    }

    private IEnumerator ExecuteReleaseAction(RobotArmActionStep step)
    {
        if (gripFingerControl == null)
        {
            CurrentState = RobotArmRuntimeState.Error;
            UpdateStatusUI("GripFingerControl is missing.");
            yield break;
        }

        yield return WaitForTaskSeconds(settleBeforeReleaseDelay);

        if (CurrentState == RobotArmRuntimeState.Interrupted ||
            CurrentState == RobotArmRuntimeState.Stopped ||
            CurrentState == RobotArmRuntimeState.Error)
        {
            yield break;
        }

        GripState = RobotArmGripState.Open;
        gripFingerControl.SetGrip(openGripAmount);

        yield return WaitForTaskSeconds(step.waitAfterAction);

        ReleaseHeldObjectToOriginalParent();

        HeldTarget = null;
        GripState = RobotArmGripState.Open;

        UpdateStatusUI("Released.");
    }

    private IEnumerator ExecuteScanAction(RobotArmActionStep step)
    {
        yield return WaitForTaskSeconds(step.waitAfterAction);
        UpdateStatusUI("Scan finished.");
    }

    private IEnumerator WaitForTaskSeconds(float duration)
    {
        if (duration <= 0f)
        {
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (CurrentState == RobotArmRuntimeState.Interrupted ||
                CurrentState == RobotArmRuntimeState.Stopped ||
                CurrentState == RobotArmRuntimeState.Error)
            {
                yield break;
            }

            if (CurrentState != RobotArmRuntimeState.Paused)
            {
                elapsed += Time.deltaTime;
            }

            yield return null;
        }
    }

    private void AttachTargetToGrip(TargetObjectAttribute target)
    {
        Rigidbody rb = target.GetComponent<Rigidbody>();

        if (rb == null)
        {
            rb = target.GetComponentInParent<Rigidbody>();
        }

        Transform rootTransform = rb != null ? rb.transform : target.transform;

        heldObjectData = new HeldObjectData
        {
            target = target,
            rootTransform = rootTransform,
            originalParent = rootTransform.parent,
            rigidbody = rb,
            originalIsKinematic = rb != null && rb.isKinematic
        };

        if (rb != null)
        {
            rb.useGravity = false;

            if (makeHeldRigidbodyKinematic)
            {
                rb.isKinematic = true;
            }
        }

        rootTransform.SetParent(gripFingerControl.transform, true);
        HeldTarget = target;
    }

    private void ReleaseHeldObjectToOriginalParent()
    {
        if (heldObjectData == null)
        {
            return;
        }

        if (heldObjectData.rootTransform != null)
        {
            heldObjectData.rootTransform.SetParent(heldObjectData.originalParent, true);
        }

        if (heldObjectData.rigidbody != null)
        {
            if (makeHeldRigidbodyKinematic)
            {
                heldObjectData.rigidbody.isKinematic = heldObjectData.originalIsKinematic;
            }

            heldObjectData.rigidbody.useGravity = true;
        }

        heldObjectData = null;
    }

    private void PauseCurrentTask()
    {
        if (CurrentState != RobotArmRuntimeState.Running)
        {
            return;
        }

        CurrentState = RobotArmRuntimeState.Paused;

        if (pathPlanner != null)
        {
            pathPlanner.PauseMove();
        }

        UpdateStatusUI("Paused.");
    }

    private void ResumeCurrentTask()
    {
        if (CurrentState != RobotArmRuntimeState.Paused)
        {
            return;
        }

        CurrentState = RobotArmRuntimeState.Running;

        if (pathPlanner != null)
        {
            pathPlanner.ResumeMove();
        }

        UpdateStatusUI("Resumed.");
    }

    private void InterruptCurrentTask()
    {
        StopActiveRoutineOnly();

        if (pathPlanner != null)
        {
            pathPlanner.StopMove();
        }

        ClearRuntimeMoveTargets();

        CurrentState = RobotArmRuntimeState.Interrupted;
        UpdateStatusUI("Interrupted. New task can now be submitted.");
    }

    private void StopCurrentTask()
    {
        StopActiveRoutineOnly();

        if (pathPlanner != null)
        {
            pathPlanner.StopMove();
        }

        ClearRuntimeMoveTargets();
        currentTask = null;
    }

    private void StopActiveRoutineOnly()
    {
        if (activeTaskRoutine != null)
        {
            StopCoroutine(activeTaskRoutine);
            activeTaskRoutine = null;
        }
    }

    private TargetObjectAttribute FindTargetObject(TargetObjectQuery query)
    {
        TargetObjectAttribute[] targets = FindObjectsByType<TargetObjectAttribute>(FindObjectsSortMode.None);

        TargetObjectAttribute best = null;
        int bestPriority = int.MinValue;

        foreach (TargetObjectAttribute target in targets)
        {
            if (target == null || !target.Matches(query))
            {
                continue;
            }

            if (best == null || target.priority > bestPriority)
            {
                best = target;
                bestPriority = target.priority;
            }
        }

        if (best == null)
        {
            LogWarning("No target object matched query: " + query);
        }

        return best;
    }

    private DestinationTarget ResolveDestination(RobotArmCommand command, bool allowDefaultWhenMissing)
    {
        if (command == null)
        {
            return null;
        }

        if (command.destination != RobotArmDestinationType.None)
        {
            foreach (DestinationTarget destination in destinationTargets)
            {
                if (destination == null || destination.target == null)
                {
                    continue;
                }

                if (destination.destinationType == command.destination)
                {
                    return destination;
                }
            }
        }

        string rawText = NormalizeLabelText(command.rawText);
        string destinationName = NormalizeLabelText(command.destination.ToString());

        foreach (DestinationTarget destination in destinationTargets)
        {
            if (destination == null || destination.target == null || destination.labels == null)
            {
                continue;
            }

            foreach (string label in destination.labels)
            {
                string normalizedLabel = NormalizeLabelText(label);

                if (string.IsNullOrEmpty(normalizedLabel))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(rawText) && rawText.Contains(normalizedLabel))
                {
                    return destination;
                }

                if (!string.IsNullOrEmpty(destinationName) && destinationName == normalizedLabel)
                {
                    return destination;
                }
            }
        }

        if (allowDefaultWhenMissing)
        {
            foreach (DestinationTarget destination in destinationTargets)
            {
                if (destination != null &&
                    destination.target != null &&
                    destination.useAsDefaultWhenDestinationMissing)
                {
                    return destination;
                }
            }
        }

        return null;
    }

    private Quaternion ResolveRotationForTargetObject(TargetObjectAttribute target)
    {
        if (target == null)
        {
            return GetDefaultTargetRotation();
        }

        return target.GetGrabTargetOrientation();
    }

    private Quaternion GetOppositeOrientation(Quaternion targetOrientation)
    {
        Vector3 forward = targetOrientation * Vector3.forward;
        Vector3 up = targetOrientation * Vector3.up;

        if (forward.sqrMagnitude <= 0.000001f)
        {
            return targetOrientation;
        }

        forward.Normalize();

        if (up.sqrMagnitude <= 0.000001f)
        {
            up = Vector3.up;
        }

        up.Normalize();

        Vector3 oppositeForward = -forward;

        if (Mathf.Abs(Vector3.Dot(oppositeForward, up)) > 0.98f)
        {
            up = Vector3.Cross(Vector3.right, oppositeForward);

            if (up.sqrMagnitude <= 0.000001f)
            {
                up = Vector3.Cross(Vector3.forward, oppositeForward);
            }

            up.Normalize();
        }

        return Quaternion.LookRotation(oppositeForward, up);
    }

    private Quaternion GetPlacementRotation(Vector3 placementPosition)
    {
        Vector3 forward = Vector3.down;
        Vector3 up = Vector3.forward;

        if (robotBase != null)
        {
            Vector3 baseDirection = robotBase.position - placementPosition;

            if (baseDirection.sqrMagnitude > 0.000001f)
            {
                // Keep the gripper forward direction strictly downward, and make its up axis
                // point as close as possible toward the base on the horizontal plane.
                Vector3 projectedUp = Vector3.ProjectOnPlane(baseDirection, forward);

                if (projectedUp.sqrMagnitude > 0.000001f)
                {
                    up = projectedUp.normalized;
                }
            }
        }

        return Quaternion.LookRotation(forward, up);
    }


    private Quaternion ApplyForwardBlendTowardBase(
        Quaternion originalRotation,
        Vector3 referencePosition,
        float blendAmount
    )
    {
        blendAmount = Mathf.Clamp01(blendAmount);

        if (blendAmount <= 0f || robotBase == null)
        {
            return originalRotation;
        }

        Vector3 originalForward = originalRotation * Vector3.forward;
        Vector3 originalUp = originalRotation * Vector3.up;

        if (originalForward.sqrMagnitude <= 0.000001f)
        {
            return originalRotation;
        }

        Vector3 directionToBase =  referencePosition - robotBase.position;

        if (directionToBase.sqrMagnitude <= 0.000001f)
        {
            return originalRotation;
        }

        originalForward.Normalize();
        directionToBase.Normalize();

        Vector3 blendedForward = Vector3.Slerp(
            originalForward,
            directionToBase,
            blendAmount
        );

        if (blendedForward.sqrMagnitude <= 0.000001f)
        {
            blendedForward = Vector3.Lerp(
                originalForward,
                directionToBase,
                blendAmount
            );
        }

        if (blendedForward.sqrMagnitude <= 0.000001f)
        {
            return originalRotation;
        }

        blendedForward.Normalize();

        return CreateRotationFromForwardAndPreferredUp(
            blendedForward,
            originalUp,
            originalRotation
        );
    }

    private Quaternion CreateRotationFromForwardAndPreferredUp(
        Vector3 forward,
        Vector3 preferredUp,
        Quaternion fallbackRotation
    )
    {
        if (forward.sqrMagnitude <= 0.000001f)
        {
            return fallbackRotation;
        }

        forward.Normalize();

        Vector3 up = Vector3.ProjectOnPlane(preferredUp, forward);

        if (up.sqrMagnitude <= 0.000001f)
        {
            Vector3 fallbackRight = fallbackRotation * Vector3.right;
            up = Vector3.ProjectOnPlane(fallbackRight, forward);
        }

        if (up.sqrMagnitude <= 0.000001f)
        {
            up = Vector3.ProjectOnPlane(Vector3.up, forward);
        }

        if (up.sqrMagnitude <= 0.000001f)
        {
            up = Vector3.ProjectOnPlane(Vector3.forward, forward);
        }

        if (up.sqrMagnitude <= 0.000001f)
        {
            return fallbackRotation;
        }

        up.Normalize();

        return Quaternion.LookRotation(forward, up);
    }

    private Quaternion GetDefaultTargetRotation()
    {
        return Quaternion.Euler(defaultTargetEulerAngles);
    }

    private string NormalizeLabelText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value
            .Replace(" ", "")
            .Replace("，", "")
            .Replace("。", "")
            .Replace("？", "")
            .Replace("！", "")
            .Replace(",", "")
            .Replace(".", "")
            .Replace("?", "")
            .Replace("!", "")
            .Trim()
            .ToLowerInvariant();
    }

    private void UpdateStatusUI(string message)
    {
        string status =
            "State: " + CurrentState + "\n" +
            "Grip: " + GripState + "\n" +
            "Held: " + (HeldTarget != null ? HeldTarget.GetDisplayName() : "-") + "\n" +
            "Message: " + message;

        if (statusText != null)
        {
            statusText.text = status;
        }

        Log("[RobotArmTaskController] " + status);
    }

    private void Log(string message)
    {
        if (printDebug)
        {
            Debug.Log(message);
        }
    }

    private void LogWarning(string message)
    {
        if (printDebug)
        {
            Debug.LogWarning(message);
        }
    }
}