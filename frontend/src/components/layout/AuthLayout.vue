<script setup lang="ts">
import BrandLogo from '@/components/BrandLogo.vue'
import LanguageSwitch from '@/components/LanguageSwitch.vue'

defineProps<{
  subtitle: string
}>()
</script>

<template>
  <div class="grid min-h-[100dvh] bg-background lg:grid-cols-[minmax(420px,0.86fr)_1.14fr]">
    <section class="auth-hero-shell relative hidden overflow-hidden border-r-[2px] border-[var(--auth-border)] px-10 py-12 lg:flex lg:flex-col lg:justify-between">
      <div class="auth-grid-glow absolute inset-0 opacity-50" />

      <div class="relative z-10">
        <div class="mb-20 flex items-center gap-4">
          <BrandLogo class="h-12" />
        </div>
        <div class="max-w-xl space-y-7">
          <h1 class="text-5xl font-bold leading-tight tracking-normal">
            {{ $t('auth.marketingHeadline') }}
            <span class="block text-[var(--auth-accent)]">NoCTF.</span>
          </h1>
          <p class="max-w-md text-lg leading-8 text-[var(--auth-muted)]">
            {{ subtitle }}
          </p>
        </div>
      </div>

      <div class="relative z-10">
        <div class="mb-12 ml-auto mr-8 flex size-56 items-center justify-center border-[2px] border-[var(--auth-border)] bg-[var(--auth-surface)]">
          <div class="flex size-28 items-center justify-center border-[2px] border-[var(--panel-dark-border)] bg-[var(--auth-surface-strong)]">
            <BrandLogo class="h-16" />
          </div>
        </div>
        <div class="grid grid-cols-3 gap-3 border-[2px] border-[var(--auth-border)] bg-[var(--auth-surface-muted)] p-4 text-sm">
          <div class="border-r border-[var(--auth-border)] pr-4">
            <div class="font-semibold text-[var(--auth-accent)]">{{ $t('auth.featureSecure') }}</div>
            <div class="mt-1 text-xs text-[var(--auth-muted)]">{{ $t('auth.featureSecureDesc') }}</div>
          </div>
          <div class="border-r border-[var(--auth-border)] px-4">
            <div class="font-semibold text-[var(--auth-accent)]">{{ $t('auth.featureStable') }}</div>
            <div class="mt-1 text-xs text-[var(--auth-muted)]">{{ $t('auth.featureStableDesc') }}</div>
          </div>
          <div class="pl-4">
            <div class="font-semibold text-[var(--auth-accent)]">{{ $t('auth.featureFair') }}</div>
            <div class="mt-1 text-xs text-[var(--auth-muted)]">{{ $t('auth.featureFairDesc') }}</div>
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
          <BrandLogo class="h-10" />
        </div>
        <slot />
      </div>
    </section>
  </div>
</template>

<style scoped>
.auth-hero-shell {
  background:
    linear-gradient(90deg, var(--auth-grid) 1px, transparent 1px),
    linear-gradient(180deg, var(--auth-grid) 1px, transparent 1px),
    linear-gradient(180deg, var(--auth-background-start) 0%, var(--auth-background-end) 100%);
  background-size: 16px 16px, 16px 16px, auto;
  color: var(--auth-foreground);
}

.auth-grid-glow {
  background-image:
    linear-gradient(var(--auth-grid-soft) 1px, transparent 1px),
    linear-gradient(90deg, var(--auth-grid-soft) 1px, transparent 1px);
  background-size: 16px 16px;
}
</style>
