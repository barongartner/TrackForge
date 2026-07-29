import SwiftUI

/// The jobs dock: every queued and finished job, newest first.
struct JobsView: View {
    @EnvironmentObject private var forge: ForgeService
    @Binding var isVisible: Bool

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider().overlay(Theme.chromeBorder)

            if forge.jobs.jobs.isEmpty {
                EmptyHint(lines: ["Nothing running.", "Downloads and bulk edits show up here."])
            } else {
                ScrollView {
                    LazyVStack(spacing: Theme.gap) {
                        ForEach(forge.jobs.jobs) { job in
                            JobRow(job: job) { forge.jobs.cancel(job.id) }
                        }
                    }
                    .padding(Theme.pad)
                }
            }
        }
        .background(Theme.chromePanel)
    }

    private var header: some View {
        HStack(spacing: 4) {
            Text("Jobs").font(Theme.emphasis).foregroundColor(Theme.text)
            Spacer()
            Button("Clear done") { forge.jobs.clearFinished() }
                .flatButton(compact: true)
            Button("Hide") { withAnimation(.easeOut(duration: 0.12)) { isVisible = false } }
                .flatButton(compact: true)
        }
        .padding(.horizontal, Theme.pad)
        .frame(height: Theme.topBarHeight)
    }
}

private struct JobRow: View {
    let job: Job
    let onCancel: () -> Void

    var body: some View {
        CardPanel(background: Theme.rowOdd, borderColor: Theme.hex(0x262c34)) {
            VStack(alignment: .leading, spacing: 5) {
                HStack(spacing: 6) {
                    Text(job.label)
                        .font(Theme.body)
                        .foregroundColor(Theme.text)
                        .lineLimit(1)
                        .truncationMode(.middle)

                    Spacer(minLength: 4)

                    if job.state == .queued || job.state == .running {
                        Button("Stop", action: onCancel).flatButton(compact: true)
                    } else {
                        Text(job.stateText)
                            .font(Theme.numericSmall)
                            .foregroundColor(stateColor)
                    }
                }

                Text(job.message)
                    .font(Theme.secondary)
                    .foregroundColor(job.state == .failed ? Theme.bad : Theme.textMuted)
                    .lineLimit(2)
                    .fixedSize(horizontal: false, vertical: true)

                FlatProgress(
                    value: job.state == .done ? 100 : job.progress,
                    barColor: barColor, height: 4)
            }
            .padding(Theme.pad)
        }
        .help(job.error ?? job.message)
    }

    private var barColor: Color {
        switch job.state {
        case .done: return Theme.good
        case .failed: return Theme.bad
        case .cancelled: return Theme.textFaint
        default: return Theme.accent
        }
    }

    private var stateColor: Color {
        switch job.state {
        case .done: return Theme.good
        case .failed: return Theme.bad
        default: return Theme.textFaint
        }
    }
}
