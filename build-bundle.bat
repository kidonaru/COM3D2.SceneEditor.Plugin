@echo off
chcp 65001
setlocal

cd /d %~dp0

rem se_bundle (シェーダ・テクスチャ一式) を Unity 5.6 でビルドして source 側へコピーする
rem 5.6 製バンドルは COM3D2 (2.0) / COM3D2.5 の両方で読める
rem 注意: Unity エディタで UnityProject を開いていると batchmode がロックで失敗する

set ENV_FILE=%~dp0.env
if exist "%ENV_FILE%" (
    for /f "usebackq eol=# tokens=1,* delims==" %%a in ("%ENV_FILE%") do set "%%a=%%b"
)
if "%UNITY_56_EXE%"=="" set "UNITY_56_EXE=C:\Program Files\Unity\Hub\Editor\5.6.4f1\Editor\Unity.exe"
if not exist "%UNITY_56_EXE%" (
    echo Unity が見つかりません: %UNITY_56_EXE%
    echo .env に UNITY_56_EXE を設定してください
    exit /b 1
)

set PROJECT_DIR=%~dp0UnityProject
set LOG_FILE=%PROJECT_DIR%\Logs\build-bundle.log
if not exist "%PROJECT_DIR%\Logs" mkdir "%PROJECT_DIR%\Logs"

echo Unity: %UNITY_56_EXE%
"%UNITY_56_EXE%" -batchmode -nographics -quit -projectPath "%PROJECT_DIR%" -executeMethod CreateAssetBundles.BuildAllAssetBundlesBatch -logFile "%LOG_FILE%"
if %ERRORLEVEL% neq 0 (
    echo バンドルのビルドに失敗しました。ログ: %LOG_FILE%
    exit /b 1
)

set SRC=%PROJECT_DIR%\Assets\Bundles\se_bundle
set DST=%~dp0source\COM3D2.SceneEditor.Plugin\Timeline\se_bundle
if not exist "%SRC%" (
    echo 生成物が見つかりません: %SRC%
    exit /b 1
)
copy /y "%SRC%" "%DST%" >nul
echo コピーしました: %DST%
