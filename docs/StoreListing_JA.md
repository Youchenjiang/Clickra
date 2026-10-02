# Microsoft Store Listing - Japanese (Japan)

Microsoft Partner Center にそのまま貼り付けられる、v3.12.0.0 向けのストア掲載情報です。

---

## Product Name
Clickra

## Description
[システム要件] 「ドキュメントを PDF に変換」機能は、Microsoft Office がインストールされている場合は Office を使用できます。Office がない場合は、Clickra から LibreOffice を無料のローカル変換エンジンとしてダウンロード、管理できます。

Clickra は、Windows 10 と Windows 11 向けの高速なネイティブ右クリックメニュー生産性ツールです。エクスプローラーのコンテキストメニューに自然に統合され、重いアプリを開かずに主要なファイル処理をすばやく実行できます。

主な機能:
 - ネイティブ ダッシュボード: PDF と Office 変換エンジンの状態をリアルタイムで確認できるダークテーマの画面。
 - Office から PDF: Word (.doc/.docx)、Excel (.xls/.xlsx)、PowerPoint (.ppt/.pptx) を高品質な PDF に静かに変換。
 - LibreOffice フォールバック: Microsoft Office がない環境でも、Clickra から LibreOffice を取得するか、検証済みの既存システム インストールを明示的に Clickra 管理へ移行してローカル変換エンジンとして利用可能。
 - PDF 本地圧縮: C#/GDI+ エンジンを使用し、重複フォントの整理、コンテンツストリームの簡素化、低解像度画像の圧縮除外ロジックなど、高度な本地圧縮を実現。
 - PDF パスワード解除: パスワード付き PDF を右クリックメニューから復号し、パスワードなしの PDF を作成。
 - PDF 結合: 複数の PDF を選択してすばやく 1 つのファイルに結合。
 - ビジュアル PDF 分割: ページサムネイル付きのダイアログで PDF を分割。カスタムセグメント、1ページずつ分割、固定ページ数の3モード、インラインズームと「このページで分割」クイックアクション。
 - PDF 翻訳ハイフェネーション分割結合: ソース行で切断された技術識別子を自動的に再結合（例: "Cop-peliaSim" → "CoppeliaSim"）し、CJK フォントのスケーリングを調整して可読性を向上。
 - コンテキストメニューアイコン: すべての変換コマンドが Windows 11 およびクラシック右クリックメニューにローカライズされたアイコンを表示。
 - 一時停止タスクの保持: 変換を安全に一時停止し、保持期間を設定して、削除前に残り時間と期限警告を確認できます。
 - 進行状況とトレイ操作: 最小化した変換をシステムトレイから復元またはキャンセルでき、進行状況と分割 UI はローカライズ済みベクター操作に統一されます。
 - 画像から PDF: JPG/PNG/WebP 画像をまとめて PDF 化。
 - 画像結合: 複数の画像を縦方向につなげて 1 枚の長い画像に変換。

Clickra はプライバシーを重視します。Office から PDF、PDF 圧縮、PDF 結合、画像結合などの主要処理はローカルで実行されます。任意機能の PDF 翻訳を使用する場合のみ、テキストが安全な接続で Google Translate に送信されます。

🔗 Open Source on GitHub: https://github.com/Youchenjiang/Clickra

## What's new in this version
 - 設定プラットフォームの一元化: 共通ディスクリプターが設定名、エディター種別、選択肢、既定値、数値範囲を Core registry で一元管理します。
 - 動的な設定コントロール: Native Dashboard と Fluent Settings が同じ registry から対応コントロールを生成し、設定追加時の UI 差分を減らします。
 - 設定のリアルタイム同期: 実行中の一方の Clickra UI で変更した設定が、再起動なしで他の実行中 UI にも反映されます。
 - より安全な数値設定: スライダーと数値入力で共通の範囲定義を使用し、安全なステップ処理で登録範囲外への値の逸脱を防ぎます。

## Product Features
*(Max 20, displayed as bullet points)*
 - PDF と Office エンジン状態を確認できるネイティブ ダッシュボード
 - Word/Excel/PPT から PDF へのワンクリック変換
 - Microsoft Office がない場合の LibreOffice フォールバックと検証済み既存インストールの管理
 - 本地での PDF 圧縮・最適化に対応
 - Small、Balanced、High Quality の3プリセットで PDF 圧縮レベルを調整
 - PDF パスワードのワンクリック解除
 - 進行状況ウィンドウ内の安全なパスワード入力
 - ビジュアル PDF 分割（ページサムネイルと3分割モード）
 - コンテキストメニューのすべての変換コマンドにアイコン表示
 - JPG/PNG/WebP 画像の一括 PDF 変換
 - PNG、JPG、WebP、GIF、HEIC への画像形式変換
 - 画像の縦方向結合
 - 複数 PDF の高速結合
 - NativeAOT シェル統合による応答性の高いコンテキストメニュー
 - ネイティブ ダッシュボードと変換進行状況画面
 - 最小化した変換のシステムトレイ復元／キャンセル
 - 一時停止した変換の保持期間、残り時間、期限切れアラート
 - LibreOffice の検証済み管理と fail-closed アンインストール保護
 - 安全なローカル処理（任意の PDF 翻訳のみクラウド翻訳を使用）

---

### Supplemental Fields

## Short title
Clickra

## Voice title
Clickra

## Short description
Clickra は Office・PDF・画像処理に加え、ローカライズ済み進行状況操作、トレイ復元／キャンセル、一時停止タスク保持を備えた高速ネイティブ右クリックメニューツールです。ローカル処理を重視します。

---

### Other Information

## Keywords
*(Max 7)*
 - Context Menu
 - PDF Merge
 - PDF Decrypt
 - PPT to PDF
 - Word to PDF
 - Excel to PDF
 - Image to PDF

## Copyright and trademark info
© 2026 Youchen Jiang. All rights reserved.
