<template>
  <div class="min-h-screen bg-muted/20">
    <div class="mx-auto flex min-h-screen w-full max-w-6xl items-center justify-center px-4 py-8">
      <div class="grid w-full items-center gap-8 md:grid-cols-[1fr_420px]">
        <section class="hidden md:block">
          <div class="max-w-md space-y-4">
            <div class="inline-flex h-10 w-10 items-center justify-center rounded-md bg-foreground text-background text-sm font-bold">
              N
            </div>
            <div>
              <h1 class="text-3xl font-semibold tracking-tight">NoCTF</h1>
              <p class="mt-2 text-sm leading-6 text-muted-foreground">
                {{ subtitle }}
              </p>
            </div>
          </div>
        </section>
        <section class="relative">
          <div class="absolute -top-12 right-0">
            <LanguageSwitch />
          </div>
          <slot />
        </section>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import LanguageSwitch from '@/components/LanguageSwitch.vue'

defineProps<{
  subtitle: string
}>()
</script>
