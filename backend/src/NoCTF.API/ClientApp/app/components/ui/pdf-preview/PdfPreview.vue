<script setup lang="ts">
defineProps<{
  source?: string | null
  accessibleLabel: string
  emptyLabel: string
  loading?: boolean
  error?: string | null
}>()
</script>

<template>
  <div class="flex h-full min-h-[32rem] min-w-0 flex-col overflow-hidden rounded-xl bg-muted/45 shadow-inner">
    <Skeleton v-if="loading" class="h-full min-h-[32rem] w-full" />
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
      class="h-full min-h-[32rem] w-full"
    />
    <Empty v-else class="h-full min-h-[32rem]">
      <EmptyHeader>
        <EmptyTitle>{{ emptyLabel }}</EmptyTitle>
      </EmptyHeader>
    </Empty>
  </div>
</template>
