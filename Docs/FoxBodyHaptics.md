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
* **部位別アクティブ判定（足のみ接地判定適用）**: 足部位に対する接地判定（`ApplyAirborneCheck = true` かつ `disableWhenInAir = true`）と、頭部・耳・尻尾に対する常時有効判定（`ApplyAirborneCheck = false`）を明確に分離し、ジャンプや滞空時も頭・耳・尻尾の触覚フィードバックを維持します。
* **手との近接接触判定 (`onlyTargetHandContact`) と部位別個別閾値**: `HCD_Pipeline` で追跡された手の点群クラスタと各部位の距離を評価します。一括共通閾値に加え、部位ごとに個別の接触距離しきい値（`useIndividualContactThresholds`）を設定可能です。
* **身体配置に沿った 2 列グリッド（行列レイアウト）UI**: Inspector 上でキツネの身体トポロジーに合わせた直感的な 2 列グリッド表示を採用し、一括 ON/OFF/足のみトグルなどの利便性を向上させています。
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
        └── HAP_FoxBodyHapticsControllerEditor.cs # Inspector 拡張（身体配置グリッド・個別閾値・診断）
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
       ├─ (足部位かつ disableWhenInAir) → キャラクター基準高さとの差が閾値以下か？（頭・耳・尻尾はスキップ）
       └─ (onlyTargetHandContact) → HCD_Pipeline の手クラスタ重心との最近接距離が部位別閾値（HandContactThreshold）以下か？
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
接地判定 (`disableWhenInAir`) や手接触判定 (`onlyTargetHandContact`)、部位別閾値、および各部位のトグルを設定します。

| 設定カテゴリ | 設定項目 | 型 | 既定値 | 説明 |
|---|---|---|---|---|
| **Dependencies** | `autdController` | `HAP_AUTDHapticsController` | `null` | 触覚司令塔の参照（未設定時はシーン内自動取得） |
| **Animation State** | `disableWhenInAir` | `bool` | `false` | 有効時、足部位が浮いている時は触覚をオフにします（頭・耳・尻尾は維持） |
| | `airborneHeightThreshold`| `float` | `0.05f` | 接地判定とするルート高さからの許容差（m） |
| | `rootTransform` | `Transform` | `null` | キャラクターのルート階層（未設定時は自動解決） |
| **Hand Contact** | `onlyTargetHandContact` | `bool` | `false` | 有効時、手の点群が部位の近くにある時のみ照射します |
| | `handContactThreshold` | `float` | `0.10f` | 手との接触と判定する共通距離閾値（m）※推奨 `0.045m` |
| | `useIndividualContactThresholds` | `bool` | `false` | 部位ごとの個別接触距離閾値を有効化するフラグ |
| | `headContactThreshold` 等 (8部位) | `float` | `-1.0f` | 部位別の接触距離閾値（m）。`<= 0` 時は共通閾値を自動適用 |
| **Touch Directions** | `headTargetTouchDirection` | `Vector3` | `(0, -1, 0)` | 頭・耳が触れる向き（最適デバイス判定基準） |
| | `footTargetTouchDirection` | `Vector3` | `(0, -1, 0)` | 足・尻尾が触れる向き（最適デバイス判定基準） |
| **Custom Mode** | `stmMode` | `HapticsSTMMode` | `FociSTM` | `FociSTM` (単焦点・ハードウェア) / `GainSTM` (多焦点) |
| | `trackMode` | `HapticsTrackMode` | `Sequential` | `Simultaneous` (同時多焦点) / `Sequential` (順次切り替え) |
| | `sequentialSTMFrequency` | `float` | `150f` | シーケンシャル切り替え周波数（Hz） |
| **Body Part Toggles** | `enableHead` 等 (8部位) | `bool` | `true` | 部位ごとの個別照射有効フラグ（身体配置グリッドで操作） |

---

## 4. 仕様・パラメータ詳細

### 4.1 部位別ターゲット定義と接地・空中判定

`HAP_FoxBodyHapticsController` は以下の 8 部位を定義します。

