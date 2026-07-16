<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { Button } from '@/components/ui/button'
</script>

<template>
  <main class="mx-auto flex min-h-screen w-full max-w-3xl items-center justify-center px-4 text-center">
    <div class="space-y-5">
      <p class="font-mono text-sm text-muted-foreground">404</p>
      <h1 class="text-3xl font-semibold">Page not found</h1>
      <Button as-child>
        <RouterLink to="/competitions">Return to competitions</RouterLink>
      </Button>
    </div>
  </main>
</template>
