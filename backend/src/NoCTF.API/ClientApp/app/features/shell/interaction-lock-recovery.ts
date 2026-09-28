const OPEN_INTERACTION_LAYER_SELECTOR = '[data-dismissable-layer][data-state="open"]'
const MODAL_OVERLAY_SELECTOR = [
  '[data-slot="dialog-overlay"]',
  '[data-slot="sheet-overlay"]',
  '[data-slot="alert-dialog-overlay"]',
].join(',')
const RECOVERED_OVERLAY_SELECTOR = '[data-interaction-lock-recovered]'

const RECOVERY_DELAY_MS = 400

type InteractionLayer = Element & {
  getClientRects?: () => ArrayLike<unknown>
  style?: CSSStyleDeclaration
}

type InteractionLockDocument = Pick<Document, 'body' | 'defaultView' | 'querySelectorAll'>

function layerIsVisible(documentRoot: InteractionLockDocument, layer: InteractionLayer): boolean {
  if (layer.isConnected === false || layer.hasAttribute('hidden') || layer.getAttribute('aria-hidden') === 'true')
    return false

  const style = documentRoot.defaultView?.getComputedStyle(layer)
  if (
    style?.display === 'none'
    || style?.visibility === 'hidden'
    || style?.pointerEvents === 'none'
    || Number(style?.opacity ?? 1) === 0
  ) return false

  return !layer.getClientRects || layer.getClientRects().length > 0
}

function restoreRecoveredOverlays(documentRoot: InteractionLockDocument): void {
  for (const overlay of documentRoot.querySelectorAll<InteractionLayer>(RECOVERED_OVERLAY_SELECTOR)) {
    overlay.style?.removeProperty('pointer-events')
    overlay.removeAttribute('data-interaction-lock-recovered')
  }
}

/**
 * Releases a stale modal pointer lock after no visible open interaction layer remains.
 * An orphaned full-screen overlay is made inert as part of the same recovery so it
 * cannot continue intercepting clicks after the body lock has been released.
 */
export function releaseStaleInteractionLock(documentRoot: InteractionLockDocument): boolean {
  const openLayers = [...documentRoot.querySelectorAll<InteractionLayer>(OPEN_INTERACTION_LAYER_SELECTOR)]
  if (openLayers.some(layer => layerIsVisible(documentRoot, layer))) {
    restoreRecoveredOverlays(documentRoot)
    return false
  }

  let recovered = false
  if (documentRoot.body.style.pointerEvents === 'none') {
    documentRoot.body.style.removeProperty('pointer-events')
    recovered = true
  }

  for (const overlay of documentRoot.querySelectorAll<InteractionLayer>(MODAL_OVERLAY_SELECTOR)) {
    if (!layerIsVisible(documentRoot, overlay)) continue
    overlay.style?.setProperty('pointer-events', 'none', 'important')
    overlay.setAttribute('data-interaction-lock-recovered', '')
    recovered = true
  }

  return recovered
}

/**
 * Reka UI owns modal pointer locking. A Portal that is torn down during a route
 * transition can leave the body lock behind even though its layer is gone. Portal
 * contents can be removed below a persistent body child, so observe state and subtree
 * membership as well as the body style. Checks are coalesced instead of debounced: a
 * busy page must not be able to postpone recovery forever.
 */
export function installInteractionLockRecovery(
  documentRoot: Document = document,
  delayMs = RECOVERY_DELAY_MS,
): () => void {
  let recoveryTimer: ReturnType<typeof setTimeout> | undefined
  let portalObserverActive = false

  const hasBlockingOverlay = () => [...documentRoot.querySelectorAll(MODAL_OVERLAY_SELECTOR)]
    .some(overlay => !overlay.hasAttribute('data-interaction-lock-recovered'))

  const portalObserver = new MutationObserver(() => scheduleRecovery())

  const syncPortalObserver = () => {
    const shouldObserve = documentRoot.body.style.pointerEvents === 'none' || hasBlockingOverlay()
    if (shouldObserve === portalObserverActive) return

    portalObserver.disconnect()
    portalObserverActive = shouldObserve
    if (!shouldObserve) return

    portalObserver.observe(documentRoot.body, {
      attributes: true,
      attributeFilter: ['data-state'],
      childList: true,
      subtree: true,
    })
  }

  function scheduleRecovery() {
    if (recoveryTimer) return
    recoveryTimer = setTimeout(() => {
      recoveryTimer = undefined
      releaseStaleInteractionLock(documentRoot)
      syncPortalObserver()
    }, delayMs)
  }

  const bodyStyleObserver = new MutationObserver(() => {
    syncPortalObserver()
    scheduleRecovery()
  })
  bodyStyleObserver.observe(documentRoot.body, {
    attributes: true,
    attributeFilter: ['style'],
  })

  const bodyChildObserver = new MutationObserver(() => {
    syncPortalObserver()
    if (portalObserverActive) scheduleRecovery()
  })
  bodyChildObserver.observe(documentRoot.body, {
    childList: true,
  })

  window.addEventListener('pageshow', scheduleRecovery)
  window.addEventListener('focus', scheduleRecovery)
  documentRoot.addEventListener('visibilitychange', scheduleRecovery)
  syncPortalObserver()
  scheduleRecovery()

  return () => {
    if (recoveryTimer) clearTimeout(recoveryTimer)
    recoveryTimer = undefined
    bodyStyleObserver.disconnect()
    bodyChildObserver.disconnect()
    portalObserver.disconnect()
    window.removeEventListener('pageshow', scheduleRecovery)
    window.removeEventListener('focus', scheduleRecovery)
    documentRoot.removeEventListener('visibilitychange', scheduleRecovery)
  }
}
