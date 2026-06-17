<script setup lang="ts">
import { ShieldAlert, Wrench } from 'lucide-vue-next'
import EmptyState from './EmptyState.vue'
import ErrorState from './ErrorState.vue'
import { Skeleton } from '@/components/ui/skeleton'

const props = withDefaults(defineProps<{
  loading?: boolean
  error?: boolean
  empty?: boolean
  forbidden?: boolean
  unsupported?: boolean
  loadingRows?: number
  loadingTitle?: string
  emptyTitle?: string
  emptyDescription?: string
  errorTitle?: string
  errorMessage?: string
  retryLabel?: string
}>(), {
  loadingRows: 3,
  loadingTitle: 'Loading...',
  emptyTitle: 'No data',
  errorTitle: 'Failed to load',
})

const emit = defineEmits<{
  retry: []
}>()
</script>

<template>
  <div v-if="props.loading" class="space-y-3">
    <div class="text-sm text-muted-foreground">{{ loadingTitle }}</div>
    <Skeleton v-for="i in loadingRows" :key="i" class="h-12 w-full" />
  </div>
  <EmptyState
    v-else-if="props.forbidden"
    :icon="ShieldAlert"
    title="Permission required"
    description="You do not have access to this view."
  />
  <EmptyState
    v-else-if="props.unsupported"
    :icon="Wrench"
    title="Unsupported"
    description="This capability is not available yet."
  />
  <ErrorState
    v-else-if="props.error"
    :title="errorTitle"
    :message="errorMessage"
    :retry-label="retryLabel"
    @retry="emit('retry')"
  />
  <EmptyState
    v-else-if="props.empty"
    :title="emptyTitle"
    :description="emptyDescription"
  />
  <slot v-else />
</template>
