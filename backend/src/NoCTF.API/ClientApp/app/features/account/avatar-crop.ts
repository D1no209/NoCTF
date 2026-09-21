export const AVATAR_CROP_SIZE = 360
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

export interface ImageCropViewport {
  width: number
  height: number
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

export function imageCropDrawMetrics(
  imageWidth: number,
  imageHeight: number,
  viewport: ImageCropViewport,
  state: AvatarCropState,
): AvatarDrawMetrics {
  const rotation = normalizedRotation(state.rotation)
  const quarterTurn = rotation === 90 || rotation === 270
  const rotatedWidth = quarterTurn ? imageHeight : imageWidth
  const rotatedHeight = quarterTurn ? imageWidth : imageHeight
  const baseScale = Math.max(viewport.width / rotatedWidth, viewport.height / rotatedHeight)
  const zoom = clamp(state.zoom, AVATAR_MIN_ZOOM, AVATAR_MAX_ZOOM)
  const scale = baseScale * zoom
  const maximumX = Math.max(0, (rotatedWidth * scale - viewport.width) / 2)
  const maximumY = Math.max(0, (rotatedHeight * scale - viewport.height) / 2)

  return {
    scale,
    offsetX: clamp(state.offsetX, -maximumX, maximumX),
    offsetY: clamp(state.offsetY, -maximumY, maximumY),
    maximumX,
    maximumY,
  }
}

export function avatarDrawMetrics(
  imageWidth: number,
  imageHeight: number,
  viewportSize: number,
  state: AvatarCropState,
): AvatarDrawMetrics {
  return imageCropDrawMetrics(
    imageWidth,
    imageHeight,
    { width: viewportSize, height: viewportSize },
    state,
  )
}

export function clampImageCropState(
  imageWidth: number,
  imageHeight: number,
  viewport: ImageCropViewport,
  state: AvatarCropState,
): AvatarCropState {
  const rotation = normalizedRotation(state.rotation)
  const zoom = clamp(state.zoom, AVATAR_MIN_ZOOM, AVATAR_MAX_ZOOM)
  const metrics = imageCropDrawMetrics(imageWidth, imageHeight, viewport, {
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

export function clampAvatarCropState(
  imageWidth: number,
  imageHeight: number,
  viewportSize: number,
  state: AvatarCropState,
): AvatarCropState {
  return clampImageCropState(
    imageWidth,
    imageHeight,
    { width: viewportSize, height: viewportSize },
    state,
  )
}

export function moveImageCrop(
  imageWidth: number,
  imageHeight: number,
  viewport: ImageCropViewport,
  state: AvatarCropState,
  deltaX: number,
  deltaY: number,
) {
  return clampImageCropState(imageWidth, imageHeight, viewport, {
    ...state,
    offsetX: state.offsetX + finiteOr(deltaX, 0),
    offsetY: state.offsetY + finiteOr(deltaY, 0),
  })
}

export function moveAvatarCrop(
  imageWidth: number,
  imageHeight: number,
  viewportSize: number,
  state: AvatarCropState,
  deltaX: number,
  deltaY: number,
) {
  return moveImageCrop(
    imageWidth,
    imageHeight,
    { width: viewportSize, height: viewportSize },
    state,
    deltaX,
    deltaY,
  )
}

export function zoomImageCropAtPoint(
  imageWidth: number,
  imageHeight: number,
  viewport: ImageCropViewport,
  state: AvatarCropState,
  requestedZoom: number,
  pointerX: number,
  pointerY: number,
) {
  const current = clampImageCropState(imageWidth, imageHeight, viewport, state)
  const zoom = clamp(requestedZoom, AVATAR_MIN_ZOOM, AVATAR_MAX_ZOOM)
  const ratio = zoom / current.zoom

  return clampImageCropState(imageWidth, imageHeight, viewport, {
    ...current,
    zoom,
    offsetX: pointerX - (pointerX - current.offsetX) * ratio,
    offsetY: pointerY - (pointerY - current.offsetY) * ratio,
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
  return zoomImageCropAtPoint(
    imageWidth,
    imageHeight,
    { width: viewportSize, height: viewportSize },
    state,
    requestedZoom,
    pointerX,
    pointerY,
  )
}

export function drawImageCrop(
  canvas: HTMLCanvasElement,
  image: CanvasImageSource & { width: number, height: number },
  viewport: ImageCropViewport,
  state: AvatarCropState,
) {
  const context = canvas.getContext('2d')
  if (!context)
    throw new Error('Canvas 2D context is unavailable.')

  const outputScale = canvas.width / viewport.width
  const normalized = clampImageCropState(
    image.width,
    image.height,
    viewport,
    state,
  )
  const metrics = imageCropDrawMetrics(
    image.width,
    image.height,
    viewport,
    normalized,
  )

  context.clearRect(0, 0, canvas.width, canvas.height)
  context.save()
  context.translate(
    canvas.width / 2 + metrics.offsetX * outputScale,
    canvas.height / 2 + metrics.offsetY * outputScale,
  )
  context.rotate((normalized.rotation * Math.PI) / 180)
  context.scale(metrics.scale * outputScale, metrics.scale * outputScale)
  context.drawImage(image, -image.width / 2, -image.height / 2)
  context.restore()
}

export function drawAvatarCrop(
  canvas: HTMLCanvasElement,
  image: CanvasImageSource & { width: number, height: number },
  state: AvatarCropState,
) {
  drawImageCrop(
    canvas,
    image,
    { width: AVATAR_CROP_SIZE, height: AVATAR_CROP_SIZE },
    state,
  )
}
