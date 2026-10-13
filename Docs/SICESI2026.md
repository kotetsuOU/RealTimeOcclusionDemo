# SICE SI 学術評価・データ収集・最適化解析システム 仕様書

> 📂 **親ノード**: [Wiki.md](./Wiki.md) | 🏷️ **種類**: 🧪 実験・評価  
> [RealTimeOcclusion Wiki (ポータル)](./Wiki.md) に戻る

本ドキュメントは、SICE SI（計測自動制御学会 システムインテグレーション部門講演会）等における学術評価およびベンチマーク実験のために構築された「ステレオオクルージョン自動撮影・占有マスク収集・最適化解析システム（SICESI）」のアーキテクチャ、Unity 撮影モジュール、Python 最適化・解析パイプライン、使用方法、データスキーマ、および各種スイープ撮影の仕様を解説する技術文書です。

---

## 1. 概要

本システムは、点群を用いたリアルタイムオクルージョン手法の学術的定量評価を行うため、仮想 3D オブジェクト、正解手メッシュ（Ground Truth: GT）、および各種密度の点群遮蔽画像をステレオカメラ（左右眼）から自動一括撮影し、IoU（Intersection over Union）解析に必要なデータセットを完全自動生成し、さらに Python 側で真値集計（`pattern_VO.csv`）、4 手法の数理最適化（MILP / 局所探索）、定性差分マップ（`diff_map`）の自動生成までを一気通貫で実行するフレームワークです。

```text
[Unity: SICESI 自動撮影・マスク収集]
   │
   ├─► SICESI_MaterialSwapper (GT黒化/肌色化・仮想物体白化・Layer一時差し替え & 完全復元)
   ├─► SICESI_ScreenCaptureUtil (URPカメラ画像キャプチャ・Transform/Params JSON保存)
   ├─► SICESI_StereoSweepRunner (GT・密度・セクター数・連続非占有・閾値スイープ撮影)
   └─► SICESI_SectorMaskCollector ──► SICESI_Sector8MaskSweepRunner (8bit占有マスク・256パターンスイープ)
           │
           ▼ (画像データセット: GT, Test, point_mask, sector_0~7_mask)
[Python: SICESI 解析・最適化・可視化パイプライン]
   │
   ├─► optimize_pattern_rules.py  【メイン解析・最適化エンジン】
   │     ├─► 直接投影領域除外の真値集計 ──► pattern_VO.csv 自動生成
   │     ├─► 4手法の最適化 (8候補, 20候補, 36クラスLUT, 256パターンLUT)
   │     │     ├─ 目的関数 A: 平均 IoU 最大化 (MILP / Steepest Ascent 局所探索)
   │     │     └─ 目的関数 B: 1-IoU 相対低減率最大化 (Weighted MILP)
   │     ├─► 最適化サマリーCSV群出力 (common_rules_summary.csv 等)
   │     └─► 定性差分マップ (diff_map) & 4手法比較モンタージュ自動生成
   │
   ├─► export_occlusion_diff_maps.py  【スタンドアロン差分可視化】
   │     └─► TP(薄灰), TN(暗灰), FN(過剰遮蔽:赤), FP(遮蔽漏れ:青) の4ペインモンタージュ生成
   │
   ├─► calc_stereoMaskedValue.py      【マルチメトリクス一括集計】
   │     └─► IoU, PSNR, SSIM, FN/FP画素数を自動計算してCSV出力
   │
   └─► verify_sector_masks.py / verify_items_1_to_6.py 【数学的整合性自動検算】
         └─► GPU遮蔽真値マスクと実測画像の一致度検証 (99.998% 一致)
```

---

## 2. 設計思想・アーキテクチャ

### 2.1 ディレクトリ構成

