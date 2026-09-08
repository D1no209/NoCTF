<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsIndexPageViewState } from '~/features/routes/admin/competitions/useAdminCompetitionsIndexPage'

const viewProps = defineProps<{ state: AdminCompetitionsIndexPageViewState }>()
const { Plus, canOrganize, items, roles, loading, error, includeDeleted, RoleLabel, CompetitionStatusBadge, GameModeBadge } = toRefs(viewProps.state)
</script>

<template>
  <div class="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-8">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 class="text-display text-2xl">{{ $t('ui.competitionAdmin') }}</h1>
        <p class="text-sm text-muted-foreground">{{ $t('ui.allCompetitionsIManage') }}</p>
      </div>
      <div class="flex items-center gap-3">
        <Label class="flex items-center gap-2 text-sm text-muted-foreground">
          <Checkbox v-model="includeDeleted" /> {{ $t('ui.containsDeleted') }} </Label>
        <Button v-if="canOrganize" as-child>
          <NuxtLink to="/admin/competitions/new">
            <Plus data-icon="inline-start" /> {{ $t('ui.newCompetition') }} </NuxtLink>
        </Button>
      </div>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
    </Alert>

    <div v-if="loading" class="flex flex-col gap-3">
      <Skeleton v-for="i in 3" :key="i" class="h-24 w-full" />
    </div>

    <Empty v-else-if="items.length === 0" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('ui.noCompetitionYet') }}</EmptyTitle>
        <EmptyDescription>{{ $t('ui.youHavenTManagedAnyContestsYet') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <div v-else class="grid gap-4 md:grid-cols-2">
      <Card v-for="c in items" :key="c.id">
        <CardHeader>
          <div class="flex items-center justify-between gap-2">
            <CardTitle class="truncate">{{ c.title }}</CardTitle>
            <div class="flex shrink-0 items-center gap-1">
              <component :is="GameModeBadge" :mode="c.mode" />
              <component :is="CompetitionStatusBadge" :status="c.status" />
              <Badge v-if="c.deletedAt" variant="destructive">{{ $t('ui.deleted') }}</Badge>
            </div>
          </div>
          <CardDescription class="line-clamp-2">{{ c.description || $t('ui.noDescriptionYet') }}</CardDescription>
        </CardHeader>
        <CardContent class="flex items-center justify-between font-mono text-sm tabular-nums text-muted-foreground">
          <span>{{ adminFormatDateTime(c.startTime) }} ~ {{ adminFormatDateTime(c.endTime) }}</span>
          <Badge variant="outline">{{ c.id && roles[c.id] ? $t(RoleLabel[roles[c.id]!]!) : '…' }}</Badge>
        </CardContent>
        <CardFooter>
          <Button variant="outline" size="sm" as-child class="w-full">
            <NuxtLink :to="`/admin/competitions/${c.id}`">{{ $t('ui.enterManagement') }}</NuxtLink>
          </Button>
        </CardFooter>
      </Card>
    </div>
  </div>
</template>
