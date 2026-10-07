using System.Collections.Generic;
using UnityEngine;

namespace Core.Keyboard
{
    /// <summary>
    /// 全機能モジュールの標準（デフォルト）キーバインド定義を提供するファクトリクラス。
    /// 初期化時やリセット時に呼び出されます。
    /// </summary>
    public static class AppKeyDefaultBindings
    {
        /// <summary>
        /// アプリケーション全体の標準キーバインドグループ一覧を生成します。
        /// 既存の重複（Weather の Cキーと PCD の Cキーなど）はここで解消・最適化されています。
        /// </summary>
        public static List<AppKeyBindingGroup> CreateDefaultGroups()
        {
            var groups = new List<AppKeyBindingGroup>();

            // 1. Weather (天候)
            var weatherGroup = new AppKeyBindingGroup("Weather (WeatherEffect)");
            weatherGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.Weather_ToggleRain, KeyCode.G, "雨 ON/OFF トグル", "雨パーティクルと環境エフェクトの有効/無効切り替え"));
            weatherGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.Weather_ToggleCloud, KeyCode.V, "雲 ON/OFF トグル", "プロシージャル雲レイヤーの有効/無効切り替え (PCDのCキー重複を回避してVに配置)"));
            weatherGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.Weather_Strike, KeyCode.B, "落雷トリガー", "プロシージャル雷撃・フラッシュ・放電エフェクトの発動"));
            weatherGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.Weather_RainPreset0, KeyCode.Alpha7, "雨プリセット: 0%", "晴れ (雨強度 0%)"));
            weatherGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.Weather_RainPreset30, KeyCode.Alpha8, "雨プリセット: 30%", "小雨 (雨強度 30%)"));
            weatherGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.Weather_RainPreset70, KeyCode.Alpha9, "雨プリセット: 70%", "強い雨 (雨強度 70%)"));
            weatherGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.Weather_RainPreset100, KeyCode.Alpha0, "雨プリセット: 100%", "豪雨・嵐 (雨強度 100%)"));
            groups.Add(weatherGroup);

            // 2. PCD (Point Cloud Display / オクルージョン)
            var pcdGroup = new AppKeyBindingGroup("PCD (Occlusion & Point Cloud)");
            pcdGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PCD_ToggleMethod, KeyCode.M, "提案/従来手法 一括切替", "Ablation Study用: 全ての最適化手法の一括ON/OFF"));
            pcdGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PCD_ToggleTagOptimization, KeyCode.Alpha1, "タグスキップ最適化", "背景/不要タグの早期破棄トグル"));
            pcdGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PCD_ToggleDensity, KeyCode.Alpha2, "密度計算補正", "奥行き・密度に応じた補正ON/OFF"));
            pcdGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PCD_ToggleSoftFade, KeyCode.Alpha3, "ソフトフェード", "輪郭境界のソフトオクルージョンフェードトグル"));
            pcdGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PCD_CycleHoleFilling, KeyCode.Alpha4, "穴埋め手法サイクル", "Hole Filling 手法 (Bilateral/PullPush/Morphology/None) の順次切り替え"));
            pcdGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PCD_ToggleFadeWidth, KeyCode.T, "FadeWidth 切替", "マスク境界幅 0.0 (くっきり) ↔ 0.2 (滑らか) 切り替え"));
            pcdGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PCD_ToggleOcclusionMap, KeyCode.O, "Occlusion Map トグル", "オクルージョン深度マップ可視化"));
            pcdGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PCD_TogglePixelTagMap, KeyCode.P, "PixelTag Map トグル", "ピクセルタグマップ可視化"));
            pcdGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PCD_CycleKernelType, KeyCode.L, "Kernel Type サイクル", "オクルージョン判定カーネル形状の切り替え"));
            pcdGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PCD_CycleEvaluationMode, KeyCode.K, "Evaluation Mode サイクル", "評価モードの順次切り替え"));
            pcdGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PCD_CycleMinSectors, KeyCode.J, "Min Sectors サイクル", "最小オクルージョンセクター数 (1〜8) のサイクル切り替え"));
            pcdGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PCD_CycleColorMode, KeyCode.C, "点群カラーモード切替", "RGB / 深度 / 均一色 などのマテリアルカラーモード順次切り替え"));
            groups.Add(pcdGroup);

            // 3. Physical Response (PR / アニメーション・オブジェクト)
            var prGroup = new AppKeyBindingGroup("PhysicalResponse (Animation & Object)");
            prGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PR_ResetAnimation, KeyCode.Escape, "アニメーションリセット", "アニメーションとオブジェクト初期位置のリセット"));
            prGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PR_CaptureDebug, KeyCode.Return, "撮影 (画像/カメラ映像)", "オクルージョンDebugMapおよびCameraCaptureの撮影・保存", KeyCode.KeypadEnter));
            prGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PR_SwitchTarget, KeyCode.Tab, "操作対象切り替え", "操作対象オブジェクト (Fox/Toy等) の切り替え"));
            prGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PR_ToggleAutoMove, KeyCode.Space, "自動周回 ON/OFF", "オブジェクトの自動巡回移動トグル"));
            prGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PR_MoveForward, KeyCode.W, "前進", "手動移動: 前方", KeyCode.UpArrow));
            prGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PR_MoveBack, KeyCode.S, "後退", "手動移動: 後方", KeyCode.DownArrow));
            prGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PR_MoveLeft, KeyCode.A, "左移動", "手動移動: 左", KeyCode.LeftArrow));
            prGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PR_MoveRight, KeyCode.D, "右移動", "手動移動: 右", KeyCode.RightArrow));
            prGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PR_MoveUp, KeyCode.E, "上昇", "手動移動: 上方向"));
            prGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PR_MoveDown, KeyCode.Q, "下降", "手動移動: 下方向"));
            prGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.PR_ToggleLookAt, KeyCode.F, "視線追従トグル", "カメラまたは特定対象への視線追従ON/OFF"));
            groups.Add(prGroup);

            // 4. Experiment (実験評価)
            var expGroup = new AppKeyBindingGroup("Experiment (Psychophysics & Evaluation)");
            expGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.EXP_Start, KeyCode.Space, "実験開始", "被験者実験セッションの開始"));
            expGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.EXP_Abort, KeyCode.Escape, "実験中断", "実行中の実験セッションの中断"));
            expGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.EXP_ToggleControlPanel, KeyCode.F1, "操作パネル表示切替", "インゲーム実験設定パネルのトグル"));
            expGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.EXP_Choice1, KeyCode.Z, "選択肢1 (Yes)", "心理物理実験の回答: 選択肢1", KeyCode.Alpha1));
            expGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.EXP_Choice2, KeyCode.X, "選択肢2 (No)", "心理物理実験の回答: 選択肢2", KeyCode.Alpha2));
            expGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.EXP_AdjustUp, KeyCode.W, "調整 Up", "調整法実験: パラメータ増加", KeyCode.UpArrow));
            expGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.EXP_AdjustDown, KeyCode.S, "調整 Down", "調整法実験: パラメータ減少", KeyCode.DownArrow));
            expGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.EXP_Confirm, KeyCode.Return, "回答決定", "調整法・実験の決定キー"));
            expGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.EXP_Next, KeyCode.N, "次の試行へ", "次試行への進行"));
            groups.Add(expGroup);

            // 5. System (システム)
            var sysGroup = new AppKeyBindingGroup("System (Global & Capture)");
            sysGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.System_ToggleRecording, KeyCode.R, "画面録画トグル", "Unity CameraCapture による連番録画の開始/停止"));
            sysGroup.entries.Add(new AppKeyBindingEntry(AppKeyAction.System_AppExit, KeyCode.None, "アプリ終了", "スタンドアロン実行時のアプリ終了キー"));
            groups.Add(sysGroup);

            return groups;
        }
    }
}
