export type HumanVerificationHeaders = Record<string, string>

/** Serializes provider challenges and settles every grant exactly once. */
export function createHumanVerificationCoordinator() {
  let resolvePending: ((headers: HumanVerificationHeaders | null) => void) | null = null

  function begin(): Promise<HumanVerificationHeaders | null> | null {
    if (resolvePending) return null
    return new Promise((resolve) => {
      resolvePending = resolve
    })
  }

  function settle(headers: HumanVerificationHeaders | null): boolean {
    const resolve = resolvePending
    if (!resolve) return false
    resolvePending = null
    resolve(headers)
    return true
  }

  return {
    begin,
    settle,
    get active() { return resolvePending !== null },
  }
}
