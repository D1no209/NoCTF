<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminChallengesIndexPageViewState } from '~/features/routes/admin/challenges/useAdminChallengesIndexPage'

const viewProps = defineProps<{ state: AdminChallengesIndexPageViewState }>()
const { Plus, canOrganize, templates, filteredTemplates, directionFilter, directionOptions, loading, loadError, includeDeleted, createOpen, setCreateOpen, templateCreated, visibilityLabel, AdminDateTime, AdminGameModeBadge, ChallengeTemplateCreateDialog } = toRefs(viewProps.state)
</script>

<template>
  <div class="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-8">
    <div class="flex items-center justify-between gap-4">
      <h1 class="text-2xl font-semibold">{{ $t('ui.challengeLibrary2') }}</h1>
      <Button v-if="canOrganize" @click="setCreateOpen(true)">
          <Plus data-icon="inline-start" /> {{ $t('ui.createNewTemplate') }}
      </Button>
    </div>

    <Alert v-if="!canOrganize" variant="destructive">
      <AlertDescription>{{ $t('ui.organizerOrAdministratorRightsAreRequiredToManageTheQuestion') }}</AlertDescription>
    </Alert>

    <template v-else>
      <div class="flex flex-wrap items-end gap-4">
        <Field class="w-full sm:w-56">
          <FieldLabel>{{ $t('ui.category') }}</FieldLabel>
          <Select v-model="directionFilter">
            <SelectTrigger class="w-full"><SelectValue /></SelectTrigger>
            <SelectContent position="popper">
              <SelectGroup>
                <SelectItem value="all">{{ $t('ui.allDirections') }}</SelectItem>
                <SelectItem v-for="option in directionOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>
        <div class="flex h-10 items-center gap-2">
          <Switch id="include-deleted" v-model="includeDeleted" />
          <Label for="include-deleted">{{ $t('ui.showDeletedTemplates') }}</Label>
        </div>
      </div>

      <Alert v-if="loadError" variant="destructive">
        <AlertDescription>{{ $message(loadError) }}</AlertDescription>
      </Alert>

      <Card v-if="loading && templates.length === 0">
        <CardContent class="flex flex-col gap-3 pt-6">
          <Skeleton v-for="i in 5" :key="i" class="h-10 w-full" />
        </CardContent>
      </Card>

      <Empty v-else-if="filteredTemplates.length === 0 && !loadError">
        <EmptyHeader>
          <EmptyTitle>{{ $t('ui.noQuestionTemplateYet') }}</EmptyTitle>
          <EmptyDescription>{{ $t('ui.clickNewTemplateInTheUpperRightCornerToCreate') }}</EmptyDescription>
        </EmptyHeader>
      </Empty>

      <Card v-else>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>{{ $t('ui.title') }}</TableHead>
              <TableHead>{{ $t('ui.mode') }}</TableHead>
              <TableHead>{{ $t('ui.category') }}</TableHead>
              <TableHead>{{ $t('ui.visibility') }}</TableHead>
              <TableHead>{{ $t('ui.quoted') }}</TableHead>
              <TableHead>{{ $t('ui.updateTime') }}</TableHead>
              <TableHead>{{ $t('ui.status') }}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="template in filteredTemplates" :key="template.id">
              <TableCell>
                <NuxtLink :to="`/admin/challenges/${template.id}`" class="font-medium hover:underline">
                  {{ template.title }}
                </NuxtLink>
              </TableCell>
              <TableCell>
                <component :is="AdminGameModeBadge" :mode="template.mode" />
                <Badge v-if="template.interactionKind === 'PatchVerification'" variant="secondary" class="ml-2">
                  {{ $t('ui.patchVerification') }}
                </Badge>
              </TableCell>
              <TableCell>{{ directionLabel(template.direction) }}</TableCell>
              <TableCell>
                <Badge variant="outline">{{ visibilityLabel(template.visibility) }}</Badge>
              </TableCell>
              <TableCell>{{ template.activeCompetitionReferenceCount ?? 0 }}</TableCell>
              <TableCell>
                <component :is="AdminDateTime" :value="template.updatedAt" />
              </TableCell>
              <TableCell>
                <Badge v-if="template.deletedAt" variant="destructive">{{ $t('ui.deleted') }}</Badge>
                <Badge v-else variant="secondary">{{ $t('ui.normal') }}</Badge>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </Card>
    </template>

    <component
      :is="ChallengeTemplateCreateDialog"
      :open="createOpen"
      @update:open="setCreateOpen"
      @created="templateCreated"
    />
  </div>
</template>
