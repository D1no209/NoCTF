import type { Slot } from 'vue'

export interface NoticePayload {
  id: string
  destructive: boolean
  content?: Slot
}
