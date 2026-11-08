# Sample slot configuration / サンプルスロット設定

`ControllerBridge-Config-Sample_v0.22.0.json` is a complete 16-slot backup for ControllerBridge firmware 0.22.0 (JSON v5 / bank v8). It is included unchanged in the release ZIP, in this `samples/` folder.

## 日本語

1. デバイスを接続し、未保存の編集を保存または破棄します。
2. **全スロットをバックアップ**で現在の設定を別のファイルへ保存します。
3. **全スロットを復帰**から、このフォルダーのサンプルJSONを選びます。
4. 確認画面の内容を確認して復帰を実行します。

復帰すると、空きスロットを含む**全16スロットがサンプルの内容に置き換わります**。個別プロファイルの「Import」用ファイルではありません。機器の識別情報や割り当てはサンプルに登録された機器用です。使用する機器に合わせて内容を確認・調整してください。共通設定はこのファイルに含まれません。

## English

1. Connect the device and save or discard pending edits.
2. Use **Back up all slots** to save the current configuration to a separate file.
3. Choose **Restore all slots** and select the sample JSON in this folder.
4. Review the confirmation and restore the sample.

Restoring **replaces all 16 slots, including unused slots**, with the sample bank. This is not an individual-profile Import file. Controller identities and mappings belong to the devices registered in the sample; review and adjust them for your hardware. Shared settings is not included.
