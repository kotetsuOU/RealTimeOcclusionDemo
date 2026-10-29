# RealTimeOcclusionDemo

Intel RealSenseとMediaPipeを利用し、オクルージョン処理を反映させた2D映像描画を実装したUnityプロジェクトです。

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
* **Display**: QHD (2560 x 1440)

### 同梱パッケージと依存関係
* **Intel RealSense SDK 2.0**: プロジェクト内部に独自にカスタマイズしたSDKを同梱しています。外部からの別途インストールは不要です。
* **フリーアニメーション素材**: プロジェクト内に組み込み済みです。
* **MediaPipe Unity Plugin**: パッケージ本体 (`com.github.homuler.mediapipe-0.16.3.tgz`) の取得と配置が別途必要です。

---

## 🛠️ インストールとセットアップ手順 (Installation)

以下の手順に従い、Git LFSによるクローンとMediaPipeのパッケージ導入を行ってください。パッケージ導入後、システムに自動で組み込まれます。

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

### 2. MediaPipe Unity Plugin の導入
本プロジェクトは、該当のパッケージファイルを指定位置にインストールすることで、自動的に機能が組み込まれる設計となっています。

1. [MediaPipe Unity Plugin v0.16.3 Release](https://github.com/homuler/MediaPipeUnityPlugin/releases/tag/v0.16.3) より `com.github.homuler.mediapipe-0.16.3.tgz` をダウンロードします。
2. ダウンロードした `.tgz` ファイルを、Unityプロジェクトの `Packages/` フォルダ内に配置します。
3. Unityの Package Manager (UPM) を開き、「Add package from tarball...」を選択して当該ファイルをインストールしてください。

---

## 使い方
基本的な使い方は以下の通りです。
詳細なキーボード操作については、[こちらのガイド](./KeyboardControls.md)を確認してください。

※ Unityで実行する際は、`Project` ウィンドウから `Scenes` フォルダ内の `RealTimeOcclusion` シーンを開いてください。
  そのシーンをアクティブにしてから再生（Play）ボタンで動作確認を行ってください。

### オクルージョン DebugMap / PixelTagMap の使い方

`PCDRendererFeature` の `Record Occlusion Debug Map` を有効にすると、**そのフレームのみ**内積計算から得られる `occlusionAverage`（0.0～1.0）を保存できます。
- 保存先: `Assets/HandTrackingData/OcclusionMaps`
- CSV: `occlusionAverage`（0.0～1.0）を保存
- 操作: `Enter` / `Return`（`KeyboardControls.md` の撮影操作。Enterで統合DepthMapなども同時撮影）

`PCDRendererFeature` の `Record Pixel Tag Map` を有効にすると、**そのフレームのみ**最終判定後の「アルファ値（遮蔽判定された0か1か）」と「クラス分類値」を保存できます。
- 保存先: `Assets/HandTrackingData/PixelTagMaps`
- 操作: 同上

`PCDRendererFeature` の `Record Integrated Depth Map` を有効にすると、**そのフレームのみ**統合DepthMapを保存できます。

- 保存先: `Assets/HandTrackingData/DepthMaps/Integrated`
- 操作: `Enter` / `Return`（`KeyboardControls.md` の撮影操作。EnterでOcclusion DebugMapも同時撮影）

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

### 球の握り込み（Grasp）と触覚フィードバック

手全体で球を握り込むと、球が手に追従して動かせるようになり、AUTD3 で手の接触位置ごとに触覚が提示されます。

セットアップ（球のGameObjectに以下をアタッチ）:
1. `HapCollisionDetectors`（`Detection Mode = TransformOnly`、`Target Object` に球自身、`Collision Compute Shader` を設定）
2. `HapGraspInteractor`（`Grasp Compute Shader` は未指定なら 1. のものを使用）
3. `SoftBodyDeform` + `HapDeformBridge`（任意。握った強さに応じたへこみ）
4. `MultiAUTD3Controller` の `Mode` を `GraspBased` に設定

動作:
- 球の周囲（半径 + `Grab Margin`）の点群が「十分な点数」「複数方向（8分割中 `Grab Min Bins` 方向以上）」「一方向に偏っていない（`Grab Max Direction Bias` 以下）」を満たすと握り込みと判定します。手のひらを当てただけでは握り込みになりません。
- 握っている間は、球の周囲（半径 + `Track Margin`）にある点群の重心の動きに合わせて球が移動します（開始時の相対位置を保持）。解除は `Release Hysteresis` / `Release Delay` でチャタリングを抑えています。離したときは `Inherit Velocity On Release` で手の速度を引き継ぎ、`Release Drag` で減速します。
- 握っている間、手が球に触れている方向（オクタント）ごとの接触位置に複数フォーカスを出し、各AUTDデバイスは最寄りのフォーカスを担当します。握っていないときは `CollisionBased` と同じ挙動です。
- 判定が厳しすぎる/緩すぎる場合は、Inspector の `debugStatus`（接触点数・偏り・フォーカス数）を見て `Grab Min Points` などを調整してください。

### 統合DepthMap出力形式

- `IntegratedDepth_*.png`: 解読しやすいグラデーション可視化画像
- `IntegratedDepth_*.raw32`: 可逆な生データ（`R32_UInt`、little-endian）
- `IntegratedDepth_*.txt`: `width/height/min/max` などのメタ情報

---

## 📜 ライセンス (License)
本プロジェクト自体は [MIT License](LICENSE) の下で公開されています。

### サードパーティライセンス (Third-Party Licenses)
本プロジェクトには以下のサードパーティ製ソフトウェアおよび外部素材が含まれています。これらの取り扱いについては、各配布元のライセンス条項に従ってください。

* **Intel RealSense SDK 2.0 (Customized)**: Apache License 2.0
* **MediaPipe Unity Plugin**: MIT License (Core MediaPipe is Apache License 2.0)
* **フリーアニメーション素材**: 各配布元の利用規約に準拠
