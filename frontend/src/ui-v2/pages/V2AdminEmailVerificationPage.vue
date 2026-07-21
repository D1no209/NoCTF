<script setup lang="ts">
import { computed, ref } from 'vue'
import CommandAdminEmailVerificationWorkspace from '../components/CommandAdminEmailVerificationWorkspace.vue'
import { useAdminEmailVerificationPage } from '@/features/admin/useAdminEmailVerificationPage'

const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

const {
  settings,
  isLoading,
  isError,
  error,
  refetch,
  form,
  saveMutation,
  testMutation,
} = useAdminEmailVerificationPage()

const workspaceState = computed<'loading' | 'error' | 'ready'>(() => {
  if (isLoading.value)
    return 'loading'
  if (isError.value)
    return 'error'
  return 'ready'
})

const errorMessage = computed(() => {
  const value = error.value
  return value instanceof Error ? value.message : undefined
})

const status = computed(() => ({
  enabled: Boolean(settings.value?.enabled),
  smtpPasswordConfigured: Boolean(settings.value?.smtpPasswordConfigured),
  persisted: Boolean(settings.value?.persisted),
  updatedAt: settings.value?.updatedAt ?? null,
}))

function reportSuccess(message: string) {
  operationTone.value = 'success'
  operationMessage.value = message
}

function reportError(error: unknown, fallback: string) {
  operationTone.value = 'danger'
  operationMessage.value = error instanceof Error ? error.message : fallback
}

function save() {
  saveMutation.mutate(undefined, {
    onSuccess: () => reportSuccess('Email verification settings saved.'),
    onError: error => reportError(error, 'Unable to save the settings.'),
  })
}

function test() {
  testMutation.mutate(undefined, {
    onSuccess: () => reportSuccess('Test email sent.'),
    onError: error => reportError(error, 'Unable to send the test email.'),
  })
}

function refresh() {
  void refetch()
}
</script>

<template>
  <CommandAdminEmailVerificationWorkspace
    v-model:form="form"
    :state="workspaceState"
    :error-message="errorMessage"
    :status="status"
    :save-pending="saveMutation.isPending.value"
    :test-pending="testMutation.isPending.value"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @refresh="refresh"
    @retry="refresh"
    @save="save"
    @test="test"
  />
</template>
