# Shibori 開発ドキュメント（SSoT）

このファイルを開発・ビルド・テスト・リリース・復旧仕様の唯一の正本として扱います。実装を変更したら、必ずここも更新してください。

## 前提

- Windows 11 x64
- .NET 8 SDK（Windows Desktop SDK / WPFを含む）
- Git
- 追加のNuGetパッケージは不要

`dotnet --info` と `dotnet --list-sdks` でSDKを確認します。

## リポジトリ構成

- `src/Shibori/`: WPFアプリ本体とアセット
- `scripts/`: ローカルビルド、診断、アイコン生成
- `.github/workflows/`: CIビルドとRelease作成
- `docs/DEVELOPMENT.md`: このドキュメント
- `docs/RELEASE.md`: 利用者向けのRelease本文・SmartScreen・スタートアップ案内

ビルド成果物は `src/Shibori/bin/` または `artifacts/` に出力します。`bin/` と `obj/` はGit管理対象外です。

## 最新版をローカルで作成・起動する（一コマンド）

リポジトリのルートで次を実行します。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-local.ps1
```

このコマンドは、古い成果物を削除し、復元・Release publishを行い、`artifacts/latest/Shibori.exe` を起動します。以後の確認は、以前の `bin` 配下のexeではなく、このexeを使用してください。

起動せずにビルドだけ行う場合:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-local.ps1 -NoLaunch
```

通常のコンパイルだけを行う場合:

```powershell
dotnet restore Shibori.sln
dotnet build Shibori.sln --configuration Release --no-restore
```

## 日本語ユーザー名・日本語パスへの対応

ユーザーフォルダー名に日本語（ひらがな・カタカナ・漢字）が含まれる環境を前提にします。スクリプトでは固定の `C:\Users\英数字` を使わず、リポジトリ位置はスクリプト自身から、ログ・バックアップ位置はWindowsの環境APIから解決します。

アプリのログと表示構成バックアップは次の場所です。

- `%LOCALAPPDATA%\Shibori\logs\`
- `%LOCALAPPDATA%\Shibori\display-backup.json`

パスを扱うPowerShellコマンドでは、必ず `-LiteralPath` または引用符を使います。日本語パスを含む場所から実行しても、カレントディレクトリを前提にした固定パスを追加しないでください。

## 診断とテスト

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-diagnostics.ps1
artifacts\latest\Shibori.exe --self-test
artifacts\latest\Shibori.exe --partial-test
```

テストは実際の表示構成を変更します。2台以上のモニターを接続し、操作中にアプリやケーブルを切断しないでください。通常ログは1日1MBまで、14日を超えたログは削除します。診断ログは2MBを上限とします。

## 表示構成と復旧の不変条件

- チェックありは接続中、チェックを外すと一時停止です。
- メインモニターも停止できますが、最低1台は接続状態を維持します。
- 接続中モニターの緑のトグルでメインを選択します。メインは常に1台だけで、現在のメインをOFFにはできません。
- 一時停止前のCCD表示構成を `%LOCALAPPDATA%\Shibori\display-backup.json` に保存します。
- 部分復旧はCCDパス識別子またはモニターのデバイスパスで対象だけを照合し、他の停止モニターを復旧しません。
- すべての元のモニターを復旧したときだけバックアップを削除します。
- 旧バックアップ形式のfallbackは実装しません。スキーマを変更した場合は開発環境のバックアップを削除します。

再起動後にモニターが見つからない場合は、Shiboriを起動してInfo内の復旧手順を確認し、Windowsの表示設定で「検出」を行ってから復旧します。スタートアップ用ショートカットの末尾に `--startup` を付けると、更新モーダルを表示せず起動できます。現状はタスクトレイ常駐ではなく通常ウィンドウです。

## バージョンとGitHub Release

バージョン形式は `YYYY.MM.DD.NN` です。現在のバージョンは `2026.07.12.04` です。

Releaseを作成する手順:

1. `src/Shibori/Shibori.csproj` の `<Version>`、`<AssemblyVersion>`、`<FileVersion>`、`<InformationalVersion>` を同じ更新番号に変更する。
2. 変更をコミットして `main` にpushする。
3. 次のコマンドでタグをpushする。

```powershell
git tag v2026.07.12.01
git push origin v2026.07.12.01
```

`v*` タグのpushをGitHub Actionsが検知し、self-contained x64版をビルドして `Shibori-win-x64.zip` を添付したReleaseを自動作成します。Release本文も非エンジニア向けの日本語でActionsが生成します。Actions実行にはリポジトリのActionsが有効で、`GITHUB_TOKEN` にContents write権限が必要です。

## アイコンとライセンス

SVGの正本は `src/Shibori/Assets/shibori.svg`、Windows用ICOは `src/Shibori/Assets/shibori.ico` です。ICOは次で再生成します。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\generate-icon.ps1
```

ShiboriはApache License 2.0です。.NET 8、WPF、Windows SDKを使用しています。
