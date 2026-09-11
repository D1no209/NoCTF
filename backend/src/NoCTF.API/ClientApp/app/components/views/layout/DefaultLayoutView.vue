<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefaultLayoutViewState } from '~/features/shell/useDefaultLayout'

const viewProps = defineProps<{ state: DefaultLayoutViewState }>()
const { Bell, isHome, wallpaperActive, wallpaperStyle, isLoggedIn, isAdministrator, configuration, platformError, platformLoading, ensureLoaded, hasUnread, t, navItems, isActive, LanguageToggle, ThemeToggle, ThemePalettePanel, AccountPanel } = toRefs(viewProps.state)
</script>

<template>
  <div data-slot="default-layout" class="relative isolate flex min-h-screen flex-col">
    <div
      v-if="!isHome"
      data-slot="page-wallpaper"
      data-page-wallpaper="true"
      :data-personal-wallpaper="wallpaperActive || undefined"
      :style="wallpaperStyle"
      aria-hidden="true"
    />
    <div data-slot="default-layout-foreground" class="flex min-h-screen flex-col">
    <header class="pointer-events-none sticky top-0 z-40">
      <div data-slot="topbar-frame" class="mx-auto grid h-20 w-full max-w-[96rem] grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-3 px-4 md:px-6">
        <div data-slot="topbar-capsule" data-position="left">
          <NuxtLink to="/" class="flex min-w-0 items-center gap-2.5 font-mono text-base font-semibold tracking-tight" :aria-label="configuration?.name ?? $t('ui.noctf')">
            <img v-if="configuration?.logoUrl" :src="configuration.logoUrl" :alt="configuration.name ?? $t('ui.noctf')" class="size-8 shrink-0 rounded-full object-contain">
            <span v-else class="text-primary">&gt;</span>
            <span data-slot="topbar-brand-name" class="hidden max-w-48 truncate sm:inline">{{ configuration?.name ?? $t('ui.noctf') }}</span>
            <span v-if="!configuration?.logoUrl" class="animate-blink text-primary">_</span>
          </NuxtLink>
        </div>

        <div data-slot="topbar-capsule" data-position="center" class="min-w-0 justify-self-center">
          <ScrollSurface as="nav" axis="x" class="scrollbar-none flex min-w-0 items-center gap-1 overflow-x-auto overflow-y-hidden" :aria-label="t('ui.mainNavigation')">
            <span
              v-for="item in navItems.filter((i) => i.show)"
              :key="item.to"
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
                <NuxtLink :to="item.to" :aria-current="isActive(item.to) ? 'page' : undefined">
                  <span data-top-nav-icon><component :is="item.icon" /></span>
                  <span data-top-nav-label>{{ item.label }}</span>
                </NuxtLink>
              </Button>
            </span>
          </ScrollSurface>
          <template v-if="isLoggedIn">
            <Button v-if="isAdministrator" variant="ghost" size="icon" as-child>
              <NuxtLink to="/admin/platform" :aria-label="t('ui.platformAdmin')">
                <PlatformGearIcon />
              </NuxtLink>
            </Button>
            <Button variant="ghost" size="icon" as-child>
              <NuxtLink
                to="/notifications"
                class="relative"
                :aria-label="hasUnread ? t('ui.notificationsUnreadMessages') : t('ui.notifications')"
              >
                <Bell />
                <span
                  v-if="hasUnread"
                  class="absolute top-1 right-1 size-2.5 rounded-full border-2 border-background bg-destructive"
                  aria-hidden="true"
                />
                <span v-if="hasUnread" class="sr-only">{{ t('ui.unreadNotifications') }}</span>
              </NuxtLink>
            </Button>
          </template>
          <component :is="LanguageToggle" />
        </div>

        <div data-slot="topbar-capsule" data-position="right" class="justify-self-end">
          <component :is="ThemePalettePanel" />
          <component :is="ThemeToggle" />
          <component v-if="isLoggedIn" :is="AccountPanel" />
          <template v-else>
            <Button variant="ghost" as-child><NuxtLink to="/auth/login">{{ t('ui.signIn') }}</NuxtLink></Button>
            <Button as-child><NuxtLink to="/auth/register">{{ t('ui.createAccount') }}</NuxtLink></Button>
          </template>
        </div>
      </div>
    </header>
    <Alert v-if="platformError" variant="destructive" class="m-3">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ $message(platformError) }}</span>
        <Button type="button" size="sm" variant="outline" :disabled="platformLoading" @click="ensureLoaded">
          <Spinner v-if="platformLoading" data-icon="inline-start" />{{ t('ui.reload') }}
        </Button>
      </AlertDescription>
    </Alert>
    <main data-slot="page-transition-viewport" class="flex-1">
      <slot />
    </main>
    </div>
  </div>
</template>
