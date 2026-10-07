# 天候演出・落雷インタラクションシステム (WeatherEffect) 仕様書

> 📂 **親ノード**: [Wiki.md](./Wiki.md) | 🏷️ **種類**: 🏗️ システム設計書  
> [RealTimeOcclusion Wiki (ポータル)](./Wiki.md) に戻る

本ドキュメントでは、Sony Spatial Reality Display (SRDisplay) などの裸眼立体ディスプレイ環境において、雨粒・飛沫パーティクル、物理光量カーブに基づく再雷撃落雷、プロシージャル DSP 音響合成、および HCD 点群クラスタ（手の平）ジェスチャーによるリアルタイム天候制御を提供する**天候演出・落雷インタラクションシステム (`WeatherEffect`)** について包括的に解説します。

---

## 1. 概要

`WeatherEffect` システムは、リアルタイム点群処理および立体視レンダリングと緊密に連携し、没入感の高い天候シミュレーションを実現する統合モジュール群です。`AGENTS.md` に定義された Feature-based Architecture に基づいて責務を細粒度に分割し、完全なゼロアロケーション（Zero-GC）と高い保守性を両立しています。

### 主な特徴

* **ワールド空間固定の雨シミュレーション (`WeatherRainProcessor` & `WeatherRainBuilder`)**: 雨粒および飛沫（Splash）のパーティクルシステム構築や衝突設定を `WeatherRainBuilder` に分離し、プロセッサ本体はランタイムの視点追従と雨強度反映に専念します。シミュレーション空間はワールド座標系に固定されるため、視点が移動してもすでに降っている雨粒が頭についてくる不自然さを排除します。
* **状態遷移とタイマーの Pure C# 分離 (`WeatherStateController` & `WeatherAutoStrikeTimer`)**: `WeatherManager` の肥大化（神クラス化）を防ぐため、雨強度の線形補間フェード計算を `WeatherStateController` に、ポアソン過程に基づく嵐モードの落雷タイマー評価を `WeatherAutoStrikeTimer` に移譲し、ゼロGC・高テスタビリティな設計を実現しています。
* **物理マルチストローク再雷撃落雷 (`WeatherLightningProcessor` & `WeatherLightningGeometry`)**: 単発の閃光ではなく、2〜4回の高速ストローク（30〜60msパルス）と残光減衰（0.22秒）からなる統合発光量カーブ $F(t) \in [0, 1]$ を生成します。中点変位法（Midpoint Displacement）による稲妻ジオメトリ生成はダブルバッファリングによりヒープ確保ゼロ（Zero-GC）で動作し、落雷瞬間のカクつきを根絶します。
* **3D 十字帯メッシュ＆着地点放電スパーク (`WeatherLightningBoltPool`)**: LineRenderer の URP `DepthOnlyPass`（深度プリパス）除外制限および親 Transform による画面外飛びバグを根本解決するため、ワールド原点独立階層に配置された `MeshRenderer` + `MeshFilter` による 3D 十字帯メッシュ（Cross Ribbon Mesh）と、着地点周囲に放射状に弾ける地上放電スパーク（Ground Impact Sparks）を採用しています。視差を持つ裸眼立体ディスプレイ（SRDisplay）や VR でも 360 度あらゆる角度から光の筋と着弾点の両方が視認でき、ダブルバッファリングによりヒープ確保ゼロ（Zero-GC）で動作します。
* **位置上限の不透明雲シミュレーション (`WeatherCloudProcessor` & `WeatherCloudBuilder`)**: `WeatherManager` で設定された空間位置上限（`cloudY`）に、DrawCall 1 回で描画可能なプロシージャル低ポリ 3D 雲メッシュ（Combined Mesh）を配置します。雨や雷と同様に `Layer 3 (PCD)` かつ完全不透明（Opaque Lit, ZWrite 1）でレンダリングされるため、点群オクルージョンパイプライン（`_VirtualDepthMap`）に完璧な立体深度を提供します。雨の激しさ（`RainIntensity` 0.0〜1.0）に応じて、雲の体積・カバレッジ倍率（`minScale` 〜 `maxScale`）および色調（穏やかな白 〜 重厚な嵐の暗黒色）が滑らかにグラデーション変化し、インスペクターやキーボード操作（`C` キー）により True/False で瞬時に表示切替が可能です。
* **立体視に配慮した環境調光 (`WeatherLightingProcessor`)**: カメラ直前の板ポリゴン配置を行わず、DirectionalLight（Sun）、環境光（Ambient）、落雷局所 PointLight を連動制御することで、SRDisplay などの立体視環境でも視差破綻を起こさずに自然な明暗・閃光を表現します。
* **プロシージャル DSP 音響合成 (`WeatherAudioSynthesizer`)**: 音声ファイル不要で、`OnAudioFilterRead` によるノイズ生成・1次ローパスフィルタリングにより雨音と雷鳴をリアルタイム合成します。着弾点と視点の距離に応じた音速遅延や周波数特性（近雷のクラック音、遠雷の重低音）を再現します。
* **点群クラスタ・ジェスチャー制御 (`WeatherGestureDetector` & `WeatherHcdInputBridge`)**: `HCD_Pipeline` の GPU クラスタ追跡結果から手の平の重心を直接取得し、頭上でのホールドによる「雨トグル」および急激な振り下ろしによる「落雷トリガー」を判定します。
* **仮想オブジェクト統合と触覚除外 (`IExcludeFromHcd`)**: `PR_VirtualObjectManager` に登録して `Tab` キーで仮想モデルと並んで天候を切り替え可能としつつ、`IExcludeFromHcd` により HCD（触覚判定）への無駄な自動バインドを安全にスキップします。

---

## 2. 設計思想・アーキテクチャ

### 2.1 ディレクトリ構成

