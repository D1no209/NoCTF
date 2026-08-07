<script setup lang="ts">
definePageMeta({ middleware: 'platform-admin' })

const route = useRoute()

const tabs = [
  { value: '/admin/platform', label: '平台信息' },
  { value: '/admin/platform/users', label: '用户' },
  { value: '/admin/platform/bots', label: 'Bot' },
  { value: '/admin/platform/logs', label: '日志' },
  { value: '/admin/platform/audit', label: '审计' },
  { value: '/admin/platform/dead-letters', label: '死信队列' },
  { value: '/admin/platform/email', label: '邮箱验证' },
]

const current = computed(() => route.path)

async function onTabChange(value: string | number): Promise<void> {
  await navigateTo(String(value))
}
</script>

<template>
  <div class="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-8">
    <div>
      <h1 class="text-2xl font-semibold">平台管理</h1>
      <p class="text-sm text-muted-foreground">平台配置、用户、日志与运维管理</p>
    </div>
    <Tabs :model-value="current" @update:model-value="onTabChange">
      <TabsList variant="line" class="w-full justify-start">
        <TabsTrigger v-for="tab in tabs" :key="tab.value" :value="tab.value">
          {{ tab.label }}
        </TabsTrigger>
      </TabsList>
    </Tabs>
    <NuxtPage />
  </div>
</template>
