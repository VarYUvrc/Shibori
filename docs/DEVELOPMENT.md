# Shibori 開発SSoT

このファイルを開発者・コーディングエージェント向けの唯一の開発ドキュメントとする。実装、依存、コマンド、テスト手順を変更した場合は、同じ変更でこのファイルも更新する。開発情報を別ファイルへ分散させない。

## 前提依存

- Windows 11 x64
- Git
- .NET 8 SDK（8.0.x）
- WPFを含むWindows Desktop SDK
- Windows SDK（CCD APIの実行はWindows上でのみ可能）

外部NuGetパッケージは使用していません。SDKの確認:

```powershell
dotnet --info
dotnet --list-sdks
```

`8.0.x`のSDKが表示されない場合は、[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)をインストールしてください。

## リポジトリ取得

```powershell
git clone https://github.com/VarYUvrc/Shibori.git
cd Shibori
```

## ローカルビルド

依存関係を復元します。

```powershell
dotnet restore Shibori.sln
```

Debugビルド:

```powershell
dotnet build Shibori.sln --configuration Debug --no-restore
```

Releaseビルド:

```powershell
dotnet build Shibori.sln --configuration Release --no-restore
```

成果物は`bin\Debug\net8.0-windows\`または`bin\Release\net8.0-windows\`に出力されます。

## 配布用ビルド

配布版は.NETランタイムを同梱するWindows x64 self-contained single-fileです。

```powershell
dotnet restore Shibori.sln --runtime win-x64
dotnet publish Shibori.csproj --configuration Release --runtime win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
```

成果物は`bin\Release\net8.0-windows\win-x64\publish\Shibori.exe`です。

## 実行と診断

```powershell
& .\bin\Release\net8.0-windows\Shibori.exe
powershell -ExecutionPolicy Bypass -File scripts\run-diagnostics.ps1
& .\bin\Release\net8.0-windows\Shibori.exe --self-test
& .\bin\Release\net8.0-windows\Shibori.exe --partial-test
```

診断ログは`%LOCALAPPDATA%\Shibori\shibori.log`に出力されます。自己テストは実際に表示構成を一時変更するため、複数モニター環境で実行前後の接続台数を確認してください。

## 目的と仕様

Shiboriは、Windows 11でVR利用中などに使わないモニターを一時停止するWPFネイティブアプリです。

- チェックあり: 接続中
- チェックなし: 一時停止中
- メインモニターは停止不可
- 停止中のモニターは再起動・再読み込み後も一覧に残る
- 停止中のモニターを個別に復元できる
- 全モニターが復元されるとバックアップを削除する

## 表示構成の不変条件

1. `QueryDisplayConfig(QDC_ALL_PATHS | QDC_VIRTUAL_MODE_AWARE)`でパスとモードを取得する。
2. `DISPLAY1`などのGDI名は再採番されるため、永続的な識別子に使わない。
3. Adapter LUID + Source IDから安定IDを作る。
4. 初回停止時に切断前のCCDパス・モード配列を`%LOCALAPPDATA%\Shibori\display-backup.json`へ保存する。
5. 停止時は対象パスを除外して`SetDisplayConfig`を適用する。
6. 個別復元時は、現在接続中のパスとクリックされた対象パスだけを保存済み構成から再適用する。
7. 旧形式のバックアップ読み替えや旧仕様APIへのfallbackは実装しない。仕様変更時は開発環境の生成データを削除し、新仕様だけで検証する。

## 構成とCI

- `MainWindow.xaml` / `MainWindow.xaml.cs`: UIとモーダル
- `DisplayConfigurationService.cs`: CCD API、バックアップ、個別復元
- `UpdateChecker.cs`: GitHub Releasesの最新版確認
- `.github/workflows/build.yml`: Windows Releaseビルドと配布zip生成

バージョンは`Shibori.csproj`の`Version`、`AssemblyVersion`、`FileVersion`を同時に更新します。UIはモニター一覧を主役にし、常設の説明・準備完了表示・再読み込みボタンを増やしません。GitHub Actionsは`windows-latest`でrestore、Releaseビルド、win-x64 self-contained publish、`Shibori-win-x64.zip`のartifact生成を行います。