```text
Assets/Features/Weather/
├── Materials/                              # URP 用マテリアル (雨・雲・稲妻)
│   ├── M_Weather_Rain.mat                  # 雨粒・飛沫用不透明マテリアル
│   ├── M_Weather_Cloud.mat                 # 雲クラスタ用不透明マテリアル (Opaque Lit)
│   └── M_Weather_Lightning.mat             # 稲妻用不透明マテリアル
├── Prefabs/                                # 天候・照明プレハブ
│   ├── SunLight.prefab                     # 太陽光 Directional Light プレハブ
│   └── BoltPointLight.prefab               # 落雷 Point Light プレハブ
├── Scripts/
│   ├── Core/                               # コア状態管理・ファサード・専任コーディネーター
│   │   ├── WeatherManager.cs               # 天候システム全体を束ねる薄いファサード (MonoBehaviour)
│   │   ├── WeatherHierarchyRegistrar.cs    # 階層自動構築・サブプロセッサ配線・PCDレイヤー管理 (Pure C#)
│   │   ├── WeatherConfigDispatcher.cs      # 共有空間バウンズ解決・全サブシステム設定同期 (Pure C#)
│   │   ├── WeatherStrikeCoordinator.cs     # 落雷トリガー・自動落雷・閃光・雷鳴音響調停 (Pure C#)
│   │   ├── WeatherSimulationCoordinator.cs # 雨フェード遷移・追従・強度伝達・ジェスチャー検知ループ (Pure C#)
│   │   ├── WeatherStateController.cs       # 雨強度フェード線形補間・状態管理 (Pure C#)
│   │   ├── WeatherSettings.cs              # カテゴリ別設定データモデル (Rain/Cloud/Lightning/Lighting/Audio/Input)
│   │   ├── WeatherKeyController.cs         # 衝突回避型デバッグキーボード操作 (G/C/B/7〜0)
│   │   └── WeatherFoxReactionHandler.cs    # 将来拡張用 Fox リアクション連携スタブ
│   ├── Rain/                               # 雨・飛沫シミュレーション
│   │   ├── WeatherRainProcessor.cs         # ランタイム強度・視点追従プロセッサ
│   │   ├── WeatherRainBuilder.cs           # ParticleSystem・サブエミッター・衝突構築ビルダー
│   │   └── WeatherRainCollisionMode.cs     # 衝突モード列挙型 (None / Plane / World)
│   ├── Cloud/                              # 雲シミュレーション・低ポリメッシュ
│   │   ├── WeatherCloudProcessor.cs        # 雲ライフサイクル・外部連携・統括プロセッサ
│   │   ├── WeatherCloudRenderer.cs         # 3D階層・メッシュ・マテリアル・PCDレイヤー描画マネージャ
│   │   ├── WeatherCloudMotion.cs           # 視点追従・スケール連動サイン波浮遊運動 (Pure C#)
│   │   ├── WeatherCloudBuilder.cs          # 3D低ポリ結合雲メッシュ生成ビルダー (Pure C#)
│   │   └── WeatherCloudColorGrading.cs     # 雨強度に応じた色・スケール評価 (Pure C#)
│   ├── Lightning/                          # 雷シミュレーション・幾何生成・階層プール
│   │   ├── WeatherLightningProcessor.cs    # 落雷ライフサイクル・イベント通知オーケストレーター
│   │   ├── WeatherLightningConfigApplier.cs # 設定展開・共有バウンズ統合・プール配線 (Pure C#)
│   │   ├── WeatherLightningStrikePlanner.cs # インターバル判定・着地点・空中始点選定 (Pure C#)
│   │   ├── WeatherLightningFlashController.cs # 閃光レベル F(t) 管理・ボルト制御・中断処理 (Pure C#)
│   │   ├── WeatherLightningGroundPicker.cs # 床面 Raycast・視線見通し選定 (Pure C#)
│   │   ├── WeatherLightningSequencer.cs    # 再雷撃マルチストローク・閃光タイムライン (Pure C#)
│   │   ├── WeatherAutoStrikeTimer.cs       # ポアソン過程自動落雷判定タイマー (Pure C#)
│   │   ├── WeatherLightningGeometry.cs     # ゼロアロケーション中点変位法 (Pure C#)
│   │   ├── WeatherLightningBoltPool.cs     # 稲妻ボルト階層・PCDレイヤー統括プール
│   │   ├── WeatherBoltGeometryDispatcher.cs # 主幹・枝・スパーク幾何計算・メッシュ更新 (Pure C#)
│   │   ├── WeatherBoltEmissionController.cs # HDR 発光色・MPB アルファ制御 (Pure C#)
│   │   └── WeatherLightningMeshBuilder.cs  # 3D十字帯メッシュ・放電スパーク構築ビルダー (Pure C#)
│   ├── Lighting/                           # 環境調光・閃光
│   │   └── WeatherLightingProcessor.cs     # 太陽光・環境光・落雷点光源制御
│   ├── Audio/                              # 音響システム
│   │   └── WeatherAudioSynthesizer.cs      # プロシージャル DSP 雨音・雷鳴合成
│   ├── Input/                              # ジェスチャー・点群入力
│   │   ├── IWeatherHandPositionProvider.cs # 手座標プロバイダー抽象インターフェース
│   │   ├── WeatherGestureDetector.cs       # 重心変位・ホールド・振り下ろし判定 (Pure C#)
│   │   └── WeatherHcdInputBridge.cs        # HCD_Pipeline クラスタ重心アダプター
│   ├── Debug/                              # AppLog 統制・デバッグ可視化
│   │   ├── WeatherLogTriggers.cs           # Weather 全体の AppLogManager 登録ヘルパー
│   │   └── WeatherGizmoDrawer.cs           # Scene ビュー Gizmo 描画ヘルパー
│   └── Editor/                             # Editor 拡張・タブ別 Drawer 分離
│       ├── WeatherManagerEditor.cs         # タブ分割 Inspector GUI (Toolbar) 薄いメインエディタ
│       ├── WeatherPlayModeControlDrawer.cs # PlayMode 実行時クイックコントロール描画 Drawer
│       ├── WeatherGeneralTabDrawer.cs      # 空間・カメラ設定・自動セットアップ描画 Drawer
│       ├── WeatherEffectTabDrawer.cs       # 雨・雲・落雷エフェクトパラメータ描画 Drawer
│       ├── WeatherEnvironmentTabDrawer.cs  # 照明・音響・ジェスチャー入力描画 Drawer
│       └── WeatherSetupUtility.cs          # 自動階層構築・アセット自動結線
```

