import type { Object3D } from 'three'

/** A single finite raising/lowering motion on a confirmed controller change. */
export function setFlagTransfer(flag: Object3D, elapsed: number, leaving: boolean) {
  const phase = Math.min(1, Math.max(0, elapsed / 900))
  const raised = 1 - (1 - phase) ** 4
  flag.scale.y = Math.max(.001, leaving ? 1 - raised : raised)
  return phase < 1
}