| 部位名 | ボーンフィールド | 空中判定 (`ApplyAirborneCheck`) | 照射向き (`TouchDirection`) | 説明 |
|---|---|---|---|---|
| **Head** | `headBone` | `false` (常時有効・除外) | `headTargetTouchDirection` | 頭部ボーン |
| **Left Ear** | `leftEarBone` | `false` (常時有効・除外) | `headTargetTouchDirection` | 左耳ボーン |
| **Right Ear** | `rightEarBone` | `false` (常時有効・除外) | `headTargetTouchDirection` | 右耳ボーン |
| **Front Left** | `frontLeftFoot` | `true` (接地判定対象) | `footTargetTouchDirection` | 左前足ボーン |
| **Front Right**| `frontRightFoot`| `true` (接地判定対象) | `footTargetTouchDirection` | 右前足ボーン |
| **Back Right** | `backRightFoot` | `true` (接地判定対象) | `footTargetTouchDirection` | 右後足ボーン |
| **Back Left**  | `backLeftFoot`  | `true` (接地判定対象) | `footTargetTouchDirection` | 左後足ボーン |
| **Tail** | `tailBone` | `false` (常時有効・除外) | `footTargetTouchDirection` | 尻尾先端ボーン |

> [!NOTE]
> `disableWhenInAir = true` が有効な場合、接地判定は**足 4 本（Front Left, Front Right, Back Left, Back Right）のみに適用**されます。  
> 頭・耳・尻尾は常に除外され、キツネがジャンプ中や抱き上げられた状態であっても照射対象として維持されます。

### 4.2 手との近接接触判定 (`onlyTargetHandContact`) と部位別個別閾値

`onlyTargetHandContact = true` の場合、`HCD_Pipeline` で追跡されている手の点群クラスタ重心 $\mathbf{C}_k$ と、各ターゲット部位の座標 $\mathbf{P}_i$ とのユークリッド距離を評価します。

$$\min_k \|\mathbf{C}_k - \mathbf{P}_i\| \le T_i$$

ここで $T_i$ は部位 $i$ の有効しきい値です：
* `useIndividualContactThresholds = false` または個別値 $\le 0$ の場合: $T_i = \text{handContactThreshold}$（共通閾値）
* `useIndividualContactThresholds = true` かつ個別値 $> 0$ の場合: $T_i = \text{個別設定値}$（例: `headContactThreshold`）

条件を満たす部位のみが `Active = true` となり、超音波焦点が照射されます。

#### Inspector での操作仕様
* **(Default) 表示と個別スライダー**: 個別値が未設定（`<= 0`）の部位は、共通閾値が自動連動し、右側に `(Default)` と表示されます。スライダーを調整すると個別カスタム値に切り替わり、横の `Reset` ボタンでいつでもデフォルト連動に戻せます。
* **一括補助機能**:
  * **`Copy Common to All`**: 現在の共通閾値を全部位の個別値へ一括コピーし、微調整のベースを作成します。
  * **`Reset All to Default`**: 全部位を `-1.0f`（未設定）にリセットし、共通閾値連動状態へ戻します。

<details><summary>📐 閾値設計の幾何学的留意点（推奨値）</summary>

キツネのボーン中心から皮膚表面までは約 `1.5cm〜2.5cm` の厚みがあり、ユーザーの手の点群重心も皮膚表面から約 `1.0cm` 離れた位置に形成されます。  
そのため、手がキツネの皮膚に触れている状態であっても、**ボーン中心から手の重心までの距離は幾何学的に 2.5cm〜4.5cm 程度**となります。  
閾値 `handContactThreshold` を `0.020m (2.0cm)` に設定すると接触が検出されにくくなるため、**`0.045m〜0.050m (4.5〜5.0cm)`** を設定することを強く推奨します。

</details>

### 4.3 身体配置グリッド（行列レイアウト）UI

Inspector 上の部位トグルは、キツネの身体トポロジーを模した 2 列グリッド（行列レイアウト）で直感的に配置されています。

```text
             [ Head (頭) ]
[ Left Ear (左耳) ]     [ Right Ear (右耳) ]
[ Front Left (前左足) ] [ Front Right (前右足) ]
[ Back Left (後左足) ]  [ Back Right (後右足) ]
             [ Tail (尻尾) ]
```

* **`Select All (全有効)`**: 全 8 部位を一括で ON にします。
* **`Deselect All (全無効)`**: 全 8 部位を一括で OFF にします。
* **`Feet Only (足のみ)`**: 足 4 本のみを ON にし、頭・耳・尻尾を OFF に切り替えます（足部専用の実験等に便利です）。

