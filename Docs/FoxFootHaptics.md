# 狐キャラクター足部触覚制御 (FoxFootHaptics) 仕様書

> 📂 **親ノード**: [Haptics.md](./Haptics.md) | 🏷️ **種類**: ⚙️ 機能仕様書  
> [RealTimeOcclusion Wiki (ポータル)](./Wiki.md) に戻る

本ドキュメントでは、狐キャラクター（Fox）の 4 足部ボーン（前後左右）および尻尾を追跡し、歩行・接地状態や手との近接判定に応じてリアルタイムにホログラフィ触覚フィードバックを提示する `HAP_FoxFootHapticsController` の仕様、アーキテクチャおよびパラメータについて解説します。

---

## 1. 概要

本コンポーネントは、歩行・走調アニメーションを行う狐キャラクターの 4 本の足（前左・前右・後右・後左）および尻尾のボーン座標を特定し、超音波フェーズドアレイ (AUTD3) から空中焦点または STM パターンを足部位置へ提示する機能です。

`HAP_BaseObjectHapticsController` を継承しており、神クラス化を防ぐために接触判定、Gizmo描画、診断レポート生成、AppLogManager 連携が専用クラスへ完全に分離されています。

### 主な特徴

* **4足・尻尾ボーン自動検出**: ルート Transform を基準に、モデル階層から 4 足の末端ボーン (`Fox_F_LLegDigit11` 等) および尻尾 (`Fox_Tail6` 等) を自動探索してバインドします。
* **シーケンシャル周回照射**: 前左 → 前右 → 後右 → 後左 → 尻尾の順に時計回りで定義されており、ハードウェア FociSTM や高速シーケンシャル切り替えとの親和性に優れています。
* **空中・接地自動抑制 (`disableWhenInAir`)**: ジャンプ中や歩行時の足上げフェーズにおいて、基準ルートからの相対高さを評価して自動的に触覚照射を ON/OFF します。
* **手の近接接触判定 (`onlyTargetHandContact`)**: `HCD_Pipeline` の手クラスタ情報と照合し、手が足元に触れようとしている時のみピンポイントで照射できます。

---

## 2. 設計思想・アーキテクチャ

### 2.1 ディレクトリ構成

```text
Assets/Features/Haptics/Scripts/
├── Core/
│   ├── HAP_BaseObjectHapticsController.cs  # オブジェクト触覚基底コントローラー
│   └── HAP_AUTDHapticsController.cs        # 触覚システム司令塔
├── CustomControllers/
│   └── HAP_FoxFootHapticsController.cs     # 狐足部・尻尾のターゲット管理
├── Processors/
│   ├── HAP_TargetContactEvaluator.cs       # 接地判定・手近接判定評価クラス (Pure C# static)
│   └── HAP_ObjectFociGenerator.cs          # 焦点データ生成クラス
├── Debug/
│   ├── HAP_ObjectGizmoDrawer.cs            # Sceneビュー Gizmo 描画ヘルパー (Pure C# static)
│   ├── HAP_ObjectHapticsDiagnostics.cs     # 診断レポート生成・出力ヘルパー (Pure C# static)
│   └── HAP_LogTriggers.cs                  # AppLogManager 連動一元ログトリガー
└── Editor/
    └── CustomControllers/
        └── HAP_FoxFootHapticsControllerEditor.cs # Inspector 拡張（ボーン検出・診断ボタン）
```

### 2.2 クラス相関図

```mermaid
graph TD
    AUTD["HAP_AUTDHapticsController"] --> Dispatcher["HAP_TargetSourceDispatcher"]
    Dispatcher --> FootCtrl["HAP_FoxFootHapticsController"]

    FootCtrl -- 継承 --> Base["HAP_BaseObjectHapticsController"]

    Base --> |接触・接地評価を委譲| Evaluator["HAP_TargetContactEvaluator"]
    Base --> |Gizmo描画を委譲| Drawer["HAP_ObjectGizmoDrawer"]
    Base --> |焦点生成を委譲| FociGen["HAP_ObjectFociGenerator"]
    Base --> |診断ログを委譲| Diag["HAP_ObjectHapticsDiagnostics"]

    LogTrig["HAP_LogTriggers"] --> |シーン内検出・サブトリガー登録| Base
    LogTrig --> |定期診断ログ実行 (5秒)| Diag

    style FootCtrl fill:#4a90d9,color:#fff
    style Base fill:#2980b9,color:#fff
    style Evaluator fill:#27ae60,color:#fff
    style Drawer fill:#f39c12,color:#fff
    style Diag fill:#8e44ad,color:#fff
    style LogTrig fill:#e74c3c,color:#fff
```

