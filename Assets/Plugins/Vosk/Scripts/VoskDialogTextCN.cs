using UnityEngine;
using UnityEngine.UI;
using System.Text.RegularExpressions;

public class VoskDialogTextCN : MonoBehaviour
{
    public VoskSpeechToText VoskSpeechToText;
    public Text DialogText;

    // Basic dialogue commands
    Regex hi_regex = new Regex(@"(你好|您好|嗨)", RegexOptions.IgnoreCase);
    Regex who_regex = new Regex(@"(你是谁|你是什么|你叫什么)", RegexOptions.IgnoreCase);
    Regex pass_regex = new Regex(@"(好的|好|可以|行|开始吧|走吧)", RegexOptions.IgnoreCase);
    Regex help_regex = new Regex(@"(帮我|帮忙|帮助|提示)", RegexOptions.IgnoreCase);

    // Move an item to the right bank
    Regex goat_regex = new Regex(@"(羊|山羊|带羊|带山羊|送羊|送山羊|把羊带过去|把山羊带过去|先带羊|先带山羊)", RegexOptions.IgnoreCase);
    Regex wolf_regex = new Regex(@"(狼|带狼|送狼|把狼带过去|先带狼)", RegexOptions.IgnoreCase);
    Regex cabbage_regex = new Regex(@"(白菜|卷心菜|菜|带白菜|带卷心菜|送白菜|把白菜带过去|先带白菜)", RegexOptions.IgnoreCase);

    // Move an item back to the left bank
    Regex goat_back_regex = new Regex(@"(羊回来|山羊回来|带羊回来|带山羊回来|把羊带回来|把山羊带回来|送羊回来|送山羊回来)", RegexOptions.IgnoreCase);
    Regex wolf_back_regex = new Regex(@"(狼回来|带狼回来|把狼带回来|送狼回来)", RegexOptions.IgnoreCase);
    Regex cabbage_back_regex = new Regex(@"(白菜回来|卷心菜回来|菜回来|带白菜回来|把白菜带回来|送白菜回来)", RegexOptions.IgnoreCase);

    // Move only the farmer
    Regex forward_regex = new Regex(@"(过河|过去|农夫过去|人过去|空船过去)", RegexOptions.IgnoreCase);
    Regex back_regex = new Regex(@"(回来|返回|回去|农夫回来|人回来|空船回来)", RegexOptions.IgnoreCase);

    // State: true = left bank, false = right bank
    bool goat_left;
    bool wolf_left;
    bool cabbage_left;
    bool man_left;

    void Awake()
    {
        if (VoskSpeechToText != null)
        {
            VoskSpeechToText.OnTranscriptionResult += OnTranscriptionResult;
        }

        ResetState();
    }

    void OnDestroy()
    {
        if (VoskSpeechToText != null)
        {
            VoskSpeechToText.OnTranscriptionResult -= OnTranscriptionResult;
        }
    }

    void ResetState()
    {
        goat_left = true;
        wolf_left = true;
        cabbage_left = true;
        man_left = true;
    }

    void CheckState()
    {
        if (goat_left && wolf_left && !man_left)
        {
            AddFinalResponse("狼吃掉了羊，请重新开始。");
            return;
        }

        if (goat_left && cabbage_left && !man_left)
        {
            AddFinalResponse("羊吃掉了白菜，请重新开始。");
            return;
        }

        if (!goat_left && !wolf_left && man_left)
        {
            AddFinalResponse("狼吃掉了羊，请重新开始。");
            return;
        }

        if (!goat_left && !cabbage_left && man_left)
        {
            AddFinalResponse("羊吃掉了白菜，请重新开始。");
            return;
        }

        if (!goat_left && !wolf_left && !cabbage_left && !man_left)
        {
            AddFinalResponse("成功了！我们再来一次。");
            return;
        }

        AddResponse("好的，下一步怎么做？");
    }

    void Say(string response)
    {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        // macOS text-to-speech. If this causes microphone feedback, comment out this line.
        System.Diagnostics.Process.Start("/usr/bin/say", response);
#else
        Debug.Log("[TTS] " + response);
#endif
    }

    void AddFinalResponse(string response)
    {
        Say(response);

        if (DialogText != null)
        {
            DialogText.text = response + "\n";
        }

        ResetState();
    }

