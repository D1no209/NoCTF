<script setup lang="ts">
import { ShieldAlert, Wrench } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import EmptyState from './EmptyState.vue'
import ErrorState from './ErrorState.vue'
import { Skeleton } from '@/components/ui/skeleton'

const { t } = useI18n()

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
})

const resolvedLoadingTitle = () => props.loadingTitle || t('common.loading')
const resolvedEmptyTitle = () => props.emptyTitle || t('state.noData')
const resolvedErrorTitle = () => props.errorTitle || t('state.failedToLoad')

const emit = defineEmits<{
  retry: []
}>()
</script>

<template>
  <div v-if="props.loading" class="space-y-3">
    <div class="text-sm text-muted-foreground">{{ resolvedLoadingTitle() }}</div>
    <Skeleton v-for="i in loadingRows" :key="i" class="h-12 w-full" />
  </div>
  <EmptyState
    v-else-if="props.forbidden"
    :icon="ShieldAlert"
    :title="t('state.permissionRequired')"
    :description="t('state.noAccessToView')"
  />
  <EmptyState
    v-else-if="props.unsupported"
    :icon="Wrench"
    :title="t('state.unsupported')"
    :description="t('state.capabilityUnavailable')"
  />
  <ErrorState
    v-else-if="props.error"
    :title="resolvedErrorTitle()"
    :message="errorMessage"
    :retry-label="retryLabel"
    @retry="emit('retry')"
  />
  <EmptyState
    v-else-if="props.empty"
    :title="resolvedEmptyTitle()"
    :description="emptyDescription"
  />
  <slot v-else />
</template>