```text
Assets/Features/SICESI/
├── Data/                             # 最適化用集計データ・出力CSV
│   ├── pattern_VO.csv                # 全48条件合算 256パターンの V, O 画素数テーブル
│   └── figures/                      # 論文・報告書用図表データ
├── Editor/
│   ├── Core/                         # 【統括】Inspector エディタ & 再撮影トリガー
│   │   ├── SICESI_StereoEvaluationEditor.cs
│   │   └── SICESI_ReCaptureTrigger.cs
│   ├── Tabs/                         # 【タブ基盤】Inspector タブ描画クラス群
│   │   ├── SICESI_TabAdditionalExperimentsDrawer.cs # 追加実験・汎化ベンチマーク統合タブ
│   │   ├── SICESI_TabSICESI2026Drawer.cs            # メイン実験 (SICE SI 2026) タブ
│   │   ├── SICESI_TabSweepsDrawer.cs                # 各種スイープ撮影 (密度・セクター・マスク) タブ
│   │   └── SICESI_TabSettingsDrawer.cs              # カメラ・ディレクトリ・パイプライン設定タブ
│   ├── Presets/                      # 【配置プリセット】プリセット保存・一覧・選択管理
│   │   ├── SICESI_PresetExecutionActionDrawer.cs    # プリセット実行アクション
│   │   ├── SICESI_PresetListSectionDrawer.cs        # プリセット一覧・シーン復元
│   │   ├── SICESI_PresetSaveSectionDrawer.cs        # シーン配置プリセット JSON 保存
│   │   └── SICESI_PresetSelectionManager.cs         # プリセット個別ON/OFF選択 & 開閉永続化
│   └── Optimization/                 # 【最適化】Python 最適化実行 & パイプライン自動置換
│       ├── SICESI_OptimizationActionDrawer.cs       # 最適化実行アクションボタン
│       └── SICESI_OptimizationSectionDrawer.cs      # 最適化パラメータ設定・置換セクション
├── Python/                           # Python 解析・最適化・可視化スイート
│   ├── sicesi_core/                  # 【共通コアパッケージ】
│   │   ├── constants.py              # 配色定義 (TP/TN/FN/FP), 二値化閾値 (128)
│   │   ├── pattern_lut.py            # 256パターン/36回転クラスLUT, ビット演算, 20候補定義
│   │   ├── metrics.py                # IoU, PSNR, SSIM, FN/FP, evaluate_lut_extended
│   │   ├── visualizer.py             # 差分マスク (diff_mask), オーバーレイ, モンタージュ描画
│   │   ├── dataset_loader.py         # 画像探索, GT解決, pattern_counts_256 & pattern_VO 集計
│   │   ├── report_exporter.py        # CSV レポート群 (サマリー, 評価一覧, GAP分析, LUT) 出力クラス
│   │   ├── validator.py              # 包含関係不等式 (J*8<=J*20<=J*36<=J*256) 数学的整合性検証
│   │   └── optimizers/               # 数理最適化モジュール群
│   │       ├── fixed_candidates.py   # 固定8候補 (最低占有数) & 固定20候補 (連続非占有) 最適化
│   │       ├── milp_optimizer.py     # McCormick 緩和 MILP ソルバー & 個別最適探索
│   │       ├── local_search.py       # 最急上昇局所探索 (Steepest Ascent)
│   │       └── pipeline.py           # 多段階最適化パイプライン (PatternOptimizationPipeline)
│   ├── optimize_pattern_rules.py     # 【主役 Facade】引数解析・パイプライン統括
│   ├── export_occlusion_diff_maps.py # 【可視化 CLI】GT vs Test 差分マップ・4ペインモンタージュ生成
│   ├── calc_stereoMaskedValue.py     # 【定量評価 CLI】IoU, PSNR, SSIM, FN/FP 一括算出
│   ├── verify_sector_masks.py        # 【検証 GUI/CLI】GPU真値マスクと実測画像の一致度検証
│   ├── verify_items_1_to_6.py        # 【検証】画像実画素数と pattern_VO.csv の数学的検算
│   ├── make_pattern_figure_data.py   # 【図表】論文用 LaTeX 表・グラフ用データ抽出
│   └── evaluate_whole_vo.py          # 【研究用】評価領域全体でのパターン別集計
└── Scripts/
    ├── Core/                         # 【基盤 Context & ロケーター】
    │   ├── SICESI_StereoEvaluationController.cs   # シーン設定・ランナー保持 Context (純粋Context化)
    │   ├── SICESI_SectorMaskCollector.cs          # マスク収集コンポーネント
    │   └── SICESI_SceneComponentLocator.cs        # カメラ・オブジェクトの自動検出ロケーター
    ├── Benchmark/                    # 【ベンチマーク & 汎化評価】
    │   ├── SICESI_GeneralizationBenchmarkRunner.cs # 汎化&プリセットベンチマーク実行 (Unlit維持)
    │   ├── SICESI_BenchmarkMethodRegistry.cs     # 5手法設定レジストリ (動的最適化値反映対応)
    │   ├── SICESI_BenchmarkReporter.cs           # サマリーCSV出力 (5条件平均値集計対応)
    │   ├── SICESI_BenchmarkGroundTruthCapturer.cs # 最終出力GT撮影制御 (手黒化+点群オフ)
    │   ├── SICESI_BenchmarkImageIO.cs            # ベンチマーク画像I/O & 残存画像削除
    │   ├── SICESI_MethodMetricsAccumulator.cs    # 手法別メトリクス累積 & 平均値計算
    │   ├── SICESI_IoUCalculator.cs               # ステレオ画像 IoU・過剰遮蔽・遮蔽漏れ高速計算
    │   ├── SICESI_EvaluationDatasetConfig.cs     # 3-Objects & 汎化セット構成定義
    │   └── SICESI_DatasetSyncService.cs          # シーンとデータセット構成の自動同期
    ├── Presets/                      # 【配置プリセット管理】
    │   ├── SICESI_PresetPoseManager.cs           # 配置プリセット走査・管理・エントリ抽出
    │   ├── SICESI_PresetRepository.cs            # プリセットJSON永続化・フォルダー探索
    │   ├── SICESI_PresetSceneApplier.cs          # プリセットJSONのシーンオブジェクト適用
    │   └── SICESI_PresetCategoryDetector.cs      # カテゴリ自動判定
    ├── Sweepers/                     # 【各種スイープ撮影ランナー群】
    │   ├── SICESI_EvaluationDispatcher.cs        # スイープ実行バリデーション & 起動統括
    │   ├── SICESI_StereoSweepRunner.cs           # パラメータ探索コーディネーター
    │   ├── SICESI_BatchReSweepRunner.cs          # 一括再検証スイープ (平均値集計対応)
    │   ├── SICESI_Sector8MaskSweepRunner.cs      # 8セクター画面保存マスクスイープ
    │   ├── SICESI_Pattern256SweepRunner.cs       # 256パターンLUT網羅スイープ
    │   ├── SICESI_SectorMaskDensityRunner.cs     # 全密度GPU Readbackマスクスイープ
    │   ├── SICESI_DensitySweepRunner.cs          # 点群密度スイープ
    │   ├── SICESI_SectorSweepRunner.cs           # セクター閾値スイープ (Average比較含む)
    │   ├── SICESI_ConsecutiveSweepRunner.cs      # 連続非占有スイープ (20代表条件)
    │   ├── SICESI_ThresholdSweepRunner.cs        # 密度×オクルージョン閾値スイープ
    │   └── SICESI_SnapshotRunner.cs              # GT撮影・単発撮影・シーン俯瞰撮影
    ├── Optimization/                 # 【Python 最適化連携 & パイプライン置換】
    │   ├── SICESI_PythonOptimizationRunner.cs    # Python 最適化バックグラウンド非同期実行ランナー
    │   ├── SICESI_OptimizationPresetRunner.cs    # プリセット最適化バッチ実行
    │   ├── SICESI_OptimizationApplier.cs         # パイプライン & レジストリへのパラメータ自動置換
    │   └── SICESI_OptimizationResult.cs          # 最適化パラメータデータモデル (JSON/CSV パース)
    ├── Capture/                      # 【画像撮影 & メタデータ保存】
    │   ├── SICESI_MaterialSwapper.cs             # GT・仮想物体のマテリアル/Layer 一時変更・復元
    │   ├── SICESI_ScreenCaptureUtil.cs           # カメラPNG保存・URP直接レンダリング・ステレオ視点保存
    │   ├── SICESI_SceneMetadataSerializer.cs     # Transform & 評価パラメータのJSONシリアライズ・保存・復元
    │   ├── SICESI_CameraPoseLock.cs              # カメラ姿勢(Pos/Rot/Proj)固定ヘルパー (IDisposable)
    │   └── SICESI_AsyncTextureReadbackHandler.cs # GPU AsyncReadback & マスクデータ保存
    └── Params/                       # 【パラメータ & 幾何計算 Pure C#】
        ├── SICESI_EvaluationParamsHelper.cs      # 密度単位サフィックス変換・ヘルパー
        └── SICESI_ConsecutivePairEvaluator.cs    # 連続非占有の幾何学的重複判定・ペア生成
```

### 2.2 クラス相関図 & データ連携図

#### Unity 側クラス相関図

