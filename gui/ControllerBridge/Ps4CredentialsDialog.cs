namespace ControllerBridge;

internal sealed class Ps4CredentialsDialog : Form
{
    private readonly TextBox privateKey = FileBox(), serial = FileBox(), signature = FileBox();
    private readonly Label status = Ui.Label("", 10);
    private readonly TableLayoutPanel files;
    private readonly Button validate, clear, close;
    private bool checking;

    private static TextBox FileBox() => new() { Dock = DockStyle.Fill, ReadOnly = true, PlaceholderText = L.T("未選択", "No file selected"), Margin = new(4, 9, 4, 4) };

    internal Ps4CredentialsDialog()
    {
        using IDisposable layout = Ui.Form(this, L.T("PS4認証ファイル（準備）", "PS4 credential files (preparation)"), new(720, 570));
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 5 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.AutoSize));
        Label intro = Ui.Label(L.T("GP2040-CEと同じ3種類のファイルを指定できます。現在はファイル形式の確認のみ対応しています。",
            "Select the same three file types used by GP2040-CE. This version supports file-format checking only."), 10, true);
        intro.MaximumSize = new(660, 0); root.Controls.Add(intro, 0, 0);
        files = new() { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 6, Margin = new(0, 14, 0, 12) };
        files.ColumnStyles.Add(new(SizeType.Percent, 100)); files.ColumnStyles.Add(new(SizeType.AutoSize));
        AddFileRow(0, L.T("秘密鍵（PEM / RSA 2048ビット）", "Private key (PEM / RSA 2048-bit)"), privateKey,
            "PEM files (*.pem)|*.pem|All files (*.*)|*.*");
        AddFileRow(2, L.T("シリアル番号（16バイト分の16進文字列・32桁）", "Serial number (16 bytes as 32 hexadecimal digits)"), serial,
            "All files (*.*)|*.*");
        AddFileRow(4, L.T("機器署名（256バイトのバイナリ）", "Device signature (256-byte binary)"), signature,
            "All files (*.*)|*.*");
        root.Controls.Add(files, 0, 1);
        Label scope = Ui.Label(L.T("デバイスへの保存・PS4認証・PS4向け出力は未対応です。\n形式が正しくても、PS4で有効な認証情報であることや3ファイルの対応関係は確認できません。\nファイルの内容は保存・送信しません。画面を閉じると選択が解除されます。",
            "Device storage, PS4 authentication and PS4 output are not available.\nA format check does not verify console acceptance or whether the three files belong together.\nFile contents are not saved or transmitted. Closing this window clears the selection."), 9);
        scope.ForeColor = Ui.Muted; scope.MaximumSize = new(660, 0); root.Controls.Add(scope, 0, 2);
        status.MaximumSize = new(660, 0); status.ForeColor = Ui.Muted; status.Margin = new(4, 14, 4, 8); root.Controls.Add(status, 0, 3);
        FlowLayoutPanel footer = new() { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        close = Ui.Button(L.T("閉じる", "Close"), (_, _) => Close());
        validate = Ui.Button(L.T("形式を確認", "Check file formats"), async (_, _) => await CheckFiles(), true);
        clear = Ui.Button(L.T("選択を解除", "Clear selection"), (_, _) => { privateKey.Clear(); serial.Clear(); signature.Clear(); });
        footer.Controls.Add(close); footer.Controls.Add(validate); footer.Controls.Add(clear); root.Controls.Add(footer, 0, 4);
        AcceptButton = validate; CancelButton = close;
        foreach (TextBox field in new[] { privateKey, serial, signature }) field.TextChanged += (_, _) => ResetStatus();
        ResetStatus();
        FormClosing += (_, e) => { if (checking) e.Cancel = true; };
        Controls.Add(root); Ui.ThemeInputs(this);
    }

    private void AddFileRow(int row, string title, TextBox field, string filter)
    {
        files.RowStyles.Add(new(SizeType.AutoSize)); files.RowStyles.Add(new(SizeType.AutoSize));
        Label label = Ui.Label(title); label.Margin = new(4, row == 0 ? 0 : 12, 4, 2);
        files.Controls.Add(label, 0, row); files.SetColumnSpan(label, 2); field.AccessibleName = title;
        files.Controls.Add(field, 0, row + 1);
        Button browse = Ui.Button(L.T("参照…", "Browse…"), (_, _) =>
        {
            using OpenFileDialog dialog = new() { Title = title, Filter = filter, CheckFileExists = true,
                Multiselect = false, RestoreDirectory = true, AddToRecent = false };
            if (dialog.ShowDialog(this) == DialogResult.OK) field.Text = dialog.FileName;
        });
        browse.AccessibleName = L.F("{0}を選択", "Choose {0}", title); files.Controls.Add(browse, 1, row + 1);
    }

    private void ResetStatus()
    {
        status.Text = L.T("3ファイルを選択して「形式を確認」を押してください。", "Select all three files, then choose Check file formats.");
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool complete = privateKey.TextLength != 0 && serial.TextLength != 0 && signature.TextLength != 0;
        validate.Enabled = !checking && complete;
        clear.Enabled = !checking && (privateKey.TextLength != 0 || serial.TextLength != 0 || signature.TextLength != 0);
        files.Enabled = close.Enabled = !checking;
    }

    private async Task CheckFiles()
    {
        if (checking || !validate.Enabled) return;
        string keyPath = privateKey.Text, serialPath = serial.Text, signaturePath = signature.Text;
        checking = true; UpdateButtons(); UseWaitCursor = true;
        status.Text = L.T("ファイル形式を確認しています…", "Checking file formats…");
        try
        {
            await Task.Run(() => Ps4CredentialFiles.Validate(keyPath, serialPath, signaturePath));
            status.Text = L.T("3ファイルの形式を確認しました。デバイスには保存していません。PS4接続は未対応です。",
                "All three file formats passed. Nothing was saved to the device. PS4 connection is not available.");
        }
        catch (InvalidDataException ex) { status.Text = ex.Message; }
        finally { checking = false; UseWaitCursor = false; UpdateButtons(); }
    }
}
