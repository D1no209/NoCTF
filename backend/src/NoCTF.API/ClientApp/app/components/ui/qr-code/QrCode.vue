<script setup lang="ts">
import { ref, watch, onMounted, onBeforeUnmount } from 'vue'
import QRCode from 'qrcode'
import { themeColor } from '~/utils/theme-color'
const props = defineProps<{ value: string; label: string }>()
const canvas = ref<HTMLCanvasElement | null>(null)
const failed = ref(false)
let observer: MutationObserver | undefined
async function draw() {
  if (!canvas.value || !props.value) return
  try {
    await QRCode.toCanvas(canvas.value, props.value, { width: 224, margin: 4, errorCorrectionLevel: 'M',
      color: { dark: themeColor('--qr-code-dark', canvas.value), light: themeColor('--qr-code-light', canvas.value) } })
    failed.value = false
  } catch { failed.value = true }
}
watch(() => props.value, draw)
onMounted(() => {
  void draw()
  observer = new MutationObserver(() => void draw())
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['class', 'style'] })
})
onBeforeUnmount(() => observer?.disconnect())
</script>
<template>
  <canvas v-show="!failed" ref="canvas" role="img" :aria-label="label" class="mx-auto size-56 max-w-full rounded-lg" />
</template>