### 2.2 クラス相関図

```mermaid
graph TD
    classDef facade fill:#1A5276,stroke:#2980B9,stroke-width:2px,color:#EBF5FB;
    classDef logic fill:#2E4053,stroke:#34495E,stroke-width:2px,color:#FDFEFE;
    classDef proc fill:#145A32,stroke:#1E8449,stroke-width:2px,color:#EAFAF1;
    classDef input fill:#78281F,stroke:#C0392B,stroke-width:2px,color:#FDEDEC;
    classDef ext fill:#4A235A,stroke:#7D3C98,stroke-width:1px,color:#F4ECF7;

    WM["WeatherManager<br/>(薄いファサード統括)"]:::facade
    WHR["WeatherHierarchyRegistrar<br/>(階層配線・PCDレイヤー Pure C#)"]:::logic
    WCD["WeatherConfigDispatcher<br/>(バウンズ・設定同期 Pure C#)"]:::logic
    WSTC["WeatherStrikeCoordinator<br/>(落雷・閃光・雷鳴調停 Pure C#)"]:::logic
    WSMC["WeatherSimulationCoordinator<br/>(雨フェード・追従・検知 Pure C#)"]:::logic

    WSC["WeatherStateController<br/>(雨フェード状態遷移 Pure C#)"]:::logic
    WAST["WeatherAutoStrikeTimer<br/>(ポアソン落雷タイマー Pure C#)"]:::logic
    WGD["WeatherGizmoDrawer<br/>(Gizmo描画ヘルパー)"]:::logic

    WK["WeatherKeyController<br/>(キーボードデバッグ操作)"]:::input
    WG["WeatherGestureDetector<br/>(ジェスチャー判定 Pure C#)"]:::input
    WHB["WeatherHcdInputBridge<br/>(HCDクラスタ重心供給)"]:::input

    WRP["WeatherRainProcessor<br/>(ランタイム雨制御)"]:::proc
    WRB["WeatherRainBuilder<br/>(ParticleSystem構築ビルダー)"]:::proc
    WCP["WeatherCloudProcessor<br/>(雲ライフサイクル統括)"]:::proc
    WCR["WeatherCloudRenderer<br/>(3D階層・メッシュ・PCD描画)"]:::proc
    WCM["WeatherCloudMotion<br/>(位置追従・浮遊ボブ計算 Pure C#)"]:::proc
    WCB["WeatherCloudBuilder<br/>(3D低ポリ雲メッシュ構築 Pure C#)"]:::proc
    WCCG["WeatherCloudColorGrading<br/>(色・量グラデーション評価 Pure C#)"]:::proc
    WLP["WeatherLightningProcessor<br/>(落雷ライフサイクル統括)"]:::proc
    WLCA["WeatherLightningConfigApplier<br/>(設定・バウンズ・配線 Pure C#)"]:::logic
    WLSP["WeatherLightningStrikePlanner<br/>(計画・始点選定 Pure C#)"]:::logic
    WLFC["WeatherLightningFlashController<br/>(F(t)閃光・終了制御 Pure C#)"]:::logic
    WLGP["WeatherLightningGroundPicker<br/>(着地点サンプリング Pure C#)"]:::proc
    WLS["WeatherLightningSequencer<br/>(再雷撃閃光シーケンサー Pure C#)"]:::proc
    WLG["WeatherLightningGeometry<br/>(ゼロアロケーション中点変位)"]:::proc
    WBP["WeatherLightningBoltPool<br/>(ボルト階層・PCDプール管理)"]:::proc
    WBGD["WeatherBoltGeometryDispatcher<br/>(幾何計算・メッシュ更新 Pure C#)"]:::logic
    WBEC["WeatherBoltEmissionController<br/>(HDR発光・MPB制御 Pure C#)"]:::logic
    WMB["WeatherLightningMeshBuilder<br/>(3D十字帯メッシュ・スパーク構築)"]:::proc
    WLT["WeatherLightingProcessor<br/>(太陽光・環境光・点光源調光)"]:::proc
    WAS["WeatherAudioSynthesizer<br/>(プロシージャルDSP音響)"]:::proc

    PCD["PCDRendererFeature<br/>(リアルタイム点群オクルージョン)"]:::ext
    HCD["HCD_Pipeline<br/>(GPU点群クラスタリング)"]:::ext

    WM *-- WHR
    WM *-- WCD
    WM *-- WSTC
    WM *-- WSMC
    WSTC *-- WAST
    WSMC *-- WSC
    WSMC *-- WG
    WM ..> WGD

    WM -->|PCD レイヤー (Layer 3) 描画| PCD
    WRP -->|PCD レイヤー (Layer 3) 描画| PCD
    WCR -->|PCD レイヤー (Layer 3) 描画| PCD
    WBP -->|PCD レイヤー (Layer 3) 描画| PCD

    WK -->|天候変更/雲トグル/落雷要求| WM
    WHB -->|手・重心座標| WG
    HCD -.->|TrackedCluster| WHB

    WSMC -->|強度 I 反映| WRP
    WRP ..>|生成・再構築委譲| WRB
    WSMC -->|強度 I 反映・トグル| WCP
    WCP --> WCM
    WCP --> WCR
    WCR ..>|結合メッシュ生成委譲| WCB
    WCP ..>|色・量評価委譲| WCCG
    WSTC -->|落雷要求・ポアソン評価| WLP
    WLP --> WLCA
    WLP --> WLSP
    WLSP --> WLGP
    WLP --> WLFC
    WLP --> WLS
    WLP --> WLG
    WLP --> WBP
    WBP --> WBGD
    WBP --> WBEC
    WBGD ..>|メッシュ生成・頂点計算委譲| WMB
    WSTC -->|減光率 / 閃光量 F(t)| WLT
    WSTC -->|音量 / 雷鳴トリガー| WAS
```

