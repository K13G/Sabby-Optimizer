@echo off
setlocal
cls
echo ============================================================
echo             SABBY OPTIMIZER - OLD FILE CLEANUP
echo ============================================================
echo.
echo This removes old ChatGPT Sabby ZIPs and extracted Sabby build folders
echo from Downloads, Documents, and Desktop.
echo.
echo It DOES NOT touch:
echo   C:\Program Files\Sabby Optimizer
echo   %%LOCALAPPDATA%%\SabbyOptimizer\UserData
echo   your installed Sabby settings / update backups
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
 "$ErrorActionPreference='SilentlyContinue';" ^
 "$roots=@((Join-Path $env:USERPROFILE 'Downloads'),(Join-Path $env:USERPROFILE 'Documents'),(Join-Path $env:USERPROFILE 'Desktop')) | Where-Object { Test-Path $_ };" ^
 "$items=@(); foreach($root in $roots){ $items += Get-ChildItem -LiteralPath $root -Force | Where-Object { ($_.PSIsContainer -and ($_.Name -like 'SabbyOptimizer_*' -or $_.Name -like 'sabby_02*' -or $_.Name -like 'sabby_quick*' -or $_.Name -like 'sabby_visibility*')) -or ((-not $_.PSIsContainer) -and ($_.Name -like 'SabbyOptimizer*.zip' -or $_.Name -like 'SabbySource*.zip')) } };" ^
 "$items=$items | Sort-Object FullName -Unique;" ^
 "if(-not $items){ Write-Host '[OK] No old Sabby ZIPs or extracted build folders were found.' -ForegroundColor Green; exit 0 };" ^
 "Write-Host 'The following OLD BUILD FILES will be deleted:' -ForegroundColor Yellow; $items | ForEach-Object { Write-Host ('  ' + $_.FullName) };" ^
 "Write-Host ''; $confirm=Read-Host 'Type DELETE to permanently remove all items above'; if($confirm -cne 'DELETE'){ Write-Host 'Cancelled. Nothing was deleted.' -ForegroundColor Cyan; exit 0 };" ^
 "$deleted=0; foreach($item in $items){ try { Remove-Item -LiteralPath $item.FullName -Recurse -Force -ErrorAction Stop; $deleted++; Write-Host ('[DELETED] ' + $item.FullName) -ForegroundColor DarkGray } catch { Write-Host ('[SKIPPED] ' + $item.FullName + ' - ' + $_.Exception.Message) -ForegroundColor Yellow } };" ^
 "Write-Host ''; Write-Host ('Cleanup finished. Deleted ' + $deleted + ' old Sabby item(s).') -ForegroundColor Green; Write-Host 'Installed Sabby and UserData were not touched.' -ForegroundColor Green;"

echo.
pause
