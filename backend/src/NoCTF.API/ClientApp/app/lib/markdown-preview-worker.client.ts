import { renderMarkdownAsync } from './markdown'

interface WorkerResponse {
  id: number
  html?: string
  error?: string
}

interface PendingTask {
  resolve: (html: string) => void
  reject: (error: Error) => void
  timer: ReturnType<typeof setTimeout>
}

/** A worker that stops answering must not hang the live preview forever. */
const previewTimeoutMs = 15_000

let markdownWorker: Worker | undefined
let workerUnavailable = false
let nextTaskId = 0
const pendingTasks = new Map<number, PendingTask>()

function takeTask(id: number): PendingTask | undefined {
  const task = pendingTasks.get(id)
  if (!task) return undefined
  pendingTasks.delete(id)
  clearTimeout(task.timer)
  return task
}

function rejectPending(error: Error) {
  for (const id of [...pendingTasks.keys()])
    takeTask(id)?.reject(error)
}

function getMarkdownWorker(): Worker | undefined {
  if (workerUnavailable || typeof Worker === 'undefined')
    return undefined
  if (markdownWorker)
    return markdownWorker
  try {
    markdownWorker = new Worker(new URL('../workers/markdown.worker.ts', import.meta.url), { type: 'module' })
    markdownWorker.addEventListener('message', (event: MessageEvent<WorkerResponse>) => {
      const task = takeTask(event.data.id)
      if (!task) return
      if (event.data.error) task.reject(new Error(event.data.error))
      else task.resolve(event.data.html ?? '')
    })
    markdownWorker.addEventListener('error', () => {
      workerUnavailable = true
      markdownWorker?.terminate()
      markdownWorker = undefined
      rejectPending(new Error('Markdown Worker unavailable'))
    })
    return markdownWorker
  }
  catch {
    workerUnavailable = true
    return undefined
  }
}

/** Renders live editor input away from the main thread with a safe fallback. */
export async function renderMarkdownPreview(source: string): Promise<string> {
  const worker = getMarkdownWorker()
  if (!worker)
    return renderMarkdownAsync(source)

  const id = ++nextTaskId
  try {
    return await new Promise<string>((resolve, reject) => {
      const timer = setTimeout(() => {
        takeTask(id)?.reject(new Error('Markdown Worker timed out'))
      }, previewTimeoutMs)
      pendingTasks.set(id, { resolve, reject, timer })
      worker.postMessage({ id, source })
    })
  }
  catch {
    takeTask(id)
    return renderMarkdownAsync(source)
  }
}
