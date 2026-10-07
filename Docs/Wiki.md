# RealTimeOcclusion システム統合 Wiki (ポータル)

本プロジェクトは、Intel RealSense 等のセンサーから取得したリアルタイム点群（Point Cloud）を基盤とし、**「視覚的な遮蔽処理（オクルージョン）」**と**「物理的な接触判定・触覚提示（ハプティクス）」**という 2 つのサブシステムを軸に構成されています。

本ドキュメントは、プロジェクト全体の構造を俯瞰し、各サブシステムの詳細ドキュメントへナビゲーションする統合ポータルです。

---

## 1. システム全体図

```mermaid
graph TD
    %% スタイル定義
    classDef render fill:#1A5276,stroke:#2980B9,stroke-width:2px,color:#EBF5FB;
    classDef haptic fill:#78281F,stroke:#C0392B,stroke-width:2px,color:#FDEDEC;
    classDef debug fill:#6C3483,stroke:#8E44AD,stroke-width:2px,color:#F5EEF8;
    classDef common fill:#1E8449,stroke:#27AE60,stroke-width:2px,color:#EAF8F2;
    classDef display fill:#D35400,stroke:#E67E22,stroke-width:2px,color:#FDEDEC;
    classDef control fill:#7D6608,stroke:#9A7D0A,stroke-width:2px,color:#FEF9E7;
    classDef sub fill:#2C3E50,stroke:#566573,stroke-width:1px,color:#D5D8DC;

    subgraph WIKI ["📄 統合ポータル (Wiki.md) - システム全体俯瞰"]
        direction TD

        subgraph Calibration ["⚙️ 0. 基盤・初期化・ログ・キーボード"]
            direction LR
            InitNode["初期化とアライメント<br/>(Initialization.md)"]:::common
            DebugNode["PCV デバッグビューア<br/>(DebugPCV.md)"]:::debug
            LogNode["統制ログ管理システム<br/>(Logging.md)"]:::common
            KeyNode["統制キーボード管理<br/>(Keyboard.md)"]:::common
        end

        PointCloudNode["📦 1. 点群ストリーミング・統合パイプライン<br/>(PointCloudPipeline.md)"]:::common
        
        subgraph RealTimeProcessing ["⚡ リアルタイム処理"]
            direction LR
            RenderNode["🎨 2. 視覚オクルージョン<br/>(OcclusionRendering.md)"]:::render

            subgraph HapticsGroup ["🔊 ハプティクス系"]
                direction TB
                CollisionNode["⚡ 3. 衝突判定<br/>(Collision.md)"]:::haptic
                CollisionAlgoNode["└ 🔬 アルゴリズム比較<br/>(CollisionAlgorithmComparison.md)"]:::sub
                AutdNode["🔊 4. AUTD制御<br/>(Haptics.md)"]:::haptic
                HapticsAlgoNode["└ 🔬 アルゴリズム比較<br/>(HapticsAlgorithmComparison.md)"]:::sub
                FoxFootNode["└ 🏗️ Fox足先照射<br/>(FoxFootHaptics.md)"]:::sub
                FoxBodyNode["└ 🏗️ Fox全身体照射<br/>(FoxBodyHaptics.md)"]:::sub
                IllusionNode["└ 🔬 触覚錯覚モジュール<br/>(HapticsIllusion.md)"]:::sub
                HowToNode["└ 📖 使い方ガイド<br/>(HowToUseHaptics.md)"]:::sub
                SDKNode["└ 🔧 SDK移行ガイド<br/>(AUTD3_SDK_Transition.md)"]:::sub
            end
        end

        DisplayNode["👓 5. 3D立体視・ハーフミラー<br/>(Display3D.md)"]:::display
        PhysicsNode["🌀 6. 物理応答・インタラクション<br/>(PhysicalResponse.md)"]:::control
        ExpNode["🧪 7. 被験者実験<br/>(Experiments.md)"]:::control

        %% パイプラインデータフロー
        Calibration -->|"アライメント行列"| PointCloudNode
        PointCloudNode -->|"点群統合データ"| RealTimeProcessing
        CollisionNode -->|"フォーカス・振幅データ"| AutdNode
        RenderNode -->|"オクルージョン合成結果"| DisplayNode
        
        %% 独立した制御系
        PhysicsNode -.->|"カメラ追従・UI操作"| RenderNode
        PhysicsNode -->|"ターゲット自動連携"| HapticsGroup
        ExpNode -.->|"条件自動適用・データ記録"| AutdNode
    end
```

---

## 2. ドキュメントナビゲーション

### ドキュメント種類の凡例
| アイコン | 種類 | 説明 |
|:---:|:---|:---|
| 🏗️ | システム設計書 | コアアーキテクチャ・アルゴリズム詳細 |
| 🔬 | アルゴリズム比較 | 旧実装 vs 新実装の深掘り資料 |
| 📖 | How-To / リファレンス | 使い方ガイド・操作一覧 |
| 🔧 | SDK移行ガイド | SDK バージョン切り替え手順 |
| 🧪 | 実験フレームワーク | 心理物理実験パラダイム・データ収集 |