### 2.3 処理フロー

```text
[毎フレーム Update]
1. WeatherManager (ファサード):
   ├─ WeatherSimulationCoordinator.UpdateSimulation():
   │   ├─ WeatherStateController.Update(deltaTime): 雨強度の線形補間フェード
   │   ├─ WeatherRainProcessor.FollowViewer() ＆ SetIntensity(currentIntensity)
   │   ├─ WeatherCloudProcessor.FollowViewer() ＆ SetIntensity(currentIntensity): 色・量グラデーション更新
   │   ├─ WeatherAudioSynthesizer.RainVolume = currentIntensity
   │   └─ WeatherHcdInputBridge から手座標取得 ＆ WeatherGestureDetector.Update()
   └─ WeatherStrikeCoordinator.EvaluateAutoStrike():
       └─ WeatherAutoStrikeTimer.Evaluate(): ポアソン分布に基づく自動落雷判定 ＆ トリガー

2. WeatherLightningProcessor (落雷実行時):
   ├─ WeatherLightningGroundPicker により指定 X, Z 矩形平面または前方円環から床面 Raycast 選定
   ├─ WeatherLightningGeometry により中点変位パスをダブルバッファ計算 (Zero-GC)
   ├─ WeatherLightningBoltPool に skyPoint / targetGround を引き渡し
   │   └─ WeatherLightningMeshBuilder により 3D 十字帯メッシュ ＆ 地上放電スパークを Zero-GC 更新
   ├─ WeatherLightningSequencer により再雷撃マルチストロークコルーチン駆動 ＆ 発光量 F(t) を更新
   └─ WeatherStrikeCoordinator が受領し、WeatherAudioSynthesizer へ距離・音速遅延付き雷鳴再生を予約

3. LateUpdate:
   └─ WeatherStrikeCoordinator.ApplyLighting():
       └─ WeatherLightingProcessor: 同フレームの F(t) と雨減光率を太陽光・環境光・PointLightへ一括適用
```

### 2.4 神クラス防止と責務分離設計 (Decoupled Architecture)

本システムは、`AGENTS.md` の原則に従い、単一クラスの肥大化を防ぐために以下の責務分離を徹底しています：

1. **`WeatherManager` のファサード化と 4 つの専任クラス分離**:
   * **階層・コンポーネント・PCDレイヤー構築**: 子プロセッサ（雨・雲・雷・光・音・入力）の探索・生成・自動配線、カメラ探索、および全階層への `Layer 3 (PCD)` 再帰適用を [`WeatherHierarchyRegistrar`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Core/WeatherHierarchyRegistrar.cs) に分離。
   * **空間バウンズ・設定同期**: 共有空間バウンズ（X, Z 平面）の解決および全サブシステムへの設定一括分配を [`WeatherConfigDispatcher`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Core/WeatherConfigDispatcher.cs) に分離。
   * **落雷・閃光・雷鳴調停**: 落雷トリガー受付、ポアソン過程自動落雷判定（[`WeatherAutoStrikeTimer`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Lightning/WeatherAutoStrikeTimer.cs) 内包）、距離に応じた雷鳴再生、閃光ライティング開始/終了、および `LateUpdate` でのフラッシュ調光を [`WeatherStrikeCoordinator`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Core/WeatherStrikeCoordinator.cs) に分離。
   * **天候シミュレーション・追従・検知ループ**: 雨強度の線形補間フェード（[`WeatherStateController`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Core/WeatherStateController.cs) 内包）、各プロセッサへの視点水平追従・強度反映、音量同期、および手ジェスチャー検知（[`WeatherGestureDetector`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Input/WeatherGestureDetector.cs) 内包）を [`WeatherSimulationCoordinator`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Core/WeatherSimulationCoordinator.cs) に分離。
   * 「Scene ビューでの Gizmo 描画」を [`WeatherGizmoDrawer`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Debug/WeatherGizmoDrawer.cs) に分離。
   * [`WeatherManager`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Core/WeatherManager.cs) 自体は Inspector 設定の保持と Unity ライフサイクル（`Awake`, `Update`, `LateUpdate`, `OnDisable`）の窓口に特化し、神クラスを解消しました。
2. **`WeatherRainProcessor` と `WeatherRainBuilder` の分離**:
   * `ParticleSystem` のメイン設定、衝突モジュール初期化、飛沫サブエミッター構築、フォールバックマテリアル生成の責務を [`WeatherRainBuilder`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Rain/WeatherRainBuilder.cs) に抽出。
   * `WeatherRainProcessor` は実行時のカメラ追従（`FollowViewer`）および強度反映（`SetIntensity`）のみを担当し、コード行数を大幅に削減しました。
3. **`WeatherCloudProcessor`、`WeatherCloudRenderer`、`WeatherCloudMotion` の 3 分割**:
   * DrawCall 1 回で描画可能なプロシージャル低ポリ 3D 雲メッシュ（Combined Mesh）の幾何生成・フォールバックマテリアル構築を [`WeatherCloudBuilder`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Cloud/WeatherCloudBuilder.cs)（Pure C# 静的クラス）に分離。
   * 雨強度（0.0〜1.0）に応じた色調および体積スケールの非線形グラデーション評価を [`WeatherCloudColorGrading`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Cloud/WeatherCloudColorGrading.cs)（Pure C# 静的クラス）に分離。
   * 3D 階層（`CloudCluster`）、`MeshFilter` / `MeshRenderer` の動的生成・探索、`Layer 3 (PCD)` の再帰保証、プロシージャルメッシュの再構築とメモリ解放、および `MaterialPropertyBlock` を用いた Zero-GC 色・スケール適用を [`WeatherCloudRenderer`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Cloud/WeatherCloudRenderer.cs) に分離。
   * 視点カメラへの水平追従（X, Z）基準位置算出、およびスケールに連動したサイン波浮遊ボブ運動（微小ドリフト）の座標計算を [`WeatherCloudMotion`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Cloud/WeatherCloudMotion.cs)（Pure C# クラス）に分離。
   * [`WeatherCloudProcessor`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Cloud/WeatherCloudProcessor.cs) はライフサイクル管理、Inspector 設定保持、AppLog 連携、および各コンポーネントのオーケストレーションに専念し、神クラスを完全解消しました。
