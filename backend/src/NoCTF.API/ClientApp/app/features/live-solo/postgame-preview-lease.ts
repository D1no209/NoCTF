type PreviewPorts = {
  changed: (url: string | null) => void
  failed: (cause: unknown) => void
  schedule?: (run: () => void, milliseconds: number) => () => void
}

// The browser handoff lasts five minutes; refresh while retaining the same PDF URL.
export class PostgamePreviewLease {
  private revision = 0
  private request: AbortController | null = null
  private cancelTimer: (() => void) | null = null
  constructor(private readonly ports: PreviewPorts) {}
  close() {
    this.revision++; this.request?.abort(); this.request = null
    this.cancelTimer?.(); this.cancelTimer = null; this.ports.changed(null)
  }
  async open(grant: (signal: AbortSignal) => Promise<string>) {
    this.close()
    const revision = this.revision, request = this.request = new AbortController()
    const renew = async () => {
      try {
        const url = await grant(request.signal)
        if (revision !== this.revision || request.signal.aborted) return
        this.ports.changed(url)
        const schedule = this.ports.schedule ?? ((run, milliseconds) => {
          const timer = setTimeout(run, milliseconds); return () => clearTimeout(timer)
        })
        this.cancelTimer = schedule(() => { this.cancelTimer = null; void renew() }, 240_000)
      }
      catch (cause) {
        if (revision !== this.revision || request.signal.aborted) return
        this.close(); this.ports.failed(cause)
      }
    }
    await renew()
  }
}
