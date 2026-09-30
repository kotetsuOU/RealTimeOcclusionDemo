# RealTimeOcclusionDemo

Intel RealSenseを利用し、オクルージョン処理を反映させた2D映像描画を実装したUnityプロジェクトです。

## ⚠️ 動作環境と前提条件 (Prerequisites)

本プロジェクトの動作には以下のソフトウェアおよびハードウェア環境が必要です。

* **Unity Version**: Unity 6 (`6000.3.9f1`)
* **Render Pipeline**: Universal Render Pipeline (URP)
* **Git LFS**: プロジェクト内のアセット（画像、3Dモデル等）取得に必須です。
* **ハードウェア**: Intel RealSense Depth Camera (例: D415, D435, D455)

### 検証済み環境 (Verified Environment)
以下の環境にて、リアルタイム処理の正常動作を検証しています。
* **CPU**: Intel Core i9-13900KF
* **GPU**: NVIDIA GeForce RTX 4080
* **Display**: 4K (3840 x 2160)

* **Intel RealSense SDK 2.0**: プロジェクト内部に独自にカスタマイズしたSDKを同梱しています。外部からの別途インストールは不要です。
* **Sony Spatial Reality Display SDK**: プロジェクト内部に独自にカスタマイズしたSDKを同梱しています。外部からの別途インストールは不要です。
* **フリーアニメーション素材**: プロジェクト内に組み込み済みです。

---

## 🛠️ インストールとセットアップ手順 (Installation)

以下の手順に従い、Git LFSによるクローンを行ってください。

### 1. Git LFS のインストールとクローン
巨大なファイルを正しく取得するため、事前にGit LFSを有効化してからクローンを実行してください。

```bash
# Git LFSをインストール（初回のみ・OS全体に適用）
git lfs install

# リポジトリをクローン
git clone https://github.com/kotetsuOU/RealTimeOcclusionDemo.git

# クローン後、ディレクトリに移動してLFSファイルを展開
cd RealTimeOcclusionDemo
git lfs pull

```

---

