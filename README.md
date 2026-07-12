# Shibori

Windows 11向けの、モニターを一時停止・復旧する小さなネイティブアプリです。

## ダウンロード

[GitHub Releases](https://github.com/VarYUvrc/Shibori/releases) から `Shibori-win-x64.zip` をダウンロードし、展開した `Shibori.exe` を実行してください。追加の.NETランタイムは不要です。

初回起動時に「WindowsによってPCが保護されました」と表示される場合は、「詳細情報」をクリックしてから「実行」を選択してください。現在コード署名を行っていないため、新しいアプリではこの確認が表示されることがあります。ダウンロード元が上記GitHub Releasesであることを確認してから操作してください。

## 使い方

- チェックあり: モニターは接続中
- チェックを外す: モニターを一時停止
- 一時停止中のモニターをチェック: 復旧
- 緑のトグル: メインモニターに設定

## スタートアップ登録

Windowsの「設定 > アプリ > スタートアップ」から登録できます。ショートカットで登録する場合は、リンク先の末尾に `--startup` を追加すると、起動時に更新ダイアログを表示しません。現在はタスクトレイ常駐ではなく通常のウィンドウです。

## 動作環境

- Windows 11
- x64 PC
- 複数モニター環境

## ライセンス

Apache License 2.0。詳細は [LICENSE](LICENSE) を参照してください。

開発者向け情報は [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md)、配布時の注意事項は [`docs/RELEASE.md`](docs/RELEASE.md) を参照してください。
