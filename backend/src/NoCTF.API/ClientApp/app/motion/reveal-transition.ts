import { nextTick } from 'vue'

type RevealTransitionLayer = 'theme' | 'wallpaper'

interface RevealTransitionDocument {
  startViewTransition?: (update: () => void | Promise<void>) => {
    finished: Promise<void>
    skipTransition?: () => void
  }
}

export const revealTransitionDeadlineMs = 1_500

let revealInProgress = false

async function waitForTransition(transition: { finished: Promise<void>, skipTransition?: () => void }): Promise<void> {
  await new Promise<void>((resolve) => {
    let settled = false
    const finish = () => {
      if (settled) return
      settled = true
      clearTimeout(deadline)
      resolve()
    }
    const deadline = setTimeout(() => {
      transition.skipTransition?.()
      finish()
    }, revealTransitionDeadlineMs)
    void transition.finished.then(finish, finish)
  })
}

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
  let updateStarted = false
  try {
    const transition = transitionDocument.startViewTransition(async () => {
      updateStarted = true
      await update()
      await nextTick()
    })
    await waitForTransition(transition)
    if (!updateStarted) {
      await update()
      await nextTick()
    }
  }
  catch {
    if (!updateStarted) {
      await update()
      await nextTick()
    }
  }
  finally {
    delete document.documentElement.dataset[datasetKey]
    revealInProgress = false
  }
}
