import { renderMarkdownAsync } from '../lib/markdown'

interface MarkdownWorkerRequest {
  id: number
  source: string
}

interface MarkdownWorkerResponse {
  id: number
  html?: string
  error?: string
}

self.addEventListener('message', async (event: MessageEvent<MarkdownWorkerRequest>) => {
  const { id, source } = event.data
  try {
    const html = await renderMarkdownAsync(source)
    self.postMessage({ id, html } satisfies MarkdownWorkerResponse)
  }
  catch (error) {
    self.postMessage({ id, error: error instanceof Error ? error.message : String(error) } satisfies MarkdownWorkerResponse)
  }
})
