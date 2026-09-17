export interface AuthThemeArtwork {
  src: string
  width: number
  height: number
  corner: 'left-bottom' | 'right-top'
}

const artworkLoaders = {
  dark: async (): Promise<AuthThemeArtwork> => ({
    src: (await import('~/assets/images/auth/login-character.png')).default,
    width: 1200,
    height: 835,
    corner: 'left-bottom',
  }),
  light: async (): Promise<AuthThemeArtwork> => ({
    src: (await import('~/assets/images/auth/register-character.png')).default,
    width: 574,
    height: 466,
    corner: 'right-top',
  }),
}

/** Loads only the artwork used by the active color scheme. */
export function useAuthThemeArtwork() {
  const { isDark } = useTheme()
  const authArtwork = shallowRef<AuthThemeArtwork | null>(null)
  let loadSequence = 0

  watch(isDark, async (dark) => {
    const sequence = ++loadSequence
    authArtwork.value = null
    const artwork = await artworkLoaders[dark ? 'dark' : 'light']()
    if (sequence === loadSequence)
      authArtwork.value = artwork
  }, { immediate: true })

  return { authArtwork }
}
