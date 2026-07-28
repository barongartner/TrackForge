using TrackForge.Core;

namespace TrackForge.UI;

/// <summary>
/// First-run bootstrap. Downloads whatever TrackForge is missing instead of telling
/// the user to go and install command line tools themselves.
/// </summary>
public sealed class ToolSetupDialog : Form
{
    private readonly ForgeService _forge;
    private readonly List<string> _missing;

    private readonly Label _headline = new();
    private readonly Label _detail = new();
    private readonly Label _status = new();
    private readonly FlatProgress _progress = new();
    private readonly FlatButton _install = new();
    private readonly FlatButton _skip = new();

    private CancellationTokenSource? _cts;

    public bool InstalledSomething { get; private set; }

    public ToolSetupDialog(ForgeService forge, List<string> missing)
    {
        _forge = forge;
        _missing = missing;

        Text = "TrackForge setup";
        Size = new Size(460, 250);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UI;

        _headline.Text = "Two more things needed";
        _headline.Font = Theme.Heading;
        _headline.ForeColor = Theme.Text;
        _headline.SetBounds(Theme.Pad * 2, 18, 400, 24);

        _detail.Text =
            $"TrackForge needs {string.Join(" and ", missing)} to download and convert audio.\r\n" +
            "They go in TrackForge's own folder, not your system, and need no admin rights.\r\n" +
            "About 40 MB total.";
        _detail.ForeColor = Theme.TextDim;
        _detail.SetBounds(Theme.Pad * 2, 48, 400, 56);

        _status.Font = Theme.Small;
        _status.ForeColor = Theme.TextFaint;
        _status.AutoEllipsis = true;
        _status.SetBounds(Theme.Pad * 2, 112, 400, 16);

        _progress.SetBounds(Theme.Pad * 2, 132, 400, 4);
        _progress.Visible = false;

        _install.Text = "Install now";
        _install.Primary = true;
        _install.Size = new Size(104, 28);
        _install.Location = new Point(224, 158);
        _install.Click += async (_, _) => await InstallAsync();

        _skip.Text = "Skip";
        _skip.Size = new Size(76, 28);
        _skip.Location = new Point(336, 158);
        _skip.Click += (_, _) => { _cts?.Cancel(); DialogResult = DialogResult.Cancel; };

        Controls.AddRange(new Control[] { _headline, _detail, _status, _progress, _install, _skip });
        AcceptButton = _install;
    }

    private async Task InstallAsync()
    {
        _install.Enabled = false;
        _skip.Text = "Cancel";
        _progress.Visible = true;
        _cts = new CancellationTokenSource();

        var progress = new Progress<(double percent, string message)>(p =>
        {
            _progress.Value = p.percent;
            _status.Text = p.message;
        });

        try
        {
            if (_missing.Contains("yt-dlp"))
                await ToolInstaller.InstallYtDlpAsync(progress, _cts.Token);

            if (_missing.Contains("ffmpeg"))
            {
                _progress.Value = 0;
                await ToolInstaller.InstallFfmpegAsync(progress, _cts.Token);
            }

            InstalledSomething = true;
            _status.Text = "Done. TrackForge is ready.";
            _status.ForeColor = Theme.Good;
            _progress.BarColour = Theme.Good;
            _progress.Value = 100;
            _skip.Text = "Close";
            await Task.Delay(700);
            DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Cancelled.";
            _status.ForeColor = Theme.TextFaint;
            _install.Enabled = true;
            _skip.Text = "Skip";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message.Length > 90 ? ex.Message[..90] : ex.Message;
            _status.ForeColor = Theme.Bad;
            _progress.BarColour = Theme.Bad;
            _install.Enabled = true;
            _install.Text = "Retry";
            _skip.Text = "Skip";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }
}
