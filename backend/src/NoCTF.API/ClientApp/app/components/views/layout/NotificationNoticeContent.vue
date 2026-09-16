<script setup lang="ts">
defineProps<{
  title: string
  body?: string | null
  href: string
  actionLabel: string
}>()
</script>

<template>
  <div class="grid min-w-0 gap-1">
    <AlertTitle>{{ title }}</AlertTitle>
    <AlertDescription v-if="body">{{ body }}</AlertDescription>
    <Button as-child variant="link" size="sm" class="mt-1 h-auto w-fit px-0 py-1">
      <NuxtLink :to="href">{{ actionLabel }}</NuxtLink>
    </Button>
  </div>
</template>
