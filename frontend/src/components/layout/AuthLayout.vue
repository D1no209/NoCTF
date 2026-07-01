<script setup lang="ts">
import LanguageSwitch from '@/components/LanguageSwitch.vue'

defineProps<{
  subtitle: string
}>()
</script>

<template>
  <div class="grid min-h-[100dvh] bg-background lg:grid-cols-[minmax(420px,0.86fr)_1.14fr]">
    <section class="noctf-dark-shell relative hidden overflow-hidden px-10 py-12 text-white lg:flex lg:flex-col lg:justify-between">
      <div class="noctf-grid-glow absolute inset-0 opacity-60" />
      <div class="absolute -left-20 bottom-16 size-72 rounded-full bg-blue-500/20 blur-3xl" />
      <div class="absolute right-10 top-20 size-44 rounded-full bg-cyan-400/10 blur-3xl" />

      <div class="relative z-10">
        <div class="mb-20 flex items-center gap-4">
          <span class="noctf-logo size-12" />
          <span class="text-3xl font-bold tracking-tight">NoCTF</span>
        </div>
        <div class="max-w-xl space-y-7">
          <h1 class="text-5xl font-bold leading-tight tracking-tight">
            Hack. Learn. Compete.
            <span class="block text-blue-400">NoCTF.</span>
          </h1>
          <p class="max-w-md text-lg leading-8 text-blue-100/80">
            {{ subtitle }}
          </p>
        </div>
      </div>

      <div class="relative z-10">
        <div class="mb-12 ml-auto mr-8 flex size-56 items-center justify-center rounded-[2rem] border border-blue-300/20 bg-blue-500/10 shadow-[0_0_80px_rgb(37_99_235/0.35)] backdrop-blur">
          <div class="flex size-28 items-center justify-center rounded-3xl border border-cyan-300/30 bg-slate-950/60">
            <span class="noctf-logo size-16" />
          </div>
        </div>
        <div class="grid grid-cols-3 gap-3 rounded-2xl border border-white/10 bg-white/[0.04] p-4 text-sm backdrop-blur">
          <div class="border-r border-white/10 pr-4">
            <div class="font-semibold text-blue-200">{{ $t('auth.featureSecure') }}</div>
            <div class="mt-1 text-xs text-slate-400">{{ $t('auth.featureSecureDesc') }}</div>
          </div>
          <div class="border-r border-white/10 px-4">
            <div class="font-semibold text-blue-200">{{ $t('auth.featureStable') }}</div>
            <div class="mt-1 text-xs text-slate-400">{{ $t('auth.featureStableDesc') }}</div>
          </div>
          <div class="pl-4">
            <div class="font-semibold text-blue-200">{{ $t('auth.featureFair') }}</div>
            <div class="mt-1 text-xs text-slate-400">{{ $t('auth.featureFairDesc') }}</div>
          </div>
        </div>
      </div>
    </section>

    <section class="relative flex min-h-[100dvh] items-center justify-center px-4 py-16 sm:px-8">
      <div class="absolute right-6 top-6 z-10">
        <LanguageSwitch />
      </div>
      <div class="absolute inset-0 bg-[radial-gradient(circle_at_85%_18%,rgb(37_99_235/0.12),transparent_22rem)]" />
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