4. **`WeatherLightningBoltPool` の 3 分割 (階層・幾何・発光制御)**:
   * 直交 2 軸（$n_1, n_2$）に基づく 3D 十字帯メッシュ（Cross Ribbon Mesh）の生成・UV/インデックス初期化・法線展開頂点更新、および着地点放電スパーク（Impact Sparks）の点列生成ロジックを [`WeatherLightningMeshBuilder`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Lightning/WeatherLightningMeshBuilder.cs)（Pure C# 静的クラス）に分離。
   * 主幹パス・枝パス・着地点スパークのパス幾何計算および各十字リボンメッシュへの頂点更新ディスパッチを [`WeatherBoltGeometryDispatcher`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Lightning/WeatherBoltGeometryDispatcher.cs)（Pure C# 静的クラス）に分離。
   * 芯（Core）の淡黄色 HDR エミッションブーストおよび外側（Glow）の雷イエロー発光、`MaterialPropertyBlock` を用いた Zero-GC アルファ反映を [`WeatherBoltEmissionController`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Lightning/WeatherBoltEmissionController.cs)（Pure C# クラス）に分離。
   * [`WeatherLightningBoltPool`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Lightning/WeatherLightningBoltPool.cs) はワールド原点独立階層（`_boltRoot`）の事前確保・管理、`Layer 3 (PCD)` の再帰保証、および表示/非表示ライフサイクルに特化しました。
5. **`WeatherLightningProcessor` の 4 分割 (設定・計画・閃光・オーケストレーション)**:
   * 設定データモデルの展開、共有空間バウンズ（X, Z）の統合、およびボルトプールの自動階層配線を [`WeatherLightningConfigApplier`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Lightning/WeatherLightningConfigApplier.cs)（Pure C# 静的クラス）に分離。
   * 最小インターバルチェック、床面検出 Physics Raycast / 視線見通し選定（[`WeatherLightningGroundPicker`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Lightning/WeatherLightningGroundPicker.cs) 内包）、およびスケール連動空中始点（SkyPoint）の選定ロジックを [`WeatherLightningStrikePlanner`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Lightning/WeatherLightningStrikePlanner.cs)（Pure C# クラス）に分離。
   * 閃光レベル $F(t)$ の保持、ボルトプールへの輝度反映、および落雷終了・中断クリーンアップを [`WeatherLightningFlashController`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Lightning/WeatherLightningFlashController.cs)（Pure C# クラス）に分離。
   * 再雷撃マルチストロークのタイミング計算、ブリンク瞬き、および残光減衰フェードのタイムライン制御を [`WeatherLightningSequencer`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Lightning/WeatherLightningSequencer.cs)（Pure C# クラス）に分離。
   * [`WeatherLightningProcessor`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Lightning/WeatherLightningProcessor.cs) は外部 API 受付、コルーチン起動、およびイベント通知に専念する薄いオーケストレーターに純化しました。
6. **`WeatherManagerEditor` と 4 つの専任 Drawer の分離**:
   * PlayMode 実行時のリアルタイム雨スライダー、トグル、天候プリセット、落雷発火、雲 ON/OFF コントロールを [`WeatherPlayModeControlDrawer`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Editor/WeatherPlayModeControlDrawer.cs) に分離。
   * 空間・カメラ設定、共有空間バウンズ、および自動セットアップ・PCD レイヤー適用ボタンを [`WeatherGeneralTabDrawer`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Editor/WeatherGeneralTabDrawer.cs) に分離。
   * 雨（Rain）、雲（Cloud）、雷（Lightning）のコア気象エフェクトパラメータ描画を [`WeatherEffectTabDrawer`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Editor/WeatherEffectTabDrawer.cs) に分離。
   * 照明（Lighting）、音響（Audio）、入力（Input）の環境・連携設定描画を [`WeatherEnvironmentTabDrawer`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Editor/WeatherEnvironmentTabDrawer.cs) に分離。
   * [`WeatherManagerEditor`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Scripts/Editor/WeatherManagerEditor.cs) は Toolbar タブ切り替えとシリアライズ同期のみを統括する薄いエディタに純化しました。

---

## 3. セットアップ・使用方法

### 方法 A: ワンクリック自動構築（推奨）

Unity Editor 上のメニューまたは Inspector ボタンから、マテリアル・ライト・プロセッサが完全にシリアライズ結線された階層を一瞬でセットアップできます。

* **メニューから作成**: Unity 上部メニューの `GameObject > Weather > Create Weather System in Scene` を選択します。
* **PCD レイヤー設定（仮想オブジェクトとしての接続）**: 天候エフェクト（雨粒・飛沫パーティクル、稲妻 3D 十字帯メッシュ）は、実測点群バッファへ流し込むのではなく、通常の **仮想オブジェクト（Virtual Object）** として `PCD` レイヤー（Layer 3）に描画されます。これにより、URP の仮想深度マップ（`_VirtualDepthMap`）を経由して `PCDRendererFeature` に渡され、実測点群（手や実空間）によるリアルタイムな遮蔽（オクルージョン）を受けます。Inspector の **「Apply PCD Layer to Weather」** ボタンまたは上部メニュー `GameObject > Weather > Apply PCD Layer to Weather` から一括適用でき、`WeatherLightningBoltPool.cs` 内部でもプール活性化時に `SetLayerRecursively` によって常に `Layer 3 (PCD)` が再帰保証されます。
* **通常の VirtualObject との分離**: Weather は広域環境演出であるため、`PR_BoneDetector` や `HCD_Pipeline`（触覚判定）と連動する通常の仮想モデル（`PR_VirtualObjectManager`）の対象外として独立動作します（誤って登録されていた場合は **「Remove Weather from PR_VirtualObjectManager」** で安全に除外できます）。
* **Inspector からセットアップ**: 空の GameObject に `WeatherManager` を追加し、Inspector の **「Setup & Serialize All Assets (Materials, Lights, Processors)」** ボタンをクリックします。
* **Prefab の生成**: 上部メニューの `Weather > Generate WeatherSystem Prefab` を選択すると、`Assets/Features/Weather/Prefabs/WeatherSystem.prefab` が自動生成されます。

