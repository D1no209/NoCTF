import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'

export interface AdminCompetitionDto {
  id: string
  title: string
  description?: string
  gameModeType: string
  status: string
  startTime: string
  endTime: string
  ownerId: string
}

export interface AdminCompetitionForm {
  title: string
  description: string
  gameModeType: string
  startTime: string
  endTime: string
  status: string
}

export function toDateTimeLocal(date: Date) {
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 16)
}

export function defaultCompetitionSchedule() {
  const start = new Date()
  start.setMinutes(start.getMinutes() + 5)
  const end = new Date(start)
  end.setHours(end.getHours() + 2)
  return {
    startTime: toDateTimeLocal(start),
    endTime: toDateTimeLocal(end),
  }
}

export function useAdminCompetitionsPage() {
  const router = useRouter()
  const queryClient = useQueryClient()

  const isCreating = ref(false)
  const selectedCompetition = ref<AdminCompetitionDto | null>(null)

  const form = ref<AdminCompetitionForm>({
    title: '',
    description: '',
    gameModeType: 'Ctf',
    startTime: '',
    endTime: '',
    status: 'Draft',
  })

  const isScheduleValid = computed(() => {
    if (!form.value.startTime || !form.value.endTime)
      return false
    return new Date(form.value.endTime).getTime() > new Date(form.value.startTime).getTime()
  })

  const canSave = computed(() => Boolean(form.value.title.trim()) && isScheduleValid.value)

  function competitionPayload() {
    return {
      ...form.value,
      title: form.value.title.trim(),
      description: form.value.description.trim() || undefined,
      startTime: new Date(form.value.startTime).toISOString(),
      endTime: new Date(form.value.endTime).toISOString(),
    }
  }

  const competitionsQuery = useQuery({
    queryKey: queryKeys.adminCompetitions,
    queryFn: () => adminApi.competitions<AdminCompetitionDto[]>(),
  })

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!canSave.value)
        throw new Error('invalid_competition_form')
      const body = competitionPayload()
      if (isCreating.value)
        await adminApi.createCompetition(body)
      else
        await adminApi.updateCompetition(selectedCompetition.value!.id, body)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.adminCompetitions })
    },
  })

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => {
      await adminApi.deleteCompetition(id)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.adminCompetitions })
    },
  })

  function prepareCreate() {
    isCreating.value = true
    selectedCompetition.value = null
    form.value = { title: '', description: '', gameModeType: 'Ctf', status: 'Draft', ...defaultCompetitionSchedule() }
  }

  function goManage(competition: { id: string }) {
    void router.push({ name: 'admin-competition-detail', params: { id: competition.id } })
  }

  function goCollaborators(competition: { id: string, title: string }) {
    void router.push({ name: 'admin-collaborators', query: { competitionId: competition.id, competitionTitle: competition.title } })
  }

  function goViewPublic(id: string) {
    void router.push({ name: 'competition-detail', params: { id } })
  }

  function goAwdpScreen(id: string) {
    void router.push({ name: 'awdp-screen', params: { gameId: id } })
  }

  return {
    competitions: competitionsQuery.data,
    isLoading: competitionsQuery.isLoading,
    isError: competitionsQuery.isError,
    error: competitionsQuery.error,
    refetch: competitionsQuery.refetch,
    form,
    isCreating,
    selectedCompetition,
    isScheduleValid,
    canSave,
    saveMutation,
    deleteMutation,
    prepareCreate,
    goManage,
    goCollaborators,
    goViewPublic,
    goAwdpScreen,
  }
}
