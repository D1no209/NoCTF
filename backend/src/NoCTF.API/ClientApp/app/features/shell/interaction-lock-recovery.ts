/**
 * "还有模态层打开"只依据本项目共享原语自己的标记
 * (DialogContent / DialogScrollContent / AlertDialogContent / SheetContent 的
 * `data-modal-scroll-lock`),不读 Reka 的 `data-dismissable-layer` / `data-state`
 * 等内部属性,避免库升级时检测静默失效。悬停定位不依赖该标记。
 */
const OPEN_INTERACTION_LAYER_SELECTOR = '[data-modal-scroll-lock]'
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
 * 在所有可见的模态层都已关闭后释放残留的 body 指针锁。
 *
 * 根因是库内部的竞态:Reka 的 scroll-lock 在 `nextTick` 里写
 * `document.body.style.pointerEvents = 'none'`,该回调可能在模态层已卸载后落地;
 * 而它自己的 reset 路径又被内部计数门控,于是锁留在页面上。恢复因此只依据
 * 本项目原语自己的标记判断"是否还有模态层",并重置同样的文档级样式。
 * 同时把残留的全屏遮罩置为 inert,避免释放后它继续吞掉点击。
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
 * 模态锁由共享原语持有的 `data-modal-scroll-lock` 标记驱动:Portal 在路由过渡中被拆除时
 * 该标记随之消失,即使库状态残留也能判定"已无模态层"。检查做合并(coalesce)而非防抖,
 * 繁忙页面不能无限推迟恢复。
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

    // 只看节点增删:模态表面出现/消失即 `data-modal-scroll-lock` 节点增删,
    // 不再监听库的 `data-state` 属性变化。
    portalObserver.observe(documentRoot.body, {
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
