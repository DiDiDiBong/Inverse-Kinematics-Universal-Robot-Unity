using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class RobotArmVoiceCommandExtractor : MonoBehaviour
{
    [Header("STT Input")]
    public SttProviderBase SttProvider;

    [Header("UI")]
    public Text DialogText;

    [Header("Debug")]
    public bool PrintRawResult = true;
    public bool PrintParsedCommand = true;

    [Tooltip("Usually keep this false. Let the STT provider control its own startup.")]
    public bool AutoStartListening = false;

    public event Action<RobotArmCommand> OnCommandExtracted;

    private readonly List<string> commandHistory = new List<string>();
    private const int MaxHistoryCount = 6;

    private void Awake()
    {
        if (SttProvider != null)
        {
            SttProvider.OnResult += OnSttResult;
            SttProvider.OnStatus += OnSttStatus;
        }

        ShowIdleMessage();
    }

    private void Start()
    {
        if (AutoStartListening && SttProvider != null && !SttProvider.IsRunning)
        {
            SttProvider.StartListening();
        }
    }

    private void OnDestroy()
    {
        if (SttProvider != null)
        {
            SttProvider.OnResult -= OnSttResult;
            SttProvider.OnStatus -= OnSttStatus;
        }
    }

    private void OnSttStatus(string status)
    {
        if (PrintRawResult)
        {
            Debug.Log("[STT Status] " + status);
        }
    }

    private void OnSttResult(SttResult result)
    {
        if (result == null)
        {
            return;
        }

        if (PrintRawResult)
        {
            Debug.Log("[STT Result] " + result.source + ": " + result.bestText);
            Debug.Log("[STT Raw] " + result.rawResult);
        }

        List<SttCandidate> candidates = result.candidates;

        if (candidates == null || candidates.Count == 0)
        {
            candidates = new List<SttCandidate>
            {
                new SttCandidate(result.bestText, 0f)
            };
        }

        string firstUnmatchedText = "";

        foreach (SttCandidate candidate in candidates)
        {
            string text = NormalizeText(candidate.text);

            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (string.IsNullOrEmpty(firstUnmatchedText))
            {
                firstUnmatchedText = text;
            }

            RobotArmCommand command = ParseCommand(text, candidate.confidence);

            if (command.isValid)
            {
                AddCommand(command);
                OnCommandExtracted?.Invoke(command);
                return;
            }
        }

        if (!string.IsNullOrEmpty(firstUnmatchedText))
        {
            Debug.Log("[Unmatched Voice Command] " + firstUnmatchedText);
            ShowUnmatchedMessage(firstUnmatchedText);
        }
    }

    private RobotArmCommand ParseCommand(string text, float confidence)
    {
        RobotArmCommand command = new RobotArmCommand
        {
            rawText = text,
            confidence = confidence,
            target = new TargetObjectQuery
            {
                color = ParseColor(text),
                objectType = ParseObjectType(text)
            },
            destination = ParseDestination(text)
        };

        command.taskType = ParseTaskType(text);
        command.primaryAction = GetPrimaryAction(command.taskType);
        command.isValid = ValidateCommand(command);
        command.debugMessage = BuildCommandLine(command);

        return command;
    }

    private RobotArmTaskType ParseTaskType(string text)
    {
        if (ContainsAny(text, "中断", "打断", "取消当前", "取消任务"))
        {
            return RobotArmTaskType.Interrupt;
        }

        if (ContainsAny(text, "停止", "停下", "急停", "别动"))
        {
            return RobotArmTaskType.Stop;
        }

        if (ContainsAny(text, "暂停"))
        {
            return RobotArmTaskType.Pause;
        }

        if (ContainsAny(text, "继续", "恢复", "接着"))
        {
            return RobotArmTaskType.Resume;
        }

        if (ContainsAny(text, "复位", "回到初始", "回原点", "归位"))
        {
            return RobotArmTaskType.Reset;
        }

        if (ContainsAny(text, "扫描", "识别", "看看", "看一下"))
        {
            return RobotArmTaskType.Scan;
        }

        if (ContainsAny(text, "放下", "放开", "松开", "释放"))
        {
            return RobotArmTaskType.Release;
        }

        if (ContainsAny(text,
                "拿过来", "拿来", "取过来", "取来", "抓过来",
                "递给我", "给我", "送过来", "移过来",
                "拿过去", "送过去", "移过去", "放过去",
                "放到那边", "放那边", "拿到那边", "移动到那边","拿去那边",
                "送走", "拿走", "移走"))
        {
            return RobotArmTaskType.PickAndMove;
        }

        if (ContainsAny(text, "拿起", "抓取", "抓起", "夹起", "拾取", "拿住"))
        {
            return RobotArmTaskType.Pick;
        }

        return RobotArmTaskType.None;
    }

    private RobotArmActionType GetPrimaryAction(RobotArmTaskType taskType)
    {
        switch (taskType)
        {
            case RobotArmTaskType.Pick:
            case RobotArmTaskType.PickAndMove:
            case RobotArmTaskType.MoveTo:
            case RobotArmTaskType.Reset:
                return RobotArmActionType.MovePath;

            case RobotArmTaskType.Release:
                return RobotArmActionType.Release;

            case RobotArmTaskType.Stop:
                return RobotArmActionType.StopMotion;

            case RobotArmTaskType.Pause:
                return RobotArmActionType.PauseMotion;

            case RobotArmTaskType.Resume:
                return RobotArmActionType.ResumeMotion;

            case RobotArmTaskType.Scan:
                return RobotArmActionType.ScanScene;

            default:
                return RobotArmActionType.None;
        }
    }

    private bool ValidateCommand(RobotArmCommand command)
    {
        switch (command.taskType)
        {
            case RobotArmTaskType.Stop:
            case RobotArmTaskType.Pause:
            case RobotArmTaskType.Resume:
            case RobotArmTaskType.Interrupt:
            case RobotArmTaskType.Reset:
            case RobotArmTaskType.Scan:
            case RobotArmTaskType.Release:
                return true;

            case RobotArmTaskType.Pick:
            case RobotArmTaskType.PickAndMove:
                return command.target != null && command.target.HasObjectType;

            default:
                return false;
        }
    }

    private string ParseColor(string text)
    {
        if (ContainsAny(text, "蓝色", "蓝")) return "蓝色";
        if (ContainsAny(text, "红色", "红")) return "红色";
        if (ContainsAny(text, "绿色", "绿")) return "绿色";
        if (ContainsAny(text, "黄色", "黄")) return "黄色";
        if (ContainsAny(text, "黑色", "黑")) return "黑色";
        if (ContainsAny(text, "白色", "白")) return "白色";
        if (ContainsAny(text, "紫色", "紫")) return "紫色";
        if (ContainsAny(text, "橙色", "橙")) return "橙色";
        if (ContainsAny(text, "灰色", "灰")) return "灰色";

        return "未指定";
    }

    private string ParseObjectType(string text)
    {
        if (ContainsAny(text, "汉堡", "汉堡包")) return "汉堡";
        if (ContainsAny(text, "小球", "球体", "圆球", "球", "求")) return "球";
        if (ContainsAny(text, "方块", "立方体", "块")) return "方块";
        if (ContainsAny(text, "盒子", "箱子")) return "盒子";
        if (ContainsAny(text, "杯子", "水杯")) return "杯子";
        if (ContainsAny(text, "瓶子", "水瓶")) return "瓶子";
        if (ContainsAny(text, "积木")) return "积木";

        return "";
    }

    private RobotArmDestinationType ParseDestination(string text)
    {
        if (ContainsAny(text, "我这里", "我这边", "我面前", "这边", "这里", "过来"))
        {
            return RobotArmDestinationType.UserArea;
        }

        if (ContainsAny(text, "那边", "那里", "过去", "远处", "送走", "拿走", "移走"))
        {
            return RobotArmDestinationType.FarArea;
        }

        if (ContainsAny(text, "左边", "左侧"))
        {
            return RobotArmDestinationType.LeftArea;
        }

        if (ContainsAny(text, "右边", "右侧"))
        {
            return RobotArmDestinationType.RightArea;
        }

        if (ContainsAny(text, "桌上", "桌子上", "桌面"))
        {
            return RobotArmDestinationType.Tabletop;
        }

        if (ContainsAny(text, "盒子里", "箱子里"))
        {
            return RobotArmDestinationType.BoxInside;
        }

        return RobotArmDestinationType.None;
    }

    private void AddCommand(RobotArmCommand command)
    {
        string commandLine = BuildCommandLine(command);
        commandHistory.Insert(0, commandLine);

        while (commandHistory.Count > MaxHistoryCount)
        {
            commandHistory.RemoveAt(commandHistory.Count - 1);
        }

        if (PrintParsedCommand)
        {
            Debug.Log("[RobotArm Command Extractor] " + commandLine);
            Debug.Log("[RobotArm Command Data] " + command);
        }

        UpdateDialog(command, commandLine);
    }

    private string BuildCommandLine(RobotArmCommand command)
    {
        switch (command.taskType)
        {
            case RobotArmTaskType.Stop:
                return "提取指令：停止机械臂当前动作";

            case RobotArmTaskType.Pause:
                return "提取指令：暂停机械臂当前动作";

            case RobotArmTaskType.Resume:
                return "提取指令：继续机械臂当前动作";

            case RobotArmTaskType.Interrupt:
                return "提取指令：中断当前任务";

            case RobotArmTaskType.Reset:
                return "提取指令：机械臂复位";

            case RobotArmTaskType.Scan:
                return "提取指令：扫描/识别当前场景";

            case RobotArmTaskType.Release:
                return "提取指令：释放当前夹持物体";

            case RobotArmTaskType.Pick:
                return "提取指令：抓取「" + command.target + "」";

            case RobotArmTaskType.PickAndMove:
                return "提取指令：抓取「" + command.target + "」并移动到 " + command.destination;

            default:
                return "提取指令：未知";
        }
    }

    private void UpdateDialog(RobotArmCommand command, string commandLine)
    {
        if (DialogText == null)
        {
            return;
        }

        DialogText.text = "识别文本：" + command.rawText + "\n";
        DialogText.text += commandLine + "\n\n";

        DialogText.text += "结构化结果\n";
        DialogText.text += "任务：" + command.taskType + "\n";
        DialogText.text += "动作：" + command.primaryAction + "\n";
        DialogText.text += "颜色：" + EmptyToDash(command.target.color) + "\n";
        DialogText.text += "物体：" + EmptyToDash(command.target.objectType) + "\n";
        DialogText.text += "目标区域：" + command.destination + "\n\n";

        DialogText.text += "指令历史\n";
        foreach (string history in commandHistory)
        {
            DialogText.text += "- " + history + "\n";
        }
    }

    private void ShowIdleMessage()
    {
        if (DialogText == null)
        {
            return;
        }

        DialogText.text = "等待语音指令...\n\n";
        DialogText.text += "示例：\n";
        DialogText.text += "把蓝色的球拿过来\n";
        DialogText.text += "把红色汉堡拿过去\n";
        DialogText.text += "拿起绿色方块\n";
        DialogText.text += "把蓝色的球送走\n";
        DialogText.text += "放下\n";
        DialogText.text += "暂停 / 继续\n";
        DialogText.text += "中断 / 停止 / 复位\n";
    }

    private void ShowUnmatchedMessage(string text)
    {
        if (DialogText == null)
        {
            return;
        }

        DialogText.text = "听到了，但没有提取到有效机械臂指令。\n";
        DialogText.text += "识别文本：" + text + "\n\n";
        DialogText.text += "可以尝试：把蓝色的球拿过来 / 把红色汉堡拿过去 / 停止 / 复位";
    }

    private string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

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

    private bool ContainsAny(string text, params string[] keywords)
    {
        foreach (string keyword in keywords)
        {
            if (text.Contains(keyword))
            {
                return true;
            }
        }

        return false;
    }

    private string EmptyToDash(string value)
    {
        return string.IsNullOrEmpty(value) ? "-" : value;
    }
}