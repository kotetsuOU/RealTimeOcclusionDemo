# ==========================================
# Unity Git Auto Commit & Push Script
# 更新日: 2025/08/13
# ==========================================

# 現在の日付（YYYY/MM/DD）
$today = (Get-Date -Format "yyyy/MM/dd")

Write-Host "=== Git 更新開始 ($today) ==="

# .git が存在するかチェック
if (-not (Test-Path ".git")) {
    Write-Host "[エラー] このフォルダはGitリポジトリではありません。"
    exit
}

# 変更をステージング
git add .

# コミット（変更があれば）
$commitMessage = "Update: $today"
git commit -m $commitMessage 2>$null

# リモートの変更を取り込む（rebaseで自動で履歴を整列）
git pull --rebase origin main

# プッシュ
git push origin main

Write-Host "=== Git 更新完了 ($today) ==="

