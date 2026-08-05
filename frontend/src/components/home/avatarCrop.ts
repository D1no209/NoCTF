export const AVATAR_CROP_SIZE = 320
export const AVATAR_OUTPUT_SIZE = 512
export const AVATAR_MIN_ZOOM = 1
export const AVATAR_MAX_ZOOM = 4

export interface AvatarCropState {
  zoom: number
  offsetX: number
  offsetY: number
  rotation: number
}

export interface AvatarDrawMetrics {
  scale: number
  offsetX: number
  offsetY: number
  maximumX: number
  maximumY: number
}

function finiteOr(value: number, fallback: number) {
  return Number.isFinite(value) ? value : fallback
}

function clamp(value: number, minimum: number, maximum: number) {
  return Math.min(maximum, Math.max(minimum, finiteOr(value, minimum)))
}

function normalizedRotation(rotation: number) {
  const quarterTurns = Math.round(finiteOr(rotation, 0) / 90)
  return ((quarterTurns % 4) + 4) % 4 * 90
}

export function avatarDrawMetrics(
  imageWidth: number,
  imageHeight: number,
  viewportSize: number,
  state: AvatarCropState,
): AvatarDrawMetrics {
  const rotation = normalizedRotation(state.rotation)
  const quarterTurn = rotation === 90 || rotation === 270
  const rotatedWidth = quarterTurn ? imageHeight : imageWidth
  const rotatedHeight = quarterTurn ? imageWidth : imageHeight
  const baseScale = Math.max(viewportSize / rotatedWidth, viewportSize / rotatedHeight)
  const zoom = clamp(state.zoom, AVATAR_MIN_ZOOM, AVATAR_MAX_ZOOM)
  const scale = baseScale * zoom
  const maximumX = Math.max(0, (rotatedWidth * scale - viewportSize) / 2)
  const maximumY = Math.max(0, (rotatedHeight * scale - viewportSize) / 2)

  return {
    scale,
    offsetX: clamp(state.offsetX, -maximumX, maximumX),
    offsetY: clamp(state.offsetY, -maximumY, maximumY),
    maximumX,
    maximumY,
  }
}

export function clampAvatarCropState(
  imageWidth: number,
  imageHeight: number,
  viewportSize: number,
  state: AvatarCropState,
): AvatarCropState {
  const rotation = normalizedRotation(state.rotation)
  const zoom = clamp(state.zoom, AVATAR_MIN_ZOOM, AVATAR_MAX_ZOOM)
  const metrics = avatarDrawMetrics(imageWidth, imageHeight, viewportSize, {
    ...state,
    rotation,
    zoom,
  })
  return {
    zoom,
    offsetX: metrics.offsetX,
    offsetY: metrics.offsetY,
    rotation,
  }
}

export function moveAvatarCrop(
  imageWidth: number,
  imageHeight: number,
  viewportSize: number,
  state: AvatarCropState,
  deltaX: number,
  deltaY: number,
) {
  return clampAvatarCropState(imageWidth, imageHeight, viewportSize, {
    ...state,
    offsetX: state.offsetX + finiteOr(deltaX, 0),
    offsetY: state.offsetY + finiteOr(deltaY, 0),
  })
}

export function zoomAvatarCropAtPoint(
  imageWidth: number,
  imageHeight: number,
  viewportSize: number,
  state: AvatarCropState,
  requestedZoom: number,
  pointerX: number,
  pointerY: number,
) {
  const current = clampAvatarCropState(imageWidth, imageHeight, viewportSize, state)
  const zoom = clamp(requestedZoom, AVATAR_MIN_ZOOM, AVATAR_MAX_ZOOM)
  const ratio = zoom / current.zoom
  return clampAvatarCropState(imageWidth, imageHeight, viewportSize, {
    ...current,
    zoom,
    offsetX: pointerX - (pointerX - current.offsetX) * ratio,
    offsetY: pointerY - (pointerY - current.offsetY) * ratio,
  })
}

export function drawAvatarCrop(
  canvas: HTMLCanvasElement,
  image: CanvasImageSource & { width: number, height: number },
  state: AvatarCropState,
) {
  const context = canvas.getContext('2d')
  if (!context)
    throw new Error('Canvas 2D context is unavailable.')

  const outputSize = canvas.width
  const viewportRatio = outputSize / AVATAR_CROP_SIZE
  const normalized = clampAvatarCropState(
    image.width,
    image.height,
    AVATAR_CROP_SIZE,
    state,
  )
  const metrics = avatarDrawMetrics(
    image.width,
    image.height,
    AVATAR_CROP_SIZE,
    normalized,
  )

  context.clearRect(0, 0, outputSize, outputSize)
  context.save()
  context.translate(
    outputSize / 2 + metrics.offsetX * viewportRatio,
    outputSize / 2 + metrics.offsetY * viewportRatio,
  )
  context.rotate((normalized.rotation * Math.PI) / 180)
  context.scale(metrics.scale * viewportRatio, metrics.scale * viewportRatio)
  context.drawImage(image, -image.width / 2, -image.height / 2)
  context.restore()
}
