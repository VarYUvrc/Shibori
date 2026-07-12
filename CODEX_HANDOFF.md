# MultiMonitorPauser-Shibori — Codex引継ぎコンテキスト

このファイルは、Windows 11上のCodexで本リポジトリの開発を再開するための作業コンテキストです。

## プロジェクトの目的

複数モニターとVRヘッドセットを同時に利用するとき、VR中に使用していない物理モニターをWindowsのデスクトップ構成から一時的に論理切断し、GPUの描画負荷や不要なデスクトップ合成負荷を下げる。

アプリ名は **MultiMonitorPauser-Shibori**。リポジトリ名、ソリューション名、実行ファイル名は短縮形の **Shibori** とする。

- リポジトリ: `VarYUvrc/Shibori`
- ソリューション: `Shibori.sln`
- 実行ファイル: `Shibori.exe`
- 対象: Windows 11

## 確定している設計方針

- C#によるWindowsネイティブ小型アプリ
- UIは現段階ではWPF
- 外部NuGetパッケージなし
- 管理者権限不要を目標にする
- 常駐アプリにはしない。操作を完了したら終了できる構成
- 設定ファイルやバックアップはユーザーが扱いやすいローカルファイルとする
- 表示構成の切替にはWindowsのCCD APIを使う
  - `QueryDisplayConfig`
  - `SetDisplayConfig`
  - 必要に応じて `DisplayConfigGetDeviceInfo`
- `ChangeDisplaySettingsEx`だけで済ませる実装に置き換えない。最終的にはCCD APIで論理的な表示構成を切り替える

## 現在の実装状態

現在の骨格は以下を実装済み。

- `Shibori.csproj`: .NET 8 Windows / WPF / Windows Forms
- `App.xaml` / `App.xaml.cs`
- `MainWindow.xaml` / `MainWindow.xaml.cs`
- `DisplayConfigurationService.cs`
  - 現在は `System.Windows.Forms.Screen.AllScreens` で接続モニターを列挙するだけ
- `README.md`
- `.gitignore`
- `.github/workflows/build.yml`

重要: 現在の「一時停止」ボタンはまだ実際の表示構成を変更せず、説明ダイアログを表示するだけ。実機の表示を切り替えるコードは未実装。

## 次に実装する順序

### 1. CCD APIのラッパー

`DisplayConfigurationService`を、次の責務に分けて拡張する。

1. 現在のパス・モード情報を`QueryDisplayConfig`で取得
2. 各モニターを安定した識別子で表示
   - 表示名だけを識別子にしない
   - adapter LUID、target id、monitor device path等を保持
3. 現在の構成をJSON等にシリアライズできるバックアップモデルを作る
4. 指定モニターをデスクトップ構成から外した構成を作る
5. `SetDisplayConfig`で適用する
6. 適用失敗時は元の構成を壊さず、エラーをユーザーに表示する

P/Invoke構造体はMicrosoftのWindows SDK定義と一致させる。特にLUID、DISPLAYCONFIG_PATH_INFO、DISPLAYCONFIG_MODE_INFO、DISPLAYCONFIG_TARGET_DEVICE_NAMEのサイズ・`Pack`・`CharSet`を慎重に確認する。

### 2. 復帰機能

- 適用前の表示構成をバックアップする
- `復帰`操作でバックアップ構成を再適用する
- バックアップが存在しない場合は何もしない
- 適用前後で接続モニター数と解像度を表示する
- 復帰処理は同じ実行ファイルに`--restore`等のコマンドライン引数を持たせてもよい

### 3. UIのMVP

- モニター一覧
- プライマリモニターを誤って選ばないための警告
- 複数選択
- 「選択したモニターを一時停止」
- 「元に戻す」
- 成功・失敗・復帰可能状態の明示
- 操作完了後に終了可能

## 安全性とUXの制約

- プライマリモニターを選択した場合は、既定で実行を拒否または強い確認を出す
- 最低1台の表示先を必ず残す
- 適用前に構成を保存する
- `SetDisplayConfig`失敗時に中途半端な状態を前提にしない。再取得して実際の状態を表示する
- VRヘッドセット自体を対象モニターとして誤認しないよう、現在の表示名・デバイスパスを見せる
- 外付けモニターの抜き差し、スリープ復帰、RDP、GPUドライバー差異を考慮する
- 管理者権限を要求しない。要求が必要なAPI設計になった場合は、実装を見直す

## 非目標（初期MVPでは作らない）

- 常駐トレイアプリ
- VRランタイムとの直接連携
- SteamVR / VRChat等のプロセス監視
- 自動ルール、スケジュール、ホットキー常駐
- クラウド同期、アカウント、テレメトリ
- インストーラー、MSIX、ストア配布

## 検証方針

Windows 11の実機で次を確認する。

1. モニター1台、2台、3台で列挙できる
2. 解像度・向き・主モニター情報が表示と一致する
3. 非プライマリモニターを1台切断できる
4. 複数モニターを切断できる
5. 復帰で元の構成に戻る
6. アプリを強制終了しても、バックアップから復帰できる
7. モニター抜き差し後にクラッシュしない
8. `dotnet build Shibori.sln --configuration Release` がWindows CIで成功する

## Codexへの最初の依頼

まずリポジトリ全体を読み、`CODEX_HANDOFF.md`と既存コードの差分を確認する。その後、CCD APIのP/Invokeと「現在構成の読み取り専用表示」を先に実装し、ビルドと実機確認を行う。表示構成の変更処理は、読み取り結果と識別子が正しいことを確認してから追加する。

変更後は、何を実機で確認すべきかを日本語で短く報告する。

