# 狐キャラクター全身部位触覚制御 (FoxBodyHaptics) 仕様書

> 📂 **親ノード**: [Haptics.md](./Haptics.md) | 🏷️ **種類**: ⚙️ 機能仕様書  
> [RealTimeOcclusion Wiki (ポータル)](./Wiki.md) に戻る

本ドキュメントでは、狐キャラクター（Fox）のボーン階層から頭部・耳・4足・尻尾の座標を特定し、接地判定および手（点群）との近接接触判定に基づいてリアルタイムにホログラフィ触覚刺激を照射する `HAP_FoxBodyHapticsController` の設計思想、アーキテクチャ、パラメータ仕様およびデバッグ手順について解説します。

---

## 1. 概要

本コンポーネントは、狐キャラクターの全身各部位（頭、左右耳、4本の足、尻尾）に超音波触覚刺激を照射するためのターゲット座標を抽出し、`HAP_AUTDHapticsController` へ提供する機能です。

単に固定座標を照射するのではなく、空中判定（ジャンプ中等の刺激抑制）や `HCD_Pipeline` (接触判定システム) と連携した手との近接判定を行い、触覚インタラクションが成立している部位のみに絞り込んで刺激を生成します。

### 主な特徴

* **8 部位ボーン自動検出**: 狐モデルの標準階層から頭 (`headBone`)、左耳 (`leftEarBone`)、右耳 (`rightEarBone`)、前左足 (`frontLeftFoot`)、前右足 (`frontRightFoot`)、後右足 (`backRightFoot`)、後左足 (`backLeftFoot`)、尻尾 (`tailBone`) を自動探索してバインドします。
* **神クラス解消と完全な責務分離**: 基底コントローラー [`HAP_BaseObjectHapticsController`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Haptics/Scripts/Core/HAP_BaseObjectHapticsController.cs) は純粋な MonoBehaviour としてパラメータ保持に専念し、接触判定、Gizmo描画、診断ログ出力、焦点生成の各処理を専任クラスへ委譲しています。
* **部位別アクティブ判定**: 足部位に対する接地判定（`disableWhenInAir`）と、頭部・耳・尻尾に対する常時有効判定（`IsTail = true`）を柔軟に共存させています。
* **手との近接接触判定 (`onlyTargetHandContact`)**: `HCD_Pipeline` で追跡された手の点群クラスタと各部位の距離を評価し、接触範囲内にある部位のみへ照射します。
* **統制ログシステム完全統合**: コントローラー本体をログコードで汚さず、[`HAP_LogTriggers`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Haptics/Scripts/Debug/HAP_LogTriggers.cs) 経由で `AppLogManager` のサブトリガーとして一元管理されます。

---

## 2. 設計思想・アーキテクチャ

### 2.1 ディレクトリ構成

```text
Assets/Features/Haptics/Scripts/
├── Core/
│   ├── HAP_BaseObjectHapticsController.cs  # オブジェクト触覚基底コントローラー (神クラス解消済み)
│   └── HAP_AUTDHapticsController.cs        # 触覚システム司令塔
├── CustomControllers/
│   ├── HAP_FoxBodyHapticsController.cs     # 狐全身8部位の触覚ターゲット管理
│   └── HAP_FoxFootHapticsController.cs     # 狐4足・尻尾の足部専用コントローラー
├── Processors/
│   ├── HAP_TargetContactEvaluator.cs       # 接地判定・手近接判定評価クラス (Pure C# static)
│   └── HAP_ObjectFociGenerator.cs          # STM/追跡モード別焦点データ生成クラス
├── Debug/
│   ├── HAP_ObjectGizmoDrawer.cs            # Sceneビュー Gizmo 描画ヘルパー (Pure C# static)
│   ├── HAP_ObjectHapticsDiagnostics.cs     # 診断レポート生成・出力ヘルパー (Pure C# static)
│   └── HAP_LogTriggers.cs                  # AppLogManager 連動一元ログトリガー
└── Editor/
    └── CustomControllers/
        └── HAP_FoxBodyHapticsControllerEditor.cs # Inspector 拡張（ボーン検出・診断ボタン）
```

### 2.2 クラス相関図

```mermaid
graph TD
    AUTD["HAP_AUTDHapticsController (司令塔)"] --> Dispatcher["HAP_TargetSourceDispatcher"]
    Dispatcher --> BodyCtrl["HAP_FoxBodyHapticsController"]

    BodyCtrl -- 継承 --> Base["HAP_BaseObjectHapticsController"]

    Base --> |接触・接地評価を委譲| Evaluator["HAP_TargetContactEvaluator (Pure C#)"]
    Base --> |Gizmo描画を委譲| Drawer["HAP_ObjectGizmoDrawer (Pure C#)"]
    Base --> |焦点組み立てを委譲| FociGen["HAP_ObjectFociGenerator (Pure C#)"]
    Base --> |診断ログを委譲| Diag["HAP_ObjectHapticsDiagnostics (Pure C#)"]

    HCD["HCD_Pipeline (手の点群クラスタ)"] -. 距離判定参照 .-> Evaluator
    HCD -. 診断情報参照 .-> Diag

    LogTrig["HAP_LogTriggers"] --> |シーン内検出・サブトリガー登録| Base
    LogTrig --> |定期診断ログ実行 (5秒)| Diag

    style BodyCtrl fill:#4a90d9,color:#fff
    style Base fill:#2980b9,color:#fff
    style Evaluator fill:#27ae60,color:#fff
    style Drawer fill:#f39c12,color:#fff
    style Diag fill:#8e44ad,color:#fff
    style LogTrig fill:#e74c3c,color:#fff
```

