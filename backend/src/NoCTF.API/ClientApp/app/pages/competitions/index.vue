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
    error.value = parseApiError(err, '加载竞赛列表失败').message
    return
  }
  items.value = data.items ?? []
})

const running = computed(() =>
  items.value
    .filter((c) => c.status === CompetitionStatus.Running || c.status === CompetitionStatus.Paused)
    .sort((a, b) => (a.endTime ?? '').localeCompare(b.endTime ?? '')),
)
const upcoming = computed(() =>
  items.value
    .filter((c) => c.status === CompetitionStatus.Published || c.status === CompetitionStatus.Visible)
    .sort((a, b) => (a.startTime ?? '').localeCompare(b.startTime ?? '')),
)
const finished = computed(() =>
  items.value
    .filter((c) => c.status === CompetitionStatus.Finished)
    .sort((a, b) => (b.endTime ?? '').localeCompare(a.endTime ?? '')),
)

const tab = ref('running')
</script>

<template>
  <div class="mx-auto flex max-w-5xl flex-col gap-6 px-4 py-8">
    <div>
      <h1 class="text-2xl font-semibold">竞赛</h1>
      <p class="text-sm text-muted-foreground">浏览平台上的公开竞赛,报名参赛并进入工作区</p>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <div v-if="loading" class="grid gap-4 md:grid-cols-2">
      <Skeleton v-for="i in 4" :key="i" class="h-36 w-full" />
    </div>

    <Tabs v-else v-model="tab">
      <TabsList>
        <TabsTrigger value="running">进行中 ({{ running.length }})</TabsTrigger>
        <TabsTrigger value="upcoming">即将开始 ({{ upcoming.length }})</TabsTrigger>
        <TabsTrigger value="finished">已结束 ({{ finished.length }})</TabsTrigger>
      </TabsList>
      <TabsContent value="running">
        <div v-if="running.length" class="grid gap-4 md:grid-cols-2">
          <CompetitionCard v-for="c in running" :key="c.id" :competition="c" />
        </div>
        <Empty v-else class="border py-12">
          <EmptyHeader>
            <EmptyTitle>暂无进行中的竞赛</EmptyTitle>
            <EmptyDescription>去看看即将开始的竞赛,提前报名吧</EmptyDescription>
          </EmptyHeader>
        </Empty>
      </TabsContent>
      <TabsContent value="upcoming">
        <div v-if="upcoming.length" class="grid gap-4 md:grid-cols-2">
          <CompetitionCard v-for="c in upcoming" :key="c.id" :competition="c" />
        </div>
        <Empty v-else class="border py-12">
          <EmptyHeader>
            <EmptyTitle>暂无即将开始的竞赛</EmptyTitle>
          </EmptyHeader>
        </Empty>
      </TabsContent>
      <TabsContent value="finished">
        <div v-if="finished.length" class="grid gap-4 md:grid-cols-2">
          <CompetitionCard v-for="c in finished" :key="c.id" :competition="c" />
        </div>
        <Empty v-else class="border py-12">
          <EmptyHeader>
            <EmptyTitle>暂无已结束的竞赛</EmptyTitle>
          </EmptyHeader>
        </Empty>
      </TabsContent>
    </Tabs>
  </div>
</template>