### 方法 B: 手動セットアップ

1. シーン内に空の GameObject を作成し、名前を `Weather` に変更します。
2. `Weather` に `WeatherManager` コンポーネントを追加します。
3. `Weather` に `WeatherKeyController`、`WeatherHcdInputBridge`、`WeatherFoxReactionHandler`、`WeatherLogTriggers` を追加します。
4. 子オブジェクトとして各プロセッサを配置し、以下のアセットを Inspector でシリアライズ割り当てします：
   * `WeatherRainProcessor.particleMaterial`: [M_Weather_Rain.mat](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Materials/M_Weather_Rain.mat)
   * `WeatherLightningProcessor.lightningMaterial`: [M_Weather_Lightning.mat](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Weather/Materials/M_Weather_Lightning.mat)
   * `WeatherLightingProcessor.boltPointLight`: 子オブジェクト `BoltPointLight` (Light: Point, Range=7, Shadows=None, Enabled=false)
   * `WeatherLightingProcessor.sunLight`: シーン内の Directional Light
   * `WeatherManager`: 各子プロセッサへの参照を割り当てます。

### デバッグキー一覧

既存の `PR_PCDKeyController`（M, 1〜4, T, O, P, L, K, J, C）や `PR_AnimationController`（Tab, Space, F, Enter）と衝突しないキーバインドとなっています。

| キー | 機能 | 動作内容 |
|---|---|---|
| `G` | 雨トグル | 雨の ON (100%) / OFF (0%) を切り替えます |
| `B` | 落雷 | 視界内の安全な領域に落雷を発生させます |
| `7` | 晴れ | 雨強度を 0% に設定します |
| `8` | 小雨 | 雨強度を 30% に設定します |
| `9` | 強い雨 | 雨強度を 70% に設定します |
| `0` | 豪雨・嵐 | 雨強度を 100% に設定します（自動落雷が有効化） |

---

## 4. 仕様・パラメータ詳細

### 4.1 パラメータ一覧

| コンポーネント | パラメータ名 | デフォルト値 | 説明 |
|---|---|---|---|
| `WeatherManager` | `cloudY` | `2.4` | 雲の高さ（m）。雨の発生面および落雷始点のワールド Y 座標 |
| `WeatherManager` | `groundY` | `0.0` | 地面の高さ（m）。雨粒消滅および落雷着弾の基準面 |
| `WeatherManager` | `rainFadeDuration` | `2.0` | 雨強度が変化する際のフェード時間（秒） |
| `WeatherManager` | `useSharedBounds` | `true` | 雨と落雷で共通の X, Z 平面矩形範囲を使用するか |
| `WeatherManager` | `boundsCenter` | `(0, 0)` | 共有 X, Z 平面の中心座標（m） |
| `WeatherManager` | `boundsSize` | `(4, 4)` | 共有 X, Z 平面の幅（X）と奥行き（Z）（m） |
| `WeatherManager` | `followViewer` | `true` | カメラ水平位置に追従（屋外モード）するか固定平面にするか |
| `WeatherManager` | `autoThunder` | `true` | 嵐（閾値以上）の際に自動落雷を行うか |
| `WeatherManager` | `stormThreshold` | `0.7` | 自動落雷が発生する雨強度閾値（0.0〜1.0） |
| `WeatherManager` | `meanStrikeInterval` | `10.0` | 自動落雷の平均間隔（ポアソン分布、秒） |
| `WeatherRainProcessor` | `fallSpeedMin` | `7.0` | 雨粒の最小落下速度（m/s） |
| `WeatherRainProcessor` | `fallSpeedMax` | `9.0` | 雨粒の最大落下速度（m/s） |
| `WeatherRainProcessor` | `dropSize` | `0.002` | 雨粒の基本サイズ・幅（m、デフォルト 2mm。卓上SRDisplay推奨: 0.001〜0.003） |
| `WeatherRainProcessor` | `lengthScale` | `1.8` | 雨粒の落下方向への引き伸ばし倍率（Stretch Length Scale） |
| `WeatherRainProcessor` | `useCustomBounds` | `false` | General の共有 Bounds を上書きして独自の X, Z 平面を使用するか |
| `WeatherRainProcessor` | `areaSize` | `(6, 6)` | 雨の発生面の幅（X）と奥行き（Z）（m） |
| `WeatherRainProcessor` | `collisionMode` | `Plane` | 衝突判定モード（`None`, `Plane`, `World`） |
| `WeatherRainProcessor` | `maxRate` | `2500` | 最大降雨時の毎秒放出パーティクル数 |
| `WeatherCloudProcessor` | `enableClouds` | `true` | 雲の表示を有効にするか（True/False 切り替え） |
| `WeatherCloudProcessor` | `cloudY` | `2.4` | 雲の配置高さ（m）。雨の発生面と完全同期 |
| `WeatherCloudProcessor` | `lightCloudColor` | `(0.92, 0.94, 0.98)` | 雨強度 0 (穏やか/晴れ) での雲の色 (明るい白) |
| `WeatherCloudProcessor` | `stormCloudColor` | `(0.18, 0.20, 0.24)` | 雨強度 1 (激しい嵐・豪雨) での雲の色 (重厚な暗黒灰色・雷雲) |
| `WeatherCloudProcessor` | `minScale` | `0.2` | 雨強度 0 での雲の体積スケール倍率（0.01〜1.5。卓上SRDisplay等の0.1〜0.2m極小設定にも対応） |
| `WeatherCloudProcessor` | `maxScale` | `0.6` | 雨強度 1 での雲の体積スケール倍率（0.02〜3.0。最大膨張・空を覆う） |
| `WeatherCloudProcessor` | `clusterCount` | `7` | 雲クラスタの配置個数（3〜16） |
| `WeatherCloudProcessor` | `puffsPerCluster` | `6` | 各クラスタ内の低ポリ球体パフの数（3〜10） |
| `WeatherCloudProcessor` | `cloudThickness` | `0.15` | 雲の基本厚み（Y方向の高さ、0.01〜0.5m） |
| `WeatherCloudProcessor` | `driftSpeed` | `0.05` | 雲の微小浮遊・揺らぎアニメーション速度 |
| `WeatherLightningProcessor` | `useCustomBounds` | `false` | General の共有設定や視野角円環を上書きして独自の X, Z 矩形平面を使用するか |
| `WeatherLightningProcessor` | `strikeAreaSize` | `(0.5, 0.5)` | 落雷対象矩形平面の幅（X）と奥行き（Z）（m、卓上SRDisplay標準） |
| `WeatherLightningProcessor` | `strikeMinRadius` | `0.05` | 落雷着弾エリアの最小半径（m、卓上SRDisplay推奨: 0.05〜0.15） |
| `WeatherLightningProcessor` | `strikeMaxRadius` | `0.35` | 落雷着弾エリアの最大半径（m、卓上SRDisplay推奨: 0.25〜0.40） |
| `WeatherLightningProcessor` | `coreColor` | `(1.0, 0.98, 0.75)` | 稲妻の芯（Core）の発光色（眩しい淡黄色） |
| `WeatherLightningProcessor` | `glowColor` | `(1.0, 0.82, 0.15)` | 稲妻の外側（Glow）の発光色（鮮烈な雷イエロー） |
| `WeatherLightningProcessor` | `flashDuration` | `0.35` | 落雷閃光の基本持続時間（秒）。稲妻形状と閃光が視認できる長さ（推奨: 0.2〜0.5s） |
| `WeatherLightningProcessor` | `fadeDuration` | `0.45` | 落雷後の残光フェードアウト時間（秒） |
| `WeatherLightningProcessor` | `trunkWidth` | `0.03` | 稲妻の主幹の太さ（m、デフォルト 3cm。推奨: 0.015〜0.06m） |
| `WeatherLightningProcessor` | `requireLineOfSight` | `true` | 視点から着弾点が見通せるか検証（家具裏の浮き防止） |
| `WeatherLightingProcessor` | `lightDarkenRate` | `0.55` | 雨最大時の太陽光減光率 |
| `WeatherLightingProcessor` | `boltLightIntensity` | `7.0` | 落雷点光源の最大照度 |