### 2.3 処理フロー

```text
[毎フレーム更新]
  1. HAP_AUTDHapticsController が TargetSourceDispatcher 経由で登録コントローラーを走査
  2. HAP_FoxBodyHapticsController.TargetInfos から 8 部位（Head, Ears, Feet, Tail）の座標を取得
  3. HAP_TargetContactEvaluator.IsTargetActive() により部位ごとの照射可否を評価:
       ├─ (足部位かつ disableWhenInAir) → キャラクター基準高さとの差が閾値以下か？
       └─ (onlyTargetHandContact) → HCD_Pipeline の手クラスタ重心との最近接距離が閾値以下か？
  4. HAP_ObjectFociGenerator がアクティブ部位の座標・STM・追跡モード（Simultaneous/Sequential）に応じた焦点リストを生成
  5. HAP_AcousticPipelineExecutor を経由して AUTD3 デバイスへ照射送信
```

---

## 3. セットアップ・使用方法

### 3.1 クイックスタート手順

#### Step 1: コンポーネントのアタッチ
狐モデルのルート GameObject（またはマネージャーオブジェクト）に `HAP_FoxBodyHapticsController` をアタッチします。

#### Step 2: ボーンの自動検出
Inspector 上部にある **「🦴 ボーン階層を自動検出 (Auto Detect)」** ボタンをクリックします。モデル階層から頭・耳・4足・尻尾が自動的にアサインされます。

#### Step 3: パラメータ設定
接地判定 (`disableWhenInAir`) や手接触判定 (`onlyTargetHandContact`)、および各部位のトグルを設定します。

| 設定カテゴリ | 設定項目 | 型 | 既定値 | 説明 |
|---|---|---|---|---|
| **Dependencies** | `autdController` | `HAP_AUTDHapticsController` | `null` | 触覚司令塔の参照（未設定時はシーン内自動取得） |
| **Animation State** | `disableWhenInAir` | `bool` | `false` | 有効時、足部位が浮いている時は触覚をオフにします |
| | `airborneHeightThreshold`| `float` | `0.05f` | 接地判定とするルート高さからの許容差（m） |
| | `rootTransform` | `Transform` | `null` | キャラクターのルート階層（未設定時は自動解決） |
| **Hand Contact** | `onlyTargetHandContact` | `bool` | `false` | 有効時、手の点群が部位の近くにある時のみ照射します |
| | `handContactThreshold` | `float` | `0.10f` | 手との接触と判定する距離閾値（m）※推奨 `0.045m` |
| **Touch Directions** | `headTargetTouchDirection` | `Vector3` | `(0, -1, 0)` | 頭・耳が触れる向き（最適デバイス判定基準） |
| | `footTargetTouchDirection` | `Vector3` | `(0, -1, 0)` | 足・尻尾が触れる向き（最適デバイス判定基準） |
| **Custom Mode** | `stmMode` | `HapticsSTMMode` | `FociSTM` | `FociSTM` (単焦点・ハードウェア) / `GainSTM` (多焦点) |
| | `trackMode` | `HapticsTrackMode` | `Sequential` | `Simultaneous` (同時多焦点) / `Sequential` (順次切り替え) |
| | `sequentialSTMFrequency` | `float` | `150f` | シーケンシャル切り替え周波数（Hz） |
| **Body Part Toggles** | `enableHead` 等 (8部位) | `bool` | `true` | 部位ごとの個別照射有効フラグ |

---

## 4. 仕様・パラメータ詳細

### 4.1 部位別ターゲット定義と接地・空中判定

`HAP_FoxBodyHapticsController` は以下の 8 部位を定義します。

| 部位名 | ボーンフィールド | 判定種別 (`IsTail`) | 照射向き (`TouchDirection`) | 説明 |
|---|---|---|---|---|
| **Head** | `headBone` | `true` (常時非接地判定) | `headTargetTouchDirection` | 頭部ボーン |
| **Left Ear** | `leftEarBone` | `true` (常時非接地判定) | `headTargetTouchDirection` | 左耳ボーン |
| **Right Ear** | `rightEarBone` | `true` (常時非接地判定) | `headTargetTouchDirection` | 右耳ボーン |
| **Front Left** | `frontLeftFoot` | `false` (接地判定対象) | `footTargetTouchDirection` | 左前足ボーン |
| **Front Right**| `frontRightFoot`| `false` (接地判定対象) | `footTargetTouchDirection` | 右前足ボーン |
| **Back Right** | `backRightFoot` | `false` (接地判定対象) | `footTargetTouchDirection` | 右後足ボーン |
| **Back Left**  | `backLeftFoot`  | `false` (接地判定対象) | `footTargetTouchDirection` | 左後足ボーン |
| **Tail** | `tailBone` | `true` (常時非接地判定) | `footTargetTouchDirection` | 尻尾先端ボーン |

