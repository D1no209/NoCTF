<script setup lang="ts">
import { X } from '@lucide/vue'
import NoticeIcon from './NoticeIcon.vue'
import ScrollSurface from '../scroll-area/ScrollSurface.vue'
import ActionButton from '../action-button/ActionButton.vue'
import { nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
defineProps<{ destructive?: boolean }>()
const emit = defineEmits<{ dismiss: [] }>()

const frame = ref<HTMLElement | null>(null)
let observer: ResizeObserver | undefined
let measureFrame = 0
let observedWidth = -1
let observedHeight = -1

function setPropertyIfChanged(element: HTMLElement, name: string, value: string) {
  if (element.style.getPropertyValue(name) !== value) element.style.setProperty(name, value)
}

function measureNoticeStack() {
  const current = frame.value
  const toast = current?.closest<HTMLElement>('[data-sonner-toast]')
  const toaster = toast?.closest<HTMLElement>('[data-sonner-toaster]')
  if (!current || !toast || !toaster) return

  const currentX = toast.dataset.xPosition
  const currentY = toast.dataset.yPosition
  const notices = [...toaster.querySelectorAll<HTMLElement>('[data-sonner-toast]')]
    .filter(item => item.dataset.xPosition === currentX && item.dataset.yPosition === currentY)
    .sort((left, right) => Number(left.style.getPropertyValue('--index')) - Number(right.style.getPropertyValue('--index')))
  const gap = Number.parseFloat(getComputedStyle(toaster).getPropertyValue('--gap')) || 14
  let offset = 0
  let frontHeight = 0

  for (const item of notices) {
    const notice = item.querySelector<HTMLElement>('.notice-frame')
    const style = getComputedStyle(item)
    const verticalPadding = (Number.parseFloat(style.paddingTop) || 0)
      + (Number.parseFloat(style.paddingBottom) || 0)
    const height = Math.ceil(notice
      ? notice.getBoundingClientRect().height + verticalPadding
      : item.scrollHeight)
    if (notice) {
      setPropertyIfChanged(item, '--notice-height', `${height}px`)
      setPropertyIfChanged(item, '--initial-height', `${height}px`)
    }
    setPropertyIfChanged(item, '--offset', `${offset}px`)
    if (item.dataset.front === 'true') frontHeight = height
    offset += height + gap
  }

  if (frontHeight) setPropertyIfChanged(toaster, '--front-toast-height', `${frontHeight}px`)
}

function scheduleMeasurement() {
  if (measureFrame) cancelAnimationFrame(measureFrame)
  measureFrame = requestAnimationFrame(() => {
    measureFrame = 0
    measureNoticeStack()
  })
}

onMounted(async () => {
  await nextTick()
  if (!frame.value) return
  observer = new ResizeObserver((entries) => {
    const size = entries[0]?.contentRect
    if (!size) return
    const width = Math.ceil(size.width)
    const height = Math.ceil(size.height)
    if (width === observedWidth && height === observedHeight) return
    observedWidth = width
    observedHeight = height
    scheduleMeasurement()
  })
  observer.observe(frame.value)
  scheduleMeasurement()
})

onBeforeUnmount(() => {
  observer?.disconnect()
  if (measureFrame) cancelAnimationFrame(measureFrame)
})
</script>

<template>
  <div ref="frame" class="notice-frame" :data-tone="destructive ? 'error' : 'info'" :role="destructive ? 'alert' : 'status'">
    <NoticeIcon :tone="destructive ? 'error' : 'info'" />
    <ScrollSurface axis="y" class="notice-message"><slot /></ScrollSurface>
    <ActionButton class="notice-dismiss" :aria-label="$t('common.action.close')" @click="emit('dismiss')"><X class="size-4" /></ActionButton>
  </div>
</template>
