<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefaultLayoutViewState } from '~/features/shell/useDefaultLayout'

const viewProps = defineProps<{ state: DefaultLayoutViewState }>()
const { ShieldAlert, isHome, routePath, wallpaperActive, wallpaperStyle, isLoggedIn, impersonation, impersonationEnding, impersonationExpiresAt, endImpersonation, configuration, platformError, platformLoading, ensureLoaded, t, navItems, isActive, LanguageToggle, ThemeToggle, ThemePalettePanel, AccountPanel } = toRefs(viewProps.state)
</script>

<template>
  <div data-slot="default-layout" class="relative isolate flex h-dvh min-h-0 flex-col overflow-hidden">
    <div
      v-if="!isHome"
      data-slot="page-wallpaper"
      data-page-wallpaper="true"
      :data-personal-wallpaper="wallpaperActive || undefined"
      :style="wallpaperStyle"
      aria-hidden="true"
    />
    <div data-slot="default-layout-foreground" class="flex h-full min-h-0 flex-col overflow-hidden">
    <header class="pointer-events-none sticky top-0 z-40">
      <div data-slot="topbar-frame" class="mx-auto grid h-20 w-full max-w-[96rem] grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-3 px-4 md:px-6">
        <div data-slot="topbar-capsule" data-position="left">
          <NuxtLink to="/" class="flex min-w-0 items-center gap-2.5 font-mono text-base font-semibold tracking-tight" :aria-label="configuration?.name ?? $t('common.label.noctf')">
            <img v-if="configuration?.logoUrl" :src="configuration.logoUrl" :alt="configuration.name ?? $t('common.label.noctf')" loading="eager" fetchpriority="high" decoding="async" class="size-8 shrink-0 rounded-full object-contain">
            <span v-else class="text-primary">&gt;</span>
            <span data-slot="topbar-brand-name" class="hidden max-w-48 truncate sm:inline">{{ configuration?.name ?? $t('common.label.noctf') }}</span>
            <span v-if="!configuration?.logoUrl" class="animate-blink text-primary">_</span>
          </NuxtLink>
        </div>

        <div data-slot="topbar-capsule" data-position="center" class="min-w-0 justify-self-center">
          <ScrollSurface as="nav" axis="x" class="scrollbar-none flex min-w-0 items-center gap-1 overflow-x-auto overflow-y-hidden" :aria-label="t('common.label.mainNavigation')">
            <span
              v-for="item in navItems.filter((i) => i.show)"
              :key="item.to ?? undefined"
              v-top-nav-motion
              data-top-nav-slot
            >
              <span data-top-nav-sizer aria-hidden="true">
                <span data-top-nav-sizer-icon />
                <span data-top-nav-sizer-label>{{ item.label }}</span>
              </span>
              <Button
                variant="ghost"
                as-child
                class="noctf-motion-top-nav relative"
                data-top-nav-item
                :data-active="isActive(item.to) || undefined"
                :class="isActive(item.to) ? 'text-foreground font-medium after:absolute after:inset-x-2.5 after:bottom-1 after:h-0.5 after:rounded-full after:bg-primary' : 'text-muted-foreground'"
              >
                <NuxtLink :to="item.to" :aria-current="isActive(item.to) ? 'page' : undefined" :aria-label="item.unread ? t('common.label.notificationsUnreadMessages') : item.label">
                  <span data-top-nav-content>
                    <span data-top-nav-icon class="relative">
                      <component :is="item.icon" />
                      <span v-if="item.unread" class="absolute top-1 right-1 size-2.5 rounded-full bg-destructive" aria-hidden="true" />
                    </span>
                    <span data-top-nav-label>{{ item.label }}</span>
                    <span v-if="item.unread" class="sr-only">{{ t('common.label.unreadNotifications') }}</span>
                  </span>
                </NuxtLink>
              </Button>
            </span>
          </ScrollSurface>
        </div>

        <div data-slot="topbar-capsule" data-position="right" class="justify-self-end">
          <component :is="LanguageToggle" />
          <component :is="ThemePalettePanel" />
          <component :is="ThemeToggle" />
          <component v-if="isLoggedIn" :is="AccountPanel" />
          <template v-else>
            <Button variant="ghost" as-child><NuxtLink to="/auth/login">{{ t('auth.login.action') }}</NuxtLink></Button>
            <Button as-child><NuxtLink to="/auth/register">{{ t('common.label.createAccount') }}</NuxtLink></Button>
          </template>
        </div>
      </div>
    </header>
    <div v-if="impersonation" class="sticky top-20 z-30 mx-auto w-full max-w-[96rem] px-4 pb-3 md:px-6">
      <Card
        as="aside"
        size="sm"
        slot-name="impersonation-banner"
        data-impersonation-banner="true"
        aria-live="polite"
        aria-atomic="true"
      >
        <CardContent class="flex flex-wrap items-center justify-between gap-3">
          <span class="flex min-w-0 items-center gap-2">
            <component :is="ShieldAlert" class="size-4 shrink-0" aria-hidden="true" />
            <span class="min-w-0">
              {{ t('common.label.impersonatingUserUntil', {
                user: impersonation.targetUserName,
                expiresAt: impersonationExpiresAt,
              }) }}
            </span>
          </span>
          <Button type="button" size="sm" variant="outline" :disabled="impersonationEnding" @click="endImpersonation">
            <Spinner v-if="impersonationEnding" data-icon="inline-start" />
            {{ t('common.label.exitImpersonation') }}
          </Button>
        </CardContent>
      </Card>
    </div>
    <Alert v-if="platformError" variant="destructive" class="m-3">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ $message(platformError) }}</span>
        <Button type="button" size="sm" variant="outline" :disabled="platformLoading" @click="ensureLoaded">
          <Spinner v-if="platformLoading" data-icon="inline-start" />{{ t('common.label.reload') }}
        </Button>
      </AlertDescription>
    </Alert>
    <ScrollSurface as="main" axis="y" :reset-key="routePath" data-slot="page-transition-viewport" class="min-h-0 flex-1 overflow-y-auto overscroll-contain">
      <slot />
    </ScrollSurface>
    </div>
  </div>
</template>
