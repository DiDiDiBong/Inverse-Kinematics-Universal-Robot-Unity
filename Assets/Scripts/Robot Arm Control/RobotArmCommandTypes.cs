using System;
using System.Collections.Generic;
using UnityEngine;

public enum RobotArmRuntimeState
{
    Idle,
    Running,
    Paused,
    Interrupted,
    Stopped,
    Error
}

public enum RobotArmTaskType
{
    None,
    Pick,
    PickAndMove,
    MoveTo,
    Release,
    Stop,
    Pause,
    Resume,
    Interrupt,
    Reset,
    Scan
}

public enum RobotArmActionType
{
    None,
    MovePath,
    Grab,
    Release,
    StopMotion,
    PauseMotion,
    ResumeMotion,
    ResetPose,
    ScanScene
}

public enum RobotArmGripState
{
    Open,
    Closed,
    Holding
}

public enum RobotArmDestinationType
{
    None,
    UserArea,
    FarArea,
    LeftArea,
    RightArea,
    Tabletop,
    BoxInside,
    Current
}

[Serializable]
public class TargetObjectQuery
{
    public string color = "";
    public string objectType = "";

    public bool HasObjectType => !string.IsNullOrEmpty(objectType);

    public bool HasColor =>
        !string.IsNullOrEmpty(color) &&
        color != "未指定";

    public override string ToString()
    {
        string colorText = HasColor ? color : "";
        string objectText = string.IsNullOrEmpty(objectType) ? "目标物体" : objectType;
        return colorText + objectText;
    }
}

[Serializable]
public class RobotArmCommand
{
    public string rawText = "";
    public RobotArmTaskType taskType = RobotArmTaskType.None;
    public RobotArmActionType primaryAction = RobotArmActionType.None;
    public TargetObjectQuery target = new TargetObjectQuery();
    public RobotArmDestinationType destination = RobotArmDestinationType.None;
    public bool isValid = false;
    public float confidence = 0f;
    public string debugMessage = "";

    public bool RequiresTargetObject
    {
        get
        {
            return taskType == RobotArmTaskType.Pick ||
                   taskType == RobotArmTaskType.PickAndMove;
        }
    }

    public override string ToString()
    {
        return $"Task={taskType}, Action={primaryAction}, Target={target}, Destination={destination}, Raw={rawText}";
    }
}

[Serializable]
public class RobotArmActionStep
{
    public RobotArmActionType actionType = RobotArmActionType.None;

    public Transform targetTransform;
    public Vector3 targetPosition;
    public Quaternion targetRotation = Quaternion.identity;

    public TargetObjectAttribute targetObject;

    public float waitAfterAction = 0.1f;
    public string description = "";

    public bool HasTransformTarget => targetTransform != null;

    public RobotArmActionStep()
    {
    }

    public RobotArmActionStep(RobotArmActionType actionType, string description = "")
    {
        this.actionType = actionType;
        this.description = description;
    }
}

[Serializable]
public class RobotArmTask
{
    public RobotArmCommand sourceCommand;
    public List<RobotArmActionStep> actions = new List<RobotArmActionStep>();
    public int currentActionIndex = -1;

    public RobotArmTask(RobotArmCommand command)
    {
        sourceCommand = command;
    }
}