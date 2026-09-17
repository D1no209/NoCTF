import { renderMarkdownAsync } from './markdown'

interface WorkerResponse {
  id: number
  html?: string
  error?: string
}

let markdownWorker: Worker | undefined
let workerUnavailable = false
let nextTaskId = 0
const pendingTasks = new Map<number, { resolve: (html: string) => void; reject: (error: Error) => void }>()

function rejectPending(error: Error) {
  for (const task of pendingTasks.values())
    task.reject(error)
  pendingTasks.clear()
}

function getMarkdownWorker(): Worker | undefined {
  if (workerUnavailable || typeof Worker === 'undefined')
    return undefined
  if (markdownWorker)
    return markdownWorker
  try {
    markdownWorker = new Worker(new URL('../workers/markdown.worker.ts', import.meta.url), { type: 'module' })
    markdownWorker.addEventListener('message', (event: MessageEvent<WorkerResponse>) => {
      const task = pendingTasks.get(event.data.id)
      if (!task)
        return
      pendingTasks.delete(event.data.id)
      if (event.data.error)
        task.reject(new Error(event.data.error))
      else
        task.resolve(event.data.html ?? '')
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
      pendingTasks.set(id, { resolve, reject })
      worker.postMessage({ id, source })
    })
  }
  catch {
    return renderMarkdownAsync(source)
  }
}

export function prefetchMarkdownHighlighter(): void {
  const load = () => { void import('./markdown-highlighter') }
  if ('requestIdleCallback' in globalThis)
    globalThis.requestIdleCallback(load, { timeout: 1200 })
  else
    setTimeout(load, 0)
}
