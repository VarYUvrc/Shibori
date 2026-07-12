# Shibori リリース案内

このファイルはGitHub Release本文の正本です。Release workflowはこのファイルを読み込んで本文を作成します。

## ダウンロードと起動

`Shibori-win-x64.zip`をダウンロードして展開し、`Shibori.exe`を実行してください。追加の.NETランタイムは不要です。

## SmartScreenの警告について

現在のShiboriはコード署名証明書を付けていません。そのため、初回起動時に「WindowsによってPCが保護されました」と表示されることがあります。

1. 「詳細情報」をクリックします。
2. アプリ名が `Shibori.exe` で、ダウンロード元がこのGitHub Releaseであることを確認します。
3. 「実行」をクリックします。

将来的にはコード署名を導入します。ただし、署名後も新しいファイルはSmartScreenの評価が蓄積されるまで警告が表示される場合があります。Microsoft Store配布はMicrosoftが署名するため、最も警告を減らしやすい方法です。詳細は[MicrosoftのSmartScreen説明](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)を参照してください。

## スタートアップ登録

Windowsの「設定 > アプリ > スタートアップ」からShiboriを登録してください。ショートカットで登録する場合は、リンク先の末尾に `--startup` を追加してください。現在はタスクトレイ常駐ではなく通常のウィンドウです。

## 更新内容

このReleaseの変更内容はコミット履歴を参照してください。
