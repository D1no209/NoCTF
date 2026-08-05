<script setup lang="ts">
import { Loader2, RotateCcw, RotateCw, Scan } from 'lucide-vue-next'
import { computed, nextTick, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  AVATAR_CROP_SIZE,
  AVATAR_OUTPUT_SIZE,
  clampAvatarCropState,
  drawAvatarCrop,
  moveAvatarCrop,
  zoomAvatarCropAtPoint,
} from './avatarCrop'

const props = defineProps<{
  open: boolean
  file: File | null
  saving: boolean
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  'save': [file: File]
}>()

const { t } = useI18n()
const previewFrame = ref<HTMLDivElement | null>(null)
const preview = ref<HTMLCanvasElement | null>(null)
const sourceImage = ref<HTMLImageElement | null>(null)
const loadError = ref(false)
const dragging = ref(false)
let sourceUrl: string | null = null
let drag: {
  pointerId: number
  clientX: number
  clientY: number
  offsetX: number
  offsetY: number
} | null = null

const crop = reactive({
  zoom: 1,
  offsetX: 0,
  offsetY: 0,
  rotation: 0,
})

const zoomPercent = computed(() => Math.round(crop.zoom * 100))

function assignCrop(next: typeof crop) {
  crop.zoom = next.zoom
  crop.offsetX = next.offsetX
  crop.offsetY = next.offsetY
  crop.rotation = next.rotation
}

function resetCrop() {
  assignCrop({ zoom: 1, offsetX: 0, offsetY: 0, rotation: 0 })
}

function releaseSource() {
  if (sourceUrl)
    URL.revokeObjectURL(sourceUrl)
  sourceUrl = null
  sourceImage.value = null
}

async function loadFile(file: File | null) {
  releaseSource()
  resetCrop()
  loadError.value = false
  if (!file)
    return

  sourceUrl = URL.createObjectURL(file)
  const image = new Image()
  image.decoding = 'async'
  image.onload = async () => {
    sourceImage.value = image
    await nextTick()
    renderPreview()
  }
  image.onerror = () => {
    loadError.value = true
  }
  image.src = sourceUrl
}

function renderPreview() {
  if (!preview.value || !sourceImage.value)
    return
  drawAvatarCrop(preview.value, sourceImage.value, crop)
}

function normalizedCrop(next: typeof crop) {
  const image = sourceImage.value
  return image
    ? clampAvatarCropState(image.width, image.height, AVATAR_CROP_SIZE, next)
    : next
}

function rotate(delta: number) {
  assignCrop(normalizedCrop({ ...crop, rotation: crop.rotation + delta }))
}

function cropPoint(event: WheelEvent) {
  const bounds = previewFrame.value?.getBoundingClientRect()
  if (!bounds)
    return { x: 0, y: 0 }
  const scale = AVATAR_CROP_SIZE / bounds.width
  return {
    x: (event.clientX - bounds.left) * scale - AVATAR_CROP_SIZE / 2,
    y: (event.clientY - bounds.top) * scale - AVATAR_CROP_SIZE / 2,
  }
}

function handleWheel(event: WheelEvent) {
  const image = sourceImage.value
  if (!image)
    return
  const point = cropPoint(event)
  const requestedZoom = crop.zoom * Math.exp(-event.deltaY * 0.0015)
  assignCrop(zoomAvatarCropAtPoint(
    image.width,
    image.height,
    AVATAR_CROP_SIZE,
    crop,
    requestedZoom,
    point.x,
    point.y,
  ))
}

function startDrag(event: PointerEvent) {
  if (!sourceImage.value || event.button !== 0)
    return
  previewFrame.value?.setPointerCapture(event.pointerId)
  drag = {
    pointerId: event.pointerId,
    clientX: event.clientX,
    clientY: event.clientY,
    offsetX: crop.offsetX,
    offsetY: crop.offsetY,
  }
  dragging.value = true
}

function continueDrag(event: PointerEvent) {
  const image = sourceImage.value
  const bounds = previewFrame.value?.getBoundingClientRect()
  if (!drag || drag.pointerId !== event.pointerId || !image || !bounds)
    return
  const scale = AVATAR_CROP_SIZE / bounds.width
  assignCrop(moveAvatarCrop(
    image.width,
    image.height,
    AVATAR_CROP_SIZE,
    { ...crop, offsetX: drag.offsetX, offsetY: drag.offsetY },
    (event.clientX - drag.clientX) * scale,
    (event.clientY - drag.clientY) * scale,
  ))
}

