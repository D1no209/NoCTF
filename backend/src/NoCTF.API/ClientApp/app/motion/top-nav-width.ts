import type { Directive } from 'vue'

export const topNavMotionDuration = 760
const topNavMotionEasing = 'cubic-bezier(0.16, 1, 0.3, 1)'

interface TopNavMotionBinding {
  button: HTMLElement
  content: HTMLElement
  hovered: boolean
  buttonAnimation?: Animation
  contentAnimation?: Animation
  dispose: () => void
  sync: (animate?: boolean) => void
}

const bindings = new WeakMap<HTMLElement, TopNavMotionBinding>()

function shouldExpand(binding: TopNavMotionBinding): boolean {
  return binding.hovered
    || binding.button.dataset.active === 'true'
    || binding.button.matches(':focus-visible')
}

function cancelAnimations(binding: TopNavMotionBinding): void {
  binding.buttonAnimation?.cancel()
  binding.contentAnimation?.cancel()
  binding.buttonAnimation = undefined
  binding.contentAnimation = undefined
}

function setExpanded(target: HTMLElement, binding: TopNavMotionBinding, expanded: boolean, animate: boolean): void {
  const current = target.dataset.topNavExpanded === 'true'
  if (current === expanded) return

  const before = binding.button.getBoundingClientRect()
  cancelAnimations(binding)
  target.dataset.topNavExpanded = String(expanded)

  if (!animate || matchMedia('(prefers-reduced-motion: reduce)').matches) return

  const after = binding.button.getBoundingClientRect()
  if (before.width <= 0 || after.width <= 0) return

  const deltaX = before.left - after.left
  const scaleX = before.width / after.width
  if (Math.abs(deltaX) < 0.5 && Math.abs(scaleX - 1) < 0.005) return

  binding.buttonAnimation = binding.button.animate(
    [
      { transform: `translate3d(${deltaX}px, 0, 0) scaleX(${scaleX})` },
      { transform: 'translate3d(0, 0, 0) scaleX(1)' },
    ],
    {
      duration: topNavMotionDuration,
      easing: topNavMotionEasing,
    },
  )
  binding.contentAnimation = binding.content.animate(
    [
      { transform: `scaleX(${1 / scaleX})` },
      { transform: 'scaleX(1)' },
    ],
    {
      duration: topNavMotionDuration,
      easing: topNavMotionEasing,
    },
  )

  const buttonAnimation = binding.buttonAnimation
  void buttonAnimation.finished.catch(() => undefined).then(() => {
    if (binding.buttonAnimation === buttonAnimation)
      binding.buttonAnimation = undefined
  })
  const contentAnimation = binding.contentAnimation
  void contentAnimation.finished.catch(() => undefined).then(() => {
    if (binding.contentAnimation === contentAnimation)
      binding.contentAnimation = undefined
  })
}

function createBinding(target: HTMLElement): TopNavMotionBinding | undefined {
  const button = target.querySelector<HTMLElement>('[data-top-nav-item]')
  const content = target.querySelector<HTMLElement>('[data-top-nav-content]')
  if (!button || !content) return undefined

  const binding: TopNavMotionBinding = {
    button,
    content,
    hovered: false,
    dispose: () => undefined,
    sync: () => undefined,
  }
  const sync = (animate = true) => setExpanded(target, binding, shouldExpand(binding), animate)
  const pointerEnter = () => {
    binding.hovered = true
    sync()
  }
  const pointerLeave = () => {
    binding.hovered = false
    sync()
  }
  const focusChanged = () => queueMicrotask(() => sync())

  target.addEventListener('pointerenter', pointerEnter)
  target.addEventListener('pointerleave', pointerLeave)
  target.addEventListener('focusin', focusChanged)
  target.addEventListener('focusout', focusChanged)
  binding.sync = sync
  binding.dispose = () => {
    cancelAnimations(binding)
    target.removeEventListener('pointerenter', pointerEnter)
    target.removeEventListener('pointerleave', pointerLeave)
    target.removeEventListener('focusin', focusChanged)
    target.removeEventListener('focusout', focusChanged)
  }
  sync(false)
  return binding
}

/** Uses FLIP transforms for the top navigation's fixed-slot expansion. */
export const topNavMotionDirective: Directive<HTMLElement> = {
  mounted(target) {
    const binding = createBinding(target)
    if (binding) bindings.set(target, binding)
  },
  updated(target) {
    bindings.get(target)?.sync()
  },
  unmounted(target) {
    bindings.get(target)?.dispose()
    bindings.delete(target)
  },
}
