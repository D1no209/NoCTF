<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { ArrowLeft } from 'lucide-vue-next'
import { toast } from 'vue-sonner'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import ChallengeTemplateForm from '@/components/admin/ChallengeTemplateForm.vue'
import { Button } from '@/components/ui/button'

interface ChallengeTemplateDto {
  id: string
}

const { t } = useI18n()
const router = useRouter()
const qc = useQueryClient()

const createMutation = useMutation({
  mutationFn: async ({ payload, attachmentFile, patchTemplateFile, penetrationTopology }: { payload: Record<string, unknown>; attachmentFile: File | null; patchTemplateFile: File | null; penetrationTopology: Record<string, unknown> | null }) => {
    const saved = await adminApi.createChallenge<ChallengeTemplateDto>(payload)
    if (attachmentFile) {
      await adminApi.uploadChallengeAttachment(saved.id, attachmentFile)
    }
    if (patchTemplateFile) {
      await adminApi.uploadChallengePatchTemplate(saved.id, patchTemplateFile)
    }
    if (penetrationTopology) {
      await adminApi.updatePenetrationTemplateTopology(saved.id, penetrationTopology)
    }
  },
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: queryKeys.adminChallenges })
    toast.success(t('admin.challenges.createSuccess'))
    router.push({ name: 'admin-challenges' })
  },
  onError: () => toast.error(t('admin.challenges.saveError')),
})
</script>

<template>
  <div class="noctf-admin-page">
    <div class="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
      <div class="space-y-2">
        <Button variant="ghost" size="sm" class="-ml-2" @click="router.push({ name: 'admin-challenges' })">
          <ArrowLeft class="mr-2 size-4" />
          {{ t('nav.back') }}
        </Button>
        <div class="space-y-1">
          <h2 class="text-2xl font-bold tracking-tight">{{ t('admin.challenges.createDialogTitle') }}</h2>
          <p class="max-w-2xl text-sm text-muted-foreground">{{ t('admin.challenges.createPageDescription') }}</p>
        </div>
      </div>
    </div>

    <div class="w-full border-t pt-6">
      <ChallengeTemplateForm
        :saving="createMutation.isPending.value"
        :submit-text="t('common.create')"
        :cancel-text="t('common.cancel')"
        @submit="createMutation.mutate($event)"
        @cancel="router.push({ name: 'admin-challenges' })"
      />
    </div>
  </div>
</template>
