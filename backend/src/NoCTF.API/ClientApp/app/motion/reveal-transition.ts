import { nextTick } from 'vue'

type RevealTransitionLayer = 'theme' | 'wallpaper'

interface RevealTransitionDocument {
  startViewTransition?: (update: () => void | Promise<void>) => {
    finished: Promise<void>
  }
}

let revealInProgress = false

/** Runs the shared top-to-bottom reveal, scoped to the requested visual layer. */
export async function runDownRevealTransition(
  layer: RevealTransitionLayer,
  update: () => void | Promise<void>,
): Promise<void> {
  if (typeof document === 'undefined' || typeof window === 'undefined') {
    await update()
    return
  }

  const transitionDocument = document as RevealTransitionDocument
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  if (reducedMotion || !transitionDocument.startViewTransition || revealInProgress) {
    await update()
    await nextTick()
    return
  }

  const datasetKey = layer === 'theme' ? 'themeTransition' : 'wallpaperTransition'
  revealInProgress = true
  document.documentElement.dataset[datasetKey] = 'down'
  try {
    const transition = transitionDocument.startViewTransition(async () => {
      await update()
      await nextTick()
    })
    await transition.finished.catch(() => undefined)
  }
  catch {
    await update()
    await nextTick()
  }
  finally {
    delete document.documentElement.dataset[datasetKey]
    revealInProgress = false
  }
}
