# ============================================================
# NomadGo SpatialVision - Local Setup Script
# Run in PowerShell as Administrator
# ============================================================

param(
    [string]$UnityVersion = "2022.3"
)

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  NomadGo SpatialVision - Local Setup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$ProjectPath = (Get-Location).Path
Write-Host "Project path: $ProjectPath" -ForegroundColor White
Write-Host ""

# --- Step 1: Check Unity Hub ---
Write-Host "[1/6] Checking Unity Hub..." -ForegroundColor Yellow

$unityHubPath = "${env:ProgramFiles}\Unity Hub\Unity Hub.exe"
$unityHubAlt = "${env:ProgramFiles(x86)}\Unity Hub\Unity Hub.exe"

if (Test-Path $unityHubPath) {
    Write-Host "  Unity Hub found: $unityHubPath" -ForegroundColor Green
} elseif (Test-Path $unityHubAlt) {
    Write-Host "  Unity Hub found: $unityHubAlt" -ForegroundColor Green
    $unityHubPath = $unityHubAlt
} else {
    Write-Host "  Unity Hub not found. Downloading..." -ForegroundColor Red
    $hubUrl = "https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup.exe"
    $hubInstaller = "$env:TEMP\UnityHubSetup.exe"
    Invoke-WebRequest -Uri $hubUrl -OutFile $hubInstaller
    Write-Host "  Downloaded. Please install Unity Hub from: $hubInstaller" -ForegroundColor Yellow
    Write-Host "  After installation, re-run this script." -ForegroundColor Yellow
    Start-Process $hubInstaller
    exit
}

# --- Step 2: Check Unity Editor ---
Write-Host "[2/6] Checking Unity Editor..." -ForegroundColor Yellow

$unityEditors = Get-ChildItem "${env:ProgramFiles}\Unity\Hub\Editor" -ErrorAction SilentlyContinue
$targetEditor = $unityEditors | Where-Object { $_.Name -like "$UnityVersion*" } | Select-Object -First 1

if ($targetEditor) {
    $unityExe = Join-Path $targetEditor.FullName "Editor\Unity.exe"
    Write-Host "  Unity $($targetEditor.Name) found" -ForegroundColor Green
} else {
    Write-Host "  Unity $UnityVersion not installed." -ForegroundColor Red
    Write-Host "  Open Unity Hub and install Unity $UnityVersion LTS with Android Build Support" -ForegroundColor Yellow
    Write-Host "  Make sure to enable: Android SDK & NDK Tools, OpenJDK" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  After installation, re-run this script." -ForegroundColor Yellow
    exit
}

# --- Step 3: Create required folders ---
Write-Host "[3/6] Creating project folders..." -ForegroundColor Yellow

$folders = @(
    "Assets\Plugins\OnnxRuntime",
    "Assets\Plugins\Android\arm64-v8a",
    "Assets\StreamingAssets\Models"
)

foreach ($folder in $folders) {
    $fullPath = Join-Path $ProjectPath $folder
    if (!(Test-Path $fullPath)) {
        New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
        Write-Host "  Created: $folder" -ForegroundColor Gray
    }
}
Write-Host "  All folders ready" -ForegroundColor Green

# --- Step 4: Download ONNX Runtime ---
Write-Host "[4/6] Downloading ONNX Runtime 1.16.3..." -ForegroundColor Yellow

$onnxPluginDir = Join-Path $ProjectPath "Assets\Plugins\OnnxRuntime"
$onnxDllExists = Test-Path (Join-Path $onnxPluginDir "Microsoft.ML.OnnxRuntime.dll")