```mermaid
classDiagram
    direction TB

    class SICESI_StereoEvaluationController {
        +Camera leftEyeCamera
        +Camera rightEyeCamera
        +Camera sceneCaptureCamera
        +GameObject groundTruthObject
        +GameObject virtualObject
        +RsDummyPointCloudProvider dummyPointCloudProvider
        +RsMaterialController materialController
        +PCDOcclusionPipelineController occlusionPipelineController
        +SICESI_EvaluationDispatcher Dispatcher
        +SICESI_MaterialSwapper MaterialSwapper
        +SICESI_SnapshotRunner SnapshotRunner
        +SICESI_StereoSweepRunner StereoSweepRunner
        +SICESI_GeneralizationBenchmarkRunner GeneralizationRunner
        +SICESI_PythonOptimizationRunner PythonOptimizationRunner
        +SICESI_OptimizationPresetRunner OptimizationPresetRunner
        +FindCameras()
        +FindDummyComponents()
        +SyncDummyPointCloudWithGroundTruth()
    }

    class SICESI_EvaluationDispatcher {
        +CaptureGroundTruth()
        +CaptureCurrentCondition()
        +RunDensitySweep()
        +RunSectorSweep()
        +RunConsecutiveSectorSweep()
        +RunDensityOcclusionThresholdSweep()
        +CaptureSceneOverview()
        +RunBatchReSweep()
        +RunGeneralizationBenchmark()
        +RunPresetBenchmark()
        +RunOptimizationPresets()
        +RunPythonOptimization()
    }

    class SICESI_MaterialSwapper {
        +SetGroundTruthState()
        +RestoreGroundTruthState()
        +SetVirtualObjectUnlitWhiteState()
        +RestoreVirtualObjectState()
        +SetGroundTruthSkinState()
        +RestoreGroundTruthSkinState()
    }

    class SICESI_ScreenCaptureUtil {
        +SaveCameraView()
        +SaveCameraViewDirect()
        +CaptureStereoViews()
        +SaveEyeViewsForScene()
    }

    class SICESI_SceneMetadataSerializer {
        +SaveSceneTransformsJson()
        +LoadAndApplySceneTransformsJson()
        +SaveEvaluationParamsJson()
    }

    class SICESI_GeneralizationBenchmarkRunner {
        +RunGeneralizationBenchmarkRoutine()
        +RunBenchmarkFromPresetsRoutine()
    }

    class SICESI_BenchmarkReporter {
        +RecordEvaluationRow()
        +FinalizeAndExportSummary()
    }

    class SICESI_MethodMetricsAccumulator {
        +Accumulate()
        +ComputeAverages()
    }

    class SICESI_SectorMaskCollector {
        +RunSectorMaskDensitySweep()
        +RunSector8MaskSweep()
        +RunPattern256MaskSweep()
    }

    SICESI_StereoEvaluationController *-- SICESI_EvaluationDispatcher : コマンド起動委譲
    SICESI_StereoEvaluationController *-- SICESI_MaterialSwapper : マテリアル管理保持
    SICESI_StereoEvaluationController *-- SICESI_SnapshotRunner : 保持
    SICESI_StereoEvaluationController *-- SICESI_StereoSweepRunner : 保持
    SICESI_StereoEvaluationController *-- SICESI_GeneralizationBenchmarkRunner : 保持
    SICESI_StereoEvaluationController *-- SICESI_PythonOptimizationRunner : 保持
    SICESI_StereoEvaluationController *-- SICESI_OptimizationPresetRunner : 保持

    SICESI_EvaluationDispatcher ..> SICESI_SnapshotRunner : コルーチン起動
    SICESI_EvaluationDispatcher ..> SICESI_StereoSweepRunner : コルーチン起動
    SICESI_EvaluationDispatcher ..> SICESI_GeneralizationBenchmarkRunner : コルーチン起動
    SICESI_EvaluationDispatcher ..> SICESI_PythonOptimizationRunner : 実行呼び出し

    SICESI_GeneralizationBenchmarkRunner ..> SICESI_MaterialSwapper : 白化・復元
    SICESI_GeneralizationBenchmarkRunner ..> SICESI_ScreenCaptureUtil : キャプチャ
    SICESI_GeneralizationBenchmarkRunner ..> SICESI_SceneMetadataSerializer : 姿勢保存
    SICESI_GeneralizationBenchmarkRunner ..> SICESI_BenchmarkReporter : CSV出力 (平均値集計)
    SICESI_BenchmarkReporter *-- SICESI_MethodMetricsAccumulator : 5条件平均値集計

    SICESI_SectorMaskCollector *-- SICESI_Sector8MaskSweepRunner : 保持・実行
    SICESI_SectorMaskCollector *-- SICESI_Pattern256SweepRunner : 保持・実行
    SICESI_SectorMaskCollector *-- SICESI_SectorMaskDensityRunner : 保持・実行
    SICESI_Sector8MaskSweepRunner ..> SICESI_ScreenCaptureUtil : 直接キャプチャ
    SICESI_Sector8MaskSweepRunner ..> SICESI_MaterialSwapper : 直接マテリアル切替
    SICESI_Pattern256SweepRunner ..> SICESI_ScreenCaptureUtil : 直接キャプチャ
    SICESI_Pattern256SweepRunner ..> SICESI_MaterialSwapper : 直接マテリアル切替
```

#### Unity と Python パイプラインのデータ連携図

```mermaid
graph LR
    subgraph Unity [Unity SICESI 実行環境]
        A[SICESI_SectorMaskCollector] -->|画面保存スイープ| B[(RawTest 画像群)]
    end

    subgraph Dataset [データセット階層]
        B -->|gt_left.png / gt_right.png| C[GT 手メッシュ]
        B -->|point_mask.png| D[点群手前直接投影]
        B -->|sector_0~7_mask.png| E[8セクター二値マスク]
        B -->|sector_mask.png| F[8bit統合パターンマスク]
    end

    subgraph Python [Python 最適化パイプライン]
        C & D & E & F --> G[optimize_pattern_rules.py]
        G -->|自動生成| H[(pattern_VO.csv)]
        G -->|MILP / 局所探索| I[(common_rules_summary.csv)]
        G -->|構造化出力| J[(optimized_parameters.json)]
        G -->|画素比較| K[(diff_map & モンタージュ)]
        
        C & B --> L[export_occlusion_diff_maps.py]
        C & B --> M[calc_stereoMaskedValue.py]
        M --> N[(stereo_evaluation_summary.csv)]
    end

    subgraph AutoApply [自動パラメータ置換基盤]
        J --> P[SICESI_PythonOptimizationRunner]
        P --> Q[SICESI_OptimizationApplier]
        Q -->|直接置換・即時適用| O[PCDOcclusionPipelineController]
        Q -->|動的オーバーライド| R[SICESI_BenchmarkMethodRegistry]
    end
```

### 2.3 処理フロー（エンドツーエンド実行シーケンス）

```mermaid
sequenceDiagram
    autonumber
    participant Unity as Unity (SICESI Editor)
    participant Runner as SICESI_PythonOptimizationRunner
    participant Disk as データセット (ファイルシステム)
    participant Opt as Python: optimize_pattern_rules.py
    participant Applier as SICESI_OptimizationApplier
    participant Shader as Unity: PCDOcclusionPipelineController
    participant MethodReg as SICESI_BenchmarkMethodRegistry

    Note over Unity: 1. データセット撮影フェーズ
    Unity->>Unity: カメラ姿勢固定 (CameraPoseLock) & Time.timeScale=0
    Unity->>Disk: GT撮影 (gt_left.png, vo_silhouette_left.png)
    Unity->>Disk: 各密度撮影 (point_mask_left.png, sector_0~7_mask_left.png)
    
    Note over Runner, Opt: 2. Python バックグラウンド最適化フェーズ
    Unity->>Runner: 最適化実行要求 (RunPythonOptimization)
    Runner->>Opt: バックグラウンドプロセス非同期起動 (--skip-diff)
    Opt->>Disk: pattern_VO.csv 自動集計 & MILP / 局所探索計算
    Opt->>Disk: optimized_parameters.json & common_rules_summary.csv 保存
    Opt-->>Runner: プロセス終了 (ExitCode: 0)
    
    Note over Applier, Shader: 3. パラメータ自動置換・パイプライン適用フェーズ
    Runner->>Applier: 最適解の受け渡し (SICESI_OptimizationResult)
    Applier->>Shader: Mode 8, 20, 36, 256 の各閾値・LUT HEX を直接上書き
    Applier->>MethodReg: ベンチマーク評価レジストリへ最新パラメータを登録
    Shader->>Shader: シェーダーへの即時バインド (UpdatePatternLUTWords)
```

#### 汎化・配置プリセット一括ベンチマーク実行シーケンス

```mermaid
sequenceDiagram
    autonumber
    participant Unity as Unity (SICESI Editor)
    participant Overview as SICESI_SceneOverviewCapturer
    participant Swapper as SICESI_MaterialSwapper
    participant GTCapturer as SICESI_BenchmarkGroundTruthCapturer
    participant MatCtrl as RsMaterialController
    participant MethodReg as SICESI_BenchmarkMethodRegistry
    participant IoU as SICESI_IoUCalculator
    participant Disk as データセット / PresetBenchmark

    Note over Unity, Disk: 各評価ケースのベンチマーク実行サイクル
    Unity->>Unity: プリセット Transform 復元 & サンプラー同期
    Unity->>Overview: シーン配置記録要求 (CaptureOverviewAndTransforms)
    Overview->>Disk: 通常URP直接レンダリング (scene_overview.png, scene_left.png, scene_right.png, scene_transforms.json)
    
    Unity->>Swapper: 仮想物体白化 (Unlit White 一貫維持開始)
    Unity->>GTCapturer: 最終出力GTステレオ撮影 (手黒化 + 点群非表示)
    GTCapturer->>Disk: gt_left.png, gt_right.png 保存
    
    Unity->>MatCtrl: 点群一時 Black 化 (ChangeColorMode(Black))
    loop 5手法 (Average, 8, 20, 36, 256) × 各点群密度
        Unity->>MethodReg: 手法パラメータ適用
        Unity->>Disk: 点群オクルージョンテスト撮影 (test_left.png, test_right.png)
        Unity->>IoU: IoU・過剰遮蔽(FN)・遮蔽漏れ(FP) 計算 (threshold=128)
        Unity->>Disk: 4色差分マップ (diff_left.png, diff_right.png) 保存
    end
    Unity->>MatCtrl: 点群カラーモード安全復元 (Skin等)
    Unity->>Swapper: 仮想物体マテリアル安全復元
    Unity->>Disk: preset_benchmark_summary.csv 自動追記
```

