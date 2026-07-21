<script setup lang="ts">
import { computed, ref } from 'vue'
import { useAdminHealthPage } from '@/features/admin/useAdminHealthPage'
import CommandAdminHealthWorkspace, {
  type CommandAdminHealthReport,
} from '../components/CommandAdminHealthWorkspace.vue'

const { health: healthData, loading, lastUpdated, lastError, fetchHealth } = useAdminHealthPage()

const operationMessage = ref('')

const health = computed<CommandAdminHealthReport | null>(() => {
  const report = healthData.value
  if (!report)
    return null
  return {
    status: report.status?.trim() || 'unknown',
    checks: (report.checks ?? []).map(check => ({
      name: check.name?.trim() || 'component',
      status: check.status?.trim() || 'unknown',
      description: check.description?.trim() || undefined,
    })),
  }
})

async function refresh() {
  operationMessage.value = ''
  await fetchHealth()
  const cause = lastError.value
  if (cause)
    operationMessage.value = cause instanceof Error ? cause.message : 'Health check failed.'
}
</script>

<template>
  <CommandAdminHealthWorkspace
    :health="health"
    :loading="loading"
    :last-updated="lastUpdated"
    :operation-message="operationMessage"
    @refresh="refresh"
  />
</template>
