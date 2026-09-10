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

  const competitionsError = ref<string | null>(null)

  async function loadCompetitions(): Promise<void> {
    competitionsLoading.value = true
    competitionsError.value = null
    const { data, error } = await listCompetitionsEndpoint()
    competitionsLoading.value = false
    if (error || !data) {
      competitionsError.value = parseApiError(error, translate("ui.failedToLoadRecentCompetitions")).message
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
    { label: translate('ui.modes'), value: translate('ui.ctfAwdAwdpKoh') },
    { label: translate('ui.live2'), value: competitionsLoading.value ? translate('ui.loading') : competitionsError.value ? '—' : translate('ui.running4', { count: liveCount.value }) },
    { label: translate('ui.upcoming3'), value: competitionsLoading.value ? translate('ui.loading') : competitionsError.value ? '—' : translate('ui.upcoming2', { count: upcomingCount.value }) },
  ])

  const terminalOutput = computed(() => {
    if (terminalCommand.value === 'help') {
      return homeTerminalCommands.map(command => ({ value: command }))
    }
    if (terminalCommand.value === 'ls') {
      if (competitionsLoading.value) return [{ value: translate('ui.loading') }]
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
