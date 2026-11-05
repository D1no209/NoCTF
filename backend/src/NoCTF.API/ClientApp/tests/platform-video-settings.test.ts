import { expect, test } from 'bun:test'
import { platformVideoDraft, platformVideoDirty, platformVideoPatch } from '../app/features/admin/platform-video'

const rules = { minimumWidth: 320, maximumWidth: 3840, minimumHeight: 180, maximumHeight: 2160,
  minimumFramesPerSecond: 1, maximumFramesPerSecond: 30, minimumBitrateBitsPerSecond: 128_000, maximumBitrateBitsPerSecond: 8_000_000 }
const policy = { maximumWidth: 1280, maximumHeight: 720, maximumFramesPerSecond: 10, maximumBitrateBitsPerSecond: 1_000_000 }

test('video settings show Kbit/s and submit the selected policy in bit/s', () => {
  const draft = platformVideoDraft(policy)
  expect(draft.kilobitsPerSecond).toBe(1000)
  expect(platformVideoDirty(draft, policy)).toBeFalse()
  draft.kilobitsPerSecond = 512
  expect(platformVideoDirty(draft, policy)).toBeTrue()
  expect(platformVideoPatch(draft, rules)).toEqual({ maximumWidth: 1280, maximumHeight: 720,
    maximumFramesPerSecond: 10, maximumBitrateBitsPerSecond: 512_000 })
})

test('video settings validate even dimensions and the limits supplied by the server', () => {
  const draft = platformVideoDraft(policy)
  for (const invalid of [{ width: 641 }, { width: 318 }, { height: 721 }, { height: 2162 }, { fps: 31 }, { fps: 0 },
    { kilobitsPerSecond: 127 }, { kilobitsPerSecond: 8001 }, { kilobitsPerSecond: Number.NaN }, { fps: null }])
    expect(platformVideoPatch({ ...draft, ...invalid }, rules)).toBeNull()
  expect(platformVideoPatch(draft, null)).toBeNull()
  expect(platformVideoPatch(draft, { ...rules, maximumWidth: undefined })).toBeNull()
  expect(platformVideoPatch({ width: 320, height: 180, fps: 1, kilobitsPerSecond: 128 }, rules)).not.toBeNull()
  expect(platformVideoPatch({ width: 3840, height: 2160, fps: 30, kilobitsPerSecond: 8000 }, rules)).not.toBeNull()
})
