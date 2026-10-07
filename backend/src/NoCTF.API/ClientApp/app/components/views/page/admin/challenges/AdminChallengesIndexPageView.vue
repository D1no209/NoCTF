<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminChallengesIndexPageViewState } from '~/features/routes/admin/challenges/useAdminChallengesIndexPage'

const viewProps = defineProps<{ state: AdminChallengesIndexPageViewState }>()
const { Filter, Plus, canOrganize, templates, filteredTemplates, search, directionFilter, directionOptions, loading, loadError, includeDeleted, createOpen, setCreateOpen, templateCreated, visibilityLabel, AdminDateTime, AdminGameModeBadge, ChallengeTemplateCreateDialog, page, pageCount, total, pageLimit, pageLoading, loadPage, setPageSize } = toRefs(viewProps.state)
</script>

<template>
  <div class="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-8">
    <div class="flex items-center justify-between gap-4">
      <h1 class="text-2xl font-semibold">{{ $t('common.label.challengeLibrary.useDefaultLayout') }}</h1>
      <Button v-if="canOrganize" @click="setCreateOpen(true)">
          <Plus data-icon="inline-start" /> {{ $t('administration.label.createNewTemplate') }}
      </Button>
    </div>

    <Alert v-if="!canOrganize" variant="destructive">
      <AlertDescription>{{ $t('administration.challengesBy.validation.organizerAdministratorRequired') }}</AlertDescription>
    </Alert>

    <template v-else>
      <div class="flex flex-wrap items-end gap-4">
        <Field class="w-full min-w-0 sm:min-w-80 sm:max-w-2xl sm:flex-1">
          <FieldLabel>{{ $t('administration.label.searchQuestionBankTemplates') }}</FieldLabel>
          <Input v-model="search" :placeholder="$t('administration.label.searchQuestionBankTemplates')" :aria-label="$t('administration.label.searchQuestionBankTemplates')" />
        </Field>
        <div class="flex h-10 items-center gap-2">
          <Switch id="include-deleted" v-model="includeDeleted" />
          <Label for="include-deleted">{{ $t('administration.label.showDeletedTemplates') }}</Label>
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

      <Card v-else>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>{{ $t('common.label.title') }}</TableHead>
              <TableHead>{{ $t('administration.label.mode') }}</TableHead>
              <TableHead>
                <DropdownMenu>
                  <DropdownMenuTrigger as-child>
                    <Button variant="ghost" size="sm" :class="directionFilter !== 'all' ? 'text-primary' : ''" :aria-label="$t('challengeLibrary.filterDirection')">
                      {{ $t('administration.label.category') }}<Filter data-icon="inline-end" />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="start">
                    <DropdownMenuRadioGroup v-model="directionFilter">
                      <DropdownMenuRadioItem value="all">{{ $t('administration.label.directions') }}</DropdownMenuRadioItem>
                      <DropdownMenuRadioItem v-for="option in directionOptions" :key="option.value" :value="option.value">
                        {{ option.label }}
                      </DropdownMenuRadioItem>
                    </DropdownMenuRadioGroup>
                  </DropdownMenuContent>
                </DropdownMenu>
              </TableHead>
              <TableHead>{{ $t('administration.label.visibility') }}</TableHead>
              <TableHead>{{ $t('administration.label.quoted') }}</TableHead>
              <TableHead>{{ $t('administration.label.updateTime') }}</TableHead>
              <TableHead>{{ $t('common.label.status') }}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-if="filteredTemplates.length === 0 && !loadError">
              <TableCell :colspan="7">
                <Empty>
                  <EmptyHeader>
                    <EmptyTitle>{{ $t('administration.label.questionTemplateYet') }}</EmptyTitle>
                    <EmptyDescription>{{ $t('administration.challengesIndex.description.clickNewTemplateUpper') }}</EmptyDescription>
                  </EmptyHeader>
                </Empty>
              </TableCell>
            </TableRow>
            <TableRow v-for="template in filteredTemplates" :key="template.id">
              <TableCell>
                <NuxtLink :to="`/admin/challenges/${template.id}`" prefetch-on="interaction" class="font-medium hover:underline">
                  {{ template.title }}
                </NuxtLink>
              </TableCell>
              <TableCell>
                <component :is="AdminGameModeBadge" :mode="template.mode" />
                <Badge v-if="template.interactionKind === 'PatchVerification'" variant="secondary" class="ml-2">
                  {{ $t('common.label.patchVerification') }}
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
                <Badge v-if="template.deletedAt" variant="destructive">{{ $t('common.label.deleted.competitionSidebarView') }}</Badge>
                <Badge v-else variant="secondary">{{ $t('administration.label.normal') }}</Badge>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </Card>

      <OffsetPagination
        v-if="filteredTemplates.length > 0 || total > 0"
        :page="page"
        :page-count="pageCount"
        :total="total"
        :limit="pageLimit"
        :loading="pageLoading"
        @update:page="loadPage"
        @update:limit="setPageSize"
      />
    </template>

    <component
      :is="ChallengeTemplateCreateDialog"
      :open="createOpen"
      @update:open="setCreateOpen"
      @created="templateCreated"
    />
  </div>
</template>
