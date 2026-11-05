import type {
  NoCtfApplicationLiveSoloMediaLiveSoloVideoPolicy as VideoPolicy,
  NoCtfapiEndpointsAdministrationPlatformLiveSoloVideoConfigurationRulesResponse as VideoRules,
  NoCtfapiEndpointsAdministrationPlatformPlatformLiveSoloVideoPatchRequest as VideoPatch,
} from '../../api'

export interface PlatformVideoDraft {
  width: number | null
  height: number | null
  fps: number | null
  kilobitsPerSecond: number | null
}

export function platformVideoDraft(policy: VideoPolicy): PlatformVideoDraft {
  return { width: policy.maximumWidth ?? null, height: policy.maximumHeight ?? null,
    fps: policy.maximumFramesPerSecond ?? null,
    kilobitsPerSecond: policy.maximumBitrateBitsPerSecond == null ? null : policy.maximumBitrateBitsPerSecond / 1000 }
}

export function platformVideoPatch(draft: PlatformVideoDraft, rules: VideoRules | null): VideoPatch | null {
  function within(value: number | null, minimum: number | undefined, maximum: number | undefined): value is number {
    return value !== null && Number.isSafeInteger(value) && minimum !== undefined && maximum !== undefined
      && value >= minimum && value <= maximum
  }
  if (!rules || !within(draft.width, rules.minimumWidth, rules.maximumWidth) || draft.width % 2 !== 0
    || !within(draft.height, rules.minimumHeight, rules.maximumHeight) || draft.height % 2 !== 0
    || !within(draft.fps, rules.minimumFramesPerSecond, rules.maximumFramesPerSecond)
    || draft.kilobitsPerSecond === null || !Number.isSafeInteger(draft.kilobitsPerSecond)
    || !within(draft.kilobitsPerSecond * 1000, rules.minimumBitrateBitsPerSecond, rules.maximumBitrateBitsPerSecond)) return null
  return { maximumWidth: draft.width, maximumHeight: draft.height, maximumFramesPerSecond: draft.fps,
    maximumBitrateBitsPerSecond: draft.kilobitsPerSecond * 1000 }
}

export function platformVideoDirty(draft: PlatformVideoDraft, saved: VideoPolicy | null): boolean {
  if (!saved) return false
  const previous = platformVideoDraft(saved)
  return draft.width !== previous.width || draft.height !== previous.height || draft.fps !== previous.fps
    || draft.kilobitsPerSecond !== previous.kilobitsPerSecond
}
