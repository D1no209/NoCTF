<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { ArrowLeft } from 'lucide-vue-next'
import ChallengeTemplateForm from '@/ui-v1/components/admin/ChallengeTemplateForm.vue'
import { useToastMutation } from '@/ui-v1/components/feedback/useToastMutation'
import { Button } from '@/ui-v1/components/ui/button'
import type { ChallengeTemplateSubmit } from '@/features/admin/challengeTemplate'
import { useAdminChallengeCreatePage } from '@/features/admin/useAdminChallengeCreatePage'

const { t } = useI18n()

const { createMutation: create, goAdminChallenges } = useAdminChallengeCreatePage()

const createMutation = useToastMutation<ChallengeTemplateSubmit>(create, {
  success: 'admin.challenges.createSuccess',
  error: 'admin.challenges.saveError',
})
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
      <div class="space-y-2">
        <Button variant="ghost" size="sm" class="-ml-2" @click="goAdminChallenges">
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
        @cancel="goAdminChallenges"
      />
    </div>
  </div>
</template>