> [!NOTE]
> `IsTail = true` が指定された部位（頭・耳・尻尾）は、`disableWhenInAir = true` が有効であっても空中判定による無効化を受けず、常に照射対象として機能します。

### 4.2 手との近接接触判定 (`onlyTargetHandContact`)

`onlyTargetHandContact = true` の場合、`HCD_Pipeline` で追跡されている手の点群クラスタ重心 $\mathbf{C}_k$ と、各ターゲット部位の座標 $\mathbf{P}_i$ とのユークリッド距離を評価します。

$$\min_k \|\mathbf{C}_k - \mathbf{P}_i\| \le \text{handContactThreshold}$$

を満たす部位のみが `Active = true` となります。

<details><summary>📐 閾値設計の幾何学的留意点（推奨値）</summary>

キツネのボーン中心から皮膚表面までは約 `1.5cm〜2.5cm` の厚みがあり、ユーザーの手の点群重心も皮膚表面から約 `1.0cm` 離れた位置に形成されます。  
そのため、手がキツネの皮膚に触れている状態であっても、**ボーン中心から手の重心までの距離は幾何学的に 2.5cm〜4.5cm 程度**となります。  
閾値 `handContactThreshold` を `0.020m (2.0cm)` に設定すると接触が検出されにくくなるため、**`0.045m〜0.050m (4.5〜5.0cm)`** を設定することを強く推奨します。

</details>

---

## 5. デバッグ・留意事項

### 5.1 Scene ビュー Gizmo 可視化

`drawGizmos = true` かつ `HAP_AUTDHapticsController.sourceMode == ObjectTarget` の時、Scene ビュー上にリアルタイムな判定状態が可視化されます。描画処理は [`HAP_ObjectGizmoDrawer`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Haptics/Scripts/Debug/HAP_ObjectGizmoDrawer.cs) が担当します。

* **照射ターゲット球**: アクティブ時は緑色の塗りつぶし球、非アクティブ時は赤色/灰色のワイヤー球を表示。
* **接地判定線**: 接地中は緑色の垂直線、滞空・空中除外時は許容高さ位置に十字マークと赤色の線を表示。
* **手接触許容球**: `onlyTargetHandContact` 有効時、ターゲット部位を中心とする判定球（黄色ワイヤー球、接触成立時は緑色）および手クラスタ重心への接続線を表示。

### 5.2 統制ログシステム (`AppLogManager`) との同期

コントローラー本体にログ用フィールドを定義せず、[`HAP_LogTriggers`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Haptics/Scripts/Debug/HAP_LogTriggers.cs) 経由で `AppLogManager` のサブトリガーとして管理されます。

| サブトリガー表示名 | タグ | 既定値 | 用途・説明 |
|---|---|---|---|
| `[HAP_FoxBodyHapticsController] 診断レポート (手動/イベント)` | `HAP_ObjectHaptics` | **ON** | Inspectorボタン押下時または手動診断時の詳細レポート出力 |
| `[HAP_FoxBodyHapticsController] 定期自動ログ (5秒間隔)` | `HAP_ObjectHaptics_Periodic` | **OFF** | 5秒間隔の定期状態監視（コンソール汚染防止のため初期OFF） |

詳細については [Logging.md](./Logging.md) を参照してください。

### 5.3 診断レポートの出力

Play モード中、`HAP_FoxBodyHapticsController` の Inspector 最下部にある **「📋 触覚診断レポートをConsoleに出力」** ボタンをクリックすると、以下のような詳細診断情報が出力されます。

```text
[HAP_FoxBodyHapticsController] === 触覚診断レポート (Frame: 3240) ===
  [AUTD] SourceMode=ObjectTarget, Connected=True, Bypass=False, Intensity=10000Pa
  [PointCloud] RealSense GlobalPoints = 338595
  [HCD] DetectionMode=SkinnedMeshRenderer, TargetMeshes=1 mesh(es) [fox], TrackedClusters=7 (Active: 2)
  [MeshDetail] Verts=2266, Tris=3450, BoundsCenter=(0.369, 0.303, 0.257), BoundsSize=(0.041, 0.071, 0.119), DistanceMode=MeshSurface, SurfThresh=0.020m, BackThresh=0.050m
  [Settings] onlyTargetHandContact=True, threshold=0.045m, disableWhenInAir=False
  • 部位 'Head': Active=True, Pos=(0.37, 0.31, 0.22), 手との最近接距離=0.034m
  • 部位 'Left Ear': Active=True, Pos=(0.38, 0.32, 0.22), 手との最近接距離=0.040m
  • 部位 'Front Left': Active=True, Pos=(0.38, 0.27, 0.24), 手との最近接距離=0.030m
  • 部位 'Front Right': Active=False, Pos=(0.36, 0.27, 0.24), 手との最近接距離=0.052m (手の距離 0.052m > 閾値 0.045m)
```
