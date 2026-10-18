using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DiffUtility
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private SplitContainer splitContainer;
        private RichTextBox txtLeft;
        private RichTextBox txtRight;
        private Label lblLeftHeader;
        private Label lblRightHeader;
        private ToolStripStatusLabel lblStatus;
        private ToolStripStatusLabel lblPairStatus;
        private NumericUpDown numThreshold;
        private CheckBox chkAutoSave;

        private string _leftFilePath = null;
        private string _rightFilePath = null;
        private string _lastDroppedFilePath = null;

        // Флаги готовности новой пары для автосохранения
        private bool _leftFileFresh = false;
        private bool _rightFileFresh = false;
        private bool _isProgrammaticChange = false;

        private string _lastSavedDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

public MainForm()
        {
            InitUI();
            
            // Берём иконку прямо из самого скомпилированного .exe
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            // this.Icon = GenerateAppIcon(); // Временно отключено
        }

        private void InitUI()
        {
            this.Text = "Diff Tool — Сравнение файлов";
            this.Width = 1150;
            this.Height = 720;
            this.StartPosition = FormStartPosition.CenterScreen;

            // Верхняя панель
            Panel topPanel = new Panel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(8) };

            Button btnCompare = new Button { Text = "Сравнить (Diff)", Left = 8, Top = 8, Width = 110, Height = 28 };
            btnCompare.Click += (s, e) => ExecuteDiff(isAutoSaveTrigger: false);

            Button btnSaveManual = new Button { Text = "Сохранить Diff...", Left = 125, Top = 8, Width = 130, Height = 28 };
            btnSaveManual.Click += (s, e) => SaveDiffManually();

            chkAutoSave = new CheckBox { Text = "Автосохранение", Left = 265, Top = 12, Checked = true, AutoSize = true };

            Label lblThreshold = new Label { Text = "Порог схожести (%):", Left = 390, Top = 14, AutoSize = true };
            numThreshold = new NumericUpDown { Left = 515, Top = 10, Width = 55, Minimum = 0, Maximum = 100, Value = 20 };

            topPanel.Controls.AddRange(new Control[] { btnCompare, btnSaveManual, chkAutoSave, lblThreshold, numThreshold });

            // Статусбар
            StatusStrip statusStrip = new StatusStrip();
            lblStatus = new ToolStripStatusLabel { Text = "Перетащите оба файла для автоматического сохранения.", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            lblPairStatus = new ToolStripStatusLabel { Text = "[Ожидание файлов: Левый ✗ | Правый ✗]", ForeColor = Color.DarkSlateGray };
            statusStrip.Items.AddRange(new ToolStripItem[] { lblStatus, lblPairStatus });

            // Разделитель
            splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6
            };

            // Левая панель
            Panel pnlLeft = new Panel { Dock = DockStyle.Fill };
            Panel pnlLeftTop = new Panel { Dock = DockStyle.Top, Height = 24 };
            Button btnClearLeft = new Button { Text = "✕", Dock = DockStyle.Right, Width = 26, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnClearLeft.FlatAppearance.BorderSize = 0;
            btnClearLeft.Click += (s, e) => ClearPane(isLeft: true);
            lblLeftHeader = new Label { Text = "Оригинал (Left): [Пусто]", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DarkSlateGray };
            pnlLeftTop.Controls.Add(lblLeftHeader);
            pnlLeftTop.Controls.Add(btnClearLeft);

            txtLeft = CreateEditor();
            pnlLeft.Controls.Add(txtLeft);
            pnlLeft.Controls.Add(pnlLeftTop);

            // Правая панель
            Panel pnlRight = new Panel { Dock = DockStyle.Fill };
            Panel pnlRightTop = new Panel { Dock = DockStyle.Top, Height = 24 };
            Button btnClearRight = new Button { Text = "✕", Dock = DockStyle.Right, Width = 26, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnClearRight.FlatAppearance.BorderSize = 0;
            btnClearRight.Click += (s, e) => ClearPane(isLeft: false);
            lblRightHeader = new Label { Text = "Изменения (Right): [Пусто]", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DarkSlateGray };
            pnlRightTop.Controls.Add(lblRightHeader);
            pnlRightTop.Controls.Add(btnClearRight);

            txtRight = CreateEditor();
            pnlRight.Controls.Add(txtRight);
            pnlRight.Controls.Add(pnlRightTop);

            SetupDragDrop(txtLeft, isLeft: true);
            SetupDragDrop(txtRight, isLeft: false);

            splitContainer.Panel1.Controls.Add(pnlLeft);
            splitContainer.Panel2.Controls.Add(pnlRight);

            this.Controls.Add(splitContainer);
            this.Controls.Add(topPanel);
            this.Controls.Add(statusStrip);

            this.Load += (s, e) => splitContainer.SplitterDistance = this.ClientSize.Width / 2;
        }

        private RichTextBox CreateEditor()
        {
            return new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10F, FontStyle.Regular),
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                AcceptsTab = true,
                DetectUrls = false
            };
        }

        private void SetupDragDrop(RichTextBox rtb, bool isLeft)
        {
            rtb.AllowDrop = true;
            rtb.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effect = DragDropEffects.Copy;
            };

            rtb.DragDrop += (s, e) =>
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    string filePath = files[0];
                    try
                    {
                        string content = File.ReadAllText(filePath, Encoding.UTF8);
                        
                        _isProgrammaticChange = true;
                        rtb.Text = content;
                        _isProgrammaticChange = false;

                        _lastDroppedFilePath = filePath;

                        if (isLeft)
                        {
                            _leftFilePath = filePath;
                            _leftFileFresh = true;
                            lblLeftHeader.Text = $"Оригинал: {Path.GetFileName(filePath)}";
                        }
                        else
                        {
                            _rightFilePath = filePath;
                            _rightFileFresh = true;
                            lblRightHeader.Text = $"Изменения: {Path.GetFileName(filePath)}";
                        }

                        UpdatePairStatusText();

                        // Проверяем: есть ли текст в соседней панели (не важно — файл или Ctrl+V)
                        bool otherHasText = isLeft 
                            ? !string.IsNullOrWhiteSpace(txtRight.Text) 
                            : !string.IsNullOrWhiteSpace(txtLeft.Text);

                        if (otherHasText)
                        {
                            ExecuteDiff(isAutoSaveTrigger: true);
                        }
                        else
                        {
                            lblStatus.Text = $"Загружен {(isLeft ? "левый" : "правый")} файл. Заполните вторую панель (файлом или Ctrl+V).";
                        }
                    }
                    catch (Exception ex)
                    {
                        _isProgrammaticChange = false;
                        MessageBox.Show($"Ошибка чтения файла: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            };

            // Отслеживаем только РЕАЛЬНЫЙ ручной ввод с клавиатуры
            rtb.KeyDown += (s, e) =>
            {
                if (isLeft)
                {
                    _leftFilePath = null;
                    _leftFileFresh = false;
                    lblLeftHeader.Text = "Оригинал: [Текст изменен вручную]";
                }
                else
                {
                    _rightFilePath = null;
                    _rightFileFresh = false;
                    lblRightHeader.Text = "Изменения: [Текст изменен вручную]";
                }
                UpdatePairStatusText();
            };

            // Проверяем полную очистку окон
            rtb.TextChanged += (s, e) =>
            {
                if (_isProgrammaticChange) return;

                if (string.IsNullOrWhiteSpace(rtb.Text))
                {
                    if (isLeft) { _leftFilePath = null; _leftFileFresh = false; lblLeftHeader.Text = "Оригинал (Left): [Пусто]"; }
                    else { _rightFilePath = null; _rightFileFresh = false; lblRightHeader.Text = "Изменения (Right): [Пусто]"; }
                }

                if (string.IsNullOrWhiteSpace(txtLeft.Text) && string.IsNullOrWhiteSpace(txtRight.Text))
                {
                    ResetPair();
                    lblStatus.Text = "Окна очищены. Ожидание новой пары файлов.";
                }
                else
                {
                    UpdatePairStatusText();
                }
            };
        }

	private void ClearPane(bool isLeft)
        {
            _isProgrammaticChange = true;
            if (isLeft)
            {
                txtLeft.Clear();
                _leftFilePath = null;
                _leftFileFresh = false;
                lblLeftHeader.Text = "Оригинал (Left): [Пусто]";
            }
            else
            {
                txtRight.Clear();
                _rightFilePath = null;
                _rightFileFresh = false;
                lblRightHeader.Text = "Изменения (Right): [Пусто]";
            }
            _isProgrammaticChange = false;

            if (string.IsNullOrWhiteSpace(txtLeft.Text) && string.IsNullOrWhiteSpace(txtRight.Text))
            {
                ResetPair();
                lblStatus.Text = "Окна очищены. Ожидание файлов.";
            }
            else
            {
                UpdatePairStatusText();
            }
        }

        private void UpdatePairStatusText()
        {
            string l = _leftFileFresh ? "✓" : "✗";
            string r = _rightFileFresh ? "✓" : "✗";
            lblPairStatus.Text = $"[Готовность пары: Левый {l} | Правый {r}]";
        }

        private void ExecuteDiff(bool isAutoSaveTrigger)
        {
            string[] leftLines = txtLeft.Lines;
            string[] rightLines = txtRight.Lines;

            if (leftLines.Length == 0 && rightLines.Length == 0)
            {
                lblStatus.Text = "Оба окна пусты.";
                return;
            }

            string leftName = !string.IsNullOrEmpty(_leftFilePath) ? Path.GetFileName(_leftFilePath) : "original";
            string rightName = !string.IsNullOrEmpty(_rightFilePath) ? Path.GetFileName(_rightFilePath) : "modified";

            var result = DiffEngine.BuildUnifiedDiff(leftLines, rightLines, leftName, rightName);
            double simPercent = Math.Round(result.Similarity * 100, 1);

            lblStatus.Text = $"Схожесть: {simPercent}%. Изменений: +{result.InsertedLines} / -{result.DeletedLines}.";

            if (result.InsertedLines == 0 && result.DeletedLines == 0)
            {
                lblStatus.Text += " Файлы идентичны.";
                ResetPair();
                return;
            }

            if (simPercent < (double)numThreshold.Value)
            {
                lblStatus.Text += $" [Схожесть ниже {numThreshold.Value}% — автосохранение отменено]";
                ResetPair();
                return;
            }

            // Автосохранение
            if (isAutoSaveTrigger && chkAutoSave.Checked && !string.IsNullOrEmpty(_lastDroppedFilePath))
            {
                string targetDir = Path.GetDirectoryName(_lastDroppedFilePath);
                string fileName = Path.GetFileName(_lastDroppedFilePath);
                string outPath = Path.Combine(targetDir, $"{fileName}.diff.txt");

                try
                {
                    File.WriteAllText(outPath, result.DiffContent, Encoding.UTF8);
                    lblStatus.Text += $" Сохранено по пути последнего файла: {Path.GetFileName(outPath)} ({new FileInfo(outPath).Length} байт).";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось сохранить diff: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    // Сбрасываем счетчик пары
                    ResetPair();
                }
            }
        }

        private void ResetPair()
        {
            _leftFileFresh = false;
            _rightFileFresh = false;
            UpdatePairStatusText();
        }

        private void SaveDiffManually()
        {
            string leftName = !string.IsNullOrEmpty(_leftFilePath) ? Path.GetFileName(_leftFilePath) : "original";
            string rightName = !string.IsNullOrEmpty(_rightFilePath) ? Path.GetFileName(_rightFilePath) : "modified";

            var result = DiffEngine.BuildUnifiedDiff(txtLeft.Lines, txtRight.Lines, leftName, rightName);

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "Diff Files (*.diff;*.patch;*.txt)|*.diff;*.patch;*.txt|All Files (*.*)|*.*";
                sfd.InitialDirectory = _lastSavedDirectory;

                string baseFile = _lastDroppedFilePath ?? _rightFilePath ?? _leftFilePath;
                sfd.FileName = !string.IsNullOrEmpty(baseFile)
                    ? $"{Path.GetFileName(baseFile)}.diff.txt"
                    : "changes.diff.txt";

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        File.WriteAllText(sfd.FileName, result.DiffContent, Encoding.UTF8);
                        _lastSavedDirectory = Path.GetDirectoryName(sfd.FileName);
                        lblStatus.Text = $"Дифф сохранён в: {sfd.FileName} ({new FileInfo(sfd.FileName).Length} байт)";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка записи файла: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        // Генерация неоновой иконки документа со сплиттером
        private Icon GenerateAppIcon()
        {
            int size = 64;
            using (Bitmap bmp = new Bitmap(size, size))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Темный фон формы документа
                var docRect = new Rectangle(8, 5, size - 16, size - 10);
                using (var fillBrush = new SolidBrush(Color.FromArgb(16, 16, 26)))
                using (var path = GetRoundedRectPath(docRect, 8))
                {
                    g.FillPath(fillBrush, path);

                    // Неоновый градиент (Голубой -> Фиолетовый)
                    using (var gradBrush = new LinearGradientBrush(new Point(0, 5), new Point(0, size - 5),
                        Color.FromArgb(0, 150, 255), Color.FromArgb(190, 0, 255)))
                    using (var borderPen = new Pen(gradBrush, 3f))
                    {
                        g.DrawPath(borderPen, path);

                        // Центральный пунктирный сплиттер
                        borderPen.DashStyle = DashStyle.Custom;
                        borderPen.DashPattern = new float[] { 3f, 2f };
                        g.DrawLine(borderPen, size / 2, 8, size / 2, size - 8);
                    }
                }

                // Строчки кода слева
                using (var leftLinePen = new Pen(Color.FromArgb(170, 210, 255), 2.2f))
                {
                    g.DrawLine(leftLinePen, 15, 17, 26, 17);
                    g.DrawLine(leftLinePen, 15, 24, 27, 24);
                    g.DrawLine(leftLinePen, 15, 31, 27, 31);
                    g.DrawLine(leftLinePen, 15, 38, 22, 38);
                }

                // Строчки кода справа
                using (var rightLinePen = new Pen(Color.FromArgb(215, 185, 255), 2.2f))
                {
                    g.DrawLine(rightLinePen, 37, 17, 49, 17);
                    g.DrawLine(rightLinePen, 37, 24, 49, 24);
                    g.DrawLine(rightLinePen, 37, 31, 49, 31);
                    g.DrawLine(rightLinePen, 37, 38, 49, 38);
                    g.DrawLine(rightLinePen, 37, 45, 45, 45);
                }

                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        private GraphicsPath GetRoundedRectPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2F;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public enum DiffType { Equal, Insert, Delete }

    public class DiffPiece
    {
        public DiffType Type;
        public string Text;
        public int OldIndex;
        public int NewIndex;
    }

    public class DiffResult
    {
        public double Similarity { get; set; }
        public string DiffContent { get; set; }
        public int InsertedLines { get; set; }
        public int DeletedLines { get; set; }
    }

    public static class DiffEngine
    {
        public static DiffResult BuildUnifiedDiff(string[] a, string[] b, string leftName, string rightName, int contextSize = 3)
        {
            int n = a.Length;
            int m = b.Length;

            int[,] lcs = new int[n + 1, m + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < m; j++)
                {
                    if (a[i] == b[j])
                        lcs[i + 1, j + 1] = lcs[i, j] + 1;
                    else
                        lcs[i + 1, j + 1] = Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                }
            }

            int commonCount = lcs[n, m];
            double similarity = (n + m == 0) ? 1.0 : (2.0 * commonCount) / (n + m);

            var pieces = new List<DiffPiece>();
            int x = n, y = m;

            while (x > 0 || y > 0)
            {
                if (x > 0 && y > 0 && a[x - 1] == b[y - 1])
                {
                    pieces.Add(new DiffPiece { Type = DiffType.Equal, Text = a[x - 1], OldIndex = x, NewIndex = y });
                    x--;
                    y--;
                }
                else if (y > 0 && (x == 0 || lcs[x, y - 1] >= lcs[x - 1, y]))
                {
                    pieces.Add(new DiffPiece { Type = DiffType.Insert, Text = b[y - 1], OldIndex = 0, NewIndex = y });
                    y--;
                }
                else if (x > 0 && (y == 0 || lcs[x, y - 1] < lcs[x - 1, y]))
                {
                    pieces.Add(new DiffPiece { Type = DiffType.Delete, Text = a[x - 1], OldIndex = x, NewIndex = 0 });
                    x--;
                }
            }

            pieces.Reverse();

            int insertedCount = pieces.Count(p => p.Type == DiffType.Insert);
            int deletedCount = pieces.Count(p => p.Type == DiffType.Delete);

            if (insertedCount == 0 && deletedCount == 0)
            {
                return new DiffResult
                {
                    Similarity = 1.0,
                    DiffContent = "Files are identical.",
                    InsertedLines = 0,
                    DeletedLines = 0
                };
            }

            List<string> sb = new List<string>();
            sb.Add($"--- {leftName}");
            sb.Add($"+++ {rightName}");

            List<int> changeIndices = new List<int>();
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i].Type != DiffType.Equal)
                    changeIndices.Add(i);
            }

            var hunks = new List<(int start, int end)>();
            if (changeIndices.Count > 0)
            {
                int currentStart = Math.Max(0, changeIndices[0] - contextSize);
                int currentEnd = Math.Min(pieces.Count - 1, changeIndices[0] + contextSize);

                for (int k = 1; k < changeIndices.Count; k++)
                {
                    int nextStart = Math.Max(0, changeIndices[k] - contextSize);
                    int nextEnd = Math.Min(pieces.Count - 1, changeIndices[k] + contextSize);

                    if (nextStart <= currentEnd + 1)
                    {
                        currentEnd = Math.Max(currentEnd, nextEnd);
                    }
                    else
                    {
                        hunks.Add((currentStart, currentEnd));
                        currentStart = nextStart;
                        currentEnd = nextEnd;
                    }
                }
                hunks.Add((currentStart, currentEnd));
            }

            foreach (var hunk in hunks)
            {
                var slice = pieces.Skip(hunk.start).Take(hunk.end - hunk.start + 1).ToList();

                int oldStart = slice.FirstOrDefault(p => p.OldIndex > 0)?.OldIndex ?? 1;
                int oldCount = slice.Count(p => p.Type == DiffType.Equal || p.Type == DiffType.Delete);

                int newStart = slice.FirstOrDefault(p => p.NewIndex > 0)?.NewIndex ?? 1;
                int newCount = slice.Count(p => p.Type == DiffType.Equal || p.Type == DiffType.Insert);

                sb.Add($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@");

                foreach (var piece in slice)
                {
                    switch (piece.Type)
                    {
                        case DiffType.Equal:
                            sb.Add(" " + piece.Text);
                            break;
                        case DiffType.Insert:
                            sb.Add("+" + piece.Text);
                            break;
                        case DiffType.Delete:
                            sb.Add("-" + piece.Text);
                            break;
                    }
                }
            }

            return new DiffResult
            {
                Similarity = similarity,
                DiffContent = string.Join(Environment.NewLine, sb),
                InsertedLines = insertedCount,
                DeletedLines = deletedCount
            };
        }
    }
}