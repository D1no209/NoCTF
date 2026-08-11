<script setup lang="ts">
import { Bot, History, Inbox, Info, MailCheck, ScrollText, Users } from '@lucide/vue'
import type { WorkspaceNavGroup } from '~/components/app/workspace-nav'

definePageMeta({ middleware: 'platform-admin' })

const navGroups = computed<WorkspaceNavGroup[]>(() => [
  {
    label: translate("平台"),
    items: [
      { to: '/admin/platform', label: translate("平台信息"), icon: Info, exact: true },
      { to: '/admin/platform/users', label: translate("用户"), icon: Users },
      { to: '/admin/platform/bots', label: 'Bot', icon: Bot },
      { to: '/admin/platform/email', label: translate("邮箱验证"), icon: MailCheck },
    ],
  },
  {
    label: translate("运维"),
    items: [
      { to: '/admin/platform/logs', label: translate("日志"), icon: ScrollText },
      { to: '/admin/platform/audit', label: translate("审计"), icon: History },
      { to: '/admin/platform/dead-letters', label: translate("死信队列"), icon: Inbox },
    ],
  },
])
</script>

<template>
  <AppWorkspaceNav :groups="navGroups" :title="$t('平台管理')">
    <div class="mx-auto flex w-full max-w-6xl flex-col gap-6 px-4 py-8 md:px-6">
      <div>
        <h1 class="text-display text-2xl">{{ $t('平台管理') }}</h1>
        <p class="text-sm text-muted-foreground">{{ $t('平台配置、用户、日志与运维管理') }}</p>
      </div>
      <NuxtPage />
    </div>
  </AppWorkspaceNav>
</template>