### 2.4 神クラス抑制と純粋 Context 化（アプローチ 1）

本システムでは、開発の進展に伴い単一の MonoBehaviour に責務や他クラスへの 1 行委譲メソッドが過度に集中する「神クラス化（God Class Anti-pattern）」を防止するため、**「純粋 Context 化 & 窓口ラッパー全廃（アプローチ 1）」** の設計原則を適用しています。

* **純粋 Context としての `SICESI_StereoEvaluationController`**:
  コントローラー本体は、シーン内のコンポーネント参照、シリアライズされた評価パラメータ、および各 Runner / サービスのインスタンス管理（ライフサイクル）に専念します。
* **1 行委譲メソッドの全廃**:
  歴史的経緯で生やされていた他クラスのメソッドを中継するだけのラッパーメソッド（計 25 個以上、約 100 行）を全廃しました。
* **直接協調アーキテクチャ**:
  各 Runner や Editor クラスは、コントローラーを経由することなく、対応する専門クラス（`controller.Dispatcher`, `controller.MaterialSwapper`, `SICESI_ScreenCaptureUtil`, `SICESI_SceneMetadataSerializer`, `SICESI_ConsecutivePairEvaluator`）を直接呼び出して協調動作します。
* **役割に応じた階層的ディレクトリ配置**:
  `Scripts/` 配下を `Core/`, `Benchmark/`, `Presets/`, `Sweepers/`, `Optimization/`, `Capture/`, `Params/` に、`Editor/` 配下を `Core/`, `Tabs/`, `Presets/`, `Optimization/` に体系的に整理し、保守性と拡張性を最大化しています。

---

## 3. セットアップ・使用方法

### 3.1 Unity での自動撮影 & ベンチマーク実行手順

#### Step 1: 評価コントローラーの配置 & 自動検出

1. シーン内の管理 GameObject に [`SICESI_StereoEvaluationController.cs`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Scripts/Core/SICESI_StereoEvaluationController.cs) および [`SICESI_SectorMaskCollector.cs`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Scripts/Core/SICESI_SectorMaskCollector.cs) をアタッチします。
2. Inspector 上部の **「🔍 シーンからカメラ・点群プロバイダーを自動検出」** ボタンをクリックし、左右眼カメラ、シーン俯瞰カメラ、点群プロバイダー、手メッシュ、仮想物体を自動検出・アサインします。

#### Step 2: Inspector タブ構成と機能選択

[`SICESI_StereoEvaluationEditor.cs`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Editor/Core/SICESI_StereoEvaluationEditor.cs) は神クラス化を防止するため、役割ごとの独立 Drawer クラス群（`Tabs/`, `Presets/`, `Optimization/`）に分割されています。用途に応じてタブを切り替えて操作します。

| タブ名 | 担当クラス | 主な機能 |
| :--- | :--- | :--- |
| **汎化ベンチマーク** | `SICESI_TabGeneralizationDrawer` | 登録された仮想オブジェクト群 × 手メッシュ群の全ペア網羅ベンチマーク実行 |
| **配置プリセット** | `SICESI_TabPresetsDrawer` | JSON 保存された配置プリセットの走査・シーン復元適用・一括ベンチマーク実行 |
| **各種スイープ** | `SICESI_TabSweepsDrawer` | 点群密度、セクター閾値、連続非占有、8セクターマスク等の各種スイープ撮影 |
| **単発キャプチャ** | `SICESI_TabCaptureDrawer` | 最終出力 GT 単発撮影、現在の条件撮影、シーン全体俯瞰撮影 |
| **データセット設定** | `SICESI_TabDatasetConfigDrawer` | 評価対象の 3-Objects（最適化用）および汎化用仮想オブジェクト・手メッシュ登録 |
| **共通設定** | `SICESI_TabSettingsDrawer` | 出力ディレクトリ、カメラ参照、待機フレーム数、sRGB変換設定 |

#### Step 3: シーン配置プリセットの保存と適用

手メッシュと仮想物体の様々な配置関係を再現・評価するため、現在のシーン配置（手・仮想物体・左右カメラ・シーンカメラの Transform）をプリセット JSON として保存・管理できます。

1. **プリセットの保存**:
   - Inspector 下部の **「配置プリセットの保存」** セクションを展開します。
   - プリセット名（例: `AdditionalCase_1`）を入力し、**「💾 現在の配置をプリセット保存」** をクリックします。
   - `AdditionalExperiments/Presets/Generalization/<プリセット名>/scene_transforms.json` に完全な姿勢データが保存されます。
2. **プリセットの適用**:
   - **「配置プリセット」** タブを開き、一覧から目的のプリセットを選択して **「適用」** をクリックします。
   - `SICESI_PresetSceneApplier` により、該当オブジェクトのアクティブ切替、Transform 復元、および点群サンプラーの即時同期（`SyncDummyPointCloudWithGroundTruth`）が実行されます。

#### Step 4: 汎化性能ベンチマークの実行 & GT 撮影プロトコル

1. Unity エディタを Play モードにします。
2. **「配置プリセット」** タブで **「🚀 プリセット一括ベンチマーク実行」**（または **「汎化ベンチマーク」** タブで **「🚀 汎化ベンチマーク実行」**）をクリックします。
3. **ベンチマーク実行プロトコル（状態維持と完全可逆性）**:
   - **シーン配置の自動記録（左・右・俯瞰の計 3 枚）**:
     各評価ケースの開始時、新設された [`SICESI_SceneOverviewCapturer.cs`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Scripts/Capture/SICESI_SceneOverviewCapturer.cs) が手メッシュ通常表示（肌色マテリアル）、点群非表示、仮想物体通常マテリアルの状態で通常 URP レンダリングを行い、`scene_overview.png`（俯瞰カメラ）、`scene_left.png`（左眼カメラ）、`scene_right.png`（右眼カメラ）、および `scene_transforms.json` を保存します。重複していた `scene.png` を完全廃止し、オクルージョン表示や 3DDisplay 補正の影響を受けないクリーンな通常 URP 配置記録を一元的に提供します。
   - **Unlit White 一貫維持**: 各評価ケースの開始時に仮想物体を完全な白（`Unlit White`）に設定し、**最終出力 GT 撮影から 5 手法のテスト撮影完了まで一貫して白化状態を維持**します。テスト完了後に元のカラーマテリアルへ安全に復元（`try-finally` 構造）されます。
   - **GT 撮影**: `SICESI_BenchmarkGroundTruthCapturer` が手メッシュを黒化（`Unlit Black`）かつ点群を非表示にしてステレオ撮影（手による真の幾何学的オクルージョン）を行います。
   - **点群一時 Black 化プロトコル（Python 完全互換 IoU 計算）**:
     テスト画像撮影時、点群カラーモードを [`RsMaterialController.cs`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/PCD/Scripts/PointCloud/RsMaterialController.cs) により `PointCloudColorMode.Black`（完全な黒: RGB 0,0,0）に一時変更します。肌色点群（RGB 各成分 > 128）が白（仮想物体）と誤判定されて発生していた「遮蔽漏れ (FP)」の激増を完全に排除し、撮影完了後は `try-finally` で元のカラーモード（Skin 等）へ安全に復元されます。
   - **5 手法のテスト撮影 & リアルタイム計測**: `SICESI_BenchmarkMethodRegistry` が 5 手法（Mode_Average, Mode 8, Mode 20, Mode 36, Mode 256）を順次適用し、点群オクルージョン画像をキャプチャします。
   - **完全可逆 PNG & 二値化閾値 128 統一による高精度 IoU 算出**: すべての画像は `Texture2D.EncodeToPNG()` によるロスレス可逆形式で保存され、[`SICESI_IoUCalculator.cs`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Scripts/Benchmark/SICESI_IoUCalculator.cs) が Python 側（`sicesi_core/metrics.py`）と同一の判定閾値 `threshold = 128`（`c.r > 128 || c.g > 128 || c.b > 128`）で即座にピクセル単位の IoU、過剰遮蔽 (FN)、遮蔽漏れ (FP) を算出して `preset_benchmark_summary.csv` に自動追記します。

