const ACTIVE_INTERACTION_LAYER_SELECTOR = [
  '[data-dismissable-layer][data-state="open"]',
  '[data-slot="dialog-overlay"][data-state="open"]',
  '[data-slot="sheet-overlay"][data-state="open"]',
  '[data-slot="alert-dialog-overlay"][data-state="open"]',
].join(',')

const RECOVERY_DELAY_MS = 400

type InteractionLockDocument = Pick<Document, 'body' | 'querySelector'>

/** Releases a modal pointer lock only after every interactive layer has left the DOM. */
export function releaseStaleInteractionLock(documentRoot: InteractionLockDocument): boolean {
  if (documentRoot.body.style.pointerEvents !== 'none') return false
  if (documentRoot.querySelector(ACTIVE_INTERACTION_LAYER_SELECTOR)) return false

  documentRoot.body.style.removeProperty('pointer-events')
  return true
}

/**
 * Reka UI owns modal pointer locking. A Portal that is torn down during a route
 * transition can leave the body lock behind even though its layer is gone.
 * Observe only direct body children so ordinary page updates do not enter this path.
 */
export function installInteractionLockRecovery(
  documentRoot: Document = document,
  delayMs = RECOVERY_DELAY_MS,
): () => void {
  let recoveryTimer: ReturnType<typeof setTimeout> | undefined

  const scheduleRecovery = () => {
    if (recoveryTimer) clearTimeout(recoveryTimer)
    recoveryTimer = setTimeout(() => {
      recoveryTimer = undefined
      releaseStaleInteractionLock(documentRoot)
    }, delayMs)
  }

  const observer = new MutationObserver(scheduleRecovery)
  observer.observe(documentRoot.body, {
    attributes: true,
    attributeFilter: ['style'],
    childList: true,
  })

  window.addEventListener('pageshow', scheduleRecovery)

  return () => {
    if (recoveryTimer) clearTimeout(recoveryTimer)
    recoveryTimer = undefined
    observer.disconnect()
    window.removeEventListener('pageshow', scheduleRecovery)
  }
}
