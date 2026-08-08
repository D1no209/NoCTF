<script setup lang="ts">
import { Bell, LogOut, Settings, ShieldCheck, Trophy, User } from '@lucide/vue'

const { user, isLoggedIn, isAdministrator, canOrganize, logout } = useAuth()
const { configuration } = usePlatform()
</script>

<template>
  <div class="flex min-h-screen flex-col">
    <header class="sticky top-0 z-40 border-b bg-background">
      <div class="mx-auto flex h-16 max-w-7xl items-center gap-8 px-4 md:px-6">
        <NuxtLink to="/" class="flex shrink-0 items-center gap-2.5 text-lg font-semibold">
          <Trophy class="size-6" />
          {{ configuration?.name ?? 'NoCTF' }}
        </NuxtLink>
        <nav class="flex min-w-0 items-center gap-2 overflow-x-auto text-base">
          <Button variant="ghost" size="sm" class="px-3 text-base" as-child>
            <NuxtLink to="/competitions">竞赛</NuxtLink>
          </Button>
          <Button v-if="isLoggedIn" variant="ghost" size="sm" class="px-3 text-base" as-child>
            <NuxtLink to="/admin/competitions">竞赛管理</NuxtLink>
          </Button>
          <Button v-if="canOrganize" variant="ghost" size="sm" class="px-3 text-base" as-child>
            <NuxtLink to="/admin/challenges">题库管理</NuxtLink>
          </Button>
          <Button v-if="isAdministrator" variant="ghost" size="sm" class="px-3 text-base" as-child>
            <NuxtLink to="/admin/platform">平台管理</NuxtLink>
          </Button>
        </nav>
        <div class="ml-auto flex items-center gap-2">
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
            <Button variant="ghost" size="sm" as-child>
              <NuxtLink to="/auth/login">登录</NuxtLink>
            </Button>
            <Button size="sm" as-child>
              <NuxtLink to="/auth/register">注册</NuxtLink>
            </Button>
          </template>
        </div>
      </div>
    </header>
    <main class="flex-1">
      <slot />
    </main>
    <footer class="border-t py-4">
      <p class="mx-auto max-w-7xl px-4 text-xs text-muted-foreground">
        {{ configuration?.name ?? 'NoCTF' }}
        <span v-if="configuration?.description"> · {{ configuration.description }}</span>
      </p>
    </footer>
  </div>
</template>
