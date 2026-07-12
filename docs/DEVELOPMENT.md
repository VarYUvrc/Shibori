# Shibori 開発SSoT

このファイルを開発者・コーディングエージェント向けの唯一の開発ドキュメントとする。実装、依存、コマンド、テスト手順を変更した場合は、同じ変更でこのファイルも更新する。

## 前提依存

- Windows 11 x64
- Git
- .NET 8 SDK（8.0.x）
- Windows Desktop SDK / WPF
- Windows SDK（CCD APIの実行に必要）
- 外部NuGetパッケージは使用しない
- SDK確認: `dotnet --info`、`dotnet --list-sdks`

## ローカルビルド

1. `git clone https://github.com/VarYUvrc/Shibori.git`
2. `cd Shibori`
3. `dotnet restore Shibori.sln`
4. `dotnet build Shibori.sln --configuration Debug --no-restore`
5. `dotnet build Shibori.sln --configuration Release --no-restore`

成果物は`bin\Debug\net8.0-windows\Shibori.exe`または`bin\Release\net8.0-windows\Shibori.exe`。

## 配布用ビルド

`dotnet restore Shibori.sln --runtime win-x64`を実行後、`dotnet publish Shibori.csproj --configuration Release --runtime win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false`を実行する。成果物は`bin\Release\net8.0-windows\win-x64\publish\Shibori.exe`。

## 診断とログ

- 診断: `powershell -ExecutionPolicy Bypass -File scripts\run-diagnostics.ps1`
- 全体自己テスト: `Shibori.exe --self-test`
- 個別復元自己テスト: `Shibori.exe --partial-test`
- ログ: `%LOCALAPPDATA%\Shibori\logs\`
- 通常ログは日次・1MB上限・14日保持、診断ログは2MB上限
- 自己テストは実際に表示構成を変更するため、複数モニター環境で実行する

## 目的と仕様

ShiboriはWindows 11でVR利用中などに使わないモニターを一時停止するWPFネイティブアプリである。チェックありは接続中、チェックなしは一時停止中。メインモニターは停止不可。停止中のモニターは再起動・再読み込み後も一覧に残り、個別に復元できる。全モニターが復元されるとバックアップを削除する。

## 表示構成の不変条件

`QueryDisplayConfig(QDC_ALL_PATHS | QDC_VIRTUAL_MODE_AWARE)`でパスを取得し、Adapter LUID + Source IDを安定IDとして使う。`DISPLAY1`などのGDI名は再採番されるため永続IDに使わない。初回停止時にCCDパス・モード配列を`%LOCALAPPDATA%\Shibori\display-backup.json`へ保存し、停止時は対象パスを除外して`SetDisplayConfig`を適用する。個別復元時は現在接続中のパスとクリックされた対象パスだけを再適用する。旧形式のバックアップ読み替えや旧仕様APIへのfallbackは実装しない。

## 更新・ライセンス・構成

- バージョン形式は`YYYY.MM.DD.NN`。現在は`2026.07.12.01`
- `UpdateChecker.cs`がGitHub Releasesの最新版と配布zipを確認する
- 更新ボタンはzipを取得し、アプリ終了後にupdaterが差し替える
- `AppLogger.cs`が低頻度の操作・エラー・更新ログを出力する
- `Assets/shibori.svg`が設計ソース、`Assets/shibori.ico`がexe用アイコン。再生成は`scripts/generate-icon.ps1`
- ShiboriはApache License 2.0。外部NuGetパッケージはない。配布物には.NET 8/WPF/Windows SDK由来のMicrosoftランタイムが含まれる
- `.github/workflows/build.yml`はCI artifact、`release.yml`は`v*`タグからRelease本文と`Shibori-win-x64.zip`を生成する
