# Microsoft Store Listing - Japanese (Japan)

Microsoft Partner Center にそのまま貼り付けられる、v3.8.0.0 向けのストア掲載情報です。

---

## Product Name
Clickra

## Description
[システム要件] 「ドキュメントを PDF に変換」機能は、Microsoft Office がインストールされている場合は Office を使用できます。Office がない場合は、Clickra から LibreOffice を無料のローカル変換エンジンとしてダウンロード、管理できます。

Clickra は、Windows 10 と Windows 11 向けの高速なネイティブ右クリックメニュー生産性ツールです。エクスプローラーのコンテキストメニューに自然に統合され、重いアプリを開かずに主要なファイル処理をすばやく実行できます。

主な機能:
 - ネイティブ ダッシュボード: PDF と Office 変換エンジンの状態をリアルタイムで確認できるダークテーマの画面。
 - Office から PDF: Word (.doc/.docx)、Excel (.xls/.xlsx)、PowerPoint (.ppt/.pptx) を高品質な PDF に静かに変換。
 - LibreOffice フォールバック: Microsoft Office がない環境でも、Clickra から LibreOffice を取得してローカル変換エンジンとして利用可能。
 - PDF 本地圧縮: C#/GDI+ エンジンを使用し、重複フォントの整理、コンテンツストリームの簡素化、低解像度画像の圧縮除外ロジックなど、高度な本地圧縮を実現。
 - PDF パスワード解除: パスワード付き PDF を右クリックメニューから復号し、パスワードなしの PDF を作成。
 - PDF 結合: 複数の PDF を選択してすばやく 1 つのファイルに結合。
 - ビジュアル PDF 分割: ページサムネイル付きのダイアログで PDF を分割。カスタムセグメント、1ページずつ分割、固定ページ数の3モード、インラインズームと「このページで分割」クイックアクション。
 - PDF 翻訳ハイフェネーション分割結合: ソース行で切断された技術識別子を自動的に再結合（例: "Cop-peliaSim" → "CoppeliaSim"）し、CJK フォントのスケーリングを調整して可読性を向上。
 - コンテキストメニューアイコン: すべての変換コマンドが Windows 11 およびクラシック右クリックメニューにローカライズされたアイコンを表示。
 - 画像から PDF: JPG/PNG/WebP 画像をまとめて PDF 化。
 - 画像結合: 複数の画像を縦方向につなげて 1 枚の長い画像に変換。

Clickra はプライバシーを重視します。Office から PDF、PDF 圧縮、PDF 結合、画像結合などの主要処理はローカルで実行されます。任意機能の PDF 翻訳を使用する場合のみ、テキストが安全な接続で Google Translate に送信されます。

🔗 Open Source on GitHub: https://github.com/Youchenjiang/Clickra

## What's new in this version
 - 画像形式変換: CLI、ネイティブダッシュボード、Fluent UI から画像を PNG、JPG、WebP、GIF、HEIC に変換できます。
 - WebP ランタイム同梱: WebP のエンコード/デコードを Clickra に同梱し、Microsoft Store の WebP コーデックに依存しなくなりました。
 - HEIC/HEIF 安全チェック: 必要な HEIF/HEIC エンコーダーまたはデコーダーがない場合は明確に通知して fail closed し、`.heic`、`.heif`、`.hif` 入力を一貫して検査します。
 - より安全な出力: 同一形式入力、未対応の変換先、出力パスの衝突を変換前に拒否し、明示した出力フォルダーを一貫して使用します。

## Product Features
*(Max 20, displayed as bullet points)*
 - PDF と Office エンジン状態を確認できるネイティブ ダッシュボード
 - Word/Excel/PPT から PDF へのワンクリック変換
 - Microsoft Office がない場合の LibreOffice フォールバック
 - 本地での PDF 圧縮・最適化に対応
 - 4段階の横型設定スライダーで PDF 圧縮レベルを直感的に調整可能
 - PDF パスワードのワンクリック解除
 - 進行状況ウィンドウ内の安全なパスワード入力
 - ビジュアル PDF 分割（ページサムネイルと3分割モード）
 - コンテキストメニューのすべての変換コマンドにアイコン表示
 - JPG/PNG/WebP 画像の一括 PDF 変換
 - 画像の縦方向結合
 - 複数 PDF の高速結合
 - NativeAOT シェル統合による応答性の高いコンテキストメニュー
 - WinUI 3 Fluent ダッシュボードと変換進行状況画面
 - 安全なローカル処理（任意の PDF 翻訳のみクラウド翻訳を使用）

---

### Supplemental Fields

## Short title
Clickra

## Voice title
Clickra

## Short description
Clickra は、Office から PDF、PDF パスワード解除、PDF 分割、PDF 結合、画像結合を高速に実行できるネイティブ右クリックメニュー ツールです。安全でローカル処理を重視します。

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
