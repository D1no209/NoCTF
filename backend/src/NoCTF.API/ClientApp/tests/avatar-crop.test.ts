import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import {
  avatarDrawMetrics,
  clampAvatarCropState,
  moveAvatarCrop,
  zoomAvatarCropAtPoint,
} from "../app/features/account/avatar-crop"

describe('avatar crop geometry', () => {
  test('uses rotated dimensions for a quarter turn', () => {
    expect(
      avatarDrawMetrics(720, 360, 360, {
        zoom: 1,
        offsetX: 0,
        offsetY: 400,
        rotation: 90,
      }),
    ).toEqual({ scale: 1, offsetX: 0, offsetY: 180, maximumX: 0, maximumY: 180 })
  })

  test('reclamps both axes after rotation without exposing empty pixels', () => {
    expect(
      clampAvatarCropState(720, 360, 360, {
        zoom: 1,
        offsetX: 150,
        offsetY: 150,
        rotation: 90,
      }),
    ).toEqual({ zoom: 1, offsetX: 0, offsetY: 150, rotation: 90 })
  })

  test('wheel zoom preserves the image point below the pointer before boundary clamp', () => {
    expect(
      zoomAvatarCropAtPoint(720, 720, 360, {
        zoom: 1,
        offsetX: 0,
        offsetY: 0,
        rotation: 0,
      }, 2, 60, -40),
    ).toEqual({ zoom: 2, offsetX: -60, offsetY: 40, rotation: 0 })
  })

  test('pointer movement is boundary-clamped and rejects non-finite state', () => {
    expect(
      moveAvatarCrop(720, 360, 360, {
        zoom: Number.NaN,
        offsetX: 120,
        offsetY: 0,
        rotation: 0,
      }, 100, Number.POSITIVE_INFINITY),
    ).toEqual({ zoom: 1, offsetX: 180, offsetY: 0, rotation: 0 })
  })
})

describe('account avatar integration', () => {
  test('selects the native file input by change event before opening the crop dialog', async () => {
    const accountPanel = await sourceFile(
      new URL('../app/features/account/AccountPanel.vue', import.meta.url),
    ).text()

    expect(accountPanel).toContain('@change="selectAvatar"')
    expect(accountPanel).toContain("<component :is=\"AvatarCropDialog\"")
    expect(accountPanel).toContain('@save="uploadAvatar"')
    expect(accountPanel).not.toContain('<Input ref="avatarInput"')
  })
})
