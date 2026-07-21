import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import type { ChallengeTemplateDto, ChallengeTemplateSubmit } from './challengeTemplate'

export function useAdminChallengesPage() {
  const router = useRouter()
  const queryClient = useQueryClient()

  const selectedTemplate = ref<ChallengeTemplateDto | null>(null)
  const revealedSecret = ref('')

  const templatesQuery = useQuery({
    queryKey: queryKeys.adminChallenges,
    queryFn: () => adminApi.challenges<ChallengeTemplateDto[]>(),
  })

  const saveMutation = useMutation({
    mutationFn: async ({ payload, attachmentFile, patchTemplateFile }: ChallengeTemplateSubmit) => {
      const saved = await adminApi.updateChallenge<ChallengeTemplateDto>(selectedTemplate.value!.id, payload)
      if (attachmentFile)
        await adminApi.uploadChallengeAttachment(saved.id, attachmentFile)
      if (patchTemplateFile)
        await adminApi.uploadChallengePatchTemplate(saved.id, patchTemplateFile)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.adminChallenges })
    },
  })

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => adminApi.deleteChallenge(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.adminChallenges })
    },
  })

  const revealMutation = useMutation({
    mutationFn: async (id: string) => adminApi.revealChallengeSecret<{ flagSecret?: string }>(id),
    onSuccess: (data) => {
      revealedSecret.value = data.flagSecret ?? ''
    },
  })

  function goCreateChallenge() {
    void router.push({ name: 'admin-challenge-create' })
  }

  return {
    templates: templatesQuery.data,
    isLoading: templatesQuery.isLoading,
    isError: templatesQuery.isError,
    error: templatesQuery.error,
    refetch: templatesQuery.refetch,
    selectedTemplate,
    revealedSecret,
    saveMutation,
    deleteMutation,
    revealMutation,
    goCreateChallenge,
  }
}
