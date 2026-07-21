<script setup lang="ts">
import { ref } from 'vue'
import CommandChallengeCreateWorkspace from '../components/CommandChallengeCreateWorkspace.vue'
import type { ChallengeTemplateSubmit } from '@/features/admin/challengeTemplate'
import { useAdminChallengeCreatePage } from '@/features/admin/useAdminChallengeCreatePage'

const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

const { createMutation, goAdminChallenges } = useAdminChallengeCreatePage()

function save(value: ChallengeTemplateSubmit) {
  createMutation.mutate(value, {
    onError: (error) => {
      operationTone.value = 'danger'
      operationMessage.value = error instanceof Error ? error.message : 'Unable to create the challenge template.'
    },
  })
}
</script>

<template>
  <CommandChallengeCreateWorkspace
    :save-pending="createMutation.isPending.value"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @back="goAdminChallenges"
    @save="save"
  />
</template>
