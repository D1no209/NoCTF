import { markRaw } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { ImagePlus, UserRound } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { authenticationUploadMyProfileCover, userProfileGet } from '../../../api'
import type { NoCtfapiEndpointsAuthenticationPublicUserProfileResponse } from '../../../api'
import type { echarts } from '../../../utils/echarts'
import { challengeDirectionOptions, directionKey, directionLabel } from '../../../utils/directions'
import { competitionStatusLabel, gameModeLabel } from '../../../utils/labels'
import { exceedsUploadLimit } from '../../account/upload-limits'
import AvatarCropDialogComponent from '../../account/AvatarCropDialog.vue'

/** Owns state, effects and commands for UsersByIdPage. */
export function useUsersByIdPage() {
  const route = useRoute()
  const { user, fetchMe } = useAuth()
  const { locale } = useLocale()
  const { configuration } = usePlatform()

  const profile = ref<NoCtfapiEndpointsAuthenticationPublicUserProfileResponse | null>(null)
  const loading = ref(true)
  const error = ref<string | null>(null)
  let loadSequence = 0

  const userId = computed(() => typeof route.params.id === 'string' ? route.params.id : '')
  const isOwnProfile = computed(() => Boolean(user.value?.userId && user.value.userId === userId.value))
  const coverUrl = computed(() => profile.value?.profileCoverUrl
    ?? (isOwnProfile.value ? user.value?.profileCoverUrl : null)
    ?? null)

  async function loadProfile() {
    const requestedUserId = userId.value
    const sequence = ++loadSequence
    loading.value = true
    error.value = null
    const { data, error: requestError } = await userProfileGet({ path: { userId: requestedUserId } })
    if (sequence !== loadSequence) return
    loading.value = false
    if (requestError || !data) {
      profile.value = null
      error.value = parseApiError(requestError, translate('ui.userDoesNotExistOrFailedToLoad')).message
      return
    }
    profile.value = data
  }

  watch(userId, () => void loadProfile(), { immediate: true })

  const modes = computed(() => profile.value?.modes ?? [])
  const directions = computed(() => profile.value?.directions ?? [])
  const directionRows = computed(() => {
    const counts = new Map<string, number>()
    for (const item of directions.value) {
      const key = directionKey(item.direction)
      if (!key) continue
      counts.set(key, (counts.get(key) ?? 0) + (item.successfulChallengeCount ?? 0))
    }
    return challengeDirectionOptions.map(direction => ({
      direction,
      label: directionLabel(direction),
      successfulChallengeCount: counts.get(directionKey(direction)) ?? 0,
    }))
  })
  const recentCompetitions = computed(() => (profile.value?.recentCompetitions ?? []).map(item => ({
    ...item,
    modeLabel: gameModeLabel(item.mode),
    statusLabel: competitionStatusLabel(item.status),
    dateRange: formatDateRange(item.startAt, item.endAt),
  })))

  function formatDateRange(start?: string, end?: string): string {
    const formatter = new Intl.DateTimeFormat(locale.value, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    })
    if (!start && !end) return translate('ui.symbol')
    if (!end) return formatter.format(new Date(start!))
    return `${start ? formatter.format(new Date(start)) : translate('ui.symbol')} · ${formatter.format(new Date(end))}`
  }

  const modeChartOption = computed<echarts.EChartsCoreOption>(() => ({
    tooltip: { trigger: 'item', formatter: '{b}<br/>{c} · {d}%' },
    legend: { type: 'scroll', bottom: 0, left: 'center' },
    series: [{
      name: translate('profile.competitionModes'),
      type: 'pie',
      radius: ['50%', '74%'],
      center: ['50%', '42%'],
      minAngle: 8,
      avoidLabelOverlap: true,
      label: { show: false },
      emphasis: { label: { show: true, fontSize: 13, fontWeight: 700 } },
      data: modes.value.map(item => ({
        name: gameModeLabel(item.mode),
        value: item.competitionCount ?? 0,
      })),
    }],
  }))

  const directionChartOption = computed<echarts.EChartsCoreOption>(() => {
    const values = directionRows.value
    const maximum = Math.max(1, ...values.map(item => item.successfulChallengeCount ?? 0))
    return {
      tooltip: { trigger: 'item' },
      radar: {
        indicator: values.map(item => ({
          name: item.label,
          max: Math.max(1, Math.ceil(maximum * 1.15)),
        })),
        center: ['50%', '51%'],
        radius: '56%',
        splitNumber: 4,
        shape: 'polygon',
        axisName: { fontSize: 10 },
        axisLine: { lineStyle: { width: 1 } },
        splitLine: { lineStyle: { width: 1 } },
        splitArea: { areaStyle: { color: 'transparent' } },
      },
      series: [{
        type: 'radar',
        data: [{
          name: translate('profile.successfulChallenges'),
          value: values.map(item => item.successfulChallengeCount ?? 0),
          lineStyle: { width: 3 },
          areaStyle: { opacity: 0.28 },
          symbol: 'circle',
          symbolSize: 6,
        }],
      }],
    }
  })

  const coverInput = ref<HTMLInputElement | null>(null)
  const coverPending = ref(false)
  const coverEditorOpen = ref(false)
  const coverSourceFile = ref<File | null>(null)
  const maximumWallpaperBytes = computed(() =>
    configuration.value?.imageUploadLimits?.maximumWallpaperBytes ?? null)
  function setCoverInputRef(element: Element | ComponentPublicInstance | null) {
    coverInput.value = (element instanceof Element ? element : element?.$el ?? null) as typeof coverInput.value
  }

  function selectCover(event: Event) {
    const input = event.target as HTMLInputElement
    const file = input.files?.[0] ?? null
    input.value = ''
    if (!file || coverPending.value) return
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
      toast.error(translate('accountPanel.wallpaperFileInvalid'))
      return
    }
    coverSourceFile.value = file
    coverEditorOpen.value = true
  }

  function setCoverEditorOpen(value: boolean) {
    if (coverPending.value) return
    coverEditorOpen.value = value
    if (!value) coverSourceFile.value = null
  }

  async function uploadCover(file: File) {
    if (exceedsUploadLimit(file.size, maximumWallpaperBytes.value)) {
      toast.error(maximumWallpaperBytes.value
        ? translate('accountPanel.fileExceedsUploadLimit', { limit: formatBytes(maximumWallpaperBytes.value) })
        : translate('ui.theUploadedFileIsTooLarge'))
      return
    }

    coverPending.value = true
    try {
      const { error: uploadError } = await authenticationUploadMyProfileCover({ body: { file } })
      if (uploadError) throw uploadError
      await fetchMe()
      await loadProfile()
      coverEditorOpen.value = false
      coverSourceFile.value = null
      toast.success(translate('profile.coverUpdated'))
    }
    catch (uploadError) {
      toast.error(parseApiError(uploadError).message)
    }
    finally {
      coverPending.value = false
    }
  }

  function reportCoverError(cropError: Error) {
    toast.error(cropError.message)
  }

  const ProfileCoverCropDialog = markRaw(AvatarCropDialogComponent)

  return {
    ImagePlus,
    UserRound,
    profile,
    loading,
    error,
    isOwnProfile,
    coverUrl,
    modes,
    directions,
    directionRows,
    recentCompetitions,
    modeChartOption,
    directionChartOption,
    coverInput,
    coverPending,
    coverEditorOpen,
    coverSourceFile,
    setCoverInputRef,
    selectCover,
    setCoverEditorOpen,
    uploadCover,
    reportCoverError,
    ProfileCoverCropDialog,
  }
}

export type UsersByIdPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useUsersByIdPage>>>