---

### 3.2 Python 最適化・解析パイプラインの実行手順

#### Step 4: `optimize_pattern_rules.py` の実行（主役スクリプト）

本スクリプトは、撮影画像から手前点群直接投影を除外した真値集計ファイル [`pattern_VO.csv`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Data/pattern_VO.csv) を自動生成し、4 手法の個別最適・共通最適を解き、`diff_map` 画像までを一括生成します。

```bash
# 1) 通常実行（データセット自動探索、pattern_VO.csv生成、MILP/局所探索、diff_map生成）
python Assets/Features/SICESI/Python/optimize_pattern_rules.py

# 2) 高速実行（画像出力をスキップし、数値計算・CSV出力のみを数秒で実行）
python Assets/Features/SICESI/Python/optimize_pattern_rules.py --skip-diff

# 3) 強制再集計（キャッシュを無視して撮影画像から pattern_VO.csv を再構築）
python Assets/Features/SICESI/Python/optimize_pattern_rules.py --force

# 4) データセットパスおよび出力先ディレクトリを明示指定
python Assets/Features/SICESI/Python/optimize_pattern_rules.py -i "C:/Users/.../SICESI_Dataset" -o "C:/Users/.../RuleOptimizationResults"
```

#### Step 5: `export_occlusion_diff_maps.py` によるスタンドアロン差分可視化

GT 画像と Test 画像を比較し、誤遮蔽（赤: FN）や遮蔽漏れ（青: FP）を色分けした差分マップをスタンドアロンで生成します。

```bash
# 1) 特定の条件名（例: Bouchiba）のみ絞り込んで差分マップを生成
python Assets/Features/SICESI/Python/export_occlusion_diff_maps.py -c Bouchiba

# 2) 論文・スライド用モンタージュ画像（[GT | Test | Diff Mask | Overlay]）のみ生成
python Assets/Features/SICESI/Python/export_occlusion_diff_maps.py -c Bouchiba --mode montage

# 3) 元のフォルダ構造内 (Left/Right 直下) に差分画像を直接配置
python Assets/Features/SICESI/Python/export_occlusion_diff_maps.py -c Bouchiba --in-place
```

#### Step 6: `calc_stereoMaskedValue.py` によるマルチメトリクス一括集計

複数条件の画像ディレクトリから、IoU、PSNR、SSIM、過剰遮蔽画素数、遮蔽漏れ画素数を一括計算して CSV に出力します。

```bash
# 1) 全条件の一括定量評価
python Assets/Features/SICESI/Python/calc_stereoMaskedValue.py

# 2) 特定の条件のみ絞り込んで高速評価 & CSV分離保存
python Assets/Features/SICESI/Python/calc_stereoMaskedValue.py -c Bouchiba -o "stereo_eval_bouchiba.csv"

# 3) 評価と同時に定性差分マップも自動出力
python Assets/Features/SICESI/Python/calc_stereoMaskedValue.py -c Bouchiba --export-diff
```

#### Step 7: Unity 内部からの Python 最適化実行 & パイプライン自動置換

従来の手動 HEX コピーに加え、Inspector 上の **「追加実験」タブ** 内に新設された **「🪐 Python 最適化の実行 & パイプライン自動置換」** セクションから、完全自動で最適化とパイプライン置換を実行できます。

1. Inspector の「追加実験」タブを開きます。
2. 対象データセットフォルダおよび目的関数（平均 IoU 最大化 / 相対低減率最大化）を選択します。
3. **「🚀 Python 最適化を実行してパイプラインに自動置換」** ボタンをクリックします。
   * バックグラウンドプロセスとして `optimize_pattern_rules.py --skip-diff` が非同期実行されます（Unity エディタはフリーズしません）。
   * 最適化完了後、生成された `optimized_parameters.json` から Mode 8（$R_{\mathrm{th}}$）、Mode 20（$R_{\mathrm{th}}, L_{\mathrm{th}}$）、Mode 36（HEX）、Mode 256（HEX）が [`PCDOcclusionPipelineController.cs`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/3DDisplay/Scripts/Occlusion/Controllers/PCDOcclusionPipelineController.cs) および [`SICESI_BenchmarkMethodRegistry.cs`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Scripts/Benchmark/SICESI_BenchmarkMethodRegistry.cs) に即時反映・置換されます。
   * 既に計算済みの結果がある場合は、**「📥 既存結果 (JSON/CSV) を再読み込みして適用」** ボタンから一瞬で最新値を再適用することも可能です。

#### Step 8: 配置プリセットのフォルダー階層分類 & 個別 ON/OFF 管理による一括ベンチマーク

追加実験タブ内の **「保存済み JSON プリセット一覧」** では、保存された各 `scene_transforms.json` をサブフォルダーごとに階層分類し、各プリセットの有効/無効（ON/OFF）を個別管理できます。

1. **プリセット保存時のフォルダー指定**:
   * 「現在のシーン配置を JSON プリセットとして保存」セクションの「フォルダー」入力欄で、任意のサブフォルダー名（例: `FoxSeries`, `Batch1` 等）を入力、または「📁 既存選択」から選択して保存できます（未入力時はルート直下）。
2. **フォルダー別グループ化表示 & 開閉**:
   * 一覧画面では、ルート直下および各サブフォルダーごとに自動でグループ化され、フォルダーヘッダー（`📁 フォルダー名`）の Foldout で自由に開閉できます。
3. **個別 ON/OFF (チェックボックス) & フォルダー単位の一括操作**:
   * 各プリセットの先頭にあるチェックボックス（☑️ / ☐）で、一括自動実行に含めるか否かを個別に選択できます（除外されたプリセットは打ち消し線表示）。
   * フォルダーヘッダーの「✔」「✖」ボタンで、フォルダー内の全プリセットを一括で選択 / 解除できます。
   * 全体ヘッダーの「✔ 全選択」「✖ 全解除」ボタンで、表示中タブ全体のプリセットを一括操作できます。
4. **選択プリセットのみの一括自動ベンチマーク実行**:
   * 「🚀 一括自動実行」ボタンをクリックすると、チェックボックスで**選択されたプリセットのみ**を対象にして、5手法 × 全密度の自動復元・IoU計測が実行されます。

---

## 4. 仕様・パラメータ詳細

### 4.1 Unity C# Inspector 主要パラメータ一覧

