using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;

public class RobotArmVoiceCommandController : MonoBehaviour
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

    public event Action<ParsedRobotArmVoiceCommand> OnCommandParsed;

    private readonly List<string> commandHistory = new List<string>();
    private const int MaxHistoryCount = 6;

    [Serializable]
    public class ParsedRobotArmVoiceCommand
    {
        public string rawText;
        public string action;
        public string color;
        public string objectType;
        public string destination;
        public bool isValid;
    }

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

            ParsedRobotArmVoiceCommand command = ParseCommand(text);

            if (command.isValid)
            {
                AddCommand(command);
                OnCommandParsed?.Invoke(command);
                return;
            }
        }

        if (!string.IsNullOrEmpty(firstUnmatchedText))
        {
            Debug.Log("[Unmatched Voice Command] " + firstUnmatchedText);
            ShowUnmatchedMessage(firstUnmatchedText);
        }
    }

    private ParsedRobotArmVoiceCommand ParseCommand(string text)
    {
        ParsedRobotArmVoiceCommand command = new ParsedRobotArmVoiceCommand
        {
            rawText = text,
            action = ParseAction(text),
            color = ParseColor(text),
            objectType = ParseObjectType(text),
            destination = ParseDestination(text),
            isValid = false
        };

        if (command.action == "停止" ||
            command.action == "复位" ||
            command.action == "扫描" ||
            command.action == "放下")
        {
            command.isValid = true;
            return command;
        }

        if (!string.IsNullOrEmpty(command.action) && !string.IsNullOrEmpty(command.objectType))
        {
            command.isValid = true;
        }

        return command;
    }

    private string ParseAction(string text)
    {
        if (ContainsAny(text, "拿过来", "拿来", "取过来", "取来", "抓过来", "递给我", "给我", "送过来", "移过来"))
        {
            return "拿过来";
        }

        if (ContainsAny(text, "拿过去", "送过去", "移过去", "放过去", "放到那边", "放那边", "拿到那边", "移动到那边", "送走", "拿走", "移走"))
        {
            return "拿过去";
        }

        if (ContainsAny(text, "拿起", "抓取", "抓起", "夹起", "拾取", "拿住"))
        {
            return "拿起";
        }

        if (ContainsAny(text, "放下", "放开", "松开", "释放"))
        {
            return "放下";
        }

        if (ContainsAny(text, "停止", "停下", "暂停", "别动"))
        {
            return "停止";
        }

        if (ContainsAny(text, "复位", "回到初始", "回原点", "归位"))
        {
            return "复位";
        }

        if (ContainsAny(text, "扫描", "识别", "看看", "看一下"))
        {
            return "扫描";
        }

        return "";
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

    private string ParseDestination(string text)
    {
        if (ContainsAny(text, "我这里", "我这边", "我面前", "这边", "这里", "过来"))
        {
            return "用户附近";
        }

        if (ContainsAny(text, "那边", "那里", "过去", "远处", "送走", "拿走", "移走"))
        {
            return "远端区域";
        }

        if (ContainsAny(text, "左边", "左侧"))
        {
            return "左侧区域";
        }

        if (ContainsAny(text, "右边", "右侧"))
        {
            return "右侧区域";
        }

        if (ContainsAny(text, "桌上", "桌子上"))
        {
            return "桌面";
        }

        if (ContainsAny(text, "盒子里", "箱子里"))
        {
            return "盒子内部";
        }

        return "未指定";
    }

    private void AddCommand(ParsedRobotArmVoiceCommand command)
    {
        string commandLine = BuildCommandLine(command);
        commandHistory.Insert(0, commandLine);

        while (commandHistory.Count > MaxHistoryCount)
        {
            commandHistory.RemoveAt(commandHistory.Count - 1);
        }

        if (PrintParsedCommand)
        {
            Debug.Log("[RobotArm Voice Command] " + commandLine);
        }

        UpdateDialog(command, commandLine);
    }

    private string BuildCommandLine(ParsedRobotArmVoiceCommand command)
    {
        if (command.action == "停止")
        {
            return "执行意图：停止机械臂当前动作";
        }

        if (command.action == "复位")
        {
            return "执行意图：机械臂复位";
        }

        if (command.action == "扫描")
        {
            return "执行意图：扫描/识别当前场景";
        }

        if (command.action == "放下")
        {
            return "执行意图：放下当前夹持物体";
        }

        string colorText = command.color == "未指定" ? "" : command.color;
        string targetText = colorText + command.objectType;

        if (command.action == "拿过来")
        {
            return "执行意图：抓取「" + targetText + "」并移动到用户附近";
        }

        if (command.action == "拿过去")
        {
            return "执行意图：抓取「" + targetText + "」并移动到远端区域";
        }

        if (command.action == "拿起")
        {
            return "执行意图：抓取「" + targetText + "」";
        }

        return "执行意图：未知";
    }

    private void UpdateDialog(ParsedRobotArmVoiceCommand command, string commandLine)
    {
        if (DialogText == null)
        {
            return;
        }

        DialogText.text = "识别文本：" + command.rawText + "\n";
        DialogText.text += commandLine + "\n\n";

        DialogText.text += "解析结果\n";
        DialogText.text += "动作：" + EmptyToDash(command.action) + "\n";
        DialogText.text += "颜色：" + EmptyToDash(command.color) + "\n";
        DialogText.text += "物体：" + EmptyToDash(command.objectType) + "\n";
        DialogText.text += "目标位置：" + EmptyToDash(command.destination) + "\n\n";

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
        DialogText.text += "停止\n";
        DialogText.text += "复位\n";
    }

    private void ShowUnmatchedMessage(string text)
    {
        if (DialogText == null)
        {
            return;
        }

        DialogText.text = "听到了，但没有匹配到有效指令。\n";
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