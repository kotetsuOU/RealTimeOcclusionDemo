# ==========================================
# Unity Git Remote Backup Script
# 目的: 現在のブランチの変更をコミットし、リモートに送信してバックアップします。
# 更新日: 2025/09/29
# ==========================================

# 現在の日付（YYYY/MM/DD）
$today = (Get-Date -Format "yyyy/MM/dd")

Write-Host "=== リモートバックアップを開始します ($today) ===" -ForegroundColor Cyan

# .git が存在するかチェック
if (-not (Test-Path ".git")) {
    Write-Host "[エラー] このフォルダはGitリポジトリではありません。" -ForegroundColor Red
    exit
}

# <<< 変更点(1): 現在のブランチ名を取得 >>>
$currentBranch = git rev-parse --abbrev-ref HEAD
Write-Host "--- 現在のブランチ: $currentBranch"

# 1. 変更をステージング
Write-Host "--- 1. 変更をステージングしています..."
git add .

# 2. コミット（変更がある場合のみ）
# ステージングされた変更があるか確認
$changes = git diff --staged --quiet
if ($LASTEXITCODE -eq 0) {
    Write-Host "--- 2. コミットする変更はありません。リモートとの同期に進みます。"
} else {
    $commitMessage = "Backup: $today"
    Write-Host "--- 2. 変更をコミットしています..."
    git commit -m $commitMessage
    Write-Host "--- コミット完了: `"$commitMessage`""
}

# 3. リモートの変更を取り込む
Write-Host "--- 3. リモートリポジトリの変更を取り込んでいます (pull)..."
# <<< 変更点(2): ブランチ名を変数に置き換え >>>
git pull --rebase origin $currentBranch
if ($LASTEXITCODE -ne 0) {
    Write-Host "[エラー] リモートの変更の取り込みに失敗しました。コンフリクトを解決してください。" -ForegroundColor Red
    exit
}

# 4. リモートにプッシュ
Write-Host "--- 4. リモートリポジトリに変更を送信しています (push)..."
# <<< 変更点(3): ブランチ名を変数に置き換え >>>
git push origin $currentBranch
if ($LASTEXITCODE -ne 0) {
    Write-Host "[エラー] リモートへのプッシュに失敗しました。" -ForegroundColor Red
    exit
}

Write-Host "=== リモートバックアップが正常に完了しました ===" -ForegroundColor Cyan
