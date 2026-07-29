using TrackForge.Core;

namespace TrackForge.UI;

/// <summary>320px dock listing every queued and finished job.</summary>
public sealed class JobsPanel : Panel
{
    private readonly ForgeService _forge;
    private readonly FlowLayoutPanel _list = new();
    private readonly Dictionary<int, JobRow> _rows = new();

    public JobsPanel(ForgeService forge)
    {
        _forge = forge;
        Dock = DockStyle.Right;
        Width = Theme.JobsDockWidth;
        Visible = false;
        BackColor = Theme.ChromePanel;
        Padding = new Padding(1, 0, 0, 0);

        var head = new Panel { Dock = DockStyle.Top, Height = Theme.TopBarHeight, BackColor = Theme.ChromePanel };
        head.Paint += (_, e) =>
        {
            TextRenderer.DrawText(e.Graphics, "Jobs", Theme.Emphasis,
                new Rectangle(Theme.Pad, 0, 120, head.Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            using var p = new Pen(Theme.ChromeBorder);
            e.Graphics.DrawLine(p, 0, head.Height - 1, head.Width, head.Height - 1);
        };

        var clear = new FlatButton
        {
            Text = "Clear done",
            Font = Theme.Secondary,
            Size = new Size(74, 22),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        clear.Click += (_, _) => { _forge.Jobs.ClearFinished(); Refresh(_forge.Jobs.Jobs); };

        var close = new FlatButton
        {
            Text = "Hide",
            Font = Theme.Secondary,
            Size = new Size(46, 22),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        close.Click += (_, _) => Visible = false;

        head.Controls.Add(clear);
        head.Controls.Add(close);
        head.Resize += (_, _) =>
        {
            int y = (Theme.TopBarHeight - 22) / 2;
            close.Location = new Point(head.Width - close.Width - Theme.Pad, y);
            clear.Location = new Point(close.Left - clear.Width - 4, y);
        };

        _list.Dock = DockStyle.Fill;
        _list.FlowDirection = FlowDirection.TopDown;
        _list.WrapContents = false;
        _list.AutoScroll = true;
        _list.BackColor = Theme.ChromePanel;
        _list.Padding = new Padding(Theme.Pad, 8, Theme.Pad, 8);

        Controls.Add(_list);
        Controls.Add(head);

        Paint += (_, e) =>
        {
            using var p = new Pen(Theme.ChromeBorder);
            e.Graphics.DrawLine(p, 0, 0, 0, Height);
        };
    }

    public void Toggle() => Visible = !Visible;

    public void Refresh(IReadOnlyCollection<Job> jobs)
    {
        _list.SuspendLayout();

        var live = new HashSet<int>(jobs.Select(j => j.Id));
        foreach (var id in _rows.Keys.Where(k => !live.Contains(k)).ToList())
        {
            _list.Controls.Remove(_rows[id]);
            _rows[id].Dispose();
            _rows.Remove(id);
        }

        foreach (var job in jobs.OrderByDescending(j => j.Id))
        {
            if (job.Id == 0) continue;
            if (!_rows.TryGetValue(job.Id, out var row))
            {
                row = new JobRow(job) { Width = _list.ClientSize.Width - Theme.Pad * 2 - 4 };
                _rows[job.Id] = row;
                _list.Controls.Add(row);
                _list.Controls.SetChildIndex(row, 0);
            }
            row.Update(job);
        }

        _list.ResumeLayout();
    }

    private sealed class JobRow : CardPanel
    {
        private readonly Label _title = new();
        private readonly Label _message = new();
        private readonly FlatProgress _bar = new();
        private readonly FlatButton _cancel = new();
        private Job _job;

        public JobRow(Job job)
        {
            _job = job;
            Height = 66;
            Margin = new Padding(0, 0, 0, Theme.Gap);
            BackColor = Theme.RowOdd;
            BorderColour = Color.FromArgb(0x26, 0x2c, 0x34);

            _title.Location = new Point(Theme.Pad, 9);
            _title.AutoSize = false;
            _title.Size = new Size(200, 16);
            _title.ForeColor = Theme.Text;
            _title.Font = Theme.Body;
            _title.AutoEllipsis = true;

            _message.Location = new Point(Theme.Pad, 28);
            _message.AutoSize = false;
            _message.Size = new Size(280, 15);
            _message.ForeColor = Theme.TextMuted;
            _message.Font = Theme.Secondary;
            _message.AutoEllipsis = true;

            _bar.Location = new Point(Theme.Pad, 51);
            _bar.Size = new Size(280, 4);
            _bar.Height = 4;

            _cancel.Text = "Stop";
            _cancel.Font = Theme.Secondary;
            _cancel.Size = new Size(42, 20);
            _cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _cancel.Click += (_, _) => _job.Cancel();

            Controls.AddRange(new Control[] { _title, _message, _bar, _cancel });
            Resize += (_, _) =>
            {
                _bar.Width = Width - Theme.Pad * 2;
                _message.Width = Width - Theme.Pad * 2;
                _title.Width = Width - Theme.Pad * 2 - 48;
                _cancel.Left = Width - _cancel.Width - Theme.Pad;
            };

            Update(job);
        }

        public void Update(Job job)
        {
            _job = job;
            _title.Text = job.Label;
            _message.Text = job.Message;
            _bar.Value = job.State == JobState.Done ? 100 : job.Progress;

            _bar.BarColour = job.State switch
            {
                JobState.Done => Theme.Good,
                JobState.Failed => Theme.Bad,
                JobState.Cancelled => Theme.TextFaint,
                _ => Theme.Accent
            };
            _message.ForeColor = job.State == JobState.Failed ? Theme.Bad : Theme.TextMuted;
            _cancel.Visible = job.State is JobState.Queued or JobState.Running;
            _bar.Invalidate();
        }
    }
}