| パラメータ名 | 型 | 既定値 | 説明 |
| :--- | :--- | :--- | :--- |
| `leftEyeCamera` | `Camera` | (自動検出) | 左目用カメラ (`SRDisplay LeftEyeCamera` 等) |
| `rightEyeCamera` | `Camera` | (自動検出) | 右目用カメラ (`SRDisplay RightEyeCamera` 等) |
| `sceneCaptureCamera` | `Camera` | (自動検出) | 通常 URP 俯瞰撮影用カメラ（オクルージョン表示の影響を受けずに独立レンダリング） |
| `groundTruthObject` | `GameObject` | (設定) | 正解手メッシュオブジェクト |
| `virtualObject` | `GameObject` | (設定) | オクルージョン対象の仮想 3D オブジェクト |
| `materialController` | `RsMaterialController` | (自動検出) | 点群マテリアルコントローラー（テスト撮影時の点群 Black 化・復元用） |
| `sweepDensities` | `float[]` | `[0.25, 0.5, 1.0, 2.0, 4.0]` | スイープする点群密度のリスト |
| `densityUnit` | `PointDensityUnit` | `PointSpacingMm` | 密度の物理単位（点間隔 mm, 点/cm² など） |
| `sweepSectors` | `int[]` | `[1..8]` | Bouchiba スイープで評価するセクター閾値リスト |
| `sweepMaxConsecutiveZeros` | `int[]` | `[0..8]` | 連続非占有スイープで評価する許容最大連続非占有数 $L_{\mathrm{th}}$ リスト |
| `outputDirectory` | `string` | (指定パス) | 撮影画像の出力先ルートディレクトリ |
| `conditionName` | `string` | `"RawTest"` | 実験条件・ケースの識別名 |

### 4.2 Python スクリプト引数仕様一覧

#### `optimize_pattern_rules.py`

| 引数 | 型 | 既定値 | 説明 |
| :--- | :--- | :--- | :--- |
| `-i, --input, --dataset-root` | `string` | (自動探索) | 撮影データセットのルートディレクトリ |
| `-o, --output-dir` | `string` | `<root>/RuleOptimizationResults` | 解析結果 CSV・最適化 LUT・diff_map の保存先 |
| `--force` | `flag` | `False` | 既存キャッシュを無視し、撮影画像から強制再集計 |
| `--skip-diff` | `flag` | `False` | diff_map 画像出力をスキップし、数値計算のみ高速実行 |

#### `export_occlusion_diff_maps.py`

| 引数 | 型 | 既定値 | 説明 |
| :--- | :--- | :--- | :--- |
| `dataset_dir` | `string` | (既定パス) | 対象データセットディレクトリ |
| `-c, --condition` | `string` | `None` | 条件名の絞り込みキーワード（例: `-c Bouchiba`） |
| `-o, --output-dir` | `string` | `<root>/DiffMaps` | 差分マップの保存先ベースディレクトリ |
| `--in-place` | `flag` | `False` | 元の `Left/Right` フォルダ直下に差分画像を直接出力 |
| `-m, --mode` | `choice` | `all` | 出力種類 (`all`, `mask`, `overlay`, `montage`) |
| `-th, --threshold` | `int` | `128` | 二値化判定閾値（0〜255、50% カバレッジ基準） |

---

### 4.3 `pattern_VO.csv` データスキーマ & 数学的定義

#### データスキーマ

[`Assets/Features/SICESI/Data/pattern_VO.csv`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Data/pattern_VO.csv) は、全 48 条件（4 シーン × 6 密度 × 2 視点）の非直接投影領域における 256 パターンの真値集計テーブルです。

| カラム名 | 型 | 説明 |
| :--- | :--- | :--- |
| `mask` | `int` | 8 セクターの占有ビットマスク（$0 \le m \le 255$） |
| `V` | `int` | 手メッシュ GT が存在しない領域（＝可視であるべき正解画素数） |
| `O` | `int` | 手メッシュ GT が存在する領域（＝遮蔽されるべき正解画素数） |

<details>
<summary>📐 手前点群直接投影除外（Fixed Classification）の理論的背景と定式化</summary>

点群の各点投影画素（手前点群マスク $\mathrm{point\_mask}$）は、深度テストにより確実に物体を手前遮蔽する確定画素（Fixed Classification）です。  
オクルージョン判定アルゴリズムが真に推定すべき対象は、**点群の隙間（未サンプリング領域）**であり、評価領域 $M$ は以下のように定義されます：

$$M = \mathrm{Silhouette}_{\mathrm{VO}} \cap \neg \mathrm{point\_mask}$$

この領域 $M$ 内における手メッシュ真値（GT）に基づき、各パターン $m \in \{0, \dots, 255\}$ について可視画素数 $V_m$ および遮蔽画素数 $O_m$ が集計されます：

$$V_m = \sum_{p \in M, \mathrm{mask}(p) = m} \mathbf{1}_{\{\mathrm{GT}(p) = 0\}}, \quad O_m = \sum_{p \in M, \mathrm{mask}(p) = m} \mathbf{1}_{\{\mathrm{GT}(p) = 1\}}$$

全 256 パターンの $V_m + O_m$ の総和は、画像内の非直接投影対象画素総数と厳密に一致します。
</details>

---

### 4.4 4 つの判定手法モデル & 最適化目的関数

本システムでは、以下の 4 つの判定器モデルを比較・最適化します：

1. **固定 8 候補（最低占有数判定）**:
   占有セクター数 $N_{\mathrm{occ}} < R$（$R \in \{1, \dots, 8\}$）を満たす場合に可視とする 1 パラメータモデル。
2. **固定 20 候補（連続非占有規則）**:
   占有数閾値 $R_{\mathrm{th}}$ または最大連続非占有数 $L_{\mathrm{th}}$ のいずれかを満たす場合に可視とする 2 パラメータモデル（SICE 2026 提案手法）。
3. **回転不変 36 クラス LUT**:
   8 ビットの巡回シフト（45 度刻みの回転）に関して不変な 36 個の同値類それぞれに独立な 0/1（遮蔽/可視）を割り当てるモデル。
4. **完全 256 パターン LUT**:
   256 通りのビットパターンそれぞれに独立な 0/1 を割り当てる理論的上限モデル（Oracle）。

#### 最適化目的関数

* **目的関数 A（平均 IoU 最大化: Unweighted Mean IoU Optimal）**:
  全データセット条件の平均 IoU を最大化します。36 クラスおよび 256 パターンでは、MILP 解や多数決割当てを初期値とする最急上昇局所探索（Steepest Ascent Local Search）によって大域的最適解を導出します。
* **目的関数 B（1-IoU 平均相対低減率最大化: Relative Reduction Optimal）**:
  従来法（固定 8 候補の共通最適 $J_{\mathrm{base}}$）からの誤り削減率 $r_i = \frac{J_i - J_{\mathrm{base}}}{1 - J_{\mathrm{base}}}$ を最大化します。重み $w_i = \frac{1}{1 - J_{\mathrm{base}}}$ を用いた重み付き MILP（混合整数計画法）により厳密解を求解します。

---

### 4.5 差分可視化マップ (Diff Map) カラーコーディング仕様

[`export_occlusion_diff_maps.py`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Python/export_occlusion_diff_maps.py) および [`optimize_pattern_rules.py`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Python/optimize_pattern_rules.py) では、統一された以下のカラーパレットで差分を可視化します：

| 分類 | RGB 値 | 表示色 | 意味・解釈 |
| :--- | :--- | :--- | :--- |
| **正しく可視 (TP)** | `[200, 200, 200]` | 薄いグレー | 正解・テスト共に可視。正常に表示された領域。 |
| **正しく遮蔽 (TN)** | `[45, 45, 45]` / `[15, 15, 15]` | 暗いグレー / 黒 | 正解・テスト共に遮蔽。正常に隠れた手メッシュ領域。 |
| **過剰遮蔽 (FN)** | `[255, 45, 45]` | **赤色 (Red)** | **重大な欠損**。本来見えるはずの仮想物体が誤って消えた領域。 |
| **遮蔽漏れ (FP)** | `[30, 144, 255]` | **青色 (Blue)** | **ゴースト透過**。本来手で隠れるはずの仮想物体が透けて見えた領域。 |

モンタージュ画像（4 ペイン構成）では、`[Ground Truth | Test 画像 | 差分マスク (Diff Mask) | 半透明オーバーレイ (Overlay)]` が横連結され、上部に IoU 値、過剰遮蔽画素数、遮蔽漏れ画素数が自動描画されます。

---

### 4.6 生成データセット & 出力ファイル一覧

