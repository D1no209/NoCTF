export type ViewerOperation = 'Enter' | 'Renew' | 'Leave'

export class ProgramViewerLease {
  active = false
  private pending = false
  private disposed = false
  private generation = 0
  constructor(private readonly send: (action: ViewerOperation) => Promise<boolean>, private readonly changed: (active: boolean) => void) {}
  async enter() {
    if (this.active || this.pending || this.disposed) return false
    return this.request('Enter')
  }
  async renew() {
    if (!this.active || this.pending || this.disposed) return false
    return this.request('Renew')
  }
  private async request(action: 'Enter' | 'Renew') {
    const generation = this.generation
    this.pending = true
    let success = false
    try { success = await this.send(action) } catch { success = false }
    finally { this.pending = false }
    if (this.disposed || generation !== this.generation) {
      if (success && action === 'Enter') void this.send('Leave').catch(() => false)
      return false
    }
    this.active = success
    this.changed(success)
    return success
  }
  leave() {
    const wasActive = this.active
    this.generation++
    this.active = false
    this.changed(false)
    if (wasActive) void this.send('Leave').catch(() => false)
  }
  dispose() { this.disposed = true; this.leave() }
}