### 2.3 処理フロー

```text
1. HAP_AUTDHapticsController の更新ループ内で TargetInfos を取得
2. HAP_TargetContactEvaluator により各足の接地高さ・手クラスタ距離を判定
3. 接地・接触が成立した足ボーンを収集
4. HAP_ObjectFociGenerator により、FociSTM または GainSTM 形式の焦点データへ変換
5. AUTD3 ハードウェアへ送信
```

---

## 3. セットアップ・使用方法

### 3.1 クイックスタート手順

#### Step 1: コンポーネントのアタッチ
狐モデルのルート GameObject に `HAP_FoxFootHapticsController` をアタッチします。

#### Step 2: ボーンの自動検出
Inspector 上部の **「🦴 足ボーン階層を自動検出 (Auto Detect)」** ボタンをクリックします。

#### Step 3: パラメータ設定

| 設定カテゴリ | 設定項目 | 型 | 既定値 | 説明 |
|---|---|---|---|---|
| **Dependencies** | `autdController` | `HAP_AUTDHapticsController` | `null` | 触覚司令塔の参照 |
| **Animation State** | `disableWhenInAir` | `bool` | `false` | 有効時、足が浮いている時は触覚をオフにします |
| | `airborneHeightThreshold`| `float` | `0.05f` | 接地判定とするルート高さからの許容差（m） |
| | `rootTransform` | `Transform` | `null` | キャラクターのルート階層 |
| **Hand Contact** | `onlyTargetHandContact` | `bool` | `false` | 手の点群が足の近くにある時のみ照射します |
| | `handContactThreshold` | `float` | `0.10f` | 手との接触判定距離閾値（m） |
| **Custom Mode** | `stmMode` | `HapticsSTMMode` | `FociSTM` | `FociSTM` / `GainSTM` |
| | `trackMode` | `HapticsTrackMode` | `Sequential` | `Simultaneous` / `Sequential` |
| | `sequentialSTMFrequency` | `float` | `150f` | シーケンシャル切り替え周波数（Hz） |
| **Foot Toggles** | `enableFrontLeft` 等 (5部位) | `bool` | `true` | 各足および尻尾の個別有効フラグ |

---

## 4. 仕様・パラメータ詳細

### 4.1 部位別ターゲット定義

時計回り順に定義されており、シーケンシャル STM 照射時の自然な回転パターンを形成します。

1. **Front Left** (`frontLeftFoot`): 前左足 (`IsTail = false`)
2. **Front Right** (`frontRightFoot`): 前右足 (`IsTail = false`)
3. **Back Right** (`backRightFoot`): 後右足 (`IsTail = false`)
4. **Back Left** (`backLeftFoot`): 後左足 (`IsTail = false`)
5. **Tail** (`tailBone`): 尻尾先端 (`IsTail = true`、空中判定除外)

---

## 5. デバッグ・留意事項

### 5.1 Scene ビュー Gizmo 可視化

`HAP_ObjectGizmoDrawer` により、Scene ビュー上に足位置（緑/赤球）、接地判定線（緑線/赤線と許容十字）、手接触許容球が表示されます。

### 5.2 統制ログシステム (`AppLogManager`) との同期

[`HAP_LogTriggers`](file:///c:/Users/hongo/Documents/tsutsumi/RealTimeOcclusion/Assets/Features/Haptics/Scripts/Debug/HAP_LogTriggers.cs) により、`AppLogManager` の `Haptics` カテゴリ内に以下のサブトリガーが自動登録されます。

* `[HAP_FoxFootHapticsController] 診断レポート (手動/イベント)`: Inspectorボタン押下時の詳細ログ
* `[HAP_FoxFootHapticsController] 定期自動ログ (5秒間隔)`: 定期監視ログ（初期OFF）

詳細については [Logging.md](./Logging.md) を参照してください。
