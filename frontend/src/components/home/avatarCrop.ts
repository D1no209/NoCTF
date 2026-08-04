export const AVATAR_CROP_SIZE = 320
export const AVATAR_OUTPUT_SIZE = 512

export interface AvatarCropState {
  zoom: number
  positionX: number
  positionY: number
  rotation: number
}

export interface AvatarDrawMetrics {
  scale: number
  offsetX: number
  offsetY: number
}

export function avatarDrawMetrics(
  imageWidth: number,
  imageHeight: number,
  viewportSize: number,
  state: AvatarCropState,
): AvatarDrawMetrics {
  const quarterTurns = Math.abs(Math.round(state.rotation / 90)) % 2
  const rotatedWidth = quarterTurns ? imageHeight : imageWidth
  const rotatedHeight = quarterTurns ? imageWidth : imageHeight
  const baseScale = Math.max(viewportSize / rotatedWidth, viewportSize / rotatedHeight)
  const scale = baseScale * Math.max(1, state.zoom)
  const maximumX = Math.max(0, (rotatedWidth * scale - viewportSize) / 2)
  const maximumY = Math.max(0, (rotatedHeight * scale - viewportSize) / 2)

  return {
    scale,
    offsetX: Math.max(-1, Math.min(1, state.positionX)) * maximumX,
    offsetY: Math.max(-1, Math.min(1, state.positionY)) * maximumY,
  }
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
  const metrics = avatarDrawMetrics(image.width, image.height, AVATAR_CROP_SIZE, state)

  context.clearRect(0, 0, outputSize, outputSize)
  context.save()
  context.translate(
    outputSize / 2 + metrics.offsetX * viewportRatio,
    outputSize / 2 + metrics.offsetY * viewportRatio,
  )
  context.rotate((state.rotation * Math.PI) / 180)
  context.scale(metrics.scale * viewportRatio, metrics.scale * viewportRatio)
  context.drawImage(image, -image.width / 2, -image.height / 2)
  context.restore()
}
