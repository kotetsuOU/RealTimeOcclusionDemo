# ==========================================
# Unity Git Local Save Script
# 目的: リモートに接続せず、ローカルリポジトリにのみ変更を記録（コミット）します。
# 更新日: 2025/09/20
# ==========================================

# 現在の日時（YYYY/MM/DD HH:mm:ss）
$now = (Get-Date -Format "yyyy/MM/dd HH:mm:ss")

Write-Host "=== ローカル保存を開始します ($now) ===" -ForegroundColor Yellow

# .git が存在するかチェック
if (-not (Test-Path ".git")) {
    Write-Host "[エラー] このフォルダはGitリポジトリではありません。" -ForegroundColor Red
    exit
}

# 変更をステージング
Write-Host "--- 変更をステージングしています..."
git add .

# ステージングされた変更があるか確認
$changes = git diff --staged --quiet
if ($LASTEXITCODE -eq 0) {
    Write-Host "--- 保存する変更がありませんでした。" -ForegroundColor Green
} else {
    # コミットメッセージを作成
    $commitMessage = "WIP save: $now"
    Write-Host "--- 変更をローカルにコミットしています..."
    
    # コミットを実行
    git commit -m $commitMessage
    
    Write-Host "--- コミットが完了しました: `"$commitMessage`"" -ForegroundColor Green
}

Write-Host "=== ローカル保存が完了しました ===" -ForegroundColor Yellow