### 4.2 数理モデル・幾何アルゴリズム

<details>
<summary>📐 中点変位法 (Midpoint Displacement) のゼロアロケーション実装詳細</summary>

稲妻パスは、始点 $\mathbf{P}_s$（雲）と終点 $\mathbf{P}_e$（着地点）の線分を再帰的に分割して生成されます。第 $k$ 世代において、隣接する2点 $\mathbf{P}_i, \mathbf{P}_{i+1}$ の中点 $\mathbf{M}$ に、進行方向 $\mathbf{D} = (\mathbf{P}_{i+1} - \mathbf{P}_i) / \|\mathbf{P}_{i+1} - \mathbf{P}_i\|$ に直交するランダム変位 $\mathbf{V}_{\perp}$ を加算します：

$$\mathbf{M} = \frac{\mathbf{P}_i + \mathbf{P}_{i+1}}{2} + \mathbf{V}_{\perp} \cdot \Delta_k, \quad \Delta_k = \Delta_0 \cdot 2^{-k}$$

本システムでは、`Vector3[256]` の固定長バッファ2面（`_bufferA`, `_bufferB`）をスワップするダブルバッファリング方式を採用しており、再帰関数呼び出しやヒープメモリ確保（`new`）を一切行いません。
</details>

<details>
<summary>⚡ マルチストローク再雷撃と統合発光量カーブ F(t)</summary>

落雷の物理的特性を再現するため、2〜4回の再雷撃（Re-strike）を行います。
* **第1ストローク（メイン閃光）**: ピーク光量 1.0 を `flashDuration * 0.45`（デフォルト約 $160\,\mathrm{ms}$）保持し、稲妻の形状を視覚に焼き付けます。
* **ストローク間隔**: `flashDuration * 0.12`（約 $40\,\mathrm{ms}$）の間、光量を 0.1 まで急減衰してリアルな明滅を表現。
* **追撃ストローク**: ピーク光量 0.70〜0.95 を `flashDuration * 0.25`（約 $90\,\mathrm{ms}$）保持。
* **最終減衰**: 全ストローク終了後、残光が `fadeDuration`（デフォルト $0.45\,\mathrm{s}$）かけて滑らかに 0 へフェードアウト。

この1本の発光カーブ $F(t) \in [0, 1]$ が、LineRenderer のアルファ、太陽光ブースト倍率、環境光ブースト倍率、および落雷 PointLight の照度へ完全同期して伝達されます。
</details>

---

## 5. デバッグ・留意事項

### 5.1 統制ログ管理 (`AppLogManager`) 連携

本システムは `AGENTS.md` の統一ログシステム規約に完全準拠しており、Inspector にローカルなデバッグフラグを持ちません。すべてのログは `AppLogManager` のインスペクター上で一元的に ON/OFF 制御可能です。

