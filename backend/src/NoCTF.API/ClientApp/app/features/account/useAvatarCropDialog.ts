import { toRefs } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { RotateCcw, RotateCw, Scan } from '@lucide/vue'
import type { AvatarCropState } from './avatar-crop'
import {
  AVATAR_CROP_SIZE,
  AVATAR_OUTPUT_SIZE,
  clampImageCropState,
  drawImageCrop,
  moveImageCrop,
  zoomImageCropAtPoint,
} from './avatar-crop'

export const PROFILE_COVER_CROP_WIDTH = 720
export const PROFILE_COVER_CROP_HEIGHT = 180
export const PROFILE_COVER_OUTPUT_WIDTH = 1600
export const PROFILE_COVER_OUTPUT_HEIGHT = 400

/** Owns state, effects and commands for AvatarCropDialog. */
export function useAvatarCropDialog(props: Readonly<{
  open: boolean
  file: File | null
  saving: boolean
  variant?: 'avatar' | 'profile-cover'
}>,
emit: { (event: "update:open", ...args: [value: boolean]): void; (event: "save", ...args: [file: File]): void; (event: "error", ...args: [error: Error]): void }) {
  const previewFrame = ref<HTMLDivElement | null>(null)

  const preview = ref<HTMLCanvasElement | null>(null)

  const sourceImage = ref<HTMLImageElement | null>(null)

  const loadError = ref(false)

  const dragging = ref(false)

  const encoding = ref(false)

  const profileCover = computed(() => props.variant === 'profile-cover')

  const cropWidth = computed(() => profileCover.value
    ? PROFILE_COVER_CROP_WIDTH
    : AVATAR_CROP_SIZE)

  const cropHeight = computed(() => profileCover.value
    ? PROFILE_COVER_CROP_HEIGHT
    : AVATAR_CROP_SIZE)

  const outputWidth = computed(() => profileCover.value
    ? PROFILE_COVER_OUTPUT_WIDTH
    : AVATAR_OUTPUT_SIZE)

  const outputHeight = computed(() => profileCover.value
    ? PROFILE_COVER_OUTPUT_HEIGHT
    : AVATAR_OUTPUT_SIZE)

  const cropViewport = computed(() => ({
    width: cropWidth.value,
    height: cropHeight.value,
  }))

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
    drawImageCrop(preview.value, sourceImage.value, cropViewport.value, crop)
  }

  function normalizedCrop(next: AvatarCropState) {
    const image = sourceImage.value
    return image
      ? clampImageCropState(image.width, image.height, cropViewport.value, next)
      : next
  }

  function rotate(delta: number) {
    assignCrop(normalizedCrop({ ...crop, rotation: crop.rotation + delta }))
  }

  function cropPoint(event: WheelEvent) {
    const bounds = previewFrame.value?.getBoundingClientRect()
    if (!bounds || bounds.width <= 0)
      return { x: 0, y: 0 }
    const scaleX = cropWidth.value / bounds.width
    const scaleY = cropHeight.value / bounds.height
    return {
      x: (event.clientX - bounds.left) * scaleX - cropWidth.value / 2,
      y: (event.clientY - bounds.top) * scaleY - cropHeight.value / 2,
    }
  }

  function handleWheel(event: WheelEvent) {
    const image = sourceImage.value
    if (!image)
      return
    const point = cropPoint(event)
    const requestedZoom = crop.zoom * Math.exp(-event.deltaY * 0.0015)
    assignCrop(zoomImageCropAtPoint(
      image.width,
      image.height,
      cropViewport.value,
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
    const scaleX = cropWidth.value / bounds.width
    const scaleY = cropHeight.value / bounds.height
    assignCrop(moveImageCrop(
      image.width,
      image.height,
      cropViewport.value,
      { ...crop, offsetX: drag.offsetX, offsetY: drag.offsetY },
      (event.clientX - drag.clientX) * scaleX,
      (event.clientY - drag.clientY) * scaleY,
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
      canvas.width = outputWidth.value
      canvas.height = outputHeight.value
      drawImageCrop(canvas, sourceImage.value, cropViewport.value, crop)
      const webp = await new Promise<Blob | null>(resolve =>
        canvas.toBlob(resolve, 'image/webp', 0.9),
      )
      const blob = webp
        ?? (await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/png')))
      if (!blob)
        throw new Error(translate(profileCover.value
          ? 'profile.unableToEncodeCover'
          : 'ui.unableToEncodeCroppedAvatar'))

      const fileBase = profileCover.value ? 'profile-cover' : 'avatar'
      emit('save', new File([blob], `${fileBase}.${webp ? 'webp' : 'png'}`, { type: blob.type }))
    }
    catch (error) {
      emit('error', error instanceof Error ? error : new Error(translate(profileCover.value
        ? 'profile.coverCroppingFailed'
        : 'ui.avatarCroppingFailed')))
    }
    finally {
      encoding.value = false
    }
  }

  watch(() => props.file, loadFile, { immediate: true })

  watch(crop, renderPreview, { deep: true, flush: 'post' })

  onBeforeUnmount(releaseSource)

  function setPreviewFrameRef(element: Element | ComponentPublicInstance | null) { previewFrame.value = (element instanceof Element ? element : element?.$el ?? null) as typeof previewFrame.value }

  function setPreviewRef(element: Element | ComponentPublicInstance | null) { preview.value = (element instanceof Element ? element : element?.$el ?? null) as typeof preview.value }

  return {
      ...toRefs(props),
      RotateCcw,
      RotateCw,
      Scan,
      profileCover,
      cropWidth,
      cropHeight,
      emit,
      previewFrame,
      preview,
      sourceImage,
      loadError,
      dragging,
      encoding,
      zoomPercent,
      resetCrop,
      rotate,
      handleWheel,
      startDrag,
      continueDrag,
      finishDrag,
      createCroppedFile,
      setPreviewFrameRef,
      setPreviewRef
    }
}

export type AvatarCropDialogViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAvatarCropDialog>>>
