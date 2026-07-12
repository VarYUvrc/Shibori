# Shibori 開発SSoT

このファイルを開発者・コーディングエージェント向けの唯一の開発ドキュメントとする。実装を変更したら、同じ変更でこのファイルも更新する。詳細を別ファイルへ分散させない。

## 目的と仕様

Shiboriは、Windows 11でVR利用中などに使わないモニターを一時停止し、必要なモニターだけをデスクトップに残すWPFネイティブアプリである。

- チェックあり: 接続中
- チェックなし: 一時停止中
- メインモニターは停止不可
- 停止中のモニターは再起動・再読み込み後も一覧に残る
- 停止中のモニターを個別に復元できる
- 全モニターが復元されるとバックアップを削除する

利用者向けの説明・ダウンロード方法はルートの`README.md`に置く。

## 技術構成

- Windows 11 / .NET 8 / WPF
- `MainWindow.xaml` / `MainWindow.xaml.cs`: UI、個別操作、情報・エラー・更新モーダル
- `DisplayConfigurationService.cs`: CCD API、安定パスID、バックアップ、切断、個別復元
- `UpdateChecker.cs`: GitHub Releases APIの最新版確認
- `.github/workflows/build.yml`: CIとwin-x64 self-contained配布zip生成
- `scripts/run-diagnostics.ps1`: 診断ログ取得

## 表示構成の不変条件

1. `QueryDisplayConfig(QDC_ALL_PATHS | QDC_VIRTUAL_MODE_AWARE)`でパスとモードを取得する。
2. `DISPLAY1`などのGDI名は再採番されるため、永続的な識別子に使わない。
3. Adapter LUID + Source IDから安定IDを作る。
4. `DisplayConfigGetDeviceInfo`でGDI名とモニター識別名を取得する。
5. 初回停止時に切断前のCCDパス・モード配列を`%LOCALAPPDATA%\Shibori\display-backup.json`へ保存する。
6. 停止時は対象パスを除外して`SetDisplayConfig`を適用する。
7. 個別復元時は、現在接続中のパスとクリックされた対象パスだけを保存済み構成から再適用する。
8. 旧形式のバックアップ読み替えや旧仕様APIへのfallbackは実装しない。仕様変更時は開発環境の生成データを削除し、新仕様だけで検証する。

## UIと更新

- UIはモニター一覧を主役にし、説明文・準備完了表示・常設ボタンを増やさない。
- 情報アイコンはホバーでバージョンを表示し、クリック時にコピー可能な詳細を表示する。
- エラー、更新通知、バージョン情報は選択・コピー可能なモーダルにする。
- 起動時にGitHub Releasesの最新版を確認し、新しい安定版がある場合だけ更新を提案する。自動インストールは行わない。

## 開発・配布

```powershell
dotnet restore Shibori.sln
dotnet build Shibori.sln --configuration Release
dotnet publish Shibori.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

GitHub Actionsは`windows-latest`でReleaseビルドと`Shibori-win-x64.zip`のartifact生成を行う。バージョンは`Shibori.csproj`の`Version`、`AssemblyVersion`、`FileVersion`を同時に更新する。

## 検証

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-diagnostics.ps1
& .\bin\Release\net8.0-windows\Shibori.exe --self-test
& .\bin\Release\net8.0-windows\Shibori.exe --partial-test
```

実機の複数モニター環境で、1台停止、2台停止、1台だけ復元、全復元の順に確認する。ログは`%LOCALAPPDATA%\Shibori\shibori.log`に出力される。
