<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { ArrowLeft } from 'lucide-vue-next'

defineProps<{
  title: string
  description?: string | null
  backTo?: string
  backLabel?: string
}>()
</script>

<template>
  <header class="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
    <div class="min-w-0 space-y-2">
      <RouterLink
        v-if="backTo"
        :to="backTo"
        class="inline-flex items-center gap-1 text-sm font-medium text-muted-foreground transition-colors hover:text-foreground"
      >
        <ArrowLeft class="size-4" />
        {{ backLabel }}
      </RouterLink>
      <div>
        <h1 class="break-words text-2xl font-bold tracking-tight sm:truncate md:text-3xl">
          {{ title }}
        </h1>
        <p v-if="description" class="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
          {{ description }}
        </p>
      </div>
    </div>
    <div
      v-if="$slots.actions"
      class="flex w-full shrink-0 flex-wrap items-center gap-2 sm:w-auto sm:justify-end"
    >
      <slot name="actions" />
    </div>
  </header>
</template>
