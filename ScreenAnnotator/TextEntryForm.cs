namespace ScreenAnnotator;

public sealed class TextEntryForm : Form
{
    private readonly TextBox inputBox = new();
    private readonly TextBox memoBox = new();
    private readonly Label saveState = new();

    public string Value => inputBox.Text.Trim();

    public TextEntryForm()
    {
        Text = "输入文字 · 备案表";
        Width = 900;
        Height = 560;
        MinimumSize = new Size(720, 420);
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        KeyPreview = true;

        var tip = new Label
        {
            Text = "右侧保存长期备用文字。选中文字按 Ctrl+C，再到左侧按 Ctrl+V；左侧按 Enter 确认放置，Shift+Enter 换行。",
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(12, 11, 0, 0),
            BackColor = Color.FromArgb(240, 246, 255),
            ForeColor = Color.FromArgb(30, 70, 120)
        };

        inputBox.Multiline = true;
        inputBox.AcceptsReturn = true;
        inputBox.ScrollBars = ScrollBars.Vertical;
        inputBox.Font = new Font("Microsoft YaHei UI", 14);
        inputBox.Dock = DockStyle.Fill;
        inputBox.KeyDown += InputBoxKeyDown;

        memoBox.Multiline = true;
        memoBox.AcceptsReturn = true;
        memoBox.AcceptsTab = true;
        memoBox.ScrollBars = ScrollBars.Both;
        memoBox.WordWrap = true;
        memoBox.Font = new Font("Microsoft YaHei UI", 11);
        memoBox.Dock = DockStyle.Fill;
        memoBox.Text = TextMemoStorage.Load();
        memoBox.TextChanged += (_, _) =>
        {
            TextMemoStorage.Save(memoBox.Text);
            saveState.Text = "备案内容已自动保存";
        };

        var inputGroup = new GroupBox
        {
            Text = "本次要放到屏幕上的文字",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        inputGroup.Controls.Add(inputBox);

        var memoGroup = new GroupBox
        {
            Text = "长期文字备案表（不删除就一直保留）",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        memoGroup.Controls.Add(memoBox);

        var split = new SplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(inputGroup);
        split.Panel2.Controls.Add(memoGroup);

        var pasteSelection = new Button
        {
            Text = "将右侧选中文字放入左侧",
            Width = 205,
            Height = 38,
            Left = 12,
            Top = 7
        };
        pasteSelection.Click += (_, _) => PasteMemoSelection();

        var confirm = new Button
        {
            Text = "确定放置  Enter",
            Width = 150,
            Height = 38,
            Left = 230,
            Top = 7
        };
        confirm.Click += (_, _) => ConfirmInput();

        var cancel = new Button
        {
            Text = "取消  Esc",
            Width = 110,
            Height = 38,
            Left = 392,
            Top = 7,
            DialogResult = DialogResult.Cancel
        };

        saveState.Text = "备案内容自动保存；清空后也会保存为空白";
        saveState.AutoSize = false;
        saveState.Width = 320;
        saveState.Height = 38;
        saveState.Left = 515;
        saveState.Top = 10;
        saveState.TextAlign = ContentAlignment.MiddleLeft;
        saveState.ForeColor = Color.DimGray;

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 54 };
        buttons.Controls.AddRange(new Control[] { pasteSelection, confirm, cancel, saveState });

        Controls.Add(split);
        Controls.Add(buttons);
        Controls.Add(tip);
        CancelButton = cancel;
        Shown += (_, _) =>
        {
            split.Panel1MinSize = 220;
            split.Panel2MinSize = 260;
            var available = Math.Max(500, split.ClientSize.Width - split.SplitterWidth);
            split.SplitterDistance = Math.Clamp((int)(available * 0.44), split.Panel1MinSize, available - split.Panel2MinSize);
            inputBox.Focus();
        };
        FormClosing += (_, _) => TextMemoStorage.Save(memoBox.Text);
    }

    private void InputBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || e.Shift) return;
        e.SuppressKeyPress = true;
        ConfirmInput();
    }

    private void ConfirmInput()
    {
        TextMemoStorage.Save(memoBox.Text);
        if (string.IsNullOrWhiteSpace(inputBox.Text))
        {
            MessageBox.Show(this, "请先在左侧输入或粘贴要放置的文字。", "屏幕笔", MessageBoxButtons.OK, MessageBoxIcon.Information);
            inputBox.Focus();
            return;
        }
        DialogResult = DialogResult.OK;
    }

    private void PasteMemoSelection()
    {
        var selected = memoBox.SelectedText;
        if (string.IsNullOrEmpty(selected))
        {
            MessageBox.Show(this, "请先在右侧备案表中选中一个词组或句子。", "文字备案表", MessageBoxButtons.OK, MessageBoxIcon.Information);
            memoBox.Focus();
            return;
        }
        inputBox.SelectedText = selected;
        inputBox.Focus();
        inputBox.SelectionStart = inputBox.TextLength;
    }
}

internal static class TextMemoStorage
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScreenAnnotator",
        "text-memo.txt");

    public static string Load()
    {
        try { return File.Exists(FilePath) ? File.ReadAllText(FilePath) : string.Empty; }
        catch { return string.Empty; }
    }

    public static void Save(string value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, value);
        }
        catch { }
    }
}