### 全ドキュメント一覧 (全 21 ファイル)

| # | 種類 | ドキュメント | 概要 |
|:---|:---:|:---|:---|
| **0** | 🏗️ | [Initialization.md](./Initialization.md) | 複数カメラのアライメント・キャリブレーション |
| | 🏗️ | [DebugPCV.md](./DebugPCV.md) | 点群データのリアルタイムプレビュー・デバッグビューア |
| | 🏗️ | [Logging.md](./Logging.md) | 統制ログ管理システム (`AppLogManager` & `AppLogger`) |
| | 🏗️ | [Keyboard.md](./Keyboard.md) | 統制キーボード管理システム (`AppKeyboardManager` & `AppKeyboard`) |
| **1** | 🏗️ | [PointCloudPipeline.md](./PointCloudPipeline.md) | RealSense 点群取得 → GPU 非同期マージ |
| | 🏗️ | └── [DummyPointCloud.md](./DummyPointCloud.md) | Unity 3Dモデルからのダミー点群生成・法線ノイズ・外れ値付与 |
| **2** | 🏗️ | [OcclusionRendering.md](./OcclusionRendering.md) | URP RenderGraph 上の点群オクルージョン処理 (Mode 8/20/36/256 & 1クロックLUT参照) |
| **3** | 🏗️ | [Collision.md](./Collision.md) | GPU 衝突判定・クラスタリング (HCD Pipeline) |
| | 🔬 | └── [CollisionAlgorithmComparison.md](./CollisionAlgorithmComparison.md) | Native C++ vs GPU の数理モデル比較 |
| **4** | 🏗️ | [Haptics.md](./Haptics.md) | AUTD3 超音波ハプティクス出力制御 |
| | 🔬 | └── [HapticsAlgorithmComparison.md](./HapticsAlgorithmComparison.md) | 提示アルゴリズム・STM 軌道比較 |
| | 🏗️ | └── [FoxFootHaptics.md](./FoxFootHaptics.md) | キツネ足先・尻尾ハプティクス仕様 + カスタム拡張 |
| | 🏗️ | └── [FoxBodyHaptics.md](./FoxBodyHaptics.md) | キツネ胴体表面ハプティクス仕様 |
| | 🔬 | └── [HapticsIllusion.md](./HapticsIllusion.md) | 触覚錯覚 (Apparent Movement / Phantom Sensation) モジュール |
| | 📖 | └── [HowToUseHaptics.md](./HowToUseHaptics.md) | ハプティクスの初回セットアップ〜使い方ガイド |
| | 🔧 | └── [AUTD3_SDK_Transition.md](./AUTD3_SDK_Transition.md) | AUTD3 SDK 新旧仕様比較と切り替え方法 |
| **5** | 🏗️ | [Display3D.md](./Display3D.md) | SRDisplay 視線追跡 + ハーフミラー鏡像制御 |
| **6** | 🏗️ | [PhysicalResponse.md](./PhysicalResponse.md) | 仮想モデル管理・操作・リフト追従・物理応答パラメータ統合制御 |
| **7** | 🏗️ | [WeatherEffect.md](./WeatherEffect.md) | 雨粒・飛沫、ゼロアロケーション再雷撃落雷、プロシージャル音響、点群ジェスチャー天候制御 |
| **8** | 🧪 | [Experiments.md](./Experiments.md) | 被験者実験フレームワーク (2AFC / ABX / 調整法 / データ出力) |
| **9** | 🧪 | [SICESI2026.md](./SICESI2026.md) | SICE SI 2026 学術評価・ステレオオクルージョン自動撮影 & 占有マスク最適化システム |

---

## 3. 各サブシステム概要

### ⚙️ 0. 基盤・初期化・ログ
複数 RealSense カメラの位置合わせ（キャリブレーション）と、JSON ベースのアライメント設定の保存・復元を管理します。PCV デバッグビューアにより点群データのプレビューが可能です。また、統制ログ管理システム (`AppLogManager` & `AppLogger`) により全モジュールのログトグルを一元制御します。

📎 詳細: [Initialization.md](./Initialization.md) / [DebugPCV.md](./DebugPCV.md) / [Logging.md](./Logging.md)

---

### 📦 1. 点群ストリーミング・統合パイプライン
RealSense からの非同期データ取得、HSV/YCbCr カラーフィルタリング、および GPU ゼロコピー CommandBuffer マージを行います。統合点群バッファはオクルージョンとハプティクスの両パイプラインへ供給されます。実機カメラなし環境向けにダミー点群生成および法線ノイズ・外れ値付与機能 (`DummyPointCloud`) も提供します。

📎 詳細: [PointCloudPipeline.md](./PointCloudPipeline.md) / [DummyPointCloud.md](./DummyPointCloud.md)

---

### 🎨 2. 視覚オクルージョン・レンダリングシステム
URP RenderGraph 上で点群をスクリーン空間に投影し、仮想オブジェクトとの前後遮蔽を計算します。多段 Compute Shader（Joint Bilateral 補間、Pull-Push 補完、モルフォロジー演算）により、エッジ保存型の滑らかな Hole Filling を実現しています。

