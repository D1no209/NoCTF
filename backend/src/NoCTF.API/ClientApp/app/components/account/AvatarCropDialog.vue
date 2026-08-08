<script setup lang="ts">
import { RotateCcw, RotateCw, Scan } from '@lucide/vue'
import type { AvatarCropState } from './avatar-crop'
import {
  AVATAR_CROP_SIZE,
  AVATAR_OUTPUT_SIZE,
  clampAvatarCropState,
  drawAvatarCrop,
  moveAvatarCrop,
  zoomAvatarCropAtPoint,
} from './avatar-crop'

const props = defineProps<{
  open: boolean
  file: File | null
  saving: boolean
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  'save': [file: File]
  'error': [error: Error]
}>()

const previewFrame = ref<HTMLDivElement | null>(null)
const preview = ref<HTMLCanvasElement | null>(null)
const sourceImage = ref<HTMLImageElement | null>(null)
const loadError = ref(false)
const dragging = ref(false)
const encoding = ref(false)
let sourceUrl: string | null = null
let loadGeneration = 0
let drag: {
  pointerId: number
  clientX: number
  clientY: number
  offsetX: number
  offsetY: number
} | null = null

const crop = reactive<AvatarCropState>({
  zoom: 1,
  offsetX: 0,
  offsetY: 0,
  rotation: 0,
})

const zoomPercent = computed(() => Math.round(crop.zoom * 100))

function assignCrop(next: AvatarCropState) {
  crop.zoom = next.zoom
  crop.offsetX = next.offsetX
  crop.offsetY = next.offsetY
  crop.rotation = next.rotation
}

function resetCrop() {
  assignCrop({ zoom: 1, offsetX: 0, offsetY: 0, rotation: 0 })
}

function releaseSource() {
  loadGeneration += 1
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

  const generation = loadGeneration
  sourceUrl = URL.createObjectURL(file)
  const image = new Image()
  image.decoding = 'async'
  image.onload = async () => {
    if (generation !== loadGeneration)
      return
    sourceImage.value = image
    await nextTick()
    renderPreview()
  }
  image.onerror = () => {
    if (generation === loadGeneration)
      loadError.value = true
  }
  image.src = sourceUrl
}

function renderPreview() {
  if (!preview.value || !sourceImage.value)
    return
  drawAvatarCrop(preview.value, sourceImage.value, crop)
}

function normalizedCrop(next: AvatarCropState) {
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
  if (!bounds || bounds.width <= 0)
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
  if (!drag || drag.pointerId !== event.pointerId || !image || !bounds || bounds.width <= 0)
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
  if (!sourceImage.value || props.saving || encoding.value)
    return

  encoding.value = true
  try {
    const canvas = document.createElement('canvas')
    canvas.width = AVATAR_OUTPUT_SIZE
    canvas.height = AVATAR_OUTPUT_SIZE
    drawAvatarCrop(canvas, sourceImage.value, crop)
    const webp = await new Promise<Blob | null>(resolve =>
      canvas.toBlob(resolve, 'image/webp', 0.9),
    )
    const blob = webp
      ?? (await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/png')))
    if (!blob)
      throw new Error('无法编码裁剪后的头像')

    emit('save', new File([blob], webp ? 'avatar.webp' : 'avatar.png', { type: blob.type }))
  }
  catch (error) {
    emit('error', error instanceof Error ? error : new Error('头像裁剪失败'))
  }
  finally {
    encoding.value = false
  }
}

watch(() => props.file, loadFile, { immediate: true })
watch(crop, renderPreview, { deep: true, flush: 'post' })
onBeforeUnmount(releaseSource)
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-[760px]">
      <DialogHeader>
        <DialogTitle>裁剪头像</DialogTitle>
        <DialogDescription>
          在图片上拖动调整位置，滚动鼠标滚轮缩放；虚线圆内是头像显示区域。
        </DialogDescription>
      </DialogHeader>

      <div class="grid gap-5 md:grid-cols-[minmax(0,360px)_minmax(0,1fr)]">
        <div
          ref="previewFrame"
          class="relative mx-auto aspect-square w-full max-w-[360px] touch-none select-none overflow-hidden rounded-lg border bg-muted outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/40"
          :class="dragging ? 'cursor-grabbing' : 'cursor-grab'"
          role="group"
          tabindex="0"
          aria-label="头像裁剪预览"
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
            aria-label="头像裁剪画布"
          />
          <div
            class="pointer-events-none absolute inset-0 rounded-full border-2 border-dashed border-background/90 shadow-[0_0_0_999px_oklch(0_0_0/0.3)]"
          />
          <div
            v-if="loadError"
            class="absolute inset-0 grid place-items-center bg-background p-6 text-center text-sm text-destructive"
          >
            无法读取这张图片，请换用 JPEG、PNG 或 WebP。
          </div>
        </div>

        <div class="flex min-w-0 flex-col gap-5">
          <div class="rounded-lg border bg-muted/50 p-3 text-sm leading-6 text-muted-foreground">
            滚轮以指针位置为中心缩放。拖动方向与图片移动方向一致，图片不会离开裁剪范围。
          </div>
          <div class="flex items-center justify-between border-b pb-3 text-sm">
            <span class="text-muted-foreground">当前缩放</span>
            <span class="font-mono font-semibold tabular-nums">{{ zoomPercent }}%</span>
          </div>
          <div class="flex flex-col gap-2">
            <span class="text-sm font-medium">旋转</span>
            <div class="grid grid-cols-2 gap-2">
              <Button variant="outline" type="button" @click="rotate(-90)">
                <RotateCcw />
                -90°
              </Button>
              <Button variant="outline" type="button" @click="rotate(90)">
                <RotateCw />
                +90°
              </Button>
            </div>
          </div>
          <Button variant="ghost" type="button" class="self-start" @click="resetCrop">
            <Scan />
            重置裁剪
          </Button>
        </div>
      </div>

      <DialogFooter>
        <Button variant="outline" :disabled="saving || encoding" @click="emit('update:open', false)">
          取消
        </Button>
        <Button :disabled="saving || encoding || !sourceImage || loadError" @click="createCroppedFile">
          <Spinner v-if="saving || encoding" data-icon="inline-start" />
          裁剪并上传
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
