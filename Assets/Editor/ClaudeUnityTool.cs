// ClaudeUnityTool.cs
//
// 【配置場所】
//   このファイルは Assets/Editor/ フォルダの中に置いてください。
//   （Editorフォルダの外に置くと、ビルド対象になったりエラーになったりします）
//
// 【事前に必要なパッケージ】(Window > Package Manager から追加)
//   1. Editor Coroutines   … Add package by name: com.unity.editorcoroutines
//   2. Newtonsoft Json     … Add package by name: com.unity.nuget.newtonsoft-json
//
// 【使い方】
//   Unityメニューの Tools > Claude Assistant からウィンドウを開いて、
//   指示テキストを入力して「送信」ボタンを押す。
//
// 【重要】
//   ApiKey は動作確認用に直接書いていますが、コミットしないよう注意してください。
//   (.gitignoreに入れる、環境変数から読むなどの対策を推奨)

using System;
using System.Collections;
using System.Text;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;

public class ClaudeUnityTool : EditorWindow
{
    // ==== 設定 ====
    private const string ApiKey = ""; // TODO: 自分のAPIキーに置き換える
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-sonnet-4-6";

    // ==== UI状態 ====
    private string instruction = "選択中のオブジェクトの右3mに、同じ大きさのCubeを作って";
    private string log = "";
    private Vector2 scroll;
    private bool isRunning = false;

    [MenuItem("Tools/Claude Assistant")]
    public static void ShowWindow()
    {
        GetWindow<ClaudeUnityTool>("Claude Assistant");
    }

    private void OnGUI()
    {
        GUILayout.Label("指示を入力", EditorStyles.boldLabel);
        instruction = EditorGUILayout.TextArea(instruction, GUILayout.Height(60));

        GUI.enabled = !isRunning && !string.IsNullOrEmpty(instruction);
        if (GUILayout.Button(isRunning ? "実行中..." : "送信"))
        {
            log = "";
            this.StartCoroutine(RunClaude(instruction));
        }
        GUI.enabled = true;

        GUILayout.Space(8);
        GUILayout.Label("ログ", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        EditorGUILayout.TextArea(log, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    // ---- メインフロー ----

    private IEnumerator RunClaude(string userInstruction)
    {
        isRunning = true;

        // 選択中オブジェクトの情報をコンテキストとして付与する
        var selected = Selection.activeGameObject;
        string context = selected != null
            ? $"現在選択中のオブジェクト: {selected.name}, position=({selected.transform.position.x:F2},{selected.transform.position.y:F2},{selected.transform.position.z:F2})"
            : "現在選択中のオブジェクトはありません。";

        var messages = new JArray
        {
            new JObject
            {
                ["role"] = "user",
                ["content"] = $"{context}\n\n指示: {userInstruction}"
            }
        };

        yield return SendRequest(messages, BuildTools());

        isRunning = false;
        Repaint();
    }

    // Claudeに渡す「使ってよい関数」の定義
    private JArray BuildTools()
    {
        return new JArray
        {
            new JObject
            {
                ["name"] = "create_primitive",
                ["description"] = "シーンにプリミティブオブジェクトを1つ作成する",
                ["input_schema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["type"] = new JObject { ["type"] = "string", ["enum"] = new JArray("Cube", "Sphere", "Cylinder", "Plane") },
                        ["x"] = new JObject { ["type"] = "number" },
                        ["y"] = new JObject { ["type"] = "number" },
                        ["z"] = new JObject { ["type"] = "number" },
                        ["name"] = new JObject { ["type"] = "string" }
                    },
                    ["required"] = new JArray("type", "x", "y", "z", "name")
                }
            },
            new JObject
            {
                ["name"] = "set_position",
                ["description"] = "既存オブジェクトの位置を変更する",
                ["input_schema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["targetName"] = new JObject { ["type"] = "string" },
                        ["x"] = new JObject { ["type"] = "number" },
                        ["y"] = new JObject { ["type"] = "number" },
                        ["z"] = new JObject { ["type"] = "number" }
                    },
                    ["required"] = new JArray("targetName", "x", "y", "z")
                }
            }
        };
    }

    // Claudeにリクエストを送り、tool_useが返ってきたら実行して再送信する（会話が終わるまで再帰）
    private IEnumerator SendRequest(JArray messages, JArray tools)
    {
        var body = new JObject
        {
            ["model"] = Model,
            ["max_tokens"] = 1024,
            ["tools"] = tools,
            ["messages"] = messages
        };

        JObject response = null;
        yield return PostToClaude(body.ToString(), r => response = r);

        if (response == null) yield break; // エラー時はPostToClaude内でログ出力済み

        var content = response["content"] as JArray;
        if (content == null)
        {
            AppendLog("応答の解析に失敗しました: " + response);
            yield break;
        }

        bool hasToolUse = false;
        var assistantContent = new JArray();
        var toolResults = new JArray();

        foreach (var block in content)
        {
            assistantContent.Add(block);
            var type = (string)block["type"];

            if (type == "text")
            {
                AppendLog("Claude: " + (string)block["text"]);
            }
            else if (type == "tool_use")
            {
                hasToolUse = true;
                string toolName = (string)block["name"];
                string toolUseId = (string)block["id"];
                var input = block["input"] as JObject;

                string result = ExecuteTool(toolName, input);
                AppendLog($"[実行] {toolName}({input}) -> {result}");

                toolResults.Add(new JObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = toolUseId,
                    ["content"] = result
                });
            }
        }

        if (hasToolUse)
        {
            // アシスタントの応答(tool_use含む)と、実行結果(tool_result)を会話履歴に積んで再送信
            messages.Add(new JObject { ["role"] = "assistant", ["content"] = assistantContent });
            messages.Add(new JObject { ["role"] = "user", ["content"] = toolResults });

            yield return SendRequest(messages, tools);
        }
    }