| サブトリガー名 | タグ定数 | 出力元クラス | 主な出力内容 |
|---|---|---|---|
| `[WeatherManager] Weather State` | `WeatherManager` | `WeatherManager` | 天候強度変化、自動落雷タイマー、落雷発生地点 |
| `[WeatherCloud] Cloud State` | `WeatherCloud` | `WeatherCloudProcessor` | 雲の有効状態、現在の雨強度、雲のワールド座標（定期ログ） |
| `[WeatherGesture] Gesture Detection` | `WeatherGesture` | `WeatherManager` | 頭上ホールド（雨トグル）、振り下ろし落雷の検知 |
| `[WeatherKeyController] Key Operations` | `WeatherKeyController` | `WeatherKeyController` | デバッグキー（G, C, B, 7〜0）の押下イベント |
| `[WeatherHcdInputBridge] Hand Tracking State` | `WeatherHcdInputBridge` | `WeatherHcdInputBridge` | HCDクラスタ追跡状態、手重心座標（定期ログ） |
| `[WeatherFoxReactionHandler] Fox Weather Reaction` | `WeatherFoxReactionHandler` | `WeatherFoxReactionHandler` | 落雷・雨イベント受信ログ（将来拡張用） |

ログシステムの詳細仕様および操作方法については、[Logging.md](./Logging.md) を参照してください。

### 5.2 裸眼立体ディスプレイ (SRDisplay) 実装時の留意事項

1. **カメラ直前の板ポリゴン配置の回避**:
   * 裸眼立体ディスプレイでは、顔位置トラッキングに応じて左右の目に異なる視差画像を生成します。カメラ直前数ミリに Quad を配置して全画面を覆う手法は、視差の歪みやクリッピングプレーンとの干渉（Z-fighting）の原因となります。
   * 本システムでは画面の明暗表現を DirectionalLight・環境光の減光および PointLight の照射によって実現しているため、立体視の整合性が完全に維持されます。
2. **パーティクルサイズと近接クリップ**:
   * 雨粒が視点直前を通過する際にパーティクルが巨大化して視界を遮らないよう、`WeatherRainProcessor` のレンダラー設定で `maxParticleSize = 0.025` にクランプしています。
3. **Fox アニメーション連携の拡張**:
   * 将来的に落雷時の Fox のリアクション（怯える、驚く等）を実装する場合は、`WeatherFoxReactionHandler.cs` 内の `HandleLightningStrike` メソッドに Animator のトリガー設定を追記してください。

### 5.3 PCD リアルタイムオクルージョン連携と深度テクスチャ仕様

本リポジトリの点群オクルージョンパイプライン（`PCDRendererFeature`）は、URP のカメラ深度テクスチャ（`cameraDepthTexture` / `_VirtualDepthMap`）を読み込んで仮想オブジェクト（`OriginType = 1`）を検出し、実測点群（`OriginType = 0`）とのオクルージョン計算を行います。

1. **仮想オブジェクトとしての認識条件**:
   * オクルージョン計算パイプラインは、`cameraDepthTexture` に有効な深度（$< 0.9999$）が記録されたピクセルのみを「仮想オブジェクト」として抽出します。
   * URP の標準仕様として、`RenderType = "Transparent"` かつレンダーキューが半透明（$> 2500$）のオブジェクトは `DepthOnlyPass`（DepthPrepass）で除外され、深度バッファに書き込まれません。
2. **親 Transform オフセットおよび LineRenderer 制限の解消 (3D 十字帯メッシュ & 着地点スパーク)**:
   * Unity URP の内部制限として、`LineRenderer` は動的なラインプリミティブであり、Opaque マテリアルを設定した場合であっても `DepthOnlyPass`（深度プリパス）の描画対象から除外される仕様が存在しました。
   * さらに、親 GameObject（Weather / LightningProcessor）が持つ位置オフセットにより、ワールド座標で生成されたメッシュ頂点が二重オフセットされ、SRDisplay の表示ボックス枠外（上空など）へ飛んでしまっていた原因も特定・解消しました。
   * 本システムでは `WeatherLightningBoltPool` をワールド原点独立ルート（`SetParent(null)`）に切り離し、**3D 十字帯メッシュ（Cross Ribbon Mesh）**へと刷新しました。線分進行方向に対して直交する 2 軸（$n_1, n_2$）からなる十字形状にすることで、裸眼立体視の左右の目や斜め視点から見ても常に完全な太さ・眩しさで光の筋が立体的に視認されます。
   * さらに、着地点（落雷点）の周囲に放射状に弾ける**地上放電スパーク（Ground Impact Sparks）**を同時生成することで、空からの光の筋と着弾点のバチバチッとした放電の両方が確実に描画され、PointLight の反射光と合わさって極めて自然で迫力ある落雷演出を実現しています。
   * これらのメッシュは通常の 3D メッシュと同様に 100% 確実に `DepthOnlyPass` で深度テクスチャに深度を書き込むため、仮想オブジェクトの有無に関わらず背景空間全体で鮮明な落雷レンダリングとリアルタイムオクルージョンが成立します。
3. **マテリアル設定の厳守**:
   * 雨（`M_Weather_Rain.mat`）、雲（`M_Weather_Cloud.mat`）、および落雷（`M_Weather_Lightning.mat`）は、すべて `Universal Render Pipeline/Lit`、`RenderType = "Opaque"`、`CustomRenderQueue = -1` (Geometry: 2000)、`_Surface = 0`、`_ZWrite = 1`、かつ `DepthOnly` パスを有効化した不透明マテリアルを使用しています。
   * これにより、雨粒・雲・落雷がカメラの深度バッファに正しく記録され、`PCDRendererFeature` の `BlitPass` による上書き消去を防ぎ、Fox の外側の背景領域でも実測点群と相互に遮蔽されるリアルタイムオクルージョン描画が成立します。
4. **HCD（触覚衝突判定）との分離**:
   * 天候エフェクト（雨・雲・落雷）は `IExcludeFromHcd` を実装し、触覚の骨追跡・物理反力計算から完全に除外されています。描画のみを Layer 3 (`PCD`) としてカメラおよびオクルージョンパイプラインへ接続します。
