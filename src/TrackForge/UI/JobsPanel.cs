using TrackForge.Core;

namespace TrackForge.UI;

/// <summary>Slide-out dock on the right listing every queued and finished job.</summary>
public sealed class JobsPanel : Panel
{
    private readonly ForgeService _forge;
    private readonly FlowLayoutPanel _list = new();
    private readonly Dictionary<int, JobRow> _rows = new();

    public JobsPanel(ForgeService forge)
    {
        _forge = forge;
        Dock = DockStyle.Right;
        Width = 340;
        Visible = false;
        BackColor = Theme.Surface;
        Padding = new Padding(1, 0, 0, 0);

        var head = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Surface };
        head.Paint += (_, e) =>
        {
            TextRenderer.DrawText(e.Graphics, "Jobs", Theme.UIBold,
                new Rectangle(14, 0, 120, head.Height), Theme.Text, TextFormatFlags.VerticalCenter);
            using var p = new Pen(Theme.Border);
            e.Graphics.DrawLine(p, 0, head.Height - 1, head.Width, head.Height - 1);
        };

        var clear = new FlatButton { Text = "Clear done", Size = new Size(96, 26), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        clear.Click += (_, _) => { _forge.Jobs.ClearFinished(); Refresh(_forge.Jobs.Jobs); };

        var close = new FlatButton { Text = "Hide", Size = new Size(56, 26), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        close.Click += (_, _) => Visible = false;

        head.Controls.Add(clear);
        head.Controls.Add(close);
        head.Resize += (_, _) =>
        {
            close.Location = new Point(head.Width - close.Width - 12, 9);
            clear.Location = new Point(close.Left - clear.Width - 6, 9);
        };

        _list.Dock = DockStyle.Fill;
        _list.FlowDirection = FlowDirection.TopDown;
        _list.WrapContents = false;
        _list.AutoScroll = true;
        _list.BackColor = Theme.Surface;
        _list.Padding = new Padding(10, 8, 10, 8);

        Controls.Add(_list);
        Controls.Add(head);

        Paint += (_, e) =>
        {
            using var p = new Pen(Theme.Border);
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
                row = new JobRow(job) { Width = _list.ClientSize.Width - 24 };
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
        private readonly Label _status = new();
        private readonly FlatProgress _bar = new();
        private readonly FlatButton _cancel = new();
        private Job _job;

        public JobRow(Job job)
        {
            _job = job;
            Height = 76;
            Margin = new Padding(0, 0, 0, 8);
            BackColor = Theme.SurfaceAlt;

            _title.Location = new Point(10, 9);
            _title.AutoSize = false;
            _title.Size = new Size(230, 17);
            _title.ForeColor = Theme.Text;
            _title.Font = Theme.UI;
            _title.AutoEllipsis = true;

            _status.Location = new Point(10, 29);
            _status.AutoSize = false;
            _status.Size = new Size(300, 16);
            _status.ForeColor = Theme.TextDim;
            _status.Font = Theme.Small;
            _status.AutoEllipsis = true;

            _bar.Location = new Point(10, 55);
            _bar.Size = new Size(280, 4);

            _cancel.Text = "Stop";
            _cancel.Size = new Size(52, 22);
            _cancel.Font = Theme.Small;
            _cancel.Location = new Point(248, 6);
            _cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _cancel.Click += (_, _) => _job.Cancel();

            Controls.AddRange(new Control[] { _title, _status, _bar, _cancel });
            Resize += (_, _) =>
            {
                _bar.Width = Width - 20;
                _title.Width = Width - 80;
                _status.Width = Width - 20;
                _cancel.Left = Width - _cancel.Width - 10;
            };

            Update(job);
        }

        public void Update(Job job)
        {
            _job = job;
            _title.Text = job.Label;
            _status.Text = job.Message;
            _bar.Value = job.State == JobState.Done ? 100 : job.Progress;

            _bar.BarColour = job.State switch
            {
                JobState.Done => Theme.Good,
                JobState.Failed => Theme.Bad,
                JobState.Cancelled => Theme.TextFaint,
                _ => Theme.Accent
            };
            _status.ForeColor = job.State == JobState.Failed ? Theme.Bad : Theme.TextDim;
            _cancel.Visible = job.State is JobState.Queued or JobState.Running;
            _bar.Invalidate();
        }
    }
}