    // ---- 実際にUnity上で実行する処理 ----
    // ここに関数を追加していくことで、Claudeに許可する操作を増やせる

    private string ExecuteTool(string name, JObject input)
    {
        try
        {
            switch (name)
            {
                case "create_primitive":
                {
                    var type = (string)input["type"];
                    float x = (float)input["x"], y = (float)input["y"], z = (float)input["z"];
                    string objName = (string)input["name"];

                    var primType = (PrimitiveType)Enum.Parse(typeof(PrimitiveType), type, true);
                    var go = GameObject.CreatePrimitive(primType);
                    go.transform.position = new Vector3(x, y, z);
                    go.name = objName;

                    Undo.RegisterCreatedObjectUndo(go, "Claude Create Object");
                    Selection.activeGameObject = go;

                    return $"{objName} を ({x},{y},{z}) に作成しました";
                }
                case "set_position":
                {
                    string targetName = (string)input["targetName"];
                    float x = (float)input["x"], y = (float)input["y"], z = (float)input["z"];

                    var go = GameObject.Find(targetName);
                    if (go == null) return $"{targetName} が見つかりません";

                    Undo.RecordObject(go.transform, "Claude Set Position");
                    go.transform.position = new Vector3(x, y, z);

                    return $"{targetName} の位置を ({x},{y},{z}) に変更しました";
                }
                default:
                    return $"未知のツール: {name}";
            }
        }
        catch (Exception e)
        {
            return $"エラー: {e.Message}";
        }
    }

    // ---- API通信 ----

    private IEnumerator PostToClaude(string jsonBody, Action<JObject> onResult)
    {
        var request = new UnityWebRequest(Endpoint, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("x-api-key", ApiKey);
        request.SetRequestHeader("anthropic-version", "2023-06-01");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            AppendLog("APIエラー: " + request.error + "\n" + request.downloadHandler.text);
            onResult(null);
            yield break;
        }

        onResult(JObject.Parse(request.downloadHandler.text));
    }

    private void AppendLog(string message)
    {
        log += message + "\n\n";
        Repaint();
    }
}
