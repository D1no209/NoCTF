<script setup lang="ts">
import { Bell, LogOut, Settings, ShieldCheck, Trophy, User } from '@lucide/vue'

const { user, isLoggedIn, isAdministrator, canOrganize, logout } = useAuth()
const { configuration } = usePlatform()
const route = useRoute()

const navItems = computed(() => [
  { to: '/competitions', label: '竞赛', show: true },
  { to: '/admin/competitions', label: '竞赛管理', show: isLoggedIn.value },
  { to: '/admin/challenges', label: '题库管理', show: canOrganize.value },
  { to: '/admin/platform', label: '平台管理', show: isAdministrator.value },
])

function isActive(to: string) {
  return route.path === to || route.path.startsWith(`${to}/`)
}
</script>

<template>
  <div class="flex min-h-screen flex-col">
    <header class="sticky top-0 z-40 border-b bg-background/80 backdrop-blur">
      <div class="mx-auto flex h-16 max-w-7xl items-center gap-6 px-4 md:px-6">
        <NuxtLink to="/" class="flex shrink-0 items-center gap-2.5 text-lg font-semibold">
          <span class="flex size-9 items-center justify-center rounded-lg bg-primary/10 text-primary">
            <Trophy class="size-5" />
          </span>
          {{ configuration?.name ?? 'NoCTF' }}
        </NuxtLink>
        <nav class="flex min-w-0 items-center gap-1 overflow-x-auto">
          <Button
            v-for="item in navItems.filter((i) => i.show)"
            :key="item.to"
            variant="ghost"
            as-child
            :class="isActive(item.to) ? 'bg-accent text-accent-foreground font-medium' : 'text-muted-foreground'"
          >
            <NuxtLink :to="item.to">{{ item.label }}</NuxtLink>
          </Button>
        </nav>
        <div class="ml-auto flex items-center gap-2">
          <ThemeToggle />
          <template v-if="isLoggedIn">
            <Button variant="ghost" size="icon" as-child>
              <NuxtLink to="/notifications" aria-label="通知">
                <Bell />
              </NuxtLink>
            </Button>
            <DropdownMenu>
              <DropdownMenuTrigger as-child>
                <Button variant="ghost" class="flex items-center gap-2">
                  <Avatar class="size-7">
                    <AvatarImage v-if="user?.avatarUrl" :src="user.avatarUrl" :alt="user?.userName ?? ''" />
                    <AvatarFallback>{{ user?.userName?.slice(0, 2) ?? '?' }}</AvatarFallback>
                  </Avatar>
                  <span class="text-sm">{{ user?.userName }}</span>
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" class="w-48">
                <DropdownMenuGroup>
                  <DropdownMenuItem as-child>
                    <NuxtLink to="/account" class="flex items-center gap-2">
                      <User />
                      账户设置
                    </NuxtLink>
                  </DropdownMenuItem>
                  <DropdownMenuItem v-if="isAdministrator" as-child>
                    <NuxtLink to="/admin/platform" class="flex items-center gap-2">
                      <ShieldCheck />
                      平台管理
                    </NuxtLink>
                  </DropdownMenuItem>
                  <DropdownMenuItem as-child>
                    <NuxtLink to="/account" class="flex items-center gap-2">
                      <Settings />
                      偏好
                    </NuxtLink>
                  </DropdownMenuItem>
                </DropdownMenuGroup>
                <DropdownMenuSeparator />
                <DropdownMenuItem class="flex items-center gap-2" @click="logout">
                  <LogOut />
                  退出登录
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </template>
          <template v-else>
            <Button variant="ghost" as-child>
              <NuxtLink to="/auth/login">登录</NuxtLink>
            </Button>
            <Button as-child>
              <NuxtLink to="/auth/register">注册</NuxtLink>
            </Button>
          </template>
        </div>
      </div>
    </header>
    <main class="flex-1">
      <slot />
    </main>
    <footer class="border-t">
      <div class="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-2 px-4 py-6 text-xs text-muted-foreground md:px-6">
        <p class="flex items-center gap-2">
          <Trophy class="size-3.5 text-primary" />
          <span class="font-medium text-foreground">{{ configuration?.name ?? 'NoCTF' }}</span>
          <span v-if="configuration?.description"> · {{ configuration.description }}</span>
        </p>
        <p class="font-mono">Powered by NoCTF</p>
      </div>
    </footer>
  </div>
</template>