if (!$onnxDllExists) {
    try {
        $onnxUrl = "https://www.nuget.org/api/v2/package/Microsoft.ML.OnnxRuntime/1.16.3"
        $onnxZip = "$env:TEMP\onnxruntime.zip"
        $onnxExtract = "$env:TEMP\onnxruntime_extracted"

        Write-Host "  Downloading Microsoft.ML.OnnxRuntime..." -ForegroundColor White
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $onnxUrl -OutFile $onnxZip -ErrorAction Stop

        if (Test-Path $onnxExtract) { Remove-Item $onnxExtract -Recurse -Force }
        Expand-Archive -Path $onnxZip -DestinationPath $onnxExtract -Force

        $managedDll = Get-ChildItem "$onnxExtract\lib\netstandard2.0" -Filter "Microsoft.ML.OnnxRuntime.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($managedDll) {
            Copy-Item $managedDll.FullName -Destination $onnxPluginDir -Force
            Write-Host "  Copied: Microsoft.ML.OnnxRuntime.dll" -ForegroundColor Green
        }

        $androidLib = Get-ChildItem "$onnxExtract\runtimes" -Recurse -Filter "libonnxruntime.so" -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*arm64*" } | Select-Object -First 1
        if ($androidLib) {
            $androidDir = Join-Path $ProjectPath "Assets\Plugins\Android\arm64-v8a"
            Copy-Item $androidLib.FullName -Destination $androidDir -Force
            Write-Host "  Copied: libonnxruntime.so (arm64)" -ForegroundColor Green
        }

        Remove-Item $onnxZip -Force -ErrorAction SilentlyContinue
        Remove-Item $onnxExtract -Recurse -Force -ErrorAction SilentlyContinue

        $managedUrl = "https://www.nuget.org/api/v2/package/Microsoft.ML.OnnxRuntime.Managed/1.16.3"
        $managedZip = "$env:TEMP\onnxruntime_managed.zip"
        $managedExtract = "$env:TEMP\onnxruntime_managed_extracted"

        Write-Host "  Downloading Microsoft.ML.OnnxRuntime.Managed..." -ForegroundColor White
        Invoke-WebRequest -Uri $managedUrl -OutFile $managedZip -ErrorAction Stop

        if (Test-Path $managedExtract) { Remove-Item $managedExtract -Recurse -Force }
        Expand-Archive -Path $managedZip -DestinationPath $managedExtract -Force

        $managedDll2 = Get-ChildItem "$managedExtract\lib\netstandard2.0" -Filter "*.dll" -ErrorAction SilentlyContinue
        foreach ($dll in $managedDll2) {
            Copy-Item $dll.FullName -Destination $onnxPluginDir -Force
            Write-Host "  Copied: $($dll.Name)" -ForegroundColor Green
        }

        Remove-Item $managedZip -Force -ErrorAction SilentlyContinue
        Remove-Item $managedExtract -Recurse -Force -ErrorAction SilentlyContinue

    } catch {
        Write-Host "  Could not download ONNX Runtime automatically." -ForegroundColor Red
        Write-Host "  Please download manually from:" -ForegroundColor Yellow
        Write-Host "  https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime/1.16.3" -ForegroundColor White
    }
} else {
    Write-Host "  ONNX Runtime already exists" -ForegroundColor Green
}

# --- Step 5: Check for YOLO model ---
Write-Host "[5/6] Checking for YOLO model..." -ForegroundColor Yellow

$modelPath = Join-Path $ProjectPath "Assets\StreamingAssets\Models\yolov8n.onnx"
if (Test-Path $modelPath) {
    Write-Host "  YOLO model found" -ForegroundColor Green
} else {
    Write-Host "  YOLO model NOT found." -ForegroundColor Red
    Write-Host "  Download yolov8n.onnx from:" -ForegroundColor Yellow
    Write-Host "  https://github.com/ultralytics/assets/releases" -ForegroundColor White
    Write-Host "  Place it in: Assets\StreamingAssets\Models\" -ForegroundColor White
}

# --- Step 6: Summary ---
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Setup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host ""
Write-Host "  1. Open Unity Hub > Open > select this project folder" -ForegroundColor White
Write-Host "  2. Wait for Unity to import (5-10 min first time)" -ForegroundColor White
Write-Host "  3. Open scene: Assets/Scenes/Main.unity" -ForegroundColor White
Write-Host "  4. Add AR components to GameObjects (see RUNBOOK section 2.7)" -ForegroundColor White
Write-Host "  5. Add ONNX_RUNTIME to Scripting Define Symbols" -ForegroundColor White
Write-Host "     (Edit > Project Settings > Player > Other Settings)" -ForegroundColor Gray
Write-Host "  6. File > Build Settings > Android > Switch Platform" -ForegroundColor White
Write-Host "  7. Connect Android phone via USB" -ForegroundColor White
Write-Host "  8. File > Build and Run" -ForegroundColor White
Write-Host ""

$openProject = Read-Host "Open project in Unity now? (y/n)"
if ($openProject -eq 'y') {
    if (Test-Path $unityExe) {
        Write-Host "Opening Unity..." -ForegroundColor Cyan
        Start-Process $unityExe -ArgumentList "-projectPath `"$ProjectPath`""
    } else {
        Write-Host "Unity not found. Please open from Unity Hub manually." -ForegroundColor Yellow
    }
}
