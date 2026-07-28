using TrackForge.Core;

namespace TrackForge.UI;

/// <summary>Grid of cover art options. Double-click or Use to pick one.</summary>
public sealed class ArtPickerDialog : Form
{
    private readonly ForgeService _forge;
    private readonly FlowLayoutPanel _grid = new();
    private readonly Label _hint = new();
    private PictureBox? _selected;

    public byte[]? SelectedBytes { get; private set; }
    public string? SelectedUrl { get; private set; }

    public ArtPickerDialog(ForgeService forge, IReadOnlyList<MetadataClient.ArtOption> options)
    {
        _forge = forge;

        Text = "Choose cover art";
        Size = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UI;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimizeBox = false;
        MaximizeBox = false;

        _grid.Dock = DockStyle.Fill;
        _grid.AutoScroll = true;
        _grid.BackColor = Theme.Background;
        _grid.Padding = new Padding(14);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Surface };
        footer.Paint += (_, e) =>
        {
            using var p = new Pen(Theme.Border);
            e.Graphics.DrawLine(p, 0, 0, footer.Width, 0);
        };

        _hint.Text = "Pick a cover. It gets square-cropped and re-encoded at 1000px.";
        _hint.Location = new Point(16, 18);
        _hint.Size = new Size(430, 18);
        _hint.ForeColor = Theme.TextDim;
        _hint.Font = Theme.Small;

        var use = new FlatButton { Text = "Use this", Primary = true, Size = new Size(100, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        use.Click += (_, _) => { if (SelectedBytes is not null) DialogResult = DialogResult.OK; };

        var browse = new FlatButton { Text = "From file...", Size = new Size(100, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        browse.Click += (_, _) => BrowseLocal();

        var cancel = new FlatButton { Text = "Cancel", Size = new Size(84, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        footer.Controls.AddRange(new Control[] { _hint, use, browse, cancel });
        footer.Resize += (_, _) =>
        {
            cancel.Location = new Point(footer.Width - cancel.Width - 14, 11);
            browse.Location = new Point(cancel.Left - browse.Width - 8, 11);
            use.Location = new Point(browse.Left - use.Width - 8, 11);
        };

        Controls.Add(_grid);
        Controls.Add(footer);

        Shown += async (_, _) => await LoadThumbnailsAsync(options);
    }

    private async Task LoadThumbnailsAsync(IReadOnlyList<MetadataClient.ArtOption> options)
    {
        foreach (var option in options)
        {
            var card = new Panel
            {
                Size = new Size(168, 196),
                Margin = new Padding(6),
                BackColor = Theme.Surface,
                Cursor = Cursors.Hand,
                Tag = option,
            };

            var pic = new PictureBox
            {
                Size = new Size(150, 150),
                Location = new Point(9, 9),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Theme.SurfaceAlt,
                Cursor = Cursors.Hand,
                Tag = option,
            };

            var caption = new Label
            {
                Text = $"{option.Label}  ({option.Source})",
                Location = new Point(9, 163),
                Size = new Size(150, 28),
                Font = Theme.Small,
                ForeColor = Theme.TextDim,
            };

            void Select() => _ = ChooseAsync(pic, option);
            card.Click += (_, _) => Select();
            pic.Click += (_, _) => Select();
            caption.Click += (_, _) => Select();
            pic.DoubleClick += async (_, _) =>
            {
                await ChooseAsync(pic, option);
                if (SelectedBytes is not null) DialogResult = DialogResult.OK;
            };

            card.Controls.Add(pic);
            card.Controls.Add(caption);
            _grid.Controls.Add(card);

            var thumbUrl = string.IsNullOrWhiteSpace(option.ThumbUrl) ? option.Url : option.ThumbUrl;
            var bytes = await _forge.Metadata.DownloadArtAsync(thumbUrl);
            if (bytes is not null && !IsDisposed)
                pic.Image = TagService.ImageFromBytes(bytes);
        }
    }

    private async Task ChooseAsync(PictureBox pic, MetadataClient.ArtOption option)
    {
        if (_selected is not null && _selected.Parent is not null)
            _selected.Parent.BackColor = Theme.Surface;

        _selected = pic;
        if (pic.Parent is not null) pic.Parent.BackColor = Theme.AccentDim;

        _hint.Text = "Downloading full resolution...";
        var full = await _forge.Metadata.DownloadArtAsync(option.Url)
                   ?? await _forge.Metadata.DownloadArtAsync(option.ThumbUrl);

        if (full is null)
        {
            _hint.Text = "That cover could not be downloaded. Try another.";
            return;
        }

        SelectedBytes = full;
        SelectedUrl = option.Url;

        using var image = TagService.ImageFromBytes(full);
        _hint.Text = image is null
            ? "Selected."
            : $"Selected  {image.Width} x {image.Height}  from {option.Source}";
    }

    private void BrowseLocal()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a cover image",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp|All files|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            SelectedBytes = File.ReadAllBytes(dialog.FileName);
            SelectedUrl = null;
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not read that image",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