📎 詳細: [OcclusionRendering.md](./OcclusionRendering.md)

---

### ⚡ 3. 衝突判定・クラスタリング
統合点群と仮想オブジェクトの接触を GPU で並列計算します。Voxel Grid による高速枝切り、Möller-Trumbore レイキャストによる厳密な内外判定、空間ハッシュによるクラスタリングを経て、安定したトラッキングデータを出力します（処理時間: 0.05ms）。

📎 詳細: [Collision.md](./Collision.md) | 📎 比較: [CollisionAlgorithmComparison.md](./CollisionAlgorithmComparison.md)

---

### 🔊 4. 超音波ハプティクス出力
衝突判定からの接触データを元に AUTD3 ハードウェアを駆動します。非同期 API による非ブロッキング送信、Focus 提示・各種 STM 軌道変調、および触覚錯覚生成モジュール (`HapticsIllusion`) を完備しています。

📎 詳細: [Haptics.md](./Haptics.md) | 📖 使い方: [HowToUseHaptics.md](./HowToUseHaptics.md)  
📎 比較: [HapticsAlgorithmComparison.md](./HapticsAlgorithmComparison.md) | 🔧 SDK: [AUTD3_SDK_Transition.md](./AUTD3_SDK_Transition.md)  
📎 Fox足照射: [FoxFootHaptics.md](./FoxFootHaptics.md) | 📎 Fox胴体照射: [FoxBodyHaptics.md](./FoxBodyHaptics.md)  
📎 触覚錯覚モジュール: [HapticsIllusion.md](./HapticsIllusion.md)

---

### 👓 5. 3D立体視・ハーフミラー制御
SDK 標準トラッキングを完全活用し、描画空間をディスプレイ中心で X 軸反転することでハーフミラー越しの正しい視差を実現します。

📎 詳細: [Display3D.md](./Display3D.md)

---

### 🌀 6. 物理応答・インタラクションシステム
仮想モデルの一元管理（`PR_VirtualObjectManager`）、アニメーション非依存ボーン自動検出・一括同期（`PR_BoneDetector` + `PR_BoneSearcher` + `PR_BoneSync`）、キーボードアニメーション・移動・LookAt制御（`PR_AnimationController`）、PCD設定キー操作（`PR_PCDKeyController`）、手の平によるキャラクター持ち上げ・加算落下復帰（`PR_LiftController`）、および統制ログ（`PR_LogTriggers`）を包括的に提供します。

📎 詳細: [PhysicalResponse.md](./PhysicalResponse.md)

---

### ⚡ 7. 天候演出・落雷インタラクションシステム
SRDisplay 等の裸眼立体ディスプレイ環境において、ワールド空間固定の雨粒・飛沫、ゼロアロケーション中点変位法による再雷撃マルチストローク落雷、プロシージャル DSP 音響合成（雨音・雷鳴）、および HCD 点群クラスタ（手の平重心）ジェスチャーによるリアルタイム天候制御を提供します。

📎 詳細: [WeatherEffect.md](./WeatherEffect.md)

---

### 🧪 8. 被験者実験フレームワーク
心理物理学実験（2AFC, ABX, 単一刺激法, 調整法）を統一的に管理・実行するフレームワークです。教示・練習・本試行・休憩の自動進行、キーボード / ゲームパッド応答受付、および物理パラメータ・反応時間の CSV / JSON 自動記録を提供します。

📎 詳細: [Experiments.md](./Experiments.md)

---

### 🧪 9. SICE SI 学術評価・ステレオオクルージョン最適化システム
ステレオカメラ（左右眼）からの自動一括撮影、8セクター二値マスク収集、および Python 側での数理最適化（MILP / 局所探索）と定性差分マップ（diff_map）自動生成を一気通貫で実行するフレームワークです。

📎 詳細: [SICESI2026.md](./SICESI2026.md)

---

## 4. 全体システムの最適化思想と共有価値

本プロジェクトは、1秒間に数万〜数十万点の点群データをリアルタイムに処理するため、以下の最適化思想をすべての機能ノードで共通して貫いています。

1. **CPU-GPU ゼロコピー転送 & 非同期 CommandBuffer マージ**:
   RealSense 等から出力されたバッファは CPU にコピーバックせず GPU 上で保持し、さらに CommandBuffer を用いて CPU を全くブロックせずに GPU のみで非同期に統合マージを完了します。URP の RenderGraph パスへもノンブロッキングで引き渡され、徹底したハイパフォーマンスを担保します。
2. **徹底的な GC（Garbage Collection）排除**:
   毎フレームの配列確保や一時オブジェクト生成を完全に排除し、バッファリサイズ時のキャッシュ＆再利用、構造体プール等を徹底しています。
3. **アンマネージドネイティブ参照の厳密なライフサイクル管理**:
   C++ ネイティブ参照のメモリリークを確実に防止するため、`using` や `Dispose()` による速やかな解放を徹底し、高頻度動作時でも安定したパフォーマンスを維持します。