| ファイル名 | 格納場所 | 内容説明 |
| :--- | :--- | :--- |
| `pattern_VO.csv` | `Assets/Features/SICESI/Data/` | 全 48 条件合算 256 パターンの可視/遮蔽正解画素数テーブル |
| `common_rules_summary.csv` | `RuleOptimizationResults/` | 目的関数 A・B × 4 手法の決定共通規則および性能サマリー |
| `objective_comparison.csv` | `RuleOptimizationResults/` | 目的関数 A（平均 IoU）と目的関数 B（相対低減率）の直接比較表 |
| `common_rule_evaluation.csv` | `RuleOptimizationResults/` | 全 36 条件データに対する各手法の評価（IoU, 相対低減率, 正味誤画素削減率） |
| `individual_optima.csv` | `RuleOptimizationResults/` | 各条件ごとの個別最適規則・上限値一覧 |
| `individual_gap_analysis.csv` | `RuleOptimizationResults/` | 個別最適からの性能低下幅（汎化ギャップ）分析表 |
| `diff_metrics_summary.csv` | `RuleOptimizationResults/DiffMaps/` | 全条件・全手法の差分マップ集計メトリクス（TP, FN, FP, IoU） |
| `comparison_*.png` | `RuleOptimizationResults/DiffMaps/` | 4 手法（8候補, 20候補, 36クラス, 256パターン）の横連結比較モンタージュ画像 |
| `preset_benchmark_summary.csv` | `AdditionalExperiments/PresetBenchmark/` | プリセット一括ベンチマークの全ケース・全手法・全密度 IoU 集計サマリー |
| `scene_overview.png` | `AdditionalExperiments/Presets/.../<Case>/` | 俯瞰カメラ (`sceneCaptureCamera`) による通常 URP レンダリング配置画像 |
| `scene_left.png`, `scene_right.png` | `AdditionalExperiments/Presets/.../<Case>/` | 左眼・右眼カメラによる手・仮想物体の初期シーン配置画像 |
| `scene_transforms.json` | `AdditionalExperiments/Presets/.../<Case>/` | 手・仮想物体・左右カメラ・俯瞰カメラの Transform 姿勢 JSON |

---

### 4.7 汎化ベンチマーク 5 手法設定仕様

[`SICESI_BenchmarkMethodRegistry.cs`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Scripts/Benchmark/SICESI_BenchmarkMethodRegistry.cs) にて一元管理される、学術比較用の 5 手法パラメータ定義です。

| 手法名 | 評価モード (`mode`) | 最低占有数 $R_{\mathrm{th}}$ | 最大連続非占有 $L_{\mathrm{th}}$ | LUT 定義 |
| :--- | :--- | :---: | :---: | :--- |
| **Mode_Average (Baseline)** | `Average` | 1 | 8 | (なし・内積平均) |
| **Mode_8 (SectorThreshold_R7)** | `SectorThreshold` | 7 | 8 | (なし・占有数のみ) |
| **Mode_20 (Consecutive_R4_L1)** | `SectorConsecutiveZeros` | 4 | 1 | (なし・連続性複合) |
| **Mode_36 (RotationLUT36)** | `RotationLUT36` | 1 | 8 | `0x131F12EF...` (36クラス回転不変) |
| **Mode_256 (PatternLUT256)** | `PatternLUT256` | 1 | 8 | `0x014FDA4F...` (256パターン最適) |

---

### 4.8 プリセットベンチマーク出力スキーマ (`preset_benchmark_summary.csv`)

#### ディレクトリ階層構造

各プリセットケースの実行により、以下の構造で撮影画像・メタデータ・評価サマリーが出力されます。

```text
AdditionalExperiments/PresetBenchmark/
├── preset_benchmark_summary.csv     # 全ケース・全手法・全密度合算 IoU 集計サマリー
└── <PresetCaseName>/                # 例: AdditionalCase_1
    ├── scene_overview.png           # 俯瞰カメラによる通常 URP レンダリング配置画像
    ├── scene_left.png               # 左眼カメラによるシーン配置画像 (手肌色・点群非表示)
    ├── scene_right.png              # 右眼カメラによるシーン配置画像 (手肌色・点群非表示)
    ├── scene_transforms.json        # 手・仮想物体・カメラの姿勢 Transform JSON
    ├── GT/                          # 幾何学的オクルージョン真値画像
    │   ├── gt_left.png
    │   └── gt_right.png
    └── Density_0.30/                # 各点群密度ディレクトリ
        ├── Mode_Average/
        │   ├── test_left.png        # 点群オクルージョンテスト画像
        │   ├── test_right.png
        │   ├── diff_left.png        # TP/TN/FN/FP 4色差分マップ (threshold=128)
        │   └── diff_right.png
        ├── Mode_8/
        ├── Mode_20/
        ├── Mode_36/
        └── Mode_256/
```

#### CSV カラム定義 (`preset_benchmark_summary.csv`)

プリセット一括実行時に [`SICESI_BenchmarkReporter.cs`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Scripts/Benchmark/SICESI_BenchmarkReporter.cs) から出力される CSV の列構成です。

| 列名 | 型 | 説明 |
| :--- | :--- | :--- |
| `Timestamp` | `string` | 測定実行日時 (`yyyy-MM-dd HH:mm:ss`) |
| `VirtualObject` | `string` | 評価対象の仮想 3D オブジェクト名 |
| `HandMesh` | `string` | 評価対象の正解手メッシュ名（または `[Average]`） |
| `Density` | `float` | 点群密度数値（既定単位: $\mathrm{pts/mm^2}$） |
| `Method` | `string` | 評価手法名（`Mode_Average`, `Mode_8`, `Mode_20`, `Mode_36`, `Mode_256`） |
| `Mean_IoU` | `float` | 左右眼の平均 IoU ($(\mathrm{Left\_IoU} + \mathrm{Right\_IoU}) / 2$) |
| `Total_IoU` | `float` | 左右眼合算画素による全体 IoU ($\sum \mathrm{TP} / \sum (\mathrm{TP} + \mathrm{FP} + \mathrm{FN})$) |
| `Left_IoU`, `Right_IoU` | `float` | 左眼 / 右眼それぞれの個別 IoU |
| `Left_TP`, `Right_TP` | `int` | 正しく可視と判定された画素数 (True Positive) |
| `Left_FP`, `Right_FP` | `int` | 遮蔽漏れ・透過ゴースト画素数 (False Positive: 本来遮蔽されるべき領域) |
| `Left_FN`, `Right_FN` | `int` | 過剰遮蔽画素数 (False Negative: 本来可視であるべき領域の欠損) |

##### 5条件・全プリセットの平均値自動集計 (Mean across conditions)
[`SICESI_MethodMetricsAccumulator.cs`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Scripts/Benchmark/SICESI_MethodMetricsAccumulator.cs) により、各手法・各密度ごとに全評価ケースのスコアが自動累積されます。全プリセットの一括実行完了時、CSV の末尾に各手法の **全配置平均行（`HandMesh = [Average]`）** が自動追記され、配置ごとの個体差を超えた総合的な手法性能を一目で把握できます。

---

## 5. デバッグ・留意事項

### 5.1 ジッター排除とカメラポーズロック

* **`Time.timeScale = 0f` による完全静止**:
  スイープ撮影中はアニメーションや時間依存の物理更新による微小なメッシュ変形・ジッターを防ぐため、`Time.timeScale = 0f` に固定されます。
* **`RenderPipelineManager.beginCameraRendering` による行列ロック**:
  テスト画像と 8 枚のセクターマスクの間で視線追跡（Eye Tracking）の微小揺らぎによるピクセルズレを防止するため、描画直前にカメラの `worldToCameraMatrix` および `projectionMatrix` をスナップショットから強制適用し、**100.00% のピクセル完全一致** を保証します。

