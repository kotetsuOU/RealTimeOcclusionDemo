using System;

namespace Core.Keyboard
{
    /// <summary>
    /// アプリケーション全体の統合キーボードアクション識別子。
    /// 各機能モジュール（Weather, PCD, PR, EXP, System等）の操作アクションを一元管理します。
    /// </summary>
    public enum AppKeyAction
    {
        None = 0,

        // ==========================================
        // Weather (天候シミュレーション)
        // ==========================================
        Weather_ToggleRain,       // 雨のON/OFF
        Weather_ToggleCloud,      // 雲のON/OFF
        Weather_Strike,           // 落雷トリガー
        Weather_RainPreset0,      // 晴れ (雨強度 0%)
        Weather_RainPreset30,     // 小雨 (雨強度 30%)
        Weather_RainPreset70,     // 強い雨 (雨強度 70%)
        Weather_RainPreset100,    // 豪雨 (雨強度 100%)

        // ==========================================
        // PCD (Point Cloud Display / オクルージョン)
        // ==========================================
        PCD_ToggleMethod,         // 提案手法 / 従来手法の切り替え
        PCD_ToggleTagOptimization, // タグスキップ最適化 ON/OFF
        PCD_ToggleDensity,        // 密度計算補正 ON/OFF
        PCD_ToggleSoftFade,       // ソフトオクルージョンフェード ON/OFF
        PCD_CycleHoleFilling,     // 穴埋め手法 (Hole Filling) のサイクル切り替え
        PCD_ToggleFadeWidth,      // フェード幅 (FadeWidth) 切り替え
        PCD_ToggleOcclusionMap,   // オクルージョンマップ表示トグル
        PCD_TogglePixelTagMap,    // ピクセルタグマップ表示トグル
        PCD_CycleKernelType,      // カーネルタイプ (Kernel Type) サイクル
        PCD_CycleEvaluationMode,  // 評価モード (Evaluation Mode) サイクル
        PCD_CycleMinSectors,      // 最小オクルージョンセクター数サイクル
        PCD_CycleColorMode,       // 点群カラーモード切り替え

        // ==========================================
        // Physical Response (PR / アニメーション・オブジェクト操作)
        // ==========================================
        PR_ResetAnimation,        // アニメーション・位置のリセット
        PR_ToggleRunWalk,         // 走る/歩くの切り替え
        PR_SwitchTarget,          // 操作対象オブジェクトの切り替え
        PR_ToggleAutoMove,        // 自動周回モード ON/OFF
        PR_MoveForward,           // 前進
        PR_MoveBack,              // 後退
        PR_MoveLeft,              // 左移動
        PR_MoveRight,             // 右移動
        PR_MoveUp,                // 上昇
        PR_MoveDown,              // 下降
        PR_ToggleLookAt,          // 視線追従 (LookAt) トグル
        PR_CaptureDebug,          // デバッグ画像・カメラ映像の撮影 (Enter/Return)

        // ==========================================
        // Experiment (評価実験・UI)
        // ==========================================
        EXP_Start,                // 実験開始
        EXP_Abort,                // 実験中断
        EXP_ToggleControlPanel,   // インゲームコントロールパネル表示切替
        EXP_Choice1,              // 選択肢1 (Yes)
        EXP_Choice2,              // 選択肢2 (No)
        EXP_AdjustUp,             // 調整値アップ
        EXP_AdjustDown,           // 調整値ダウン
        EXP_Confirm,              // 回答決定
        EXP_Next,                 // 次の試行へ

        // ==========================================
        // System (システム・録画・キャプチャ)
        // ==========================================
        System_ToggleRecording,   // キャプチャ録画トグル
        System_AppExit            // アプリ終了
    }
}
