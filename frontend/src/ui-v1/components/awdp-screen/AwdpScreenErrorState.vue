<script setup lang="ts">
import { AlertTriangle, RefreshCw } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { Button } from '@/ui-v1/components/ui/button'
import { Panel } from '@/ui-v1/components/ui/panel'
import AwdpScreenShell from '@/ui-v1/components/awdp-screen/AwdpScreenShell.vue'

defineProps<{
  message?: string
}>()

defineEmits<{
  retry: []
}>()

const { t } = useI18n()
</script>

<template>
  <AwdpScreenShell variant="center">
    <Panel variant="dark" class="max-w-xl p-6 text-center">
      <div class="mx-auto flex size-12 items-center justify-center rounded-lg border border-[var(--semantic-warning-border)] bg-[var(--semantic-warning-soft)] text-[var(--semantic-warning)]">
        <AlertTriangle class="size-6" />
      </div>
      <h1 class="mt-4 text-xl font-semibold text-[var(--awdp-text-inverse)]">
        {{ t('awdpScreen.errors.dataUnavailable') }}
      </h1>
      <p class="mt-2 text-sm text-[var(--awdp-text-muted)]">
        {{ message || t('awdpScreen.errors.snapshotUnavailable') }}
      </p>
      <Button
        variant="outline"
        class="mt-5 gap-2"
        @click="$emit('retry')"
      >
        <RefreshCw class="size-4" />
        {{ t('awdpScreen.errors.retrySnapshot') }}
      </Button>
    </Panel>
  </AwdpScreenShell>
</template>
