<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdProgressionPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdProgressionPage'

const props = defineProps<{ state: AdminCompetitionsByIdProgressionPageViewState }>()
const {
  competition, canWrite, enabled, showPlayerMap, nodes, edges, badges,
  availableChallenges, filteredChallenges, challengeSearch, challengeDirection,
  challengeDirections, loading, saving, badgeSaving, error,
  selectedNode, selectedEdge, selectedCount, newBadgeName, newBadgeDescription, newBadgeImage,
  batch, batchTargets, batchSourceId, batchCondition, batchDisabledReasons, previewEdges,
  layoutRevision,
  newBadgeUploadKey, editingBadgeId, editBadgeName, editBadgeDescription,
  editBadgeUploadKey,
  load, addChallenge, addBadge, connect, removeSelected, changeNodeSelection,
  changeEdgeSelection, updateNodePositions, setEdgeCondition,
  setSelectedNodeRequiresPrerequisites, save, autoArrange,
  beginBatch, cancelBatch, setBatchCondition, toggleBatchTarget, applyBatch,
  onBadgeFileChange, createBadge, deleteBadge, ProgressionCanvas,
  onEditBadgeFileChange, beginEditBadge, updateBadge,
} = toRefs(props.state)
</script>

<template>
  <section class="flex flex-col gap-5">
    <header class="flex flex-wrap items-start justify-between gap-4 border-b pb-4">
      <div>
        <h2 class="text-xl font-semibold">{{ $t('progression.title') }}</h2>
        <p class="mt-1 text-sm text-muted-foreground">{{ $t('progression.description') }}</p>
      </div>
      <div class="flex flex-wrap gap-2">
        <Button type="button" variant="secondary" :disabled="loading || batch || !nodes.length" @click="autoArrange">
          {{ $t('progression.autoArrange') }}
        </Button>
        <Button :disabled="!canWrite || saving || loading || batch || competition?.mode !== 'Ctf'" @click="save">
          {{ saving ? $t('progression.saving') : $t('progression.save') }}
        </Button>
      </div>
    </header>
    <Alert v-if="competition?.mode !== 'Ctf'"><AlertDescription>{{ $t('progression.ctfOnly') }}</AlertDescription></Alert>
    <Alert v-if="error" variant="destructive">
      <AlertDescription class="flex items-center justify-between gap-3">
        <span>{{ error }}</span><Button size="sm" variant="outline" @click="load">{{ $t('common.label.retry') }}</Button>
      </AlertDescription>
    </Alert>
    <div v-if="loading" class="h-80 animate-pulse rounded-lg border bg-muted/30" />
    <template v-else-if="competition?.mode === 'Ctf'">
      <div class="grid gap-3 rounded-lg border bg-card p-4 sm:grid-cols-2">
        <Field orientation="horizontal"><Switch id="progression-enabled" v-model="enabled" :disabled="!canWrite" /><FieldLabel for="progression-enabled">{{ $t('progression.enable') }}</FieldLabel></Field>
        <Field orientation="horizontal"><Switch id="progression-show-map" v-model="showPlayerMap" :disabled="!canWrite" /><FieldLabel for="progression-show-map">{{ $t('progression.showMap') }}</FieldLabel></Field>
        <p class="text-xs text-muted-foreground sm:col-span-2">{{ $t('progression.saveWarning') }}</p>
      </div>

      <div class="grid gap-5 xl:grid-cols-[19rem_minmax(0,1fr)_18rem]">
        <aside class="space-y-5 rounded-lg border bg-card p-4">
          <div>
            <h3 class="font-semibold">{{ $t('progression.challengeCatalog') }}</h3>
            <p class="mb-3 text-xs text-muted-foreground">{{ $t('progression.challengeImportHint') }}</p>
            <div class="mb-3 grid gap-2">
              <Input v-model="challengeSearch" :placeholder="$t('progression.searchChallenges')" :aria-label="$t('progression.searchChallenges')" />
              <Select v-model="challengeDirection">
                <SelectTrigger class="w-full" :aria-label="$t('administration.label.category')"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value="all">{{ $t('administration.label.directions') }}</SelectItem>
                    <SelectItem v-for="direction in challengeDirections" :key="direction.value ?? undefined" :value="direction.value">{{ direction.label }}</SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
            </div>
            <ScrollSurface axis="y" class="max-h-[26rem]" :aria-label="$t('progression.challengeCatalog')">
              <Button v-for="challenge in filteredChallenges" :key="challenge.id ?? undefined"
                type="button" variant="ghost" :disabled="!canWrite || !!batch"
                class="flex w-full items-center justify-between gap-2 px-2 text-left text-sm"
                @click="addChallenge(challenge)">
                <span class="truncate">{{ challenge.customTitle || challenge.title }}</span>
                <span class="shrink-0 text-xs text-muted-foreground">+</span>
              </Button>
              <p v-if="!availableChallenges.length" class="text-xs text-muted-foreground">{{ $t('progression.noChallenges') }}</p>
              <p v-else-if="!filteredChallenges.length" class="text-xs text-muted-foreground">{{ $t('progression.noMatchingChallenges') }}</p>
            </ScrollSurface>
          </div>
          <div class="border-t pt-4">
            <h3 class="font-semibold">{{ $t('progression.badgeCatalog') }}</h3>
            <p class="mb-3 text-xs text-muted-foreground">{{ $t('progression.badgeImportHint') }}</p>
            <ScrollSurface axis="y" class="max-h-56" :aria-label="$t('progression.badgeCatalog')">
              <div v-for="badge in badges" :key="badge.id ?? undefined" class="flex items-center gap-2 rounded border p-2">
                <img :src="badge.imageUrl ?? undefined" :alt="badge.name ?? undefined" class="size-9 rounded object-cover" />
                <span class="min-w-0 flex-1 truncate text-sm">{{ badge.name }}</span>
                <Button type="button" size="icon-sm" variant="ghost" :disabled="!canWrite || !!batch" :aria-label="$t('progression.importBadge')" @click="addBadge(badge)">+</Button>
                <Button type="button" size="icon-sm" variant="ghost" :disabled="!canWrite" :aria-label="$t('progression.editBadge')" @click="beginEditBadge(badge)">✎</Button>
                <Button type="button" size="icon-sm" variant="ghost" :disabled="!canWrite" :aria-label="$t('progression.deleteBadge')" @click="deleteBadge(badge.id!)">×</Button>
              </div>
            </ScrollSurface>
            <FieldGroup v-if="editingBadgeId && canWrite" class="mt-3 gap-2 rounded border p-2">
              <Field>
                <FieldLabel for="edit-badge-name">{{ $t('progression.badgeName') }}</FieldLabel>
                <Input id="edit-badge-name" v-model="editBadgeName" maxlength="160" />
              </Field>
              <Field>
                <FieldLabel for="edit-badge-description">{{ $t('progression.badgeDescription') }}</FieldLabel>
                <Textarea id="edit-badge-description" v-model="editBadgeDescription" maxlength="1000" rows="2" />
              </Field>
              <Field>
                <FieldLabel for="edit-badge-image">{{ $t('progression.badgeImage') }}</FieldLabel>
                <FileUpload :key="editBadgeUploadKey ?? undefined" id="edit-badge-image" accept="image/png,image/jpeg,image/webp"
                  :pending="badgeSaving" @change="onEditBadgeFileChange($event)" />
              </Field>
              <Button size="sm" :disabled="badgeSaving || !editBadgeName.trim()" @click="updateBadge">{{ $t('progression.updateBadge') }}</Button>
            </FieldGroup>
            <FieldGroup v-if="canWrite" class="mt-3 gap-2 border-t pt-3">
              <Field>
                <FieldLabel for="new-badge-name">{{ $t('progression.badgeName') }}</FieldLabel>
                <Input id="new-badge-name" v-model="newBadgeName" maxlength="160" />
              </Field>
              <Field>
                <FieldLabel for="new-badge-description">{{ $t('progression.badgeDescription') }}</FieldLabel>
                <Textarea id="new-badge-description" v-model="newBadgeDescription" maxlength="1000" rows="2" />
              </Field>
              <Field>
                <FieldLabel for="new-badge-image">{{ $t('progression.badgeImage') }}</FieldLabel>
                <FileUpload :key="newBadgeUploadKey ?? undefined" id="new-badge-image" accept="image/png,image/jpeg,image/webp"
                  :pending="badgeSaving" @change="onBadgeFileChange($event)" />
              </Field>
              <Button size="sm" class="w-full" :disabled="badgeSaving || !newBadgeName.trim() || !newBadgeImage" @click="createBadge">
                {{ $t('progression.createBadge') }}
              </Button>
            </FieldGroup>
          </div>
        </aside>

        <div class="relative min-h-[46rem] overflow-hidden rounded-lg border bg-card">
          <component :is="ProgressionCanvas" v-model:nodes="nodes" v-model:edges="edges"
            :read-only="!canWrite" :batch-mode="!!batch" :batch-source-id="batchSourceId"
            :batch-selected-ids="batchTargets" :batch-disabled-reasons="batchDisabledReasons"
            :preview-edges="previewEdges" :layout-revision="layoutRevision"
            height="46rem" @connect="connect"
            @node-selection-change="changeNodeSelection" @edge-selection-change="changeEdgeSelection"
            @node-positions-change="updateNodePositions" @node-click="toggleBatchTarget" />
          <div v-if="!nodes.length" class="pointer-events-none absolute inset-0 flex items-center justify-center text-sm text-muted-foreground">
            {{ $t('progression.emptyCanvas') }}
          </div>
        </div>

        <aside class="rounded-lg border bg-card p-4">
          <h3 class="font-semibold">{{ $t('progression.inspector') }}</h3>
          <div v-if="batch" class="mt-4 flex flex-col gap-3">
            <p class="text-sm font-medium">{{ $t('progression.batchSuccessors') }}</p>
            <p class="text-xs text-muted-foreground">{{ $t('progression.batchInstruction') }}</p>
            <p class="text-sm">{{ $t('progression.predecessorCondition') }}</p>
            <div class="flex flex-wrap gap-2">
              <Button size="sm" :variant="batchCondition === 0 ? 'default' : 'outline'" @click="setBatchCondition(0)">{{ $t('progression.completed') }}</Button>
              <Button size="sm" :variant="batchCondition === 1 ? 'default' : 'outline'" @click="setBatchCondition(1)">{{ $t('progression.incomplete') }}</Button>
            </div>
            <Button :disabled="!batchTargets.size" @click="applyBatch">{{ $t('progression.addConnections', { count: batchTargets.size }) }}</Button>
            <Button variant="secondary" @click="cancelBatch">{{ $t('common.action.cancel') }}</Button>
          </div>
          <div v-else-if="selectedCount > 1" class="mt-4 flex flex-col gap-3">
            <p class="text-sm font-medium">{{ $t('progression.selectedCount', { count: selectedCount }) }}</p>
            <Button v-if="canWrite" size="sm" variant="destructive" @click="removeSelected">{{ $t('progression.removeSelected') }}</Button>
          </div>
          <div v-else-if="selectedEdge" class="mt-4 space-y-4">
            <p class="text-sm">{{ $t('progression.predecessorCondition') }}</p>
            <Button size="sm" :variant="selectedEdge.data?.condition === 0 ? 'default' : 'outline'" :disabled="!canWrite" @click="setEdgeCondition(0)">{{ $t('progression.completed') }}</Button>
            <Button size="sm" :variant="selectedEdge.data?.condition === 1 ? 'default' : 'outline'" :disabled="!canWrite" @click="setEdgeCondition(1)">{{ $t('progression.incomplete') }}</Button>
            <Button v-if="canWrite" size="sm" variant="destructive" @click="removeSelected">{{ $t('progression.removeEdge') }}</Button>
          </div>
          <div v-else-if="selectedNode" class="mt-4 space-y-3">
            <p class="break-words text-sm font-medium">{{ selectedNode.data?.title }}</p>
            <p class="text-xs text-muted-foreground">{{ selectedNode.data?.kind === 0 ? $t('progression.challengeNode') : $t('progression.badgeNode') }}</p>
            <Field orientation="horizontal">
              <Switch :id="`progression-node-gate-${selectedNode.id}`"
                :model-value="selectedNode.data?.requiresPrerequisites ?? true"
                :disabled="!canWrite" @update:model-value="setSelectedNodeRequiresPrerequisites($event)" />
              <FieldLabel :for="`progression-node-gate-${selectedNode.id}`">
                {{ selectedNode.data?.kind === 0
                  ? $t('progression.challengeRequiresPrerequisites')
                  : $t('progression.badgeRequiresPrerequisites') }}
              </FieldLabel>
            </Field>
            <p class="text-xs leading-5 text-muted-foreground">
              {{ selectedNode.data?.kind === 0
                ? $t('progression.challengeGateHint')
                : $t('progression.badgeGateHint') }}
            </p>
            <Button v-if="canWrite" size="sm" @click="beginBatch">{{ $t('progression.batchSuccessors') }}</Button>
            <Button v-if="canWrite" size="sm" variant="destructive" @click="removeSelected">{{ $t('progression.removeNode') }}</Button>
          </div>
          <p v-else class="mt-4 text-sm text-muted-foreground">{{ $t('progression.selectHint') }}</p>
          <p class="mt-6 text-xs leading-5 text-muted-foreground">{{ $t('progression.ruleHint') }}</p>
        </aside>
      </div>
    </template>
  </section>
</template>
