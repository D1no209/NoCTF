<script setup lang="ts">
defineProps<{
  source?: string | null
  accessibleLabel: string
  emptyLabel: string
  loading?: boolean
  error?: string | null
  fill?: boolean
}>()
</script>

<template>
  <div class="flex h-full min-w-0 flex-col overflow-hidden rounded-xl bg-muted/45 shadow-inner" :class="fill ? 'min-h-0' : 'min-h-[32rem]'">
    <Skeleton v-if="loading" class="h-full w-full" :class="fill ? 'min-h-0' : 'min-h-[32rem]'" />
    <Alert v-else-if="error" variant="destructive" class="m-4">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
    </Alert>
    <!-- Edge disables its built-in PDF viewer in restricted frames. This source is an
         authenticated application/pdf Blob URL whose lifetime is owned by the caller. -->
    <iframe
      v-else-if="source"
      :src="source"
      :title="accessibleLabel"
      referrerpolicy="no-referrer"
      class="h-full w-full"
      :class="fill ? 'min-h-0' : 'min-h-[32rem]'"
    />
    <Empty v-else class="h-full" :class="fill ? 'min-h-0' : 'min-h-[32rem]'">
      <EmptyHeader>
        <EmptyTitle>{{ emptyLabel }}</EmptyTitle>
      </EmptyHeader>
    </Empty>
  </div>
</template>