## 使い方
基本的な使い方は以下の通りです。
詳細なキーボード操作については、[こちらのガイド (PhysicalResponse.md)](./Docs/PhysicalResponse.md#41-キーボード操作対応表)を確認してください。

※ Unityで実行する際は、`Project` ウィンドウから `Scenes` フォルダ内の `RealTimeOcclusion` シーンを開いてください。
  そのシーンをアクティブにしてから再生（Play）ボタンで動作確認を行ってください。

### ⚙️ 初期化とアライメント・キャリブレーション (Camera Alignment & Calibration)

複数台の RealSense カメラの位置合わせ（アライメント）や保存・復元は、シーン内の **`RsGlobalPointCloudManager`** および **`RsTransformController`** から制御します。

1. **ガイド表示**: `RsTransformController` の `Show Calibration Guide` を有効にすると、位置合わせの目安となる緑のボックス（Gizmo）がシーンビューに表示されます。
2. **位置微調整**: `RsGlobalPointCloudManager` が認識した子オブジェクト（各RealSenseカメラのレンダラー）の `Transform` を調整し、ガイドボックスに合わせます。
3. **JSON保存・読み込み**: 
   - 調整完了後、`RsTransformController` の Inspector に表示される **「Save Transforms to JSON」** ボタンを押すと、カメラ位置姿勢が `Assets/Config/RealSense/[saveFileName].json` に保存されます。
   - 以前の設定を反映させたいときは **「Load Transforms from JSON」** ボタンで一括読み込み復元が可能です（ロード処理は Undo（Ctrl+Z）に対応しています）。

詳細な設計と手順については、[初期化とアライメント・キャリブレーションシステム (Initialization.md)](./Docs/Initialization.md) を参照してください。

---

### オクルージョン設定の管理とデバッグ機能の使い方

本プロジェクトのオクルージョン設定（カーネル手法、しきい値、各種デバッグ/モルフォロジー設定等）は、シーン内の **`OcclusionPipelineController`** という GameObject にアタッチされた **`PCDRenderController`** コンポーネントから一元的に制御します。エディタ再生中・非再生中を問わず、パラメータを変更すると即座にレンダリング表示に反映されます。

`PCDRenderController` の `Record Occlusion Debug Map` を有効にすると、**そのフレームのみ**内積計算から得られる `occlusionAverage`（0.0～1.0）を保存できます。
- 保存先: `Assets/HandTrackingData/OcclusionMaps`
- CSV: `occlusionAverage`（0.0～1.0）を保存
- 操作: `Enter` / `Return`（[キーボード操作対応表](./Docs/PhysicalResponse.md#41-キーボード操作対応表)の撮影操作。Enterで統合DepthMapなども同時撮影）

`PCDRenderController` の `Record Pixel Tag Map` を有効にすると、**そのフレームのみ**最終判定後の「アルファ値（遮蔽判定された0か1か）」と「クラス分類値」を保存できます。
- 保存先: `Assets/HandTrackingData/PixelTagMaps`
- 操作: 同上

`PCDRenderController` の `Record Integrated Depth Map` を有効にすると、**そのフレームのみ**統合DepthMapを保存できます。

- 保存先: `Assets/HandTrackingData/DepthMaps/Integrated`
- 操作: `Enter` / `Return`（[キーボード操作対応表](./Docs/PhysicalResponse.md#41-キーボード操作対応表)の撮影操作。EnterでOcclusion DebugMapも同時撮影）

現在の可視化ルール（`PCDOcclusionDebugExporter`）:

- **白**: 背景 (`-1.0`)
- **緑**: 実点群（Tag最適化 ON 時スキップされた実点群 / OFF 時に遮蔽されていない実点群, `-3.0`）
- **シアン**: その他スキップ / Tag最適化 OFF 時に遮蔽されている実点群 (`-2.0`)
- **マゼンタ**: 仮想オブジェクト (`>= 1.9`)
- **グレー**: ほぼ `0` の値
- それ以外の `0.0 ~ 1.0`: 16段階パレット

補足:

- `Enable PixelTagMap` を ON にすると、DebugMap と同じ配色ルールの可視化(最終判定後のアルファ値)を画面上で常時確認できます。
- `Enable Occlusion Map` を ON にすると、`Record Occlusion Debug Map` と同じく `occlusionAverage`（0.0～1.0）を同じ配色ルールで画面上に常時表示します。
- `Record Occlusion Debug Map` や `Record Pixel Tag Map` は連続保存を避けるため、保存後に自動で `false` に戻ります。
- `Record Integrated Depth Map` も保存後に自動で `false` に戻ります。
- `Enable Tag Based Optimization` が OFF のとき、実点群は「可視=緑 / 遮蔽=シアン」で確認できます。

### 統合DepthMap出力形式

- `IntegratedDepth_*.png`: 解読しやすいグラデーション可視化画像
- `IntegratedDepth_*.raw32`: 可逆な生データ（`R32_UInt`、little-endian）
- `IntegratedDepth_*.txt`: `width/height/min/max` などのメタ情報

---

## 📚 ドキュメント (Documentation)

プロジェクトの全体構成、描画・物理・触覚パイプライン、実験フレームワーク、統制ログシステムの詳細仕様については `Docs/` 配下の各種ドキュメントを参照してください。

* **[システム統合ポータル (Wiki.md)](./Docs/Wiki.md)**: プロジェクト全体の構造と各ドキュメントへのナビゲーションポータルです。
* **[被験者実験フレームワーク (Experiments.md)](./Docs/Experiments.md)**: 2AFC, ABX, 調整法などの心理物理実験システム仕様です。
* **[統制ログ管理システム (Logging.md)](./Docs/Logging.md)**: `AppLogManager` および `AppLogger` による統一ログ管理の仕様と利用ガイドです。
* **[物理応答・インタラクションシステム (PhysicalResponse.md)](./Docs/PhysicalResponse.md)**: モデル切り替え、ボーン自動検出・同期、リフト追従、キー操作仕様です。

---

## 📜 ライセンス (License)
本プロジェクト自体は [MIT License](LICENSE) の下で公開されています。

### サードパーティライセンス (Third-Party Licenses)
本プロジェクトには以下のサードパーティ製ソフトウェアおよび外部素材が含まれています。これらの取り扱いについては、各配布元のライセンス条項に従ってください。

* **Intel RealSense SDK 2.0 (Customized)**: Apache License 2.0
* **Sony Spatial Reality Display SDK**: 各配布元の利用規約に準拠
* **フリーアニメーション素材**: 各配布元の利用規約に準拠