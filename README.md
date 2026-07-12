# MultiMonitorPauser-Shibori

Windows 11向けの軽量ユーティリティです。複数モニターやVRヘッドセットを利用するとき、未使用モニターをWindowsのデスクトップ構成から一時的に切断し、GPU負荷を抑えます。

## MVP

- 管理者権限不要
- 外部パッケージ不要
- 常駐せず、操作後に終了
- Windows CCD API（QueryDisplayConfig / SetDisplayConfig）を使った表示構成の切替
- 現在の構成を保存し、復帰操作で元に戻す

## 開発

Windows上で .NET 8 SDK を使い、`dotnet build Shibori.csproj` を実行してください。

実機の表示構成を変更する機能は次の段階で追加します。まずはモニター列挙とUIの動作を確認できる骨格を提供します。

