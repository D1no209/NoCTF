<script setup lang="ts">
import { listCompetitionsEndpoint } from '~/api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'

type Competition = NoCtfapiEndpointsCompetitionsCompetitionResponse

const items = ref<Competition[]>([])
const loading = ref(true)
const error = ref<string | null>(null)

onMounted(async () => {
  const { data, error: err } = await listCompetitionsEndpoint()
  loading.value = false
  if (err || !data) {
    error.value = parseApiError(err, translate("加载竞赛列表失败")).message
    return
  }
  items.value = data.items ?? []
})

const running = computed(() =>
  items.value
    .filter((c) => c.status === 'Running' || c.status === 'Paused')
    .sort((a, b) => (a.endTime ?? '').localeCompare(b.endTime ?? '')),
)
const upcoming = computed(() =>
  items.value
    .filter((c) => c.status === 'Published' || c.status === 'Visible')
    .sort((a, b) => (a.startTime ?? '').localeCompare(b.startTime ?? '')),
)
const finished = computed(() =>
  items.value
    .filter((c) => c.status === 'Finished')
    .sort((a, b) => (b.endTime ?? '').localeCompare(a.endTime ?? '')),
)

const tab = ref('running')
</script>

<template>
  <div class="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-10 md:px-6">
    <div>
      <h1 class="text-display text-2xl">{{ $t('竞赛') }}</h1>
      <p class="text-sm text-muted-foreground">{{ $t('浏览平台上的公开竞赛,报名参赛并进入工作区') }}</p>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <div v-if="loading" class="grid gap-5 md:grid-cols-2 xl:grid-cols-3">
      <Skeleton v-for="i in 4" :key="i" class="h-44 w-full" />
    </div>

    <Tabs v-else v-model="tab">
      <TabsList>
        <TabsTrigger value="running">{{ $t('进行中（{count}）', { count: running.length }) }}</TabsTrigger>
        <TabsTrigger value="upcoming">{{ $t('即将开始（{count}）', { count: upcoming.length }) }}</TabsTrigger>
        <TabsTrigger value="finished">{{ $t('已结束（{count}）', { count: finished.length }) }}</TabsTrigger>
      </TabsList>
      <TabsContent value="running">
        <div v-if="running.length" class="grid gap-5 md:grid-cols-2 xl:grid-cols-3">
          <CompetitionCard v-for="c in running" :key="c.id" :competition="c" />
        </div>
        <Empty v-else class="border border-dashed py-12">
          <EmptyHeader>
            <EmptyTitle>{{ $t('暂无进行中的竞赛') }}</EmptyTitle>
            <EmptyDescription>{{ $t('去看看即将开始的竞赛,提前报名吧') }}</EmptyDescription>
          </EmptyHeader>
        </Empty>
      </TabsContent>
      <TabsContent value="upcoming">
        <div v-if="upcoming.length" class="grid gap-5 md:grid-cols-2 xl:grid-cols-3">
          <CompetitionCard v-for="c in upcoming" :key="c.id" :competition="c" />
        </div>
        <Empty v-else class="border border-dashed py-12">
          <EmptyHeader>
            <EmptyTitle>{{ $t('暂无即将开始的竞赛') }}</EmptyTitle>
          </EmptyHeader>
        </Empty>
      </TabsContent>
      <TabsContent value="finished">
        <div v-if="finished.length" class="grid gap-5 md:grid-cols-2 xl:grid-cols-3">
          <CompetitionCard v-for="c in finished" :key="c.id" :competition="c" />
        </div>
        <Empty v-else class="border border-dashed py-12">
          <EmptyHeader>
            <EmptyTitle>{{ $t('暂无已结束的竞赛') }}</EmptyTitle>
          </EmptyHeader>
        </Empty>
      </TabsContent>
    </Tabs>
  </div>
</template>