### 5.2 数学的整合性自動検算ツール

* **`verify_items_1_to_6.py`**:
  データセット全画像から直接計算した画素総数と、`pattern_VO.csv` の $\sum (V + O)$、および最適化評価関数の値が数学的に完全一致することを自動検算します。
* **`verify_sector_masks.py`**:
  8-bit 占有パターンマスク（`sector_mask_*.png`）による予測と、GPU シェーダーで直接計算された実遮蔽判定マスク（`gpu_occluded_mask_*.png`）を画素比較し、**一致率 99.998%** であることを実証・可視化します。

### 5.3 統制ログ管理 (`AppLogManager`)

SICESI 関連のすべてのログは `AppLogger.Log("SICESI", ...)` を経由して出力され、`AppLogManager` から一元制御されます。個別コンポーネントでの不要な Inspector トグル変数は存在しません。

| サブトリガー名 (`subTag`) | 担当コンポーネント | 対象処理 |
| :--- | :---: | :--- |
| `SICESI_Core` | `SICESI_StereoEvaluationController` | 初期化、カメラ・仮想物体自動検出、パラメータ検証ログ |
| `SICESI_Capture` | `SICESI_StereoEvaluationController` | 単発撮影、GT 撮影、俯瞰撮影、マテリアル・Transform 復元ログ |
| `SICESI_StereoSweep` | `SICESI_StereoEvaluationController` | 密度・セクター数・連続非占有・閾値スイープの進行ログ |
| `SICESI_MaskSweep` | `SICESI_SectorMaskCollector` | 8 セクター画面保存マスクスイープ、256 パターンスイープ、Readback 処理 |

### 5.4 最終出力 Ground Truth 撮影プロトコルと完全可逆マスク比較

* **Unlit White 一貫維持プロトコル**:
  ステレオ評価（`SICESI_IoUCalculator` / `calc_stereoMaskedValue.py`）では、ピクセル値（RGB）を閾値 `128` で二値化して IoU を算出します。
  GT 撮影直後に仮想物体が通常カラーマテリアル（暗い羽やテクスチャの影など）に戻ってしまうと、テスト画像側でテクスチャ陰影部がすべて「遮蔽された背景」と誤判定され、大量の False Negative（偽陰性）が発生して IoU が不当に暴落します。
  そのため、各ケースの評価ループでは **GT 撮影から 5 手法のテスト撮影完了まで一貫して仮想物体を `Unlit White`（白 255）に維持** し、全テスト完了後に `finally` 節で確実に元のマテリアルへ復元するプロトコルを採用しています。
* **点群カラーモード一時 Black 化と Python 完全互換 IoU 計算**:
  テスト画像撮影時、点群マテリアルの描画色が肌色（Skin: RGB 約 244, 203, 179）のままだと、各カラーチャンネルが二値化閾値 `128` を超えているため、点群によって遮蔽された領域（本来隠れるべき手領域）が二値化時に「白（仮想物体）」と誤認識され、膨大な False Positive（遮蔽漏れ）と判定されて IoU が約 5% 不当に低下する問題が生じていました。
  これを解決するため、テスト画像撮影時のみ [`RsMaterialController.cs`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/PCD/Scripts/PointCloud/RsMaterialController.cs) を経由して点群カラーモードを `PointCloudColorMode.Black`（完全な黒: RGB 0,0,0）に一時変更し、撮影完了後に `try-finally` 構造により元のカラーモード（Skin 等）へ安全復元するプロトコルを確立しました。
  これにより、閾値を `threshold = 128` のまま Python 側（`sicesi_core/metrics.py`）と数学的・ビット単位で完全一致させ、真値幾何メッシュ（GT）に対する点群オクルージョンの一致度（IoU）を正確に測定できます。
* **完全可逆（ロスレス）PNG 保存**:
  すべての画像は `Texture2D.EncodeToPNG()` を用いて **PNG 形式（完全可逆圧縮・ロスレス）** で保存されます。JPEG のような不可逆圧縮ブロックノイズや色のにじみは一切発生せず、GPU の出力ピクセル値がそのままビット完全で保持されます。

### 5.5 高解像度・超高密度時における動的 LOD と探索半径の特性

PCD オクルージョンパイプラインの動的 LOD（`enableDensityBasedLOD`）では、局所密度に応じて探索半径（Level）が決定されます：

$$L = \frac{p'}{\sqrt{\text{density}}}$$

* **低密度時 (0.25〜1.0 $\mathrm{pts/mm^2}$)**:
  点の間隙を埋めるために探索半径が自動的に Level 2〜6 に拡大し、手の輪郭や間隙が確実にカバーされて **IoU 94%〜96.6%** の高い一致率が得られます。
* **超高密度時 (4.0〜16.0 $\mathrm{pts/mm^2}$)**:
  4K 解像度（3840×2160）において点群が密集すると、数式に基づき探索半径が **Level 0（半径わずか 1 ピクセル）** まで極小化されます。
  この極小半径下では、手と仮想物体が近接・接触している境界領域で点群が探索範囲外となり、点群をすり抜けて仮想物体が描画される「遮蔽漏れ (FP)」が急増するため、IoU が約 **80%〜85%** に低下する現象が発生します。
* **下限探索半径の保証 (`minSearchLevel`)**:
  超高密度時でも 95% 以上のスコアを維持したい場合は、インスペクター上で `minSearchLevel` を `2` または `3`（数ピクセル以上の探索半径を下限として保証）に設定することで、輪郭のすり抜けを効果的に防止できます。

### 5.6 俯瞰カメラ通常 URP レンダリングキャプチャ (SaveCameraViewDirect) の技術的背景

* **3DDisplay 投影と通常 URP レンダリングの完全分離**:
  本プロジェクトのメイン GameView は、Spatial Reality Display (SRDisplay) 等の裸眼 3D ディスプレイへのステレオ投影や HalfMirror 反転、ホモグラフィ補正パイプラインが組み込まれています。
  しかし、シーン配置記録用の俯瞰カメラ (`sceneCaptureCamera`) は、実験環境内の 3D 空間配置（手・仮想物体・ステレオカメラの幾何学的相対位置）を客観的に記録するための通常 URP カメラ画像です。
* **`SaveCameraViewDirect` による直接レンダリング**:
  従来の `ScreenCapture.CaptureScreenshot` や GameView キャプチャでは、3DDisplay の歪み補正や画面解像度に依存してしまい、さらに同一画像（`scene.png` と `scene_overview.png`）が重複生成される冗長性がありました。
  [`SICESI_ScreenCaptureUtil.SaveCameraViewDirect`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Scripts/Capture/SICESI_ScreenCaptureUtil.cs) では、指定カメラの画角・アスペクト比に合わせた一時 `RenderTexture` を `cam.targetTexture` に割り当て、`cam.Render()` を明示的に直接呼び出します。
  これにより、GameView の表示状態やディスプレイ投影に一切影響されることなく、歪みのない通常 2D 画角によるロスレス PNG 画像を直接生成・保存できます。
* **独立クラス [`SICESI_SceneOverviewCapturer.cs`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Scripts/Capture/SICESI_SceneOverviewCapturer.cs) による責務集約**:
  俯瞰撮影・ステレオ配置撮影（`scene_left.png`, `scene_right.png`）・Transform シリアライズ（`scene_transforms.json`）の全処理を新設された本クラスに集約し、[`SICESI_SnapshotRunner.cs`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Scripts/Sweepers/SICESI_SnapshotRunner.cs) および [`SICESI_GeneralizationBenchmarkRunner.cs`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/SICESI/Scripts/Benchmark/SICESI_GeneralizationBenchmarkRunner.cs) から委譲呼び出しとすることで、コードの重複・冗長性を完全に解消しました。

詳細なログ仕様については [Logging.md](./Logging.md) を参照してください。
