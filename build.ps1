# CS2 饰品抽奖模拟器 —— 构建脚本
# 用法: powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "C# compiler not found: $csc" }

if (-not (Test-Path "data\items.json")) {
  throw "missing data\items.json - run: python build_items.py"
}

Write-Output "[1/3] compiling C# ..."
New-Item -ItemType Directory -Force -Path build, release | Out-Null
$sources = Get-ChildItem "src\*.cs" | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /utf8output `
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  /out:build\CS2Roulette.exe @sources
if ($LASTEXITCODE -ne 0) { throw "compile failed" }

Write-Output "[2/3] deploying ..."
Copy-Item build\CS2Roulette.exe release\ -Force
New-Item -ItemType Directory -Force -Path "release\data" | Out-Null
Copy-Item data\items.json "release\data\" -Force

# 图片目录：优先用 junction 省空间（同一卷内可用），失败则整体复制
$imgSrc = (Resolve-Path "images").Path
$imgDst = Join-Path (Resolve-Path "release\data").Path "images"
if (Test-Path $imgDst) {
  # 可能是 junction（用 rmdir 删链接本身）也可能是实体副本（需递归删）
  $item = Get-Item $imgDst -Force
  if ($item.LinkType) { cmd /c rmdir "$imgDst" 2>$null | Out-Null }
  else { Remove-Item $imgDst -Recurse -Force }
}
$linked = $false
try {
  cmd /c mklink /J "$imgDst" "$imgSrc" 2>&1 | Out-Null
  # 建立后检查链接类型（Junction）比猜文件名可靠
  if (Test-Path $imgDst) {
    $it = Get-Item $imgDst -Force
    if ($it.LinkType -eq "Junction") { $linked = $true }
  }
} catch { }
if (-not $linked) {
  Write-Output "      junction unavailable, copying images (about 200 MB) ..."
  if (Test-Path $imgDst) { Remove-Item $imgDst -Recurse -Force }
  Copy-Item images $imgDst -Recurse -Force
} else {
  Write-Output "      images linked as junction (no copy)"
}

Copy-Item data\*.json "release\data\" -Force   # items / prices / excluded 一并部署
Copy-Item data\quiz.txt "release\data\" -Force   # 游戏大题库

# 界面素材（立绘 / 背景）
if (Test-Path "assets\ui") {
  New-Item -ItemType Directory -Force -Path "release\data\ui" | Out-Null
  Copy-Item "assets\ui\*" "release\data\ui\" -Force
}

# 数据文件加密（分发用）。明文 → CKXE 魔数 + XOR
# 密钥由 "HFUT2026_CKX免费分享" 派生，水印与数据绑定
Write-Output "      encrypting data ..."
python tools\encrypt_data.py "release\data" 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "data encryption failed" }
# 清理明文备份，避免随包分发
Get-ChildItem "release\data" -Filter "*.plain" -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Output "[3/3] done"
$exe = Get-Item "release\CS2Roulette.exe"
$imgs = (Get-ChildItem "release\data\images" -File -ErrorAction SilentlyContinue).Count
Write-Output ("      release\CS2Roulette.exe   {0:N1} KB" -f ($exe.Length/1KB))
Write-Output ("      items.json               {0:N0} KB" -f ((Get-Item 'release\data\items.json').Length/1KB))
foreach ($f in @("prices.json", "excluded.json")) {
  if (Test-Path "release\data\$f") {
    Write-Output ("      {0,-24} {1:N1} KB" -f $f, ((Get-Item "release\data\$f").Length/1KB))
  } else {
    Write-Output ("      {0,-24} (missing)" -f $f)
  }
}
Write-Output ("      images                   {0} files" -f $imgs)
