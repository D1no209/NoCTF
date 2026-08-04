<script setup lang="ts">
import { Info, MailCheck, Palette } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { RouterLink, RouterView, useRoute } from 'vue-router'

const { t } = useI18n()
const route = useRoute()

const tabs = [
  { to: '/admin/settings/basic', labelKey: 'admin.settings.tabs.basic', icon: Palette },
  {
    to: '/admin/settings/email-verification',
    labelKey: 'admin.settings.tabs.emailVerification',
    icon: MailCheck,
  },
  { to: '/admin/settings/information', labelKey: 'admin.settings.tabs.information', icon: Info },
]
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6">
    <div class="space-y-1">
      <h1 class="text-3xl font-black tracking-tight">
        {{ t('admin.settings.title') }}
      </h1>
      <p class="max-w-3xl text-sm text-muted-foreground">
        {{ t('admin.settings.subtitle') }}
      </p>
    </div>

    <nav class="grid border-2 border-border bg-card sm:grid-cols-3" :aria-label="t('admin.settings.title')">
      <RouterLink
        v-for="tab in tabs"
        :key="tab.to"
        :to="tab.to"
        class="flex min-h-12 items-center gap-2 border-b-2 border-border px-4 text-sm font-bold transition-colors last:border-b-0 hover:bg-muted/60 sm:border-b-0 sm:border-r-2 sm:last:border-r-0"
        :class="route.path === tab.to ? 'bg-foreground text-background hover:bg-foreground' : ''"
      >
        <component :is="tab.icon" class="size-4" />
        <span>{{ t(tab.labelKey) }}</span>
      </RouterLink>
    </nav>

    <RouterView />
  </div>
</template>
