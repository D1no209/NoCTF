<script setup lang="ts">
import LanguageSwitch from '@/components/LanguageSwitch.vue'

defineProps<{
  subtitle: string
}>()
</script>

<template>
  <div class="grid min-h-[100dvh] bg-background lg:grid-cols-[minmax(420px,0.86fr)_1.14fr]">
    <section class="noctf-dark-shell relative hidden overflow-hidden px-10 py-12 text-white lg:flex lg:flex-col lg:justify-between">
      <div>
        <div class="mb-16 flex items-center gap-4">
          <span class="noctf-logo size-12" />
          <span class="text-3xl font-bold tracking-tight">NoCTF</span>
        </div>
        <div class="max-w-lg space-y-5">
          <p class="text-xs font-semibold uppercase tracking-[0.18em] text-slate-400">
            Competition control surface
          </p>
          <h1 class="text-4xl font-bold leading-tight tracking-normal">
            NoCTF
          </h1>
          <p class="max-w-md text-base leading-7 text-slate-300">
            {{ subtitle }}
          </p>
        </div>
      </div>

      <div class="max-w-xl border-t border-white/10 pt-6">
        <div class="grid gap-4 text-sm">
          <div class="grid grid-cols-[7rem_minmax(0,1fr)] gap-4">
            <div class="font-semibold text-slate-100">
              {{ $t('auth.featureSecure') }}
            </div>
            <div class="text-slate-400">
              {{ $t('auth.featureSecureDesc') }}
            </div>
          </div>
          <div class="grid grid-cols-[7rem_minmax(0,1fr)] gap-4">
            <div class="font-semibold text-slate-100">
              {{ $t('auth.featureStable') }}
            </div>
            <div class="text-slate-400">
              {{ $t('auth.featureStableDesc') }}
            </div>
          </div>
          <div class="grid grid-cols-[7rem_minmax(0,1fr)] gap-4">
            <div class="font-semibold text-slate-100">
              {{ $t('auth.featureFair') }}
            </div>
            <div class="text-slate-400">
              {{ $t('auth.featureFairDesc') }}
            </div>
          </div>
        </div>
      </div>
    </section>

    <section class="relative flex min-h-[100dvh] items-center justify-center px-4 py-16 sm:px-8">
      <div class="absolute right-6 top-6 z-10">
        <LanguageSwitch />
      </div>
      <div class="relative z-10 w-full max-w-[470px]">
        <div class="mb-8 flex items-center justify-center gap-3 lg:hidden">
          <span class="noctf-logo size-10" />
          <span class="text-2xl font-bold tracking-tight">NoCTF</span>
        </div>
        <slot />
      </div>
    </section>
  </div>
</template>
