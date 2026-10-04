import { message as describeMessage } from '../../utils/i18n'
import type { UiMessage } from '../../utils/i18n'
import { ArrowRight } from '@lucide/vue'
import { listCompetitionsEndpoint } from '../../api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '../../api'
import { executeHomeTerminalInput, homeTerminalCommands, homeTerminalIdentity, type HomeTerminalCommand } from './home-terminal'

/** Owns state, effects and commands for IndexPage. */
export function useIndexPage() {
  const { configuration } = usePlatform()

  const { isLoggedIn, user } = useAuth()

  const items = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse[]>([])

  const competitionsLoading = ref(true)

  const competitionsError = ref<UiMessage | null>(null)

  async function loadCompetitions(): Promise<void> {
    competitionsLoading.value = true
    competitionsError.value = null
    const { data, error } = await listCompetitionsEndpoint()
    competitionsLoading.value = false
    if (error || !data) {
      competitionsError.value = parseApiError(error, describeMessage("common.index.error.loadRecentCompetitionsFailed")).displayMessage
      return
    }
    items.value = data?.items ?? []
  }

  onMounted(() => void loadCompetitions())

  const liveCount = computed(
    () => items.value.filter((c) => c.status === 'Running').length,
  )

  const upcomingCount = computed(
    () =>
      items.value.filter((c) =>
        c.status === 'Published' || c.status === 'Visible',
      ).length,
  )

  const terminalInput = ref('')
  const terminalCommand = ref<HomeTerminalCommand | null>('status')
  const unknownTerminalCommand = ref('')

  const statusRows = computed(() => [
    { label: translate('common.label.modes'), value: translate('common.label.ctfAwdAwdpKoh') },
    { label: translate('common.label.live'), value: competitionsLoading.value ? translate('common.label.loading') : competitionsError.value ? '—' : translate('common.label.running.useIndexPage', { count: liveCount.value }) },
    { label: translate('common.label.upcoming.useIndexPage'), value: competitionsLoading.value ? translate('common.label.loading') : competitionsError.value ? '—' : translate('common.label.upcoming', { count: upcomingCount.value }) },
  ])

  const terminalOutput = computed(() => {
    if (terminalCommand.value === 'help') {
      return homeTerminalCommands.map(command => ({ value: command }))
    }
    if (terminalCommand.value === 'ls') {
      if (competitionsLoading.value) return [{ value: translate('common.label.loading') }]
      if (competitionsError.value) return [{ value: '—' }]
      if (!items.value.length) return [{ value: translate('terminal.noCompetitions') }]
      return items.value.map((competition, index) => ({
        label: String(index + 1).padStart(2, '0'),
        value: competition.title ?? '—',
      }))
    }
    if (terminalCommand.value === 'status') return statusRows.value
    if (terminalCommand.value === 'whoami') {
      return [{ value: homeTerminalIdentity(isLoggedIn.value ? user.value?.userName : null) }]
    }
    return [{ value: translate('terminal.commandNotFound', { command: unknownTerminalCommand.value }) }]
  })

  function executeTerminalCommand(): void {
    const execution = executeHomeTerminalInput(terminalInput.value)
    terminalInput.value = execution.input
    terminalCommand.value = execution.command
    unknownTerminalCommand.value = execution.unknownCommand
  }

  return {
      ArrowRight,

      configuration,
      isLoggedIn,
      items,
      competitionsLoading,
      competitionsError,
      loadCompetitions,
      liveCount,
      upcomingCount,

      terminalInput,
      terminalOutput,
      executeTerminalCommand,
    }
}

export type IndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useIndexPage>>>