### 4.4 単焦点集中照射による高音圧触覚提示仕様

複数部位が登録されている場合でも、多焦点ホログラフィによるエネルギー分散（各焦点の音圧激減）を防ぐため、システムはアクティブな部位の中から対象ターゲットに対して 249 素子すべての超音波エネルギー（10,000 Pa）を単焦点集中照射（`Holo.Naive`）します。

| 項目 | 仕様 | 理由・効果 |
|---|---|---|
| **アルゴリズム** | `Holo.Naive`（単焦点） | 249 素子すべての位相を 1 点に同相集中させ、最大の音響放射圧を発生させます。 |
| **照射音圧** | 10,000 Pa (最大出力) | 多焦点分散を回避し、皮膚の機械受容器（マイスナー小体）が確実に知覚できる強い刺激を提供します。 |
| **振幅変調** | `Sine` 150 Hz | 人間の触覚感度が最も高い周波数帯で明滅させ、明瞭な触覚フィードバックを提示します。 |

<details><summary>🔬 物理モデル：なぜ多焦点同時照射ではなく単焦点集中照射なのか？</summary>

1 台の AUTD3（249 素子）から同時に複数焦点（例えば 4〜8 箇所）を照射する多焦点ホログラフィを行うと、各焦点に割り当てられる音響エネルギーが素子数 / 焦点数に激減（1/4 = 25%、1/8 = 12% 以下）します。さらに隣接焦点同士の位相干渉によって焦点像がぼやけ、物理的な音圧が人間の知覚閾値を下回ってしまいます。  
そのため、本システムでは接触部位に対して常に 249 素子すべてのエネルギー（10,000 Pa）を単焦点として集中照射し、皮膚上で確実に強い触覚が得られるように設計されています。

</details>

### 4.5 実機物理配置と頭部・足元の照射幾何最適化仕様

実環境における 8 台の AUTD3 フェーズドアレイの空間配置と、キツネモデル各部位への照射幾何関係は以下の通り最適化されています。

```text
[左側面: Device 0, 1]  (斜め右下・内向き照射) ──────┐
                                                    ▼
[床面机上: Device 2, 3, 4, 5] (真上向き照射) ───► [キツネ全身: Head, Ears, Feet, Tail]
                                                    ▲
[右側面: Device 6, 7]  (斜め左下・内向き照射) ──────┘
```

| 部位グループ | 主な照射源デバイス | 空間配置と幾何的特徴 |
|---|---|---|
| **頭部・耳 (`Head`, `Ears`)** | **Device 3** (床面手前左) および床面アレイ | 頭部直下（距離わずか約 14cm）に位置する Device 3 が主たる音圧源となり、真上方向へ高密度な超音波エネルギーを集中照射します。 |
| **足元・尻尾 (`Feet`, `Tail`)** | **Device 0, 1** (左側面) および **Device 6, 7** (右側面) | 側面の斜めスタンドからキツネの身体側面・足元に向けて照射され、床面アレイとの協調により全身を立体的に包み込みます。 |

#### ローカル Y 軸 180° 反転補正による位相整合
Unity の `AUTD3.prefab` のメッシュ構造に起因する座標系の不整合を解消するため、[`HAP_DeviceOrientationHelper`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Haptics/Scripts/Hardware/HAP_DeviceOrientationHelper.cs) が全デバイス一律でローカル Y 軸回りに 180° 反転補正（`rotation * Quaternion.Euler(0f, 180f, 0f)`）を適用します。  
これにより、Scene ビューや Inspector の Transform（UI 表示）を一切変更することなく、床面および側面の全 8 台からキツネ各部位へ向かう波面が完全に同位相で干渉し、頭部・足元の双方で最大音圧が保証されます。

---

## 5. デバッグ・留意事項

### 5.1 ジャンプ・空中停止からの出力復帰保証 (`Airborne Recovery & Static Modulation`)

`disableWhenInAir = true` が有効な場合、キャラクターの跳躍や足上げによって足部位が全非アクティブ化し、ハードウェアへ出力停止コマンド（`SendNull`）が送られます。
着地時に超音波出力を瞬時に復活させるため、システムは以下の復帰保証を備えています：