    void AddResponse(string response)
    {
        Say(response);

        if (DialogText == null)
        {
            return;
        }

        DialogText.text = response + "\n\n";

        DialogText.text += "农夫：" + (man_left ? "左岸" : "右岸") + "\n";
        DialogText.text += "狼：" + (wolf_left ? "左岸" : "右岸") + "\n";
        DialogText.text += "羊：" + (goat_left ? "左岸" : "右岸") + "\n";
        DialogText.text += "白菜：" + (cabbage_left ? "左岸" : "右岸") + "\n";

        DialogText.text += "\n";
    }

    string NormalizeText(string text)
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

    private void OnTranscriptionResult(string obj)
    {
        Debug.Log(obj);

        var result = new RecognitionResult(obj);

        foreach (RecognizedPhrase p in result.Phrases)
        {
            string text = NormalizeText(p.Text);

            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            Debug.Log("[Recognized Chinese Text] " + text);

            if (hi_regex.IsMatch(text))
            {
                AddResponse("你好。");
                return;
            }

            if (who_regex.IsMatch(text))
            {
                AddResponse("我是机器人老师。");
                return;
            }

            if (pass_regex.IsMatch(text))
            {
                AddResponse("好的。");
                return;
            }

            if (help_regex.IsMatch(text))
            {
                AddResponse("你可以先想想怎么过河。");
                return;
            }

            if (goat_back_regex.IsMatch(text))
            {
                if (goat_left == true)
                {
                    AddResponse("羊还在左岸。");
                }
                else if (man_left == true)
                {
                    AddResponse("农夫还在左岸。");
                }
                else
                {
                    goat_left = true;
                    man_left = true;
                    CheckState();
                }

                return;
            }

            if (wolf_back_regex.IsMatch(text))
            {
                if (wolf_left == true)
                {
                    AddResponse("狼还在左岸。");
                }
                else if (man_left == true)
                {
                    AddResponse("农夫还在左岸。");
                }
                else
                {
                    wolf_left = true;
                    man_left = true;
                    CheckState();
                }

                return;
            }

            if (cabbage_back_regex.IsMatch(text))
            {
                if (cabbage_left == true)
                {
                    AddResponse("白菜还在左岸。");
                }
                else if (man_left == true)
                {
                    AddResponse("农夫还在左岸。");
                }
                else
                {
                    cabbage_left = true;
                    man_left = true;
                    CheckState();
                }

                return;
            }

            if (goat_regex.IsMatch(text))
            {
                if (goat_left == false)
                {
                    AddResponse("羊已经在右岸了。");
                }
                else if (man_left == false)
                {
                    AddResponse("农夫已经在右岸了。");
                }
                else
                {
                    goat_left = false;
                    man_left = false;
                    CheckState();
                }

                return;
            }

            if (wolf_regex.IsMatch(text))
            {
                if (wolf_left == false)
                {
                    AddResponse("狼已经在右岸了。");
                }
                else if (man_left == false)
                {
                    AddResponse("农夫已经在右岸了。");
                }
                else
                {
                    wolf_left = false;
                    man_left = false;
                    CheckState();
                }

                return;
            }

            if (cabbage_regex.IsMatch(text))
            {
                if (cabbage_left == false)
                {
                    AddResponse("白菜已经在右岸了。");
                }
                else if (man_left == false)
                {
                    AddResponse("农夫已经在右岸了。");
                }
                else
                {
                    cabbage_left = false;
                    man_left = false;
                    CheckState();
                }

                return;
            }

            if (forward_regex.IsMatch(text))
            {
                if (man_left == false)
                {
                    AddResponse("农夫已经在右岸了。");
                }
                else
                {
                    man_left = false;
                    CheckState();
                }

                return;
            }

            if (back_regex.IsMatch(text))
            {
                if (man_left == true)
                {
                    AddResponse("农夫还在左岸。");
                }
                else
                {
                    man_left = true;
                    CheckState();
                }

                return;
            }
        }

        // Important: do not speak on unknown results by default.
        // Otherwise Vosk may misrecognize background noise, then macOS "say" speaks,
        // then the microphone hears that speech again and creates a feedback loop.
        if (result.Phrases.Length > 0 && result.Phrases[0].Text != "")
        {
            Debug.Log("[Unmatched Voice Command] " + result.Phrases[0].Text);
            // AddResponse("我没听懂。");
        }
    }
}