function finishDrag(event: PointerEvent) {
  if (!drag || drag.pointerId !== event.pointerId)
    return
  if (previewFrame.value?.hasPointerCapture(event.pointerId))
    previewFrame.value.releasePointerCapture(event.pointerId)
  drag = null
  dragging.value = false
}

async function createCroppedFile() {
  if (!sourceImage.value || props.saving)
    return

  const canvas = document.createElement('canvas')
  canvas.width = AVATAR_OUTPUT_SIZE
  canvas.height = AVATAR_OUTPUT_SIZE
  drawAvatarCrop(canvas, sourceImage.value, crop)
  const webp = await new Promise<Blob | null>(resolve =>
    canvas.toBlob(resolve, 'image/webp', 0.9),
  )
  const blob
    = webp ?? (await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/png')))
  if (!blob)
    throw new Error('Avatar crop could not be encoded.')

  emit('save', new File([blob], webp ? 'avatar.webp' : 'avatar.png', { type: blob.type }))
}

watch(() => props.file, loadFile, { immediate: true })
watch(crop, renderPreview, { deep: true, flush: 'post' })
onBeforeUnmount(releaseSource)
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-[620px]">
      <DialogHeader>
        <DialogTitle>{{ t('profile.avatarEditorTitle') }}</DialogTitle>
        <DialogDescription>{{ t('profile.avatarEditorDescription') }}</DialogDescription>
      </DialogHeader>

      <div class="grid gap-5 py-2 md:grid-cols-[320px_minmax(0,1fr)]">
        <div
          ref="previewFrame"
          class="relative aspect-square w-[320px] max-w-full touch-none select-none overflow-hidden border-2 border-border bg-muted outline-none focus-visible:border-primary focus-visible:ring-2 focus-visible:ring-primary/25"
          :class="dragging ? 'cursor-grabbing' : 'cursor-grab'"
          role="group"
          tabindex="0"
          :aria-label="t('profile.avatarCropPreview')"
          @dragstart.prevent
          @wheel.prevent="handleWheel"
          @pointerdown="startDrag"
          @pointermove="continueDrag"
          @pointerup="finishDrag"
          @pointercancel="finishDrag"
          @lostpointercapture="finishDrag"
        >
          <canvas
            ref="preview"
            :width="AVATAR_CROP_SIZE"
            :height="AVATAR_CROP_SIZE"
            class="size-full"
            :aria-label="t('profile.avatarCropPreview')"
          />
          <div
            class="pointer-events-none absolute inset-4 rounded-full border-2 border-dashed border-white/90 shadow-[0_0_0_999px_rgba(0,0,0,0.28)]"
          />
          <div
            v-if="loadError"
            class="absolute inset-0 grid place-items-center bg-background p-6 text-center text-sm text-destructive"
          >
            {{ t('profile.avatarLoadError') }}
          </div>
        </div>

        <div class="space-y-5">
          <div class="border-l-4 border-primary bg-muted px-3 py-2 text-xs leading-5 text-muted-foreground">
            {{ t('profile.avatarInteractionHint') }}
          </div>
          <div class="flex items-center justify-between border-b-2 border-border pb-2 text-sm">
            <span>{{ t('profile.avatarZoomStatus') }}</span>
            <span class="font-mono font-bold tabular-nums">{{ zoomPercent }}%</span>
          </div>
          <div class="space-y-2">
            <div class="text-sm font-medium">
              {{ t('profile.avatarRotation') }}
            </div>
            <div class="grid grid-cols-2 gap-2">
              <Button variant="outline" type="button" @click="rotate(-90)">
                <RotateCcw class="size-4" />
                −90°
              </Button>
              <Button variant="outline" type="button" @click="rotate(90)">
                <RotateCw class="size-4" />
                +90°
              </Button>
            </div>
          </div>
          <Button variant="ghost" size="sm" type="button" @click="resetCrop">
            <Scan class="size-4" />
            {{ t('profile.avatarReset') }}
          </Button>
        </div>
      </div>

      <DialogFooter>
        <Button variant="outline" :disabled="saving" @click="emit('update:open', false)">
          {{ t('common.cancel') }}
        </Button>
        <Button :disabled="saving || !sourceImage || loadError" @click="createCroppedFile">
          <Loader2 v-if="saving" class="size-4 animate-spin" />
          {{ t('profile.avatarSave') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
