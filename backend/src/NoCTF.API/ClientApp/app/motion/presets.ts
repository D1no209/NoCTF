import type { CSSProperties } from 'vue'

export type MotionPreset = 'list-enter' | 'detail-enter' | 'terminal-caret' | 'status-mark'

/** Presentation-only presets shared by primitives; no domain state or event handling. */
export function motionAttributes(preset: MotionPreset, index = 0): { class: string; style: CSSProperties } {
  return {
    class: `noctf-motion-${preset}`,
    style: { '--motion-stagger': `${Math.min(Math.max(index, 0), 6) * 25}ms` },
  }
}