* **非同期競合防止**: `SendNull` は先行する焦点送信タスクの完了を待機してから実行され、着地直後の再開パケットと空中の停止パケットが順序逆転・衝突することを完全に防止します。
* **Static 変調の確実な再ロード**: 直流（AM 変調なし・定常波）モード時、一度出力 0（Null）を受信した FPGA の積算器が 0 でクランプされるのを防ぐため、出力停止時に変調キャッシュを自動リセットし、着地フレームの先頭で FPGA へ `Modulation.Constant` を即座に再適用します。

### 5.2 Scene ビュー Gizmo 可視化

`drawGizmos = true` かつ `HAP_AUTDHapticsController.sourceMode == ObjectTarget` の時、Scene ビュー上にリアルタイムな判定状態が可視化されます。描画処理は [`HAP_ObjectGizmoDrawer`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Haptics/Scripts/Debug/HAP_ObjectGizmoDrawer.cs) が担当します。

* **照射ターゲット球**: アクティブ時は緑色の塗りつぶし球、非アクティブ時は赤色/灰色のワイヤー球を表示。
* **接地判定線**: 接地中は緑色の垂直線、滞空・空中除外時は許容高さ位置に十字マークと赤色の線を表示（足部位のみ描画）。
* **手接触許容球**: `onlyTargetHandContact` 有効時、ターゲット部位を中心とする判定球（黄色ワイヤー球、接触成立時は緑色）および手クラスタ重心への接続線を表示。**判定球の半径は部位ごとの有効閾値（$T_i$）と正確に連動**します。

### 5.3 統制ログシステム (`AppLogManager`) との同期

コントローラー本体にログ用フィールドを定義せず、[`HAP_LogTriggers`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Haptics/Scripts/Debug/HAP_LogTriggers.cs) 経由で `AppLogManager` のサブトリガーとして管理されます。

| サブトリガー表示名 | タグ | 既定値 | 用途・説明 |
|---|---|---|---|
| `[HAP_FoxBodyHapticsController] 診断レポート (手動/イベント)` | `HAP_ObjectHaptics` | **ON** | Inspectorボタン押下時または手動診断時の詳細レポート出力 |
| `[HAP_FoxBodyHapticsController] 定期自動ログ (5秒間隔)` | `HAP_ObjectHaptics_Periodic` | **OFF** | 5秒間隔の定期状態監視（コンソール汚染防止のため初期OFF） |

詳細については [Logging.md](./Logging.md) を参照してください。

### 5.4 診断レポートの出力

Play モード中、`HAP_FoxBodyHapticsController` の Inspector 最下部にある **「📋 触覚診断レポートをConsoleに出力」** ボタンをクリックすると、以下のような詳細診断情報が出力されます。部位ごとの接触閾値と判定理由も明記されます。

```text
[HAP_FoxBodyHapticsController] === 触覚診断レポート (Frame: 3240) ===
  [AUTD] SourceMode=ObjectTarget, Connected=True, Bypass=False, Intensity=10000Pa
  [PointCloud] RealSense GlobalPoints = 338595
  [HCD] DetectionMode=SkinnedMeshRenderer, TargetMeshes=1 mesh(es) [fox], TrackedClusters=7 (Active: 2)
  [MeshDetail] Verts=2266, Tris=3450, BoundsCenter=(0.369, 0.303, 0.257), BoundsSize=(0.041, 0.071, 0.119), DistanceMode=MeshSurface, SurfThresh=0.020m, BackThresh=0.050m
  [Settings] onlyTargetHandContact=True, threshold=0.045m, disableWhenInAir=False
  • 部位 'Head': Active=True, Pos=(0.37, 0.31, 0.22), 接触閾値=0.045m, 手との最近接距離=0.034m
  • 部位 'Left Ear': Active=True, Pos=(0.38, 0.32, 0.22), 接触閾値=0.045m, 手との最近接距離=0.040m
  • 部位 'Front Left': Active=True, Pos=(0.38, 0.27, 0.24), 接触閾値=0.045m, 手との最近接距離=0.030m
  • 部位 'Front Right': Active=False, Pos=(0.36, 0.27, 0.24), 接触閾値=0.045m, 手との最近接距離=0.052m (手の距離 0.052m > 閾値 0.045m)
```
