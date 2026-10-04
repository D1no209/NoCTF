
import { api, multipartBody } from '../../../lib/api'
import { message as describeMessage } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'
import { markRaw } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { ImagePlus, UserRound } from '@lucide/vue'
import { toast } from '../../../utils/message-toast'

import type { NoCTFAPIEndpointsAuthenticationPublicUserProfileResponse } from '../../../api/models'
import type { echarts } from '../../../utils/echarts'
import { buildProfileDirectionRows } from '../../../lib/profile-directions'
import { competitionStatusLabel, gameModeLabel } from '../../../utils/labels'
import { exceedsUploadLimit } from '../../account/upload-limits'
import AvatarCropDialogComponent from '../../account/AvatarCropDialog.vue'

/** Owns state, effects and commands for UsersByIdPage. */
export function useUsersByIdPage() {
  const route = useRoute()
  const { user, fetchMe } = useAuth()
  const { locale } = useLocale()
  const { configuration } = usePlatform()

  const profile = ref<NoCTFAPIEndpointsAuthenticationPublicUserProfileResponse | null>(null)
  const loading = ref(true)
  const error = ref<UiMessage | null>(null)
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
    let requestError: unknown;
    const data = await api.api.v1.users.byUserId(requestedUserId).get().catch(cause => { requestError = cause; return undefined });
    if (sequence !== loadSequence) return
    loading.value = false
    if (requestError || !data) {
      profile.value = null
      error.value = parseApiError(requestError, describeMessage('common.usersBy.error.userExistLoadFailed')).displayMessage
      return
    }
    profile.value = data
  }

  watch(userId, () => void loadProfile(), { immediate: true })

  const modes = computed(() => profile.value?.modes ?? [])
  const modeRows = computed(() => modes.value.map((item, index) => ({
    mode: item.mode,
    label: gameModeLabel(item.mode),
    competitionCount: item.competitionCount ?? 0,
    colorClass: ['bg-chart-1', 'bg-chart-2', 'bg-chart-3', 'bg-chart-4'][index % 4],
  })))
  const directions = computed(() => profile.value?.directions ?? [])
  const directionRows = computed(() => buildProfileDirectionRows(directions.value))
  const recentCompetitions = computed(() => (profile.value?.recentCompetitions ?? []).map(item => ({
    ...item,
    modeLabel: gameModeLabel(item.mode),
    statusLabel: competitionStatusLabel(item.status),
    dateRange: formatDateRange(item.startAt, item.endAt),
  })))

  function formatDateRange(start?: Date | string | null, end?: Date | string | null): string {
    const formatter = new Intl.DateTimeFormat(locale.value, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    })
    if (!start && !end) return translate('common.label.symbol')
    if (!end) return formatter.format(new Date(start!))
    return `${start ? formatter.format(new Date(start)) : translate('common.label.symbol')} · ${formatter.format(new Date(end))}`
  }

  const modeChartOption = computed<echarts.EChartsCoreOption>(() => ({
    tooltip: { trigger: 'item', formatter: '{b}<br/>{c} · {d}%' },
    legend: { show: false },
    series: [{
      name: translate('profile.competitionModes'),
      type: 'pie',
      radius: ['48%', '69%'],
      center: ['50%', '50%'],
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
    const highest = Math.max(0, ...values.map(item => item.successfulChallengeCount))
    const maximum = Math.max(1, highest)
    const visualInset = highest > 0 ? Math.max(1, Math.ceil(highest * 0.18)) : 0
    return {
      tooltip: { trigger: 'item' },
      radar: {
        indicator: values.map(item => ({
          name: item.label,
          min: -visualInset,
          max: Math.max(1, Math.ceil(maximum * 1.15)),
        })),
        center: ['50%', '52%'],
        radius: '73%',
        splitNumber: 3,
        shape: 'polygon',
        axisName: { fontSize: 11 },
        axisLine: { lineStyle: { width: 1, opacity: 0.42 } },
        splitLine: { lineStyle: { width: 1, opacity: 0.36 } },
        splitArea: { areaStyle: { color: 'transparent' } },
      },
      series: [{
        type: 'radar',
        data: [{
          name: translate('profile.successfulChallenges'),
          value: values.map(item => item.successfulChallengeCount ?? 0),
          lineStyle: { width: 2.5 },
          areaStyle: { opacity: 0.18 },
          symbol: 'circle',
          symbolSize: 5,
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
      toast.error(describeMessage('accountPanel.wallpaperFileInvalid'))
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
        : translate('common.error.uploadTooLarge'))
      return
    }

    coverPending.value = true
    try {
      await api.api.v1.auth.me.profileCover.put(await multipartBody({ file }));
      await fetchMe()
      await loadProfile()
      coverEditorOpen.value = false
      coverSourceFile.value = null
      toast.success(describeMessage('profile.coverUpdated'))
    }
    catch (uploadError) {
      toast.error(parseApiError(uploadError).displayMessage)
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
    modeRows,
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
