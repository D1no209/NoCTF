<script setup lang="ts">
import { Plus } from '@lucide/vue'
import { adminChallengeBankListTemplates } from '~/api'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse } from '~/api'

definePageMeta({ middleware: 'auth' })

const { canOrganize } = useAuth()

const templates = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
const includeDeleted = ref(false)

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  const { data, error } = await adminChallengeBankListTemplates({
    query: { includeDeleted: includeDeleted.value },
  })
  if (error || !data) {
    loadError.value = parseApiError(error).message
  }
  else {
    templates.value = data.items ?? []
  }
  loading.value = false
}

watch(includeDeleted, () => {
  void load()
})

onMounted(() => {
  if (canOrganize.value) void load()
})

function visibilityLabel(visibility?: string): string {
  return visibility === 'Shared' ? translate("共享") : translate("私有")
}
</script>

<template>
  <div class="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-8">
    <div class="flex items-center justify-between gap-4">
      <div>
        <h1 class="text-2xl font-semibold">{{ $t('题库管理') }}</h1>
        <p class="text-sm text-muted-foreground">{{ $t('全局题目模板,可实例化到各场竞赛') }}</p>
      </div>
      <Button v-if="canOrganize" as-child>
        <NuxtLink to="/admin/challenges/new">
          <Plus data-icon="inline-start" /> {{ $t('新建模板') }} </NuxtLink>
      </Button>
    </div>

    <Alert v-if="!canOrganize" variant="destructive">
      <AlertDescription>{{ $t('需要组织者或管理员权限才能管理题库。') }}</AlertDescription>
    </Alert>

    <template v-else>
      <div class="flex items-center gap-2">
        <Switch id="include-deleted" v-model="includeDeleted" />
        <Label for="include-deleted">{{ $t('显示已删除的模板') }}</Label>
      </div>

      <Alert v-if="loadError" variant="destructive">
        <AlertDescription>{{ loadError }}</AlertDescription>
      </Alert>

      <Card v-if="loading && templates.length === 0">
        <CardContent class="flex flex-col gap-3 pt-6">
          <Skeleton v-for="i in 5" :key="i" class="h-10 w-full" />
        </CardContent>
      </Card>

      <Empty v-else-if="templates.length === 0 && !loadError">
        <EmptyHeader>
          <EmptyTitle>{{ $t('暂无题目模板') }}</EmptyTitle>
          <EmptyDescription>{{ $t('点击右上角「新建模板」创建第一个题目模板。') }}</EmptyDescription>
        </EmptyHeader>
      </Empty>

      <Card v-else>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>{{ $t('标题') }}</TableHead>
              <TableHead>{{ $t('模式') }}</TableHead>
              <TableHead>{{ $t('方向') }}</TableHead>
              <TableHead>{{ $t('可见性') }}</TableHead>
              <TableHead>{{ $t('被引用') }}</TableHead>
              <TableHead>{{ $t('更新时间') }}</TableHead>
              <TableHead>{{ $t('状态') }}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="template in templates" :key="template.id">
              <TableCell>
                <NuxtLink :to="`/admin/challenges/${template.id}`" class="font-medium hover:underline">
                  {{ template.title }}
                </NuxtLink>
              </TableCell>
              <TableCell>
                <AdminGameModeBadge :mode="template.mode" />
              </TableCell>
              <TableCell>{{ directionLabel(template.direction) }}</TableCell>
              <TableCell>
                <Badge variant="outline">{{ visibilityLabel(template.visibility) }}</Badge>
              </TableCell>
              <TableCell>{{ template.activeCompetitionReferenceCount ?? 0 }}</TableCell>
              <TableCell>
                <AdminDateTime :value="template.updatedAt" />
              </TableCell>
              <TableCell>
                <Badge v-if="template.deletedAt" variant="destructive">{{ $t('已删除') }}</Badge>
                <Badge v-else variant="secondary">{{ $t('正常') }}</Badge>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </Card>
    </template>
  </div>
</template>
