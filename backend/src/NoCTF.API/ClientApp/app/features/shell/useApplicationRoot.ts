

/** Owns state, effects and commands for ApplicationRoot. */
export function useApplicationRoot() {
  const { configuration, ensureLoaded } = usePlatform()

  const { isDark } = useTheme()

  useHead(() => ({
    link: configuration.value?.logoUrl
      ? [{ key: 'platform-icon', rel: 'icon', href: configuration.value.logoUrl }]
      : [],
  }))

  void ensureLoaded()

  return {
      isDark
    }
}

export type ApplicationRootViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useApplicationRoot>>>
