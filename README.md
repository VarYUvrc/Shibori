# Shibori - MultiMonitorPauser

マルチディスプレイ環境におけるPCモニターの接続を簡単に一時停止・再接続する、小さなWindows11ネイティブアプリです。

おもにVRChatなどの高負荷VRゲームプレイ時に、不要な高解像度モニターの接続を停止してGPUリソースの消費を抑え、ゲームのパフォーマンスを最大化する用途を想定しています。


## ダウンロード

[GitHub Releases](https://github.com/VarYUvrc/Shibori/releases) から `Shibori-win-x64.zip` をダウンロードし、zipファイルを展開したあと、 `Shibori.exe` を実行してください。

Windows11 22H2以降のバージョンで標準搭載されている機能で動作します。

初回起動時に「WindowsによってPCが保護されました」と表示される場合は、「詳細情報」をクリックしてから「実行」を選択してください。

## 使い方

- 緑のトグル: モニターの接続状態を変更（最低限1つのモニターは残ります）
- チェック: メインモニターに設定（1つのモニターのみ）

## スタートアップ登録

Windowsの「設定 > アプリ > スタートアップ」から登録できます。

## 動作確認済の環境

- Windows11 25H2

## ライセンス

Apache License 2.0。詳細は [LICENSE](LICENSE) を参照してください。

開発者向け情報は [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md) を参照してください。
