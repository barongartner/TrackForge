import SwiftUI

/// First-run bootstrap. Downloads whatever TrackForge is missing instead of
/// telling the user to go and install command line tools themselves.
struct ToolSetupView: View {
    let missing: [String]
    let onFinish: (Bool) -> Void

    @State private var installing = false
    @State private var progress: Double = 0
    @State private var status = ""
    @State private var statusColor = Theme.textFaint
    @State private var barColor = Theme.accent
    @State private var failed = false
    @State private var task: Task<Void, Never>?

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(missing.count > 1 ? "Two more things needed" : "One more thing needed")
                .font(Theme.inspectorTitle)
                .foregroundColor(Theme.text)

            Text(detail)
                .font(Theme.body)
                .foregroundColor(Theme.textDim)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, 10)

            Text(status)
                .font(Theme.secondary)
                .foregroundColor(statusColor)
                .lineLimit(2)
                .fixedSize(horizontal: false, vertical: true)
                .frame(height: 28, alignment: .topLeading)
                .padding(.top, 14)

            FlatProgress(value: progress, barColor: barColor, height: 4)
                .opacity(installing || progress > 0 ? 1 : 0)

            HStack(spacing: 8) {
                Spacer()
                Button(installing ? "Cancel" : "Skip") {
                    task?.cancel()
                    onFinish(false)
                }
                .flatButton()
                .keyboardShortcut(.cancelAction)

                Button(failed ? "Retry" : "Install now") { start() }
                    .flatButton(primary: true)
                    .disabled(installing)
                    .keyboardShortcut(.defaultAction)
            }
            .padding(.top, 20)
        }
        .padding(24)
        .frame(width: 470)
        .background(Theme.background)
    }

    private var detail: String {
        """
        TrackForge needs \(missing.joined(separator: " and ")) to download and \
        convert audio. They go in TrackForge's own folder, not your system, and \
        need no admin rights. About 90 MB total.
        """
    }

    private func start() {
        installing = true
        failed = false
        progress = 0
        barColor = Theme.accent
        statusColor = Theme.textFaint

        task = Task {
            do {
                if missing.contains("yt-dlp") {
                    try await ToolInstaller.installYtDlp { update in
                        progress = update.percent
                        status = update.message
                    }
                }
                if missing.contains("ffmpeg") {
                    progress = 0
                    try await ToolInstaller.installFfmpeg { update in
                        progress = update.percent
                        status = update.message
                    }
                }

                status = "Done. TrackForge is ready."
                statusColor = Theme.good
                barColor = Theme.good
                progress = 100
                installing = false

                try? await Task.sleep(nanoseconds: 700_000_000)
                onFinish(true)
            } catch is CancellationError {
                status = "Cancelled."
                statusColor = Theme.textFaint
                installing = false
            } catch {
                status = error.localizedDescription
                statusColor = Theme.bad
                barColor = Theme.bad
                installing = false
                failed = true
            }
        }
    }
}
