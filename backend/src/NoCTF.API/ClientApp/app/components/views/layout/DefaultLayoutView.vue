<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefaultLayoutViewState } from '~/features/shell/useDefaultLayout'

const viewProps = defineProps<{ state: DefaultLayoutViewState }>()
const { Bell, LogOut, ShieldCheck, User, user, isLoggedIn, isAdministrator, logout, configuration, platformError, platformLoading, ensureLoaded, hasUnread, t, navItems, isActive, LanguageToggle, ThemeToggle } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex min-h-screen flex-col">
    <header class="sticky top-0 z-40 border-b bg-background/80 backdrop-blur">
      <div class="mx-auto flex h-16 max-w-7xl items-center gap-2 px-4 sm:gap-6 md:px-6">
        <NuxtLink to="/" class="flex shrink-0 items-baseline gap-1.5 font-mono text-base font-semibold tracking-tight">
          <span class="text-primary">&gt;</span>
          <span>{{ configuration?.name ?? $t('ui.noctf') }}</span>
          <span class="animate-blink text-primary">_</span>
        </NuxtLink>
        <nav class="scrollbar-none flex min-w-0 items-center gap-1 overflow-x-auto overflow-y-hidden py-1" :aria-label="t('ui.mainNavigation')">
          <Button
            v-for="item in navItems.filter((i) => i.show)"
            :key="item.to"
            variant="ghost"
            as-child
            class="relative shrink-0"
            :class="isActive(item.to) ? 'text-foreground font-medium after:absolute after:inset-x-2.5 after:bottom-1 after:h-0.5 after:rounded-full after:bg-primary' : 'text-muted-foreground'"
          >
            <NuxtLink :to="item.to">
              <component :is="item.icon" />
              {{ item.label }}
            </NuxtLink>
          </Button>
        </nav>
        <div class="ml-auto flex shrink-0 items-center gap-2">
          <component :is="ThemeToggle" />
          <component :is="LanguageToggle" />
          <template v-if="isLoggedIn">
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
            <DropdownMenu>
              <DropdownMenuTrigger as-child>
                <Button variant="ghost" class="flex items-center gap-2" :aria-label="user?.userName">
                  <Avatar class="size-7">
                    <AvatarImage v-if="user?.avatarUrl" :src="user.avatarUrl" :alt="user?.userName ?? ''" />
                    <AvatarFallback>{{ user?.userName?.slice(0, 2) ?? '?' }}</AvatarFallback>
                  </Avatar>
                  <span class="hidden max-w-40 truncate text-sm sm:inline">{{ user?.userName }}</span>
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" class="w-48">
                <DropdownMenuGroup>
                  <DropdownMenuItem as-child>
                    <NuxtLink to="/account" class="flex items-center gap-2">
                      <User />
                      {{ t('ui.accountSettings') }}
                    </NuxtLink>
                  </DropdownMenuItem>
                  <DropdownMenuItem v-if="isAdministrator" as-child>
                    <NuxtLink to="/admin/platform" class="flex items-center gap-2">
                      <ShieldCheck />
                      {{ t('ui.platformAdmin') }}
                    </NuxtLink>
                  </DropdownMenuItem>
                </DropdownMenuGroup>
                <DropdownMenuSeparator />
                <DropdownMenuItem class="flex items-center gap-2" @click="logout">
                  <LogOut />
                  {{ t('ui.signOut') }}
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </template>
          <template v-else>
            <Button variant="ghost" as-child>
              <NuxtLink to="/auth/login">{{ t('ui.signIn') }}</NuxtLink>
            </Button>
            <Button as-child>
              <NuxtLink to="/auth/register">{{ t('ui.createAccount') }}</NuxtLink>
            </Button>
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
    <main class="flex-1">
      <slot />
    </main>
    <footer class="border-t">
      <div class="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-2 px-4 py-6 text-xs text-muted-foreground md:px-6">
        <p class="flex items-baseline gap-1.5">
          <span class="font-mono text-primary">&gt;</span>
          <span class="font-mono font-medium text-foreground">{{ configuration?.name ?? $t('ui.noctf') }}</span>
          <span v-if="configuration?.description">{{ configuration.description }}</span>
        </p>
        <p class="font-mono">{{ $t('ui.poweredByNoctf') }}</p>
      </div>
    </footer>
  </div>
</template>
