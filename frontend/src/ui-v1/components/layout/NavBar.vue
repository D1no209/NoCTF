<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import { Button } from '@/ui-v1/components/ui/button'
import BrandLogo from '@/ui-v1/components/BrandLogo.vue'
import LanguageSwitch from '@/ui-v1/components/LanguageSwitch.vue'
import { Home, LayoutDashboard, Menu, Users } from 'lucide-vue-next'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogTrigger } from '@/ui-v1/components/ui/dialog'
import NotificationCenter from '@/ui-v1/components/notifications/NotificationCenter.vue'
import { useChromeSession } from '@/features/chrome/useChromeSession'

const { t } = useI18n()
const {
  isAuthenticated,
  canManage,
  userName: displayName,
  logout,
} = useChromeSession()

const menuOpen = ref(false)

async function handleLogout() {
  menuOpen.value = false
  await logout()
}
</script>

<template>
  <header class="sticky top-0 z-50 w-full border-b-2 border-border bg-muted">
    <div class="mx-auto flex h-16 max-w-[1600px] items-center justify-between gap-4 px-4 md:px-6">
      <div class="flex items-center gap-6">
        <RouterLink to="/" class="flex items-center gap-2 hover:opacity-80 transition-all active:scale-95">
          <BrandLogo class="h-8" />
        </RouterLink>
        <nav class="hidden items-center gap-2 text-sm font-medium sm:flex">
          <RouterLink
            to="/"
            class="relative inline-flex items-center gap-1 border-b-[2px] border-transparent px-3 py-2 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
            active-class="border-primary bg-accent text-foreground"
          >
            <Home class="size-4" />
            {{ t('nav.home') }}
          </RouterLink>
          <RouterLink
            to="/competitions"
            class="relative border-b-[2px] border-transparent px-3 py-2 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
            active-class="border-primary bg-accent text-foreground"
          >
            {{ t('nav.competitions') }}
          </RouterLink>
          <RouterLink
            to="/teams"
            class="relative inline-flex items-center gap-1 border-b-[2px] border-transparent px-3 py-2 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
            active-class="border-primary bg-accent text-foreground"
          >
            <Users class="size-4" />
            {{ t('nav.teams') }}
          </RouterLink>
          <RouterLink
            v-if="canManage"
            to="/admin"
            class="relative inline-flex items-center gap-1 border-b-[2px] border-transparent px-3 py-2 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
            active-class="border-primary bg-accent text-foreground"
          >
            <LayoutDashboard class="size-4" />
            {{ t('nav.admin') }}
          </RouterLink>
        </nav>
      </div>

      <div class="flex items-center gap-3">
        <div class="shrink-0">
          <LanguageSwitch />
        </div>

        <NotificationCenter />
        
        <template v-if="isAuthenticated">
          <div v-if="displayName" class="flex min-w-0 items-center gap-2 border-2 border-border bg-card px-2 py-1.5 text-sm">
            <span class="text-muted-foreground hidden lg:inline">{{ displayName }}</span>
          </div>
          <Button variant="ghost" size="sm" class="hidden sm:inline-flex" @click="handleLogout">{{ t('auth.logout') }}</Button>
        </template>
        <template v-else>
          <RouterLink to="/login" class="hidden sm:block">
            <Button variant="outline" size="sm">{{ t('auth.login') }}</Button>
          </RouterLink>
        </template>

        <!-- Mobile Menu -->
        <Dialog v-model:open="menuOpen">
          <DialogTrigger as-child>
            <Button variant="ghost" size="icon" class="sm:hidden">
              <Menu class="size-5" />
            </Button>
          </DialogTrigger>
          <DialogContent class="max-w-[320px]">
            <DialogHeader class="text-left">
              <DialogTitle>NoCTF</DialogTitle>
            </DialogHeader>
            <div class="flex flex-col gap-4 py-4">
              <RouterLink to="/" class="text-lg font-medium hover:text-primary transition-colors" @click="menuOpen = false">
                {{ t('nav.home') }}
              </RouterLink>
              <RouterLink to="/competitions" class="text-lg font-medium hover:text-primary transition-colors" @click="menuOpen = false">
                {{ t('nav.competitions') }}
              </RouterLink>
              <RouterLink to="/teams" class="text-lg font-medium hover:text-primary transition-colors" @click="menuOpen = false">
                {{ t('nav.teams') }}
              </RouterLink>
              <RouterLink v-if="canManage" to="/admin" class="text-lg font-medium hover:text-primary transition-colors" @click="menuOpen = false">
                {{ t('nav.admin') }}
              </RouterLink>

              <template v-if="isAuthenticated">
                <div class="flex flex-col gap-1 py-2">
                  <span class="text-xs text-muted-foreground">{{ t('common.user') }}</span>
                  <span class="font-medium">{{ displayName }}</span>
                </div>
                <Button variant="outline" class="w-full mt-4" @click="handleLogout">{{ t('auth.logout') }}</Button>
              </template>
              <template v-else>
                <RouterLink to="/login" @click="menuOpen = false">
                  <Button class="w-full">{{ t('auth.login') }}</Button>
                </RouterLink>
              </template>
            </div>
          </DialogContent>
        </Dialog>
      </div>
    </div>
  </header>
</template>
