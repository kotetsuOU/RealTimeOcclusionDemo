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
cd RealTimeOcclusion
git lfs pull

```

### 2. MediaPipe Unity Plugin の導入
本プロジェクトは、該当のパッケージファイルを指定位置にインストールすることで、自動的に機能が組み込まれる設計となっています。

1. [MediaPipe Unity Plugin v0.16.3 Release](https://github.com/homuler/MediaPipeUnityPlugin/releases/tag/v0.16.3) より `com.github.homuler.mediapipe-0.16.3.tgz` をダウンロードします。
2. ダウンロードした `.tgz` ファイルを、Unityプロジェクトの `Packages/` フォルダ内に配置します。
3. Unityの Package Manager (UPM) を開き、「Add package from tarball...」を選択して当該ファイルをインストールしてください。

---

## 📜 ライセンス (License)
本プロジェクト自体は [MIT License](LICENSE) の下で公開されています。

### サードパーティライセンス (Third-Party Licenses)
本プロジェクトには以下のサードパーティ製ソフトウェアおよび外部素材が含まれています。これらの取り扱いについては、各配布元のライセンス条項に従ってください。

* **Intel RealSense SDK 2.0 (Customized)**: Apache License 2.0
* **MediaPipe Unity Plugin**: MIT License (Core MediaPipe is Apache License 2.0)
* **フリーアニメーション素材**: 各配布元の利用規約に準拠