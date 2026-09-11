import { describe, expect, test } from 'bun:test'
import { exceedsUploadLimit } from '../app/features/account/upload-limits'
import { readFeatureSource } from './support/feature-source'

describe('account image upload limits', () => {
  test('compares exact byte lengths only when the server capability is available', () => {
    expect(exceedsUploadLimit(101, 100)).toBeTrue()
    expect(exceedsUploadLimit(100, 100)).toBeFalse()
    expect(exceedsUploadLimit(101, null)).toBeFalse()
    expect(exceedsUploadLimit(101, undefined)).toBeFalse()
    expect(exceedsUploadLimit(101, 0)).toBeFalse()
    expect(exceedsUploadLimit(101, Number.MAX_SAFE_INTEGER + 1)).toBeFalse()
  })

  test('prevalidates the uploaded crop and wallpaper in the feature controller', () => {
    const source = readFeatureSource(new URL('../app/features/account/AccountPanel.vue', import.meta.url))
    expect(source).toContain('platformConfiguration.value?.imageUploadLimits?.maximumAvatarBytes')
    expect(source).toContain('platformConfiguration.value?.imageUploadLimits?.maximumWallpaperBytes')
    expect(source).toContain('exceedsUploadLimit(file.size, maximumAvatarBytes.value)')
    expect(source).toContain('exceedsUploadLimit(file.size, maximumWallpaperBytes.value)')
    expect(source).toContain('avatarRequirements')
    expect(source).toContain('wallpaperRequirements')
  })
})
