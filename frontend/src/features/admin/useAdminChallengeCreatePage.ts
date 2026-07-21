import { useRouter } from 'vue-router'
import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import type { ChallengeTemplateSubmit } from './challengeTemplate'

export function useAdminChallengeCreatePage() {
  const router = useRouter()
  const queryClient = useQueryClient()

  const createMutation = useMutation({
    mutationFn: async ({ payload, attachmentFile, patchTemplateFile }: ChallengeTemplateSubmit) => {
      const saved = await adminApi.createChallenge<{ id: string }>(payload)
      if (attachmentFile)
        await adminApi.uploadChallengeAttachment(saved.id, attachmentFile)
      if (patchTemplateFile)
        await adminApi.uploadChallengePatchTemplate(saved.id, patchTemplateFile)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.adminChallenges })
      void router.push({ name: 'admin-challenges' })
    },
  })

  function goAdminChallenges() {
    void router.push({ name: 'admin-challenges' })
  }

  return {
    createMutation,
    goAdminChallenges,
  }
}
