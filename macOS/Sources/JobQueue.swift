import Foundation

enum JobState: String {
    case queued, running, done, failed, cancelled
}

struct Job: Identifiable {
    let id: Int
    var kind: String
    var label: String
    var state: JobState = .queued
    var progress: Double = 0
    var message = "Queued"
    var error: String?

    var stateText: String {
        switch state {
        case .queued: return "Queued"
        case .running: return String(format: "%.0f%%", progress)
        case .done: return "Done"
        case .failed: return "Failed"
        case .cancelled: return "Cancelled"
        }
    }

    var isFinished: Bool { state == .done || state == .failed || state == .cancelled }
}

/// Reports progress from inside a running job. Safe to call from any thread —
/// every update hops back to the main actor, which is where the jobs array lives.
struct JobReporter: Sendable {
    let report: @Sendable (Double, String) -> Void

    func callAsFunction(_ progress: Double, _ message: String) {
        report(progress, message)
    }
}

/// Runs jobs on a bounded number of concurrent tasks.
///
/// The Windows build used a thread pool over a blocking collection and marshalled
/// every event back to the form by hand. Here the queue simply lives on the main
/// actor and the work runs in detached tasks, which gets the same bounded
/// concurrency with none of the marshalling.
@MainActor
final class JobQueue: ObservableObject {
    @Published private(set) var jobs: [Job] = []

    var maxConcurrent: Int = 2

    private var sequence = 0
    private var running = 0
    private var tasks: [Int: Task<Void, Never>] = [:]
    private var pending: [(id: Int, work: @Sendable (JobReporter) async throws -> Void)] = []

    var activeCount: Int {
        jobs.filter { $0.state == .queued || $0.state == .running }.count
    }

    @discardableResult
    func enqueue(
        kind: String, label: String,
        work: @escaping @Sendable (JobReporter) async throws -> Void
    ) -> Int {
        sequence += 1
        let id = sequence
        jobs.insert(Job(id: id, kind: kind, label: label), at: 0)
        pending.append((id, work))
        pump()
        return id
    }

    private func pump() {
        while running < max(1, maxConcurrent), !pending.isEmpty {
            let next = pending.removeFirst()

            // Cancelled before it ever started.
            guard let index = jobs.firstIndex(where: { $0.id == next.id }),
                  jobs[index].state == .queued
            else { continue }

            running += 1
            jobs[index].state = .running
            jobs[index].message = "Starting"

            let id = next.id
            let reporter = JobReporter { [weak self] progress, message in
                Task { @MainActor in self?.report(id, progress, message) }
            }

            // Detached on purpose: a plain Task would inherit this class's main
            // actor, and the CPU-bound parts of a job — tag writes, the FFT —
            // would then run on the thread that draws the window.
            tasks[id] = Task.detached { [weak self] in
                var outcome: JobState = .done
                var errorText: String?

                do {
                    try await next.work(reporter)
                } catch is CancellationError {
                    outcome = .cancelled
                } catch {
                    outcome = .failed
                    errorText = error.localizedDescription
                }

                await self?.finish(id, outcome: outcome, error: errorText)
            }
        }
    }

    private func finish(_ id: Int, outcome: JobState, error: String?) {
        running = max(0, running - 1)
        tasks[id] = nil

        if let index = jobs.firstIndex(where: { $0.id == id }) {
            // A job cancelled while queued has already been marked; don't undo it.
            if jobs[index].state == .running || jobs[index].state == .queued {
                jobs[index].state = outcome
            }
            switch outcome {
            case .done:
                jobs[index].progress = 100
                if jobs[index].message == "Starting" || jobs[index].message == "Queued" {
                    jobs[index].message = "Done"
                }
            case .failed:
                jobs[index].error = error
                let text = error ?? "Failed"
                jobs[index].message = text.count > 200 ? String(text.prefix(200)) : text
            case .cancelled:
                jobs[index].message = "Cancelled"
            default:
                break
            }
        }
        pump()
    }

    func report(_ id: Int, _ progress: Double, _ message: String) {
        guard let index = jobs.firstIndex(where: { $0.id == id }),
              jobs[index].state == .running
        else { return }
        jobs[index].progress = progress
        jobs[index].message = message
    }

    func job(_ id: Int) -> Job? { jobs.first(where: { $0.id == id }) }

    func cancel(_ id: Int) {
        tasks[id]?.cancel()
        if let index = jobs.firstIndex(where: { $0.id == id }), jobs[index].state == .queued {
            jobs[index].state = .cancelled
            jobs[index].message = "Cancelled"
            pending.removeAll { $0.id == id }
        }
    }

    func clearFinished() {
        jobs.removeAll { $0.isFinished }
    }

    func cancelAll() {
        for task in tasks.values { task.cancel() }
        pending.removeAll()
    }
